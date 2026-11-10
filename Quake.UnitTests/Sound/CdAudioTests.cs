// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// No OpenAL device is opened in tests (SndHost.Initialized is false), so
// these cover the cd_win.c state machine and track lookup up to the point
// where a decoded track would be handed to OpenAL.
[TestClass]
public sealed unsafe class CdAudioTests
{
    private byte* _buf;
    private const int Size = 2 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
    }

    [TestCleanup]
    public void Teardown()
    {
        Com.com_searchpaths = null;
        NativeMemory.Free(_buf);
    }

    // the part of CDAudio_Init that doesn't need an OpenAL context
    private static void EnableWithoutDevice()
    {
        for (int n = 0; n < CdAudio.MAX_TRACKS; n++)
        {
            CdAudio.remap[n] = (byte)n;
        }

        CdAudio.enabled = true;
        CdAudio.cdValid = true;
        CdAudio.maxTrack = CdAudio.MAX_TRACKS - 1;
    }

    private static string Str(byte* s) => Marshal.PtrToStringAnsi((nint)s) ?? string.Empty;

    [TestMethod]
    public void Init_ReturnsMinusOne_WhenSoundNotInitialized()
    {
        Assert.AreEqual(-1, CdAudio.Init());
        Assert.IsFalse(CdAudio.enabled);
        Assert.IsFalse(CdAudio.initialized);
    }

    [TestMethod]
    public void EntryPoints_WithoutInit_DoNotThrow()
    {
        CdAudio.Play(2, true);
        CdAudio.Stop();
        CdAudio.Pause();
        CdAudio.Resume();
        CdAudio.Update();
        CdAudio.Shutdown();
        Assert.IsFalse(CdAudio.playing);
    }

    [TestMethod]
    [DataRow(2, "music/track02.ogg")]
    [DataRow(11, "music/track11.ogg")]
    [DataRow(99, "music/track99.ogg")]
    public void TrackFileName_FormatsTwoDigits(int track, string expected)
    {
        byte* name = stackalloc byte[32];
        CdAudio.TrackFileName(track, name);
        Assert.AreEqual(expected, Str(name));
    }

    [TestMethod]
    public void Play_MissingTrackFile_DoesNotStartPlaying()
    {
        EnableWithoutDevice();

        CdAudio.Play(2, true);

        Assert.IsFalse(CdAudio.playing);
        Assert.IsFalse(CdAudio.wasPlaying);
    }

    [TestMethod]
    public void Play_CorruptTrackFile_DoesNotStartPlayingAndClosesHandle()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, "music"));
        try
        {
            File.WriteAllBytes(Path.Combine(gamedir, "music", "track02.ogg"), [1, 2, 3, 4, 5, 6, 7, 8]);
            Com.AddGameDirectory(Ansi(gamedir));
            EnableWithoutDevice();

            CdAudio.Play(2, false);

            Assert.IsFalse(CdAudio.playing);
            Assert.IsTrue(Sys.sys_handles.All(h => h == null));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(100)]
    [DataRow(255)]
    public void Play_BadTrackNumber_IsRejected(int track)
    {
        EnableWithoutDevice();

        CdAudio.Play((byte)track, false);

        Assert.IsFalse(CdAudio.playing);
    }

    [TestMethod]
    public void Play_RemappedToZero_IsRejected()
    {
        EnableWithoutDevice();
        CdAudio.remap[2] = 0;

        CdAudio.Play(2, false);

        Assert.IsFalse(CdAudio.playing);
    }

    [TestMethod]
    public void CdRemap_SetsTableFromTrackOne()
    {
        EnableWithoutDevice();

        Cmd.TokenizeString(Ansi("cd remap 5 6 7"));
        CdAudio.CD_f();

        Assert.AreEqual(5, CdAudio.remap[1]);
        Assert.AreEqual(6, CdAudio.remap[2]);
        Assert.AreEqual(7, CdAudio.remap[3]);
        Assert.AreEqual(4, CdAudio.remap[4]);
    }

    [TestMethod]
    public void CdOffOn_TogglesEnabled()
    {
        EnableWithoutDevice();

        Cmd.TokenizeString(Ansi("cd off"));
        CdAudio.CD_f();
        Assert.IsFalse(CdAudio.enabled);

        Cmd.TokenizeString(Ansi("cd on"));
        CdAudio.CD_f();
        Assert.IsTrue(CdAudio.enabled);
    }

    [TestMethod]
    public void CdReset_RestoresIdentityRemap()
    {
        EnableWithoutDevice();
        CdAudio.remap[3] = 9;
        CdAudio.enabled = false;

        Cmd.TokenizeString(Ansi("cd reset"));
        CdAudio.CD_f();

        Assert.IsTrue(CdAudio.enabled);
        Assert.IsTrue(CdAudio.cdValid);
        Assert.AreEqual(3, CdAudio.remap[3]);
    }

    [TestMethod]
    public void CdEject_InvalidatesDisc_AndInfoDoesNotThrow()
    {
        EnableWithoutDevice();

        Cmd.TokenizeString(Ansi("cd eject"));
        CdAudio.CD_f();
        Assert.IsFalse(CdAudio.cdValid);

        Cmd.TokenizeString(Ansi("cd info"));
        CdAudio.CD_f();
        Assert.IsTrue(CdAudio.cdValid); // revalidated, like GetAudioDiskInfo on a re-inserted disc
    }
}
