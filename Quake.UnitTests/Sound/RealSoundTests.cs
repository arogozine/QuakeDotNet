// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Loads a real sound from the retail PAK0.PAK (via TestConfiguration's
// PakFilesDirectory, same opt-in convention as RealPakFileTests) and, in the
// second test, plays it through the real OpenAL backend (SndHost). Opt-in
// and best-effort: Inconclusive when no PAK is configured/found, or when no
// audio device is available in the environment (CI boxes typically have
// none).
//
// Shares static state with the other loader/sound tests (Com/Hunk/Cache,
// SndDma's cvars/channels/known_sfx/shm), so this can't run concurrently
// with them.
[TestClass]
public sealed unsafe class RealSoundTests
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
        // some other test classes (e.g. CommonTests' "-safe" case) leave
        // com_argv/com_argc pointing at leftover args like "-nosound",
        // which would make S_Init bail out early -- reset them here too.
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
    }

    [TestCleanup]
    public void Teardown()
    {
        SndDma.Shutdown();

        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(_buf);
    }

    private static string RequireGameDir()
    {
        string? dir = TestConfiguration.PakFilesDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive("PakFilesDirectory is not configured in configuration.json; skipping real sound test.");

        if (!File.Exists(Path.Combine(dir, "pak0.pak")))
            Assert.Inconclusive($"pak0.pak was not found in the configured PakFilesDirectory ({dir}); skipping real sound test.");

        return dir;
    }

    [TestMethod]
    public void PrecacheSound_FromRealPak0_LoadsAndResamplesRealWavData()
    {
        string dir = RequireGameDir();
        Com.AddGameDirectory(Ansi(dir));

        // fakedma keeps this to loading/resampling only -- no real audio
        // device is needed to prove S_LoadSound/ResampleSfx work against a
        // real retail WAV.
        SndDma.fakedma = true;
        SndDma.Init();

        Sfx_t* sfx = SndDma.PrecacheSound(Ansi("items/health1.wav"));

        Assert.IsTrue(sfx != null, "items/health1.wav should precache from PAK0.PAK");

        SfxCache_t* sc = (SfxCache_t*)Cache.Check(&sfx->Cache);
        Assert.IsTrue(sc != null, "the sound should be resident in the cache after precaching");
        Assert.IsGreaterThan(0, sc->Length);
        Assert.AreEqual(SndDma.shm->Speed, sc->Speed);
    }

    [TestMethod]
    public void StartSound_FromRealPak0_PlaysThroughRealAudioDevice()
    {
        string dir = RequireGameDir();
        Com.AddGameDirectory(Ansi(dir));

        SndDma.fakedma = false;
        SndDma.Init();

        if (!SndDma.sound_started)
            Assert.Inconclusive("No real audio device is available in this environment; skipping playback test.");

        Sfx_t* sfx = SndDma.PrecacheSound(Ansi("items/health1.wav"));
        Assert.IsTrue(sfx != null, "items/health1.wav should precache from PAK0.PAK");

        // route through the viewentity so SND_Spatialize always gives it
        // full volume, regardless of listener position/orientation
        SndDma.listener_origin = MathLib.vec3_origin;
        SndDma.StartSound(Cl.viewentity, 0, sfx, MathLib.vec3_origin, 1.0f, 1.0f);

        bool mixed = SpinWait.SpinUntil(() =>
        {
            SndDma.Update_();
            return SndDma.paintedtime > 0;
        }, 5000);

        Assert.IsTrue(mixed, "sound should have been mixed into the DMA buffer");
    }
}
