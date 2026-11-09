// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using NVorbis;
using OpenTK.Audio.OpenAL;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// cd_win.c -- CD audio, with the CD-ROM transport replaced by ripped Ogg
/// Vorbis tracks. The original drives a physical drive through WinMM's MCI;
/// there is no OS-agnostic CD-ROM API (rule 5), so tracks are instead read
/// from "music/trackNN.ogg" through the Quake search path (so rogue/,
/// hipnotic/ or -game directories override id1/, and pak files work too),
/// decoded with NVorbis (managed, OS agnostic -- .NET has no built-in Vorbis
/// decoder) and streamed through a dedicated OpenAL source on SndHost's
/// context. The state machine (enabled/cdValid/playing/wasPlaying/remap) and
/// the "cd" console command are kept from cd_win.c.
///
/// Deviations (rule 8):
/// - There is no disc table of contents, so maxTrack is fixed at 99 and a
///   missing track file is rejected at play time instead of up front.
/// - bgmvolume is applied as a continuous source gain (0 pauses, &gt;0 resumes)
///   rather than cd_win.c's forced 0/1 toggle, since a CD drive had no volume
///   control but an OpenAL source does.
/// - "close"/"eject" have no door to operate; eject just stops playback.
/// </summary>
public static unsafe class CdAudio
{
    // streaming buffer setup (no original equivalent; MCI streamed itself)
    private const int NUM_BUFFERS = 4;
    internal const int BUFFER_FRAMES = 8192;
    private const int CD_CHANNELS = 2;
    private const int BUFFER_SAMPLES = BUFFER_FRAMES * CD_CHANNELS;
    internal const int MAX_TRACKS = 100;

    // static qboolean cdValid, playing, wasPlaying, initialized, enabled, playLooping
    internal static bool cdValid;
    internal static bool playing;
    internal static bool wasPlaying;
    internal static bool initialized;
    internal static bool enabled;
    internal static bool playLooping;

    // cdvolume
    internal static float cdvolume;

    // remap
    internal static readonly byte* remap = NativeMemory.AllocArrayZeroed<byte>(MAX_TRACKS);

    // playTrack, maxTrack
    internal static byte playTrack;
    internal static byte maxTrack;

    // OpenAL music source and its buffer queue. Deliberately separate from SndHost's SFX
    // source/buffers (same device and context, different stream); not in the C original.
    private static int source;
    private static readonly int[] alBuffers = new int[NUM_BUFFERS];

    // currently open track
    internal static VorbisReader? reader;
    private static byte* trackData;
    internal static bool streamEnded;

    // decode/convert scratch
    internal static readonly float* decodeBuf = NativeMemory.AllocArrayZeroed<float>(BUFFER_SAMPLES);
    internal static readonly short* pcmBuf = NativeMemory.AllocArrayZeroed<short>(BUFFER_SAMPLES);

    // CDAudio_Eject / CDAudio_CloseDoor -- no drive door; nothing to do.

    // CDAudio_GetAudioDiskInfo
    // There is no disc to query: the "disc" is always present and individual
    // tracks are validated when they are opened.
    private static int GetAudioDiskInfo()
    {
        cdValid = true;
        maxTrack = MAX_TRACKS - 1;
        return 0;
    }

    // "music/track%02i.ogg" -- dest must hold at least 20 bytes
    internal static void TrackFileName(int track, byte* dest)
    {
        ReadOnlySpan<byte> prefix = "music/track"u8;
        ReadOnlySpan<byte> suffix = ".ogg"u8;

        byte* p = dest;
        foreach (byte b in prefix)
            *p++ = b;
        *p++ = (byte)('0' + track / 10 % 10);
        *p++ = (byte)('0' + track % 10);
        foreach (byte b in suffix)
            *p++ = b;
        *p = 0;
    }

    // Replaces cd_win.c's MCI_STATUS track-type/length queries: loads the
    // whole track into native memory so no Sys file handle stays open while
    // it plays, and the same path works for loose files and pak entries.
    internal static bool OpenTrack(byte track)
    {
        byte* name = stackalloc byte[32];
        TrackFileName(track, name);

        int handle;
        int len = Com.FindFile(name, &handle, true);
        if (len <= 0 || handle == -1)
        {
            if (handle != -1)
                Sys.FileClose(handle);
            Con.DPrintf($"CDAudio: no file for track {track}\n");
            return false;
        }

        byte* data = NativeMemory.AllocArray<byte>(len);
        int total = 0;
        while (total < len)
        {
            int read = Sys.FileRead(handle, data + total, len - total);
            if (read <= 0)
                break;
            total += read;
        }
        Sys.FileClose(handle);

        if (total != len)
        {
            NativeMemory.Free(data);
            Con.DPrintf($"CDAudio: error reading track {track}\n");
            return false;
        }

        VorbisReader? r = null;
        try
        {
            r = new VorbisReader(new UnmanagedMemoryStream(data, len), true);
            if (r.Channels < 1 || r.Channels > CD_CHANNELS)
                throw new InvalidDataException($"{r.Channels} channels");
        }
        catch (Exception e)
        {
            r?.Dispose();
            NativeMemory.Free(data);
            Con.DPrintf($"CDAudio: track {track} is not playable ({e.Message})\n");
            return false;
        }

        reader = r;
        trackData = data;
        streamEnded = false;
        return true;
    }

