// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// SaveGame/EdictSerialization/Progs/Cmd/Com share static state, so, like the
// other loader tests, these can't run concurrently with anything else
// touching them.
[TestClass]
public sealed unsafe class SaveGameTests
{
    private byte* _buf;
    private const int Size = 2 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;

        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
        Cmd.cmd_source = CmdSource_t.SrcCommand;

        // Sv.SpawnServer re-derives SaveGame.current_skill straight from
        // Host.skill->Value (its own "make cvars consistant" step, matching
        // the original's SV_SpawnServer), so Host_Loadgame_f's
        // Cvar.SetValue("skill", ...) needs Host.skill itself registered
        // under that name to round trip through it, same as Host.InitLocal
        // registers in the real game.
        *Host.skill = default;
        Host.skill->Name = Ansi("skill");
        Host.skill->String = Ansi("1");
        Cvar.RegisterVariable(Host.skill);

        // Sv.SpawnServer's very first line reads Net.hostname->String[0], so
        // that also needs a real (non-null) String, same as Net.Init()
        // registers in the real game.
        *Net.hostname = default;
        Net.hostname->Name = Ansi("hostname");
        Net.hostname->String = Ansi("UNNAMED");
        Cvar.RegisterVariable(Net.hostname);

        // Loadgame_f now reaches the real Sv.SpawnServer -> Host.ClearMemory,
        // which walks DSurf's surface-cache list and Model's known-model
        // table; both are Hunk-backed statics tied to whichever arena was
        // active when a previous test class populated them, so they must be
        // reset here too (same reasoning as AliasModelTests/RealPakFileTests'
        // Model resets) or they dangle into this test's freed arena.
        DSurf.sc_base = null;
        DLocal.sc_rover = null;
        NativeMemory.Clear(Model.mod_known, Model.mod_numknown);
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;

        // Sv.SpawnServer's Host.ClearMemory() calls Hunk.FreeToLowMark
        // (host_hunklevel), and Sv.SpawnServer itself checks `if (active)
        // SendReconnect()` before that -- both process-wide statics a
        // previous test class may have left non-default (host_hunklevel
        // from a larger Hunk than this test's own MemoryManager.Memory_Init
        // just reset hunk_low_used against, which makes FreeToLowMark's own
        // "mark > hunk_low_used" guard throw before SpawnServer ever reaches
        // Progs.LoadProgs -- the failure this test is actually about).
        Host.host_hunklevel = 0;
        Sv.active = false;

        // Loadgame_f's Sv.SpawnServer path routes through
        // ClMain.Disconnect_f -> ClMain.Disconnect(), which only touches
        // Cls.netcon (Net.SendUnreliableMessage/Net.Close) when
        // Cls.state == CActive.Connected. A previous test class that established
        // a real loopback connection and left Cls.state == CActive.Connected
        // without resetting it would make this dereference that class's own
        // Cls.netcon -- already closed/freed by that class's Teardown.
        Cls.state = CActive.Disconnected;
        Cls.netcon = null;

        // reset SaveGame's scoped state
        SaveGame.current_skill = 0;
        Sv.Name = null;
        Sv.time = 0;
        NativeMemory.ClearPointerArray<byte>(Sv.lightstyles, QuakeDef.MAX_LIGHTSTYLES);
        Sv.num_edicts = 0;
        NativeMemory.Clear(Cl.levelname, Cl.LEVELNAME_LENGTH);
        new Span<int>(Cl.stats, QuakeDef.MAX_CL_STATS).Clear();

        // a minimal progs setup so ED_WriteGlobals/ED_Write have something
        // (empty) to walk without crashing
        Progs.progs = NativeMemory.AllocZeroed<DPrograms_t>();
        Progs.pr_strings = NativeMemory.AllocArrayZeroed<byte>(16);
        Progs.pr_fielddefs = NativeMemory.AllocZeroed<DDef_t>();
        Progs.pr_globaldefs = NativeMemory.AllocZeroed<DDef_t>();
        Progs.pr_edict_size = 4;
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
        {
            if (s->Pack != null)
            {
                Sys.FileClose(s->Pack->Handle);
            }
        }

        NativeMemory.Free(Progs.progs);
        NativeMemory.Free(Progs.pr_strings);
        NativeMemory.Free(Progs.pr_fielddefs);
        NativeMemory.Free(Progs.pr_globaldefs);

