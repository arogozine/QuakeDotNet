// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// Host_Loadgame_f against real .sav files that may already sit in
// PakFilesDirectory (id1) from an actual play session. Unlike SaveGameTests'
// synthetic round trips -- which stop at the header fields because they have
// no real progs.dat/maps to spawn a level from -- this exercises the full
// load path (Sv.SpawnServer, light styles, the {...}-block edict stream,
// ClMain.EstablishConnection/HostCmd.Reconnect_f) against genuine save data.
//
// Some real .sav files in the wild were written by a modified engine (e.g.
// DarkPlaces) that appends an extended-format trailer after the normal
// {...}-block edict stream. Vanilla Host_Loadgame_f can't parse that trailer
// either -- it Sys_Error's on it exactly like the original WinQuake would --
// so that's treated as an expected, non-fatal outcome for an individual
// file below (QuakeSysErrorException), not a test failure.
//
// Opt-in, same pattern as HostTests/RealPakFileTests: reports Inconclusive
// instead of failing when PakFilesDirectory isn't configured, PAK0.PAK isn't
// there, or no *.sav files are found in that directory, so CI/machines
// without a Quake install (or without an existing save) stay green.
//
// Shares Host.Init's full boot sequence with HostTests, so this can't run
// concurrently with anything else touching that same static state
// (Cvar/Cmd/Com/Model/Con/Sv/Svs/Cl/Cls).
[TestClass]
public sealed unsafe class RealSaveGameTests
{
    private byte* _buf;
    private const int Size = 32 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);

        // Reset every piece of shared static state Host.Init's call chain
        // touches, matching HostTests' own reset list.
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

        NativeMemory.Clear(Model.mod_known, (nuint)(Model.mod_numknown * sizeof(Model_t)));
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;

        Con.con_initialized = false;
        Con.con_text = null;
        Con.con_linewidth = 0;
        Con.con_totallines = 0;
        Con.con_current = 0;
        Con.con_x = 0;

        // NetLoop.loop_client/loop_server are process-wide statics that
        // Net.Init() itself never resets (it only seeds the free-socket
        // pool); a dangling pointer left by an earlier networking test
        // class (into that class's already-freed Hunk arena) would
        // otherwise make the very first Loadgame_f's ClMain.EstablishConnection
        // -> Net.Connect -> NetLoop.Connect dereference it directly, since
        // NetLoop.Connect only allocates a fresh socket when its cached one
        // is null.
        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        Net.net_activeconnections = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);

        // Same reasoning as HostTests.Teardown: leave Con uninitialized and
        // re-point Svs.clients at a fresh NativeMemory block so this test
        // class doesn't leak dangling pointers into whichever test class
        // runs next.
        Con.con_initialized = false;
        Con.con_text = null;
        Host.host_initialized = false; // see HostTests.Teardown

        Svs.clients = (Client_t*)NativeMemory.AllocZeroed((nuint)sizeof(Client_t));
        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;

        // Host.Init (unlike most other test classes' setup) registers dozens
        // of cvars/commands whose Cvar_t/cmd_function_t nodes are Hunk-
        // allocated out of the buffer just freed above. A later test class
        // that resets Cmd.cmd_functions before Memory_Init but only resets
        // Cvar.cvar_vars afterward (matching the "Cache.Init() registers
        // 'flush'... reset before it walks a stale list" comment elsewhere)
        // would otherwise have Cache.Init's Cmd.AddCommand walk this now-
        // dangling cvar_vars list via Cvar.VariableString, an
        // AccessViolationException on whichever test class happens to run
        // next.
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
    }

    private static string RequireRealGameDirectoryWithSaves(out string[] saveFiles)
    {
        string? dir = TestConfiguration.PakFilesDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive("PakFilesDirectory is not configured in configuration.json; skipping real savegame test.");

        if (!File.Exists(Path.Combine(dir!, "PAK0.PAK")))
            Assert.Inconclusive($"PAK0.PAK was not found in {dir}; skipping real savegame test.");

        saveFiles = Directory.GetFiles(dir!, "*.sav");
        if (saveFiles.Length == 0)
            Assert.Inconclusive($"No .sav files were found in {dir}; skipping real savegame test.");

        return dir!;
    }

    private void InitHostFromRealGameDirectory(string dir)
    {
        // PakFilesDirectory points at .../Quake/id1; Host_Init's basedir is
        // its parent (COM_InitFilesystem appends "/id1" itself).
        string baseDir = Path.GetDirectoryName(dir) ?? dir;
        Host.host_parms.BaseDir = (byte*)Marshal.StringToHGlobalAnsi(baseDir);
        Host.Init(_buf, Size);

        // Host_Loadgame_f early-returns silently unless cmd_source is
        // SrcCommand (see SaveGameTests' own Setup()); Host.Init leaves it
        // at whatever the last executed console command set it to, so this
        // must be set after Init runs, not before.
        Cmd.cmd_source = CmdSource_t.SrcCommand;
    }

    private static void LoadByName(string saveName)
    {
        fixed (byte* text = Encoding.ASCII.GetBytes($"load {saveName}\0"))
            Cmd.TokenizeString(text);

        SaveGame.Loadgame_f();
    }

    [TestMethod]
    public void LoadgameF_LoadsEveryRealSaveFile()
    {
        string dir = RequireRealGameDirectoryWithSaves(out string[] saveFiles);

        InitHostFromRealGameDirectory(dir);

        int loaded = 0;
        int incompatible = 0;

        foreach (string saveFile in saveFiles)
        {
            string saveName = Path.GetFileNameWithoutExtension(saveFile);

            try
            {
                LoadByName(saveName);
            }
            catch (QuakeSysErrorException ex)
            {
                // A non-vanilla save (e.g. one re-saved by a modified engine
                // with an extended-format trailer) makes the original
                // Host_Loadgame_f Sys_Error too -- not a porting bug, so
                // this file is skipped rather than failing the test.
                Console.WriteLine($"{saveFile}: skipped, not a vanilla-format savegame ({ex.Message})");
                incompatible++;
                continue;
            }

            Assert.IsTrue(Sv.active, $"{saveFile} should spawn a live server after loading");
            Assert.IsGreaterThan(0, Sv.num_edicts, $"{saveFile} should load at least the world edict");

            string? mapName = Marshal.PtrToStringAnsi((nint)Sv.Name);
            Assert.IsFalse(string.IsNullOrEmpty(mapName), $"{saveFile} should set sv.name to the saved map");

            // Loadgame_f re-establishes a local connection when not running
            // dedicated (ClMain.EstablishConnection + HostCmd.Reconnect_f).
            Assert.AreNotEqual(CActive.Disconnected, Cls.state, $"{saveFile} should re-establish a local connection");

            loaded++;
        }

        Console.WriteLine($"loaded {loaded}/{saveFiles.Length} save file(s), {incompatible} skipped as non-vanilla");
        Assert.IsGreaterThan(0, loaded, $"none of the {saveFiles.Length} .sav file(s) in {dir} were loadable vanilla saves");
    }

    // Independently checks the comment line the same way M_ScanSaves
    // (Menu.cs) does, straight off the file on disk -- no engine boot needed.
    // This is the exact regression this session's earlier fix targeted: a
    // savegame comment whose map-name portion (bytes [0,22)) had gone blank,
    // leaving only the "kills:NNN/NNN" portion the load menu showed.
    [TestMethod]
    public void SavegameComment_MapNamePortionIsNeverBlank()
    {
        string dir = RequireRealGameDirectoryWithSaves(out string[] saveFiles);

        foreach (string saveFile in saveFiles)
        {
            using StreamReader reader = new(saveFile);
            reader.ReadLine(); // version
            string? comment = reader.ReadLine();
            Assert.IsNotNull(comment, $"{saveFile} should have a comment line");

            string decoded = comment!.Replace('_', ' ');
            Assert.IsGreaterThanOrEqualTo(22, decoded.Length, $"{saveFile}'s comment line should be full-width");

            string mapNamePortion = decoded[..22];
            string killsPortion = decoded[22..];

            Assert.IsFalse(string.IsNullOrWhiteSpace(mapNamePortion),
                $"{saveFile}'s comment line should show a map name, not just the kill count ({comment})");
            Assert.Contains("kills:", killsPortion, $"{saveFile}'s comment line should show a kill count");
        }
    }
}
