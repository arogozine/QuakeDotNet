// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using OpenTK.Audio.OpenAL;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// snd_win.c -- OS-agnostic replacement (same naming convention as the
/// still-pending VidHost.cs for vid_win.c) using OpenTK's OpenAL bindings.
///
/// The original has two backends: DirectSound (primary) and waveOut
/// (fallback, a ring of fixed-size buffers submitted to the device in
/// order). This ports the waveOut *shape* rather than DirectSound's
/// lock/unlock model, because it maps directly onto OpenAL's streaming
/// buffer-queue API: shm->Buffer stays the ring buffer snd_mix.c paints
/// into, unchanged from the original design; SNDDMA_Submit slices it into
/// fixed-size chunks and queues/unqueues them exactly like the waveOut path
/// queued/reclaimed WAVEHDRs.
/// </summary>
public static unsafe class SndHost
{
    // WAV_BUFFERS, WAV_MASK, WAV_BUFFER_SIZE (snd_win.c)
    private const int WAV_BUFFERS = 64;
    private const int WAV_MASK = WAV_BUFFERS - 1;
    private const int WAV_BUFFER_SIZE = 0x0400;
    private const int SND_BUF_SIZE = WAV_BUFFERS * WAV_BUFFER_SIZE; // gSndBufSize

    // sn
    internal static readonly Dma_t* sn = NativeMemory.AllocZeroed<Dma_t>();

    private static ALDevice device;
    private static ALContext context;
    private static int source;
    private static readonly int[] alBuffers = new int[WAV_BUFFERS];
    private static bool initialized;

    // true once SNDDMA_Init has created the OpenAL context (CdAudio shares it)
    internal static bool Initialized => initialized;

    // snd_sent, snd_completed
    private static int snd_sent;
    private static int snd_completed;

    // sample16 -- (samplebits/8) - 1, used to convert a byte offset into a
    // mono-sample offset the same way the original waveOut path does.
    private static int sample16;

    // SNDDMA_Init
    public static int SNDDMA_Init()
    {
        device = ALC.OpenDevice(null);
        if (device == ALDevice.Null)
        {
            Con.SafePrintf("Couldn't open OpenAL device\n"u8);
            return 0;
        }

        context = ALC.CreateContext(device, (int[]?)null);
        if (context == ALContext.Null || !ALC.MakeContextCurrent(context))
        {
            Con.SafePrintf("Couldn't create OpenAL context\n"u8);
            ALC.CloseDevice(device);
            device = ALDevice.Null;
            return 0;
        }

        source = AL.GenSource();
        AL.GenBuffers(alBuffers);

        snd_sent = 0;
        snd_completed = 0;

        SndDma.shm = sn;

        SndDma.shm->Channels = 2;
        SndDma.shm->SampleBits = 16;
        SndDma.shm->Speed = 11025;
        SndDma.shm->SplitBuffer = false;
        SndDma.shm->Samples = SND_BUF_SIZE / (SndDma.shm->SampleBits / 8);
        SndDma.shm->SamplePos = 0;
        SndDma.shm->SubmissionChunk = 1;
        SndDma.shm->Buffer = NativeMemory.AllocArrayZeroed<byte>(SND_BUF_SIZE);
        SndDma.shm->SoundAlive = true;
        SndDma.shm->GameAlive = true;

        sample16 = (SndDma.shm->SampleBits / 8) - 1;

        initialized = true;

        Con.SafePrintf("OpenAL sound initialized\n"u8);
        return 1;
    }

    // SNDDMA_GetDMAPos
    public static int SNDDMA_GetDMAPos()
    {
        if (!initialized)
        {
            return 0;
        }

        int s = snd_sent * WAV_BUFFER_SIZE;
        s >>= sample16;
        s &= SndDma.shm->Samples - 1;
        return s;
    }

    // SNDDMA_Submit
    public static void SNDDMA_Submit()
    {
        if (!initialized)
        {
            return;
        }

        // find which sound blocks have completed
        AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
        while (processed-- > 0)
        {
            AL.SourceUnqueueBuffer(source);
            snd_completed++;
        }

        // submit new sound blocks, same 4-buffers-ahead cadence as the original
        while (((snd_sent - snd_completed) >> sample16) < 4)
        {
            int bufname = alBuffers[snd_sent & WAV_MASK];
            byte* chunk = SndDma.shm->Buffer + ((snd_sent & WAV_MASK) * WAV_BUFFER_SIZE);

            AL.BufferData(bufname, ALFormat.Stereo16, chunk, WAV_BUFFER_SIZE, SndDma.shm->Speed);
            AL.SourceQueueBuffer(source, bufname);

            snd_sent++;
        }

        AL.GetSource(source, ALGetSourcei.SourceState, out int state);
        if ((ALSourceState)state != ALSourceState.Playing)
        {
            AL.SourcePlay(source);
        }
    }

    // waveOutReset (hWaveOut), as called from S_BlockSound -- stops playback
    // and returns every queued buffer, so nothing stale replays on unblock.
    public static void Reset()
    {
        if (!initialized)
        {
            return;
        }

        AL.SourceStop(source);
        AL.GetSource(source, ALGetSourcei.BuffersQueued, out int queued);
        while (queued-- > 0)
        {
            AL.SourceUnqueueBuffer(source);
        }

        snd_completed = snd_sent;
    }

    // SNDDMA_Shutdown
    public static void SNDDMA_Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        AL.SourceStop(source);
        AL.DeleteSource(source);
        AL.DeleteBuffers(alBuffers);

        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(context);
        ALC.CloseDevice(device);

        if (SndDma.shm != null && SndDma.shm->Buffer != null)
        {
            NativeMemory.Free(SndDma.shm->Buffer);
        }

        initialized = false;
        snd_sent = 0;
        snd_completed = 0;
    }

}
