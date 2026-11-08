// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// snd_mix.c -- portable code to mix sounds for snd_dma.c.
/// Only the !id386 C fallback mixing routines are ported (no x86 asm), same
/// rule the renderer phases already followed. The DirectSound Lock/Unlock
/// branches from the original (guarded by #ifdef _WIN32) are omitted: with
/// SndHost.cs's single OS-agnostic backend, the mixer always writes straight
/// into shm->Buffer.
/// </summary>
public static unsafe class SndMix
{
    public const int PAINTBUFFER_SIZE = 512;

    // paintbuffer
    public static readonly PortableSamplePair_t* paintbuffer = NativeMemory.AllocArrayZeroed<PortableSamplePair_t>(PAINTBUFFER_SIZE);

    internal const int SND_SCALETABLE_COLS = 256;
    // snd_scaletable[32][256] -- flat buffer, indexed by row * SND_SCALETABLE_COLS + col.
    public static readonly int* snd_scaletable = NativeMemory.AllocArrayZeroed<int>(32 * SND_SCALETABLE_COLS);

    // snd_p, snd_linear_count, snd_vol, snd_out
    public static int* snd_p;
    public static int snd_linear_count;
    public static int snd_vol;
    public static short* snd_out;

    // Snd_WriteLinearBlastStereo16
    public static void WriteLinearBlastStereo16()
    {
        SndMixSimd.ScaleClamp16(snd_p, snd_out, snd_linear_count, snd_vol);
    }

    // S_TransferStereo16
    public static void TransferStereo16(int endtime)
    {
        snd_vol = (int)(SndDma.volume->Value * 256);

        {
            snd_p = (int*)paintbuffer;
            int lpaintedtime = SndDma.paintedtime;

            uint* pbuf = (uint*)SndDma.shm->Buffer;

            while (lpaintedtime < endtime)
            {
                // handle recirculating buffer issues
                int lpos = lpaintedtime & ((SndDma.shm->Samples >> 1) - 1);

                snd_out = (short*)pbuf + (lpos << 1);

                snd_linear_count = (SndDma.shm->Samples >> 1) - lpos;
                if (lpaintedtime + snd_linear_count > endtime)
                    snd_linear_count = endtime - lpaintedtime;

                snd_linear_count <<= 1;

                // write a linear blast of samples
                WriteLinearBlastStereo16();

                snd_p += snd_linear_count;
                lpaintedtime += snd_linear_count >> 1;
            }
        }
    }

    // S_TransferPaintBuffer
    public static void TransferPaintBuffer(int endtime)
    {
        if (SndDma.shm->SampleBits == 16 && SndDma.shm->Channels == 2)
        {
            TransferStereo16(endtime);
            return;
        }

        {
            int* p = (int*)paintbuffer;
            int count = (endtime - SndDma.paintedtime) * SndDma.shm->Channels;
            int out_mask = SndDma.shm->Samples - 1;
            int out_idx = SndDma.paintedtime * SndDma.shm->Channels & out_mask;
            int step = 3 - SndDma.shm->Channels;
            int vol = (int)(SndDma.volume->Value * 256);

            byte* pbuf = SndDma.shm->Buffer;

            if (SndDma.shm->SampleBits == 16)
            {
                short* @out = (short*)pbuf;
                if (step == 1)
                {
                    // stereo: contiguous runs between wraps of the output ring
                    while (count > 0)
                    {
                        int run = Math.Min(count, SndDma.shm->Samples - out_idx);
                        SndMixSimd.ScaleClamp16(p, @out + out_idx, run, vol);
                        p += run;
                        count -= run;
                        out_idx = (out_idx + run) & out_mask;
                    }
                }
                while (count-- != 0)
                {
                    int val = (*p * vol) >> 8;
                    p += step;
                    if (val > 0x7fff)
                        val = 0x7fff;
                    else if (val < -0x8000)
                        val = -0x8000;
                    @out[out_idx] = (short)val;
                    out_idx = (out_idx + 1) & out_mask;
                }
            }
            else if (SndDma.shm->SampleBits == 8)
            {
                byte* @out = pbuf;
                if (step == 1)
                {
                    while (count > 0)
                    {
                        int run = Math.Min(count, SndDma.shm->Samples - out_idx);
                        SndMixSimd.ScaleClamp8(p, @out + out_idx, run, vol);
                        p += run;
                        count -= run;
                        out_idx = (out_idx + run) & out_mask;
                    }
                }
                while (count-- != 0)
                {
                    int val = (*p * vol) >> 8;
                    p += step;
                    if (val > 0x7fff)
                        val = 0x7fff;
                    else if (val < -0x8000)
                        val = -0x8000;
                    @out[out_idx] = (byte)((val >> 8) + 128);
                    out_idx = (out_idx + 1) & out_mask;
                }
            }
        }
    }

