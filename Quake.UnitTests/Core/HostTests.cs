// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// Host_Init end-to-end: this is the plan's actual acceptance test -- does
// the full startup call chain (Memory_Init through SV_Init, matching
// host.c's Host_Init) complete without throwing, using the real retail
// game data (PakFilesDirectory in configuration.json), the same opt-in
// pattern as RealPakFileTests. Reports Inconclusive rather than failing
// when that directory isn't configured, so CI without a Quake install
// stays green.
[TestClass]
public sealed unsafe class HostTests
{
    private byte* _buf;
    private const int Size = 32 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);

        // Reset every piece of shared static state Host.Init's call chain
        // touches, matching the reset lists in CvarTests/RealPakFileTests.
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Com.com_searchpaths = null;
        Com.com_modified = false;
        Host.host_initialized = false;
        Host.host_parms.BaseDir = new Utf8String("."u8);
        Sv.state = ServerState_t.Loading;
        Sv.num_edicts = 0;

        // Model/Cache statics are Hunk-backed and share state across every
        // test class touching them (see RealPakFileTests' own comment on
        // this); a previous test's now-freed Hunk arena can otherwise leave
        // dangling pointers here that crash mid-load.
        NativeMemory.Clear(Model.mod_known, Model.mod_numknown);
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;

        // Con.Init() (called by Host.Init) allocates con_text out of this
        // test's Hunk arena; a stale pointer from a previous test run would
        // dangle into memory this Setup is about to free-and-replace.
        Con.con_initialized = false;
        Con.con_text = null;
        Con.con_linewidth = 0;
        Con.con_totallines = 0;
        Con.con_current = 0;
        Con.con_x = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);

        // Same reasoning as ConsoleTests.Teardown: leave Con uninitialized
        // so a later test class's Printf takes the "not initialized" early
        // return instead of writing through con_text, which now dangles
        // into the arena just freed above.
        Con.con_initialized = false;
        Con.con_text = null;

        // Host.Init sets host_initialized, which makes Cmd.AddCommand
        // Sys.Error for every later test class that registers a command.
        Host.host_initialized = false;

        // Host.Init -> Host.FindMaxClients Hunk-allocates Svs.clients out of
        // this test's own arena (just freed above); every other test class
        // that touches Svs.clients[0] does so assuming a NativeMemory-backed
        // pointer that outlives any one test's Hunk arena (Sv.cs's static
        // constructor's own fallback allocation). Re-point it at a fresh
        // one-slot NativeMemory block so this test class doesn't leak a
        // dangling pointer into whichever test class runs next.
        Svs.clients = NativeMemory.AllocZeroed<Client_t>();
        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;
    }

    [TestMethod]
    public void Init_FromRealGameDirectory_CompletesWithoutThrowing()
    {
        string? dir = TestConfiguration.PakFilesDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            Assert.Inconclusive("PakFilesDirectory is not configured in configuration.json; skipping Host.Init end-to-end test.");
        }

        // PakFilesDirectory points at .../Quake/id1; Host_Init's basedir is
        // its parent (COM_InitFilesystem appends "/id1" itself).
        string baseDir = Path.GetDirectoryName(dir!) ?? dir!;
        if (!File.Exists(Path.Combine(dir!, "PAK0.PAK")))
        {
            Assert.Inconclusive($"PAK0.PAK was not found in {dir}; skipping Host.Init end-to-end test.");
        }

        Host.host_parms.BaseDir = (byte*)Marshal.StringToHGlobalAnsi(baseDir);

        Host.Init(_buf, Size);

        Assert.IsTrue(Host.host_initialized);
        Assert.IsTrue(Cvar.FindVar(new Utf8String("sv_gravity"u8)) != null);
        Assert.IsTrue(Cvar.FindVar(new Utf8String("chase_back"u8)) != null);
        Assert.IsTrue(Cmd.Exists(new Utf8String("togglemenu"u8)) || Cmd.Exists(new Utf8String("menu_main"u8)));
        Assert.AreEqual(1, Svs.maxclients);
    }
}