    internal static void CloseTrack()
    {
        reader?.Dispose();
        reader = null;
        if (trackData != null)
        {
            NativeMemory.Free(trackData);
            trackData = null;
        }
        streamEnded = true;
    }

    // Decodes up to one buffer of audio into pcmBuf as signed 16-bit samples,
    // rewinding a looping track at its end. Returns the number of samples
    // (frames * channels) written; 0 at the end of a non-looping track.
    internal static int DecodeChunk()
    {
        VorbisReader r = reader!;
        int want = BUFFER_FRAMES * r.Channels;
        int got = 0;
        bool rewound = false;

        while (got < want)
        {
            try
            {
                int n = r.ReadSamples(new Span<float>(decodeBuf + got, want - got));
                if (n > 0)
                {
                    got += n;
                    rewound = false;
                    continue;
                }

                // end of track: MCI_NOTIFY_SUCCESSFUL in CDAudio_MessageHandler
                // restarts a looping track; rewinding here loops seamlessly.
                if (playLooping && !rewound)
                {
                    r.SamplePosition = 0;
                    rewound = true; // guard against an empty stream spinning forever
                    continue;
                }
            }
            catch (Exception e)
            {
                // MCI_NOTIFY_FAILURE: a damaged stream ends the track rather
                // than taking the game down with it
                Con.DPrintf($"CDAudio: decode failed on track {playTrack} ({e.Message})\n");
            }

            streamEnded = true;
            break;
        }

        float* src = decodeBuf;
        short* dst = pcmBuf;
        for (int i = 0; i < got; i++)
        {
            int v = (int)(src[i] * 32767.0f);
            if (v > short.MaxValue)
                v = short.MaxValue;
            else if (v < short.MinValue)
                v = short.MinValue;
            dst[i] = (short)v;
        }

        return got;
    }

    // Decodes up to one buffer of audio into an OpenAL buffer and queues it.
    // Returns false when no data was produced (end of a non-looping track).
    private static bool FillBuffer(int buffer)
    {
        int got = DecodeChunk();
        if (got == 0)
            return false;

        VorbisReader r = reader!;
        ALFormat format = r.Channels == 2 ? ALFormat.Stereo16 : ALFormat.Mono16;
        AL.BufferData(buffer, format, pcmBuf, got * sizeof(short), r.SampleRate);
        AL.SourceQueueBuffer(source, buffer);
        return true;
    }

    private static void SetGain()
    {
        float gain = cdvolume < 0 ? 0 : cdvolume > 1 ? 1 : cdvolume;
        AL.Source(source, ALSourcef.Gain, gain);
    }

    // CDAudio_Play
    public static void Play(byte track, bool looping)
    {
        if (!enabled)
            return;

        if (!cdValid)
        {
            GetAudioDiskInfo();
            if (!cdValid)
                return;
        }

        track = remap[track < MAX_TRACKS ? track : 0];

        if (track < 1 || track > maxTrack)
        {
            Con.DPrintf($"CDAudio: Bad track number {track}.\n");
            return;
        }

        if (playing)
        {
            if (playTrack == track)
                return;
            Stop();
        }

        // release a paused or finished track still holding the source
        if (reader != null)
        {
            AL.SourceStop(source);
            UnqueueAll();
            CloseTrack();
            wasPlaying = false;
        }

        // don't try to play a missing or non-audio track
        if (!OpenTrack(track))
            return;

        playLooping = looping;
        for (int i = 0; i < NUM_BUFFERS; i++)
            if (!FillBuffer(alBuffers[i]))
                break;

        AL.SourcePlay(source);

        playTrack = track;
        playing = true;

        if (cdvolume == 0.0f)
            Pause();
    }

    private static void UnqueueAll()
    {
        AL.GetSource(source, ALGetSourcei.BuffersQueued, out int queued);
        while (queued-- > 0)
            AL.SourceUnqueueBuffer(source);
    }

    // CDAudio_Stop
    public static void Stop()
    {
        if (!enabled)
            return;

        if (!playing && !wasPlaying)
            return;

        AL.SourceStop(source);
        UnqueueAll();
        CloseTrack();

        wasPlaying = false;
        playing = false;
    }

    // CDAudio_Pause
    public static void Pause()
    {
        if (!enabled)
            return;

        if (!playing)
            return;

        AL.SourcePause(source);

        wasPlaying = playing;
        playing = false;
    }

    // CDAudio_Resume
    public static void Resume()
    {
        if (!enabled)
            return;

        if (!cdValid)
            return;

        if (!wasPlaying)
            return;

        // MCI_PLAY with MCI_TO only resumes from the current position
        AL.SourcePlay(source);
        playing = true;
    }