    // S_PaintChannels
    public static void PaintChannels(int endtime)
    {
        while (SndDma.paintedtime < endtime)
        {
            // if paintbuffer is smaller than DMA buffer
            int end = endtime;
            if (endtime - SndDma.paintedtime > PAINTBUFFER_SIZE)
                end = SndDma.paintedtime + PAINTBUFFER_SIZE;

            // clear the paint buffer
            NativeMemory.Clear(paintbuffer, end - SndDma.paintedtime);

            // paint in the channels.
            for (int i = 0; i < SndDma.total_channels; i++)
            {
                Channel_t* ch = &SndDma.channels[i];
                if (ch->Sfx == null)
                    continue;
                if (ch->LeftVol == 0 && ch->RightVol == 0)
                    continue;
                SfxCache_t* sc = Wav.LoadSound(ch->Sfx);
                if (sc == null)
                    continue;

                int ltime = SndDma.paintedtime;

                while (ltime < end)
                {
                    // paint up to end
                    int count = ch->End < end ? ch->End - ltime : end - ltime;

                    if (count > 0)
                    {
                        if (sc->Width == 1)
                            PaintChannelFrom8(ch, sc, count);
                        else
                            PaintChannelFrom16(ch, sc, count);

                        ltime += count;
                    }

                    // if at end of loop, restart
                    if (ltime >= ch->End)
                    {
                        if (sc->LoopStart >= 0)
                        {
                            ch->Pos = sc->LoopStart;
                            ch->End = ltime + sc->Length - ch->Pos;
                        }
                        else
                        {
                            // channel just stopped
                            ch->Sfx = null;
                            break;
                        }
                    }
                }
            }

            // transfer out according to DMA format
            TransferPaintBuffer(end);
            SndDma.paintedtime = end;
        }
    }

    // SND_InitScaletable
    public static void InitScaletable()
    {
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 256; j++)
                snd_scaletable[i * SND_SCALETABLE_COLS + j] = (sbyte)j * i * 8;
    }

    // SND_PaintChannelFrom8
    public static void PaintChannelFrom8(Channel_t* ch, SfxCache_t* sc, int count)
    {
        if (ch->LeftVol > 255)
            ch->LeftVol = 255;
        if (ch->RightVol > 255)
            ch->RightVol = 255;

        {
            // snd_scaletable[row][j] == (sbyte)j * row * 8, so the table lookup
            // is a multiply by (vol >> 3) * 8 (the table is still built by
            // InitScaletable, just no longer read here).
            int lscale = (ch->LeftVol >> 3) * 8;
            int rscale = (ch->RightVol >> 3) * 8;
            sbyte* sfx = (sbyte*)(sc + 1) + ch->Pos;

            SndMixSimd.Paint8((int*)paintbuffer, sfx, count, lscale, rscale);
        }

        ch->Pos += count;
    }

    // SND_PaintChannelFrom16
    public static void PaintChannelFrom16(Channel_t* ch, SfxCache_t* sc, int count)
    {
        int leftvol = ch->LeftVol;
        int rightvol = ch->RightVol;
        short* sfx = (short*)(sc + 1) + ch->Pos;

        SndMixSimd.Paint16((int*)paintbuffer, sfx, count, leftvol, rightvol);

        ch->Pos += count;
    }
}