        NativeMemory.Free(_buf);
    }

    private static string TempGameDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void SavegameF_WritesVersionCommentSpawnParmsSkillMapnameTimeAndLightstyles()
    {
        string gamedir = TempGameDir();
        bool oldActive = Sv.active;
        int oldIntermission = Cl.intermission;
        int oldMaxclients = Svs.maxclients;
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            // Savegame_f's sv.active/cl.intermission/svs.maxclients guards
            // (Host_Savegame_f) must all pass for the save to proceed.
            Sv.active = true;
            Cl.intermission = 0;
            Svs.maxclients = 1;

            Svs.clients->spawn_parms[0] = 1f;
            Svs.clients->spawn_parms[1] = 2f;
            SaveGame.current_skill = 1;
            Sv.Name = Ansi("start");
            Sv.time = 12.5f;
            Sv.lightstyles[0] = Ansi("aaaaaaaa");

            fixed (byte* text = Encoding.ASCII.GetBytes("save mysave\0"))
            {
                Cmd.TokenizeString(text);
            }

            SaveGame.Savegame_f();

            string[] lines = File.ReadAllLines(Path.Combine(gamedir, "mysave.sav"));

            Assert.AreEqual(SaveGame.SAVEGAME_VERSION.ToString(), lines[0]);
            // lines[1] is the 39-char comment
            Assert.AreEqual(1f.ToString("F6", System.Globalization.CultureInfo.InvariantCulture), lines[2]);
            Assert.AreEqual(2f.ToString("F6", System.Globalization.CultureInfo.InvariantCulture), lines[3]);
            // lines[4..17] are the remaining 14 zeroed spawn_parms
            Assert.AreEqual("1", lines[18]);
            Assert.AreEqual("start", lines[19]);
            Assert.AreEqual(12.5f.ToString("F6", System.Globalization.CultureInfo.InvariantCulture), lines[20]);
            Assert.AreEqual("aaaaaaaa", lines[21]);
            Assert.AreEqual("m", lines[22]); // lightstyles[1] defaulted to "m"

            // globals block ("{" then "}") immediately follows the 64 lightstyle lines
            Assert.AreEqual("{", lines[21 + 64]);
            Assert.AreEqual("}", lines[22 + 64]);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
            Sv.active = oldActive;
            Cl.intermission = oldIntermission;
            Svs.maxclients = oldMaxclients;
        }
    }

    [TestMethod]
    public void SavegameF_TimeIsWrittenAtDoublePrecision()
    {
        // host_cmd.c Host_Savegame_f: fprintf (f, "%f\n", sv.time) -- sv.time
        // is a double, so a float cast would write 1234.568115.
        string gamedir = TempGameDir();
        bool oldActive = Sv.active;
        int oldIntermission = Cl.intermission;
        int oldMaxclients = Svs.maxclients;
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            Sv.active = true;
            Cl.intermission = 0;
            Svs.maxclients = 1;
            Sv.Name = Ansi("start");
            Sv.time = 1234.56789;

            fixed (byte* text = Encoding.ASCII.GetBytes("save mysave\0"))
            {
                Cmd.TokenizeString(text);
            }

            SaveGame.Savegame_f();

            string[] lines = File.ReadAllLines(Path.Combine(gamedir, "mysave.sav"));
            Assert.AreEqual("1234.567890", lines[20]);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
            Sv.active = oldActive;
            Cl.intermission = oldIntermission;
            Svs.maxclients = oldMaxclients;
        }
    }

    [TestMethod]
    public void SavegameF_WritesRealEdictsNotAnEmptyStandIn()
    {
        string gamedir = TempGameDir();
        bool oldActive = Sv.active;
        int oldIntermission = Cl.intermission;
        int oldMaxclients = Svs.maxclients;
        int oldNumEdicts = Sv.num_edicts;
        int oldMaxEdicts = Sv.max_edicts;
        EDict_t* oldEdicts = Sv.edicts;
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            Sv.active = true;
            Cl.intermission = 0;
            Svs.maxclients = 1;

            // Sv.num_edicts/Sv.edicts/Progs.EdictNum(i) are the real edict
            // store; Savegame_f must read straight off these instead of the
            // always-empty stand-in arrays this file used to keep.
            Sv.max_edicts = 2;
            Sv.edicts = (EDict_t*)NativeMemory.AllocArrayZeroed<byte>(2 * Progs.pr_edict_size);
            Sv.num_edicts = 2;
            EDict_t* ent = Progs.EdictNum(1);
            ent->Free = 0;

            fixed (byte* text = Encoding.ASCII.GetBytes("save mysave\0"))
            {
                Cmd.TokenizeString(text);
            }

            SaveGame.Savegame_f();

            string contents = File.ReadAllText(Path.Combine(gamedir, "mysave.sav"));
            int globalsClose = contents.IndexOf("\n}\n", StringComparison.Ordinal);
            string afterGlobals = contents[(globalsClose + 3)..];

            // one "{"/"}" edict block per Sv.num_edicts entry, past the globals block
            Assert.AreEqual(2, afterGlobals.Split('{').Length - 1);
            Assert.AreEqual(2, afterGlobals.Split('}').Length - 1);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
            Sv.active = oldActive;
            Cl.intermission = oldIntermission;
            Svs.maxclients = oldMaxclients;
            NativeMemory.Free(Sv.edicts);
            Sv.edicts = oldEdicts;
            Sv.num_edicts = oldNumEdicts;
            Sv.max_edicts = oldMaxEdicts;
        }
    }

    [TestMethod]
    public void SavegameF_RejectsRelativePathnames()
    {
        fixed (byte* text = Encoding.ASCII.GetBytes("save ../evil\0"))
        {
            Cmd.TokenizeString(text);
        }

        SaveGame.Savegame_f();

        // no exception, no file written -- nothing further to assert without
        // a filesystem probe, so this just confirms it returns cleanly
    }

    [TestMethod]
    public void LoadgameF_ParsesHeader_ThenThrowsWithoutARealProgsDat()
    {
        string gamedir = TempGameDir();
        bool oldActive = Sv.active;
        int oldIntermission = Cl.intermission;
        int oldMaxclients = Svs.maxclients;
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            // Savegame_f's sv.active/cl.intermission/svs.maxclients guards
            // (Host_Savegame_f) must all pass for the save to proceed.
            Sv.active = true;
            Cl.intermission = 0;
            Svs.maxclients = 1;

            // write a save file through Savegame_f, then read it back through
            // Loadgame_f -- a genuine round trip of the header fields
            Svs.clients->spawn_parms[3] = 9.5f;
            SaveGame.current_skill = 2;
            Sv.Name = Ansi("e1m1");
            Sv.time = 42f;

            fixed (byte* text = Encoding.ASCII.GetBytes("save roundtrip\0"))
            {
                Cmd.TokenizeString(text);
            }

            SaveGame.Savegame_f();

            // reset observable state before reloading
            SaveGame.current_skill = 0;
            Sv.Name = null;
            Sv.time = 0;

            fixed (byte* text = Encoding.ASCII.GetBytes("load roundtrip\0"))
            {
                Cmd.TokenizeString(text);
            }

            // Loadgame_f now calls the real Sv.SpawnServer, which needs a
            // real progs.dat/maps/e1m1.bsp to actually spawn a level; this
            // synthetic test fixture has neither, so SpawnServer's own
            // Progs.LoadProgs call fails exactly like it would for any
            // other missing-game-data scenario.
            Assert.ThrowsExactly<QuakeSysErrorException>(() => SaveGame.Loadgame_f());

            Assert.AreEqual(2, SaveGame.current_skill);
            Assert.AreEqual("e1m1", Marshal.PtrToStringAnsi((nint)Sv.Name));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
            Sv.active = oldActive;
            Cl.intermission = oldIntermission;
            Svs.maxclients = oldMaxclients;
        }
    }

    [TestMethod]
    public void LoadgameF_WrongVersion_PrintsMessage_DoesNotThrow()
    {
        string gamedir = TempGameDir();
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            File.WriteAllText(Path.Combine(gamedir, "bad.sav"), "99\ncomment\n");

            fixed (byte* text = Encoding.ASCII.GetBytes("load bad\0"))
            {
                Cmd.TokenizeString(text);
            }

            SaveGame.Loadgame_f(); // should return quietly, not throw
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }
}