    // CD_f
    internal static void CD_f()
    {
        if (Cmd.Argc() < 2)
            return;

        byte* command = Cmd.Argv(1);

        if (Q.StrCaseCmp(command, "on"u8) == 0)
        {
            enabled = true;
            return;
        }

        if (Q.StrCaseCmp(command, "off"u8) == 0)
        {
            if (playing || wasPlaying)
                Stop();
            enabled = false;
            return;
        }

        if (Q.StrCaseCmp(command, "reset"u8) == 0)
        {
            enabled = true;
            if (playing || wasPlaying)
                Stop();
            for (int n = 0; n < MAX_TRACKS; n++)
                remap[n] = (byte)n;
            GetAudioDiskInfo();
            return;
        }

        if (Q.StrCaseCmp(command, "remap"u8) == 0)
        {
            int ret = Cmd.Argc() - 2;
            if (ret <= 0)
            {
                for (int n = 1; n < MAX_TRACKS; n++)
                    if (remap[n] != n)
                        Con.Printf($"  {n} -> {remap[n]}\n");
                return;
            }
            for (int n = 1; n <= ret && n < MAX_TRACKS; n++)
                remap[n] = (byte)Q.Atoi(Cmd.Argv(n + 1));
            return;
        }

        if (Q.StrCaseCmp(command, "close"u8) == 0)
            return; // CDAudio_CloseDoor -- no door

        if (!cdValid)
        {
            GetAudioDiskInfo();
            if (!cdValid)
            {
                Con.Printf("No CD in player.\n"u8);
                return;
            }
        }

        if (Q.StrCaseCmp(command, "play"u8) == 0)
        {
            Play((byte)Q.Atoi(Cmd.Argv(2)), false);
            return;
        }

        if (Q.StrCaseCmp(command, "loop"u8) == 0)
        {
            Play((byte)Q.Atoi(Cmd.Argv(2)), true);
            return;
        }

        if (Q.StrCaseCmp(command, "stop"u8) == 0)
        {
            Stop();
            return;
        }

        if (Q.StrCaseCmp(command, "pause"u8) == 0)
        {
            Pause();
            return;
        }

        if (Q.StrCaseCmp(command, "resume"u8) == 0)
        {
            Resume();
            return;
        }

        if (Q.StrCaseCmp(command, "eject"u8) == 0)
        {
            if (playing || wasPlaying)
                Stop();
            cdValid = false;
            return;
        }

        if (Q.StrCaseCmp(command, "info"u8) == 0)
        {
            Con.Printf($"{maxTrack} tracks\n");
            if (playing)
                Con.Printf($"Currently {(playLooping ? "looping" : "playing")} track {playTrack}\n");
            else if (wasPlaying)
                Con.Printf($"Paused {(playLooping ? "looping" : "playing")} track {playTrack}\n");
            Con.Printf($"Volume is {cdvolume:F6}\n");
            return;
        }
    }

    // CDAudio_Update
    // Also does the work of CDAudio_MessageHandler: MCI notified the window
    // when a track finished; here the stream is polled once per frame.
    public static void Update()
    {
        if (!enabled)
            return;

        float vol = SndDma.bgmvolume->Value;
        if (vol != cdvolume)
        {
            bool wasSilent = cdvolume <= 0;
            cdvolume = vol;
            SetGain();
            if (cdvolume <= 0)
                Pause();
            else if (wasSilent)
                Resume();
        }

        if (!playing)
            return;

        // refill buffers the source has finished with
        AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
        while (processed-- > 0)
        {
            int buffer = AL.SourceUnqueueBuffer(source);
            if (!streamEnded)
                FillBuffer(buffer);
        }

        AL.GetSource(source, ALGetSourcei.BuffersQueued, out int queued);
        AL.GetSource(source, ALGetSourcei.SourceState, out int state);
        if ((ALSourceState)state != ALSourceState.Playing)
        {
            if (queued > 0)
            {
                AL.SourcePlay(source); // underrun (e.g. a long frame); keep going
            }
            else
            {
                // MCI_NOTIFY_SUCCESSFUL: a non-looping track ran out
                playing = false;
                CloseTrack();
            }
        }
    }

    // CDAudio_Init
    public static int Init()
    {
        if (Cls.state == CActive.Dedicated)
            return -1;

        if (Com.CheckParm("-nocdaudio"u8) != 0)
            return -1;

        // the music source lives on SndHost's OpenAL context
        if (!SndHost.Initialized)
        {
            Con.Printf("CDAudio_Init: no sound device\n"u8);
            return -1;
        }

        source = AL.GenSource();
        AL.GenBuffers(alBuffers);

        for (int n = 0; n < MAX_TRACKS; n++)
            remap[n] = (byte)n;
        initialized = true;
        enabled = true;
        playing = false;
        wasPlaying = false;

        GetAudioDiskInfo();

        cdvolume = SndDma.bgmvolume->Value;
        SetGain();

        Cmd.AddCommand("cd"u8, &CD_f);

        Con.Printf("CD Audio Initialized\n"u8);

        return 0;
    }

    // CDAudio_Shutdown
    public static void Shutdown()
    {
        if (!initialized)
            return;

        Stop();
        CloseTrack();

        AL.SourceStop(source);
        AL.DeleteSource(source);
        AL.DeleteBuffers(alBuffers);

        initialized = false;
        enabled = false;
    }
}
