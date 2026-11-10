// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// snd_mem.c -- WAV loading and sound caching (GetWavinfo/S_LoadSound/
/// ResampleSfx). Playback/output (SNDDMA_*) lives in SndHost.cs.
/// </summary>
public static unsafe class Wav
{
    // data_p / iff_end / last_chunk / iff_data / iff_chunk_len
    private static byte* data_p;
    private static byte* iff_end;
    private static byte* last_chunk;
    private static byte* iff_data;
    private static int iff_chunk_len;

    // GetLittleShort
    private static short GetLittleShort()
    {
        short val = data_p[0];
        val = (short)(val + (data_p[1] << 8));
        data_p += 2;
        return val;
    }

    // GetLittleLong
    private static int GetLittleLong()
    {
        int val = data_p[0];
        val += data_p[1] << 8;
        val += data_p[2] << 16;
        val += data_p[3] << 24;
        data_p += 4;
        return val;
    }

    // FindNextChunk
    private static void FindNextChunk(Utf8String name)
    {
        while (true)
        {
            data_p = last_chunk;

            if (data_p >= iff_end)
            {
                // didn't find the chunk
                data_p = null;
                return;
            }

            data_p += 4;
            iff_chunk_len = GetLittleLong();
            if (iff_chunk_len < 0)
            {
                data_p = null;
                return;
            }

            data_p -= 8;
            last_chunk = data_p + 8 + ((iff_chunk_len + 1) & ~1);
            if (Q.StrNCmp(data_p, name, 4) == 0)
            {
                return;
            }
        }
    }

    // FindChunk
    private static void FindChunk(Utf8String name)
    {
        last_chunk = iff_data;
        FindNextChunk(name);
    }

    // GetWavinfo
    public static WavInfo_t GetWavinfo(byte* name, byte* wav, int wavlength)
    {
        WavInfo_t info = default;

        if (wav == null)
        {
            return info;
        }

        iff_data = wav;
        iff_end = wav + wavlength;

        // find "RIFF" chunk
        FindChunk("RIFF"u8);
        if (!(data_p != null && Q.StrNCmp(data_p + 8, "WAVE"u8, 4) == 0))
        {
            Con.Printf("Missing RIFF/WAVE chunks\n"u8);
            return info;
        }

        // get "fmt " chunk
        iff_data = data_p + 12;

        FindChunk("fmt "u8);
        if (data_p == null)
        {
            Con.Printf("Missing fmt chunk\n"u8);
            return info;
        }
        data_p += 8;
        int format = GetLittleShort();
        if (format != 1)
        {
            Con.Printf("Microsoft PCM format only\n"u8);
            return info;
        }

        info.Channels = GetLittleShort();
        info.Rate = GetLittleLong();
        data_p += 4 + 2;
        info.Width = GetLittleShort() / 8;

        // get cue chunk
        FindChunk("cue "u8);
        if (data_p != null)
        {
            data_p += 32;
            info.LoopStart = GetLittleLong();

            // if the next chunk is a LIST chunk, look for a cue length marker
            FindNextChunk("LIST"u8);
            if (data_p != null)
            {
                if (Q.StrNCmp(data_p + 28, "mark"u8, 4) == 0)
                {
                    // this is not a proper parse, but it works with cooledit...
                    data_p += 24;
                    int i = GetLittleLong(); // samples in loop
                    info.Samples = info.LoopStart + i;
                }
            }
        }
        else
        {
            info.LoopStart = -1;
        }

        // find data chunk
        FindChunk("data"u8);
        if (data_p == null)
        {
            Con.Printf("Missing data chunk\n"u8);
            return info;
        }

        data_p += 4;
        int samples = GetLittleLong() / info.Width;

        if (info.Samples != 0)
        {
            if (samples < info.Samples)
            {
                Sys.Error($"Sound {name} has a bad loop length");
            }
        }
        else
        {
            info.Samples = samples;
        }

        info.DataOfs = (int)(data_p - wav);

        return info;
    }

    // ResampleSfx
    public static void ResampleSfx(Sfx_t* sfx, int inrate, int inwidth, byte* data)
    {
        SfxCache_t* sc = (SfxCache_t*)Cache.Check(&sfx->Cache);
        if (sc == null)
        {
            return;
        }

        float stepscale = (float)inrate / SndDma.shm->Speed; // this is usually 0.5, 1, or 2

        int outcount = (int)(sc->Length / stepscale);
        sc->Length = outcount;
        if (sc->LoopStart != -1)
        {
            sc->LoopStart = (int)(sc->LoopStart / stepscale);
        }

        sc->Speed = SndDma.shm->Speed;
        sc->Width = SndDma.loadas8bit->Value != 0 ? 1 : inwidth;
        sc->Stereo = 0;

        byte* scData = (byte*)(sc + 1);

        // resample / decimate to the current source rate
        if (stepscale == 1 && inwidth == 1 && sc->Width == 1)
        {
            // fast special case
            for (int i = 0; i < outcount; i++)
            {
                ((sbyte*)scData)[i] = (sbyte)((int)data[i] - 128);
            }
        }
        else
        {
            // general case
            int samplefrac = 0;
            int fracstep = (int)(stepscale * 256);
            for (int i = 0; i < outcount; i++)
            {
                int srcsample = samplefrac >> 8;
                samplefrac += fracstep;
                int sample = inwidth == 2
                    ? ((short*)data)[srcsample]
                    : ((int)data[srcsample] - 128) << 8;
                if (sc->Width == 2)
                {
                    ((short*)scData)[i] = (short)sample;
                }
                else
                {
                    ((sbyte*)scData)[i] = (sbyte)(sample >> 8);
                }
            }
        }
    }

    // S_LoadSound
    [SkipLocalsInit]
    public static SfxCache_t* LoadSound(Sfx_t* s)
    {
        // see if still in memory
        SfxCache_t* sc = (SfxCache_t*)Cache.Check(&s->Cache);
        if (sc != null)
        {
            return sc;
        }

        // load it in
        byte* namebuffer = stackalloc byte[256];
        Q.StrCpy(namebuffer, "sound/"u8);
        Q.StrCat(namebuffer, s->Name);

        byte* stackbuf = stackalloc byte[1024];
        byte* data = Com.LoadStackFile(namebuffer, stackbuf, 1024);

        if (data == null)
        {
            Con.Printf($"Couldn't load {namebuffer}\n");
            return null;
        }

        WavInfo_t info = GetWavinfo(s->Name, data, Com.com_filesize);
        if (info.Channels != 1)
        {
            Con.Printf($"{s->Name} is a stereo sample\n");
            return null;
        }

        float stepscale = (float)info.Rate / SndDma.shm->Speed;
        int len = (int)(info.Samples / stepscale);
        len = len * info.Width * info.Channels;

        sc = (SfxCache_t*)Cache.Alloc(&s->Cache, len + sizeof(SfxCache_t), s->Name);
        if (sc == null)
        {
            return null;
        }

        sc->Length = info.Samples;
        sc->LoopStart = info.LoopStart;
        sc->Speed = info.Rate;
        sc->Width = info.Width;
        sc->Stereo = info.Channels;

        ResampleSfx(s, sc->Speed, sc->Width, data + info.DataOfs);

        return sc;
    }
}
