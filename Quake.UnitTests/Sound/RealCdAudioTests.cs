// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Loads and decodes real ripped CD tracks from <PakFilesDirectory>/music
// (trackNN.ogg), same opt-in convention as RealPakFileTests/RealSoundTests:
// Inconclusive when PakFilesDirectory isn't configured, has no music
// directory, or is missing a needed track; the playback test is also
// Inconclusive when no audio device is available.
//
// Shares static state with the other loader/sound tests (Com/Cmd/Cvar,
// SndDma, CdAudio), so this can't run concurrently with them.
[TestClass]
public sealed unsafe class RealCdAudioTests
{
    private byte* _buf;
    private const int Size = 8 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;
        Com.com_modified = false;
        Com.com_argc = 0;
        Com.com_argv = [];

        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

        SndDma.snd_initialized = false;
        SndDma.sound_started = false;
        SndDma.fakedma = false;
        SndDma.num_sfx = 0;
        SndDma.total_channels = 0;
        SndDma.shm = null;
        SndDma.paintedtime = 0;
        NativeMemory.Clear(SndDma.channels, (nuint)(SndDma.MAX_CHANNELS * sizeof(Channel_t)));

        CdAudio.playLooping = false;
        CdAudio.streamEnded = false;
    }

    [TestCleanup]
    public void Teardown()
    {
        CdAudio.Shutdown();
        CdAudio.CloseTrack();
        SndDma.Shutdown();

        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);
        Com.com_searchpaths = null;

        NativeMemory.Free(_buf);
    }

    // returns the game directory (PakFilesDirectory) once its music/ holds every requested track
    private static string RequireMusic(params int[] tracks)
    {
        string? dir = TestConfiguration.PakFilesDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive("PakFilesDirectory is not configured in configuration.json; skipping real CD audio test.");

        string music = Path.Combine(dir, "music");
        if (!Directory.Exists(music))
            Assert.Inconclusive($"No music directory in the configured PakFilesDirectory ({dir}); skipping real CD audio test.");

        foreach (int track in tracks)
        {
            string file = Path.Combine(music, $"track{track:D2}.ogg");
            if (!File.Exists(file))
                Assert.Inconclusive($"{file} was not found; skipping real CD audio test.");
        }

        return dir;
    }

    [TestMethod]
    public void OpenTrack_EveryTrackInMusicDirectory_LoadsAsVorbis()
    {
        string dir = RequireMusic();
        Com.AddGameDirectory(Ansi(dir));

        int found = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(dir, "music"), "track??.ogg"))
        {
            byte track = byte.Parse(Path.GetFileNameWithoutExtension(file).AsSpan(5));

            Assert.IsTrue(CdAudio.OpenTrack(track), $"{file} should open");
            Assert.IsNotNull(CdAudio.reader);
            Assert.IsTrue(CdAudio.reader.Channels is 1 or 2, $"{file}: {CdAudio.reader.Channels} channels");
            Assert.IsGreaterThan(0, CdAudio.reader.SampleRate);
            Assert.IsGreaterThan(0L, CdAudio.reader.TotalSamples);
            Assert.IsFalse(CdAudio.streamEnded);
            Assert.IsTrue(Sys.sys_handles.Skip(1).All(h => h == null || IsPakHandle(h)),
                "the track's file handle should be closed after loading");

            CdAudio.CloseTrack();
            found++;
        }

        if (found == 0)
            Assert.Inconclusive("The music directory holds no trackNN.ogg files; skipping real CD audio test.");
    }

    private static bool IsPakHandle(FileStream h)
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null && ReferenceEquals(Sys.sys_handles[s->Pack->Handle], h))
                return true;
        return false;
    }

    [TestMethod]
    public void DecodeChunk_RealTrack_ProducesFullNonSilentBuffer()
    {
        string dir = RequireMusic(2);
        Com.AddGameDirectory(Ansi(dir));

        Assert.IsTrue(CdAudio.OpenTrack(2));
        int channels = CdAudio.reader!.Channels;

        // skip any silent lead-in: a couple of seconds into the track
        CdAudio.reader.SamplePosition = CdAudio.reader.SampleRate * 2L;
        int got = CdAudio.DecodeChunk();

        Assert.AreEqual(CdAudio.BUFFER_FRAMES * channels, got);
        Assert.IsFalse(CdAudio.streamEnded);

        bool nonSilent = false;
        for (int i = 0; i < got && !nonSilent; i++)
            nonSilent = CdAudio.pcmBuf[i] != 0;
        Assert.IsTrue(nonSilent, "decoded audio should not be all zeros");
    }

    // NVorbis can't seek inside the last Ogg page of some rips (it throws a
    // GranulePos mismatch), so these start 2 s before the end and decode up
    // to it, like playback would.
    private const int TailSeconds = 2;

    [TestMethod]
    public void DecodeChunk_AtEndOfNonLoopingTrack_EndsStream()
    {
        string dir = RequireMusic(2);
        Com.AddGameDirectory(Ansi(dir));

        Assert.IsTrue(CdAudio.OpenTrack(2));
        CdAudio.playLooping = false;
        int full = CdAudio.BUFFER_FRAMES * CdAudio.reader!.Channels;
        long tail = (long)CdAudio.reader.SampleRate * TailSeconds;
        CdAudio.reader.SamplePosition = CdAudio.reader.TotalSamples - tail;

        int got;
        int chunks = 0;
        long maxChunks = tail / CdAudio.BUFFER_FRAMES + 2;
        do
        {
            got = CdAudio.DecodeChunk();
            Assert.IsLessThanOrEqualTo(maxChunks, ++chunks, "the stream should end within the decoded tail");
        } while (got == full);

        Assert.IsTrue(CdAudio.streamEnded);
        Assert.AreEqual(0, CdAudio.DecodeChunk(), "nothing left after the end of a non-looping track");
    }

    [TestMethod]
    public void DecodeChunk_AtEndOfLoopingTrack_RewindsSeamlessly()
    {
        string dir = RequireMusic(2);
        Com.AddGameDirectory(Ansi(dir));

        Assert.IsTrue(CdAudio.OpenTrack(2));
        CdAudio.playLooping = true;
        int full = CdAudio.BUFFER_FRAMES * CdAudio.reader!.Channels;
        long tail = (long)CdAudio.reader.SampleRate * TailSeconds;
        long start = CdAudio.reader.TotalSamples - tail;
        CdAudio.reader.SamplePosition = start;

        // decode one chunk past the end of the track
        long chunks = tail / CdAudio.BUFFER_FRAMES + 1;
        for (long i = 0; i < chunks; i++)
            Assert.AreEqual(full, CdAudio.DecodeChunk(), "every buffer should be filled, including across the loop point");

        Assert.IsFalse(CdAudio.streamEnded);
        Assert.IsLessThan(start, CdAudio.reader.SamplePosition, "the reader should have wrapped to the start");
    }

    [TestMethod]
    public void OpenTrack_GameDirMusic_OverridesId1()
    {
        string dir = RequireMusic(2, 3);
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, "music"));
        try
        {
            // a mission pack's track02 that is really id1's track03
            File.Copy(Path.Combine(dir, "music", "track03.ogg"), Path.Combine(gamedir, "music", "track02.ogg"));

            Com.AddGameDirectory(Ansi(dir));
            Assert.IsTrue(CdAudio.OpenTrack(3));
            long track03Samples = CdAudio.reader!.TotalSamples;
            CdAudio.CloseTrack();

            Assert.IsTrue(CdAudio.OpenTrack(2));
            long id1Track02Samples = CdAudio.reader!.TotalSamples;
            CdAudio.CloseTrack();
            Assert.AreNotEqual(track03Samples, id1Track02Samples, "test needs two different tracks");

            Com.AddGameDirectory(Ansi(gamedir)); // added last = searched first, like -rogue/-hipnotic

            Assert.IsTrue(CdAudio.OpenTrack(2));
            Assert.AreEqual(track03Samples, CdAudio.reader!.TotalSamples, "the game directory's track should win");
        }
        finally
        {
            CdAudio.CloseTrack();
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void Play_RealTrack_StreamsThroughRealAudioDevice()
    {
        string dir = RequireMusic(2);
        Com.AddGameDirectory(Ansi(dir));

        SndDma.fakedma = false;
        SndDma.Init();

        if (!SndHost.Initialized)
            Assert.Inconclusive("No real audio device is available in this environment; skipping playback test.");

        Assert.AreEqual(0, CdAudio.Init());

        CdAudio.Play(2, true);
        Assert.IsTrue(CdAudio.playing);
        Assert.AreEqual(2, CdAudio.playTrack);

        long start = CdAudio.reader!.SamplePosition;
        bool refilled = SpinWait.SpinUntil(() =>
        {
            CdAudio.Update();
            return CdAudio.reader!.SamplePosition > start;
        }, 5000);

        Assert.IsTrue(CdAudio.playing, "a looping track should still be playing");
        Assert.IsTrue(refilled, "buffers should have been refilled as they played");

        CdAudio.Pause();
        Assert.IsFalse(CdAudio.playing);
        Assert.IsTrue(CdAudio.wasPlaying);

        CdAudio.Resume();
        Assert.IsTrue(CdAudio.playing);

        CdAudio.Stop();
        Assert.IsFalse(CdAudio.playing);
        Assert.IsNull(CdAudio.reader);
    }
}
