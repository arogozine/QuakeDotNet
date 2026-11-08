// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// host_cmd.c's single-player debug/cheat commands (Host_God_f/Host_Notarget_f/
// Host_Noclip_f/Host_Fly_f/Host_Kill_f/Host_Pause_f/Host_Give_f/Host_Version_f)
// -- HostCmd.cs's own doc comment covers what's ported vs still missing.
// Shares Cmd/Cvar/Sv/Svs/Progs static state with the other DoNotParallelize
// classes touching them.
[TestClass]
public sealed unsafe class HostCmdTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private EDict_t* _player;
    private GlobalVars_t* _globals;
    private byte* _edicts;
    private const int MaxEdicts = 8;
    private readonly List<string> _tempGamedirs = new();

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
        Cmd.BufInit();

        // Run as if dispatched from a (int)Clc.StringCmd (the only path that
        // reaches these commands' real logic instead of Cmd_ForwardToServer).
        Cmd.cmd_source = CmdSource_t.SrcClient;

        _globals = (GlobalVars_t*)NativeMemory.AllocZeroed((nuint)sizeof(GlobalVars_t));
        Progs.pr_global_struct = _globals;
        Progs.pr_globals = (float*)_globals;
        Progs.pr_strings = (byte*)NativeMemory.AllocZeroed(64);

        _player = (EDict_t*)NativeMemory.AllocZeroed((nuint)sizeof(EDict_t));
        Sv.sv_player = _player;

        Svs.clients[0] = default;
        Svs.clients[0].Message.Data = (byte*)NativeMemory.AllocZeroed(QuakeDef.MAX_MSGLEN);
        Svs.clients[0].Message.MaxSize = QuakeDef.MAX_MSGLEN;
        Svs.host_client = &Svs.clients[0];

        Sv.paused = false;
        Host.pausable->Value = 1;

        *Sv.reliable_datagram = default;
        Sv.reliable_datagram->Data = (byte*)NativeMemory.AllocZeroed(1024);
        Sv.reliable_datagram->MaxSize = 1024;

        // FindViewthing/mcache/demo-loop state
        Progs.pr_edict_size = sizeof(EDict_t);
        _edicts = (byte*)NativeMemory.AllocZeroed((nuint)(Progs.pr_edict_size * MaxEdicts));
        Sv.edicts = (EDict_t*)_edicts;
        Sv.num_edicts = 0;
        Sv.max_edicts = MaxEdicts;
        NativeMemory.ClearPointerArray<Model_t>(Cl.model_precache, QuakeDef.MAX_MODELS);
        Com.com_searchpaths = null;
        NativeMemory.Clear(Model.mod_known, (nuint)(Model.mod_numknown * sizeof(Model_t)));
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;
        Model.Init();
        Cls.state = CActive.Disconnected;
        ClDemo.demonum = 0;
        ClDemo.demoplayback = false;
        ClDemo.demorecording = false;
        ClDemo.demofile = -1;
        NativeMemory.Clear(ClDemo.demos, ClDemo.MAX_DEMOS * ClDemo.MAX_DEMONAME);
    }

    [TestCleanup]
    public void Teardown()
    {
        // com_searchpaths is Hunk-allocated out of _buf -- walk it before
        // freeing _buf below (matches AliasModelTests's Teardown ordering).
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(Svs.clients[0].Message.Data);
        NativeMemory.Free(Sv.reliable_datagram->Data);
        NativeMemory.Free(_player);
        NativeMemory.Free(Progs.pr_strings);
        NativeMemory.Free(_globals);
        NativeMemory.Free(_edicts);
        NativeMemory.Free(_buf);

        foreach (string dir in _tempGamedirs)
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    // Builds a minimal synthetic IDPO/v6 .mdl with a single frame (mirrors
    // AliasModelTests.BuildMdlFile -- kept local since that one is private).
    private static byte[] BuildMdlFile()
    {
        const int numSkins = 1;
        const int numVerts = 3;
        const int numTris = 1;
        const int numFrames = 1;
        const int skinWidth = 4, skinHeight = 4;
        int skinSize = skinWidth * skinHeight;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Model.IDPOLYHEADER);
        bw.Write(AliasModel.ALIAS_VERSION);
        bw.Write(1f); bw.Write(1f); bw.Write(1f);
        bw.Write(0f); bw.Write(0f); bw.Write(0f);
        bw.Write(10f);
        bw.Write(0f); bw.Write(0f); bw.Write(0f);
        bw.Write(numSkins);
        bw.Write(skinWidth);
        bw.Write(skinHeight);
        bw.Write(numVerts);
        bw.Write(numTris);
        bw.Write(numFrames);
        bw.Write((int)SyncType_t.Sync);
        bw.Write(0);
        bw.Write(1f);

        bw.Write((int)AliasSkinType_t.Single);
        for (int i = 0; i < skinSize; i++)
            bw.Write((byte)(i & 0xFF));

        for (int i = 0; i < numVerts; i++)
        {
            bw.Write(0);
            bw.Write(i);
            bw.Write(0);
        }

        bw.Write(1);
        bw.Write(0); bw.Write(1); bw.Write(2);

        bw.Write((int)AliasFrameType_t.Single);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((byte)8); bw.Write((byte)8); bw.Write((byte)8); bw.Write((byte)0);
        byte[] nameBytes = new byte[16];
        Encoding.ASCII.GetBytes("frame0").CopyTo(nameBytes, 0);
        bw.Write(nameBytes);
        for (int i = 0; i < numVerts; i++)
        {
            bw.Write((byte)i); bw.Write((byte)i); bw.Write((byte)i);
            bw.Write((byte)0);
        }

        return ms.ToArray();
    }

    private string WriteMdlToGamedir(byte[] bytes, string name = "progs/test.mdl")
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, Path.GetDirectoryName(name) ?? ""));
        File.WriteAllBytes(Path.Combine(gamedir, name), bytes);
        _tempGamedirs.Add(gamedir);
        return gamedir;
    }

    // Spawns edict 1 as a "viewthing" classname'd entity in Sv.edicts/pr_strings,
    // matching FindViewthing's linear scan for classname == "viewthing".
    private EDict_t* SpawnViewthing()
    {
        byte* name = Progs.pr_strings + 32; // scratch region past index 0's empty string
        byte[] nameBytes = Encoding.ASCII.GetBytes("viewthing\0");
        for (int i = 0; i < nameBytes.Length; i++)
            name[i] = nameBytes[i];

        Sv.num_edicts = 2;
        EDict_t* e = Progs.EdictNum(1);
        e->V.ClassName = 32;
        return e;
    }

    // Swaps Svs.clients/Svs.maxclients out for a fresh, fully zeroed N-client
    // array (each with its own Message buffer and a real QSocket_t so
    // Status_f/DropClient's NetConnection dereferences are safe) for the
    // duration of `body`, then restores the single-client fixture Setup()
    // built -- so the base class's Teardown() still frees the right pointer.
    private static void WithClients(int count, Action body)
    {
        Client_t* savedClients = Svs.clients;
        int savedMax = Svs.maxclients;
        Client_t* extra = (Client_t*)NativeMemory.AllocZeroed((nuint)(count * sizeof(Client_t)));
        QSocket_t*[] sockets = new QSocket_t*[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                extra[i].Message.Data = (byte*)NativeMemory.AllocZeroed(QuakeDef.MAX_MSGLEN);
                extra[i].Message.MaxSize = QuakeDef.MAX_MSGLEN;
                sockets[i] = (QSocket_t*)NativeMemory.AllocZeroed((nuint)sizeof(QSocket_t));
                // Disconnected -- Net.Init (net_drivers[]) never runs in
                // these fixtures, so a "live" socket would call through a
                // null driver function pointer the moment DropClient/
                // Status_f touch it (Net.CanSendMessage/Net.Close both
                // short-circuit on Disconnected before reaching Sfunc()).
                sockets[i]->Disconnected = true;
                extra[i].NetConnection = sockets[i];
            }
            Svs.clients = extra;
            Svs.maxclients = count;
            Svs.host_client = &extra[0];

            body();
        }
        finally
        {
            for (int i = 0; i < count; i++)
            {
                NativeMemory.Free(extra[i].Message.Data);
                NativeMemory.Free(sockets[i]);
            }
            NativeMemory.Free(extra);
            Svs.clients = savedClients;
            Svs.maxclients = savedMax;
            Svs.host_client = &Svs.clients[0];
        }
    }

    // Decodes a Message buffer holding one or more back-to-back (int)Svc.Print
    // entries (Host.ClientPrintf's only output) into their concatenated text.
    private static string ReadAllPrints(SizeBuf_t* sb)
    {
        StringBuilder text = new();
        int pos = 0;
        while (pos < sb->CurSize)
        {
            Assert.AreEqual((byte)(int)Svc.Print, sb->Data[pos]);
            pos++;
            string s = Marshal.PtrToStringAnsi((nint)(sb->Data + pos)) ?? string.Empty;
            text.Append(s);
            pos += s.Length + 1;
        }
        return text.ToString();
    }

    private static string CaptureOutput(Action action)
    {
        TextWriter original = Console.Out;
        StringWriter sw = new();
        Console.SetOut(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    [TestMethod]
    public void God_f_TogglesFlagOn_ThenOff()
    {
        Assert.AreEqual(0f, _player->V.Flags);

        HostCmd.God_f();
        Assert.AreEqual(64f, _player->V.Flags); // FL_GODMODE

        HostCmd.God_f();
        Assert.AreEqual(0f, _player->V.Flags);
    }

    [TestMethod]
    public void Notarget_f_TogglesFlag()
    {
        HostCmd.Notarget_f();
        Assert.AreEqual(128f, _player->V.Flags); // FL_NOTARGET

        HostCmd.Notarget_f();
        Assert.AreEqual(0f, _player->V.Flags);
    }

    [TestMethod]
    public void Noclip_f_SetsMoveTypeAndAnglehack_ThenRestoresWalk()
    {
        Cl.noclip_anglehack = false;

        HostCmd.Noclip_f();
        Assert.AreEqual((float)(int)MoveType.NoClip, _player->V.MoveType);
        Assert.IsTrue(Cl.noclip_anglehack);

        HostCmd.Noclip_f();
        Assert.AreEqual((float)(int)MoveType.Walk, _player->V.MoveType);
        Assert.IsFalse(Cl.noclip_anglehack);
    }

    [TestMethod]
    public void Fly_f_SetsMoveType_ThenRestoresWalk()
    {
        HostCmd.Fly_f();
        Assert.AreEqual((float)(int)MoveType.Fly, _player->V.MoveType);

        HostCmd.Fly_f();
        Assert.AreEqual((float)(int)MoveType.Walk, _player->V.MoveType);
    }

    [TestMethod]
    public void God_f_Deathmatch_NonPrivilegedClient_IsNoOp()
    {
        Progs.pr_global_struct->DeathMatch = 1;
        Svs.host_client->Privileged = false;

        HostCmd.God_f();

        Assert.AreEqual(0f, _player->V.Flags);
    }

    [TestMethod]
    public void Kill_f_DeadPlayer_DoesNotExecuteClientKill()
    {
        _player->V.Health = 0;
        Progs.pr_global_struct->ClientKill = 7; // sentinel: should never be read as a function to run

        HostCmd.Kill_f();

        // No crash and no state change beyond the "already dead" message --
        // ExecuteProgram would throw/crash on function 7 with no real progs
        // loaded, so reaching here at all proves it wasn't called.
        Assert.AreEqual(0f, _player->V.Health);
    }

    [TestMethod]
    public void Pause_f_TogglesPausedAndWritesSetpauseToReliableDatagram()
    {
        Sv.paused = false;

        HostCmd.Pause_f();

        Assert.IsTrue(Sv.paused);
        Assert.AreEqual(2, Sv.reliable_datagram->CurSize); // (int)Svc.SetPause byte + on/off byte
        Assert.AreEqual((byte)(int)Svc.SetPause, Sv.reliable_datagram->Data[0]);
        Assert.AreEqual(1, Sv.reliable_datagram->Data[1]);
    }

    [TestMethod]
    public void Pause_f_NotPausable_PrintsMessage_DoesNotToggle()
    {
        Host.pausable->Value = 0;

        HostCmd.Pause_f();

        Assert.IsFalse(Sv.paused);
        Assert.AreEqual(0, Sv.reliable_datagram->CurSize);
    }

    [TestMethod]
    public void Give_f_HealthLetter_SetsHealth()
    {
        Cmd.TokenizeString(Ansi("give h 75\n"));

        HostCmd.Give_f();

        Assert.AreEqual(75f, _player->V.Health);
    }

    [TestMethod]
    public void Give_f_ShellsLetter_SetsAmmoShells()
    {
        Cmd.TokenizeString(Ansi("give s 30\n"));

        HostCmd.Give_f();

        Assert.AreEqual(30f, _player->V.AmmoShells);
    }

    [TestMethod]
    public void Give_f_WeaponDigit_OrsItemBit()
    {
        // '3' -> IT_SHOTGUN << (3 - '2') == IT_SUPER_SHOTGUN (2)
        Cmd.TokenizeString(Ansi("give 3 0\n"));

        HostCmd.Give_f();

        Assert.AreEqual(2f, _player->V.Items);
    }

    // Each Give_f_Rogue_* test below registers a single fake progs field
    // ("ammo_shells1"/"ammo_nails1"/etc.) resolving via
    // EdictSerialization.GetEdictFieldValue to an unused EntVars_t float
    // (DmgSave -- never touched by Give_f), mirroring how rogue's real
    // progs.dat defines these extra ammo-pool fields (see HostCmd.Give_f's
    // doc comment: GetEdictFieldValue resolves fields by name against
    // whatever progs.dat is loaded, same as the already-ported "items2"
    // lookup).
    [TestMethod]
    public void Give_f_Rogue_ShellsLetter_WritesRogueFieldAndAmmoShells()
    {
        fixed (DDef_t* fields = new DDef_t[2])
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes("ammo_shells1\0");
            Marshal.Copy(nameBytes, 0, (nint)Progs.pr_strings + 1, nameBytes.Length);
            int ofs = Marshal.OffsetOf<EntVars_t>(nameof(EntVars_t.DmgSave)).ToInt32() / 4;
            fields[1] = new DDef_t { Type = (ushort)EType_t.Float, Ofs = (ushort)ofs, S_Name = 1 };

            DPrograms_t progsHeader = default;
            progsHeader.NumFieldDefs = 2;
            Progs.progs = &progsHeader;
            Progs.pr_fielddefs = fields;
            Com.rogue = true;

            try
            {
                Cmd.TokenizeString(Ansi("give s 30\n"));

                HostCmd.Give_f();

                Assert.AreEqual(30f, _player->V.AmmoShells);
                Assert.AreEqual(30f, _player->V.DmgSave);
            }
            finally
            {
                Com.rogue = false;
                Progs.progs = null;
                Progs.pr_fielddefs = null;
            }
        }
    }

    [TestMethod]
    public void Give_f_Rogue_NailsLetter_WeaponAtOrBelowLightning_UpdatesLegacyAmmoNails()
    {
        fixed (DDef_t* fields = new DDef_t[2])
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes("ammo_nails1\0");
            Marshal.Copy(nameBytes, 0, (nint)Progs.pr_strings + 1, nameBytes.Length);
            int ofs = Marshal.OffsetOf<EntVars_t>(nameof(EntVars_t.DmgSave)).ToInt32() / 4;
            fields[1] = new DDef_t { Type = (ushort)EType_t.Float, Ofs = (ushort)ofs, S_Name = 1 };

            DPrograms_t progsHeader = default;
            progsHeader.NumFieldDefs = 2;
            Progs.progs = &progsHeader;
            Progs.pr_fielddefs = fields;
            Com.rogue = true;
            _player->V.Weapon = 64; // IT_LIGHTNING -- "<=" branch still counts as legacy pool

            try
            {
                Cmd.TokenizeString(Ansi("give n 20\n"));

                HostCmd.Give_f();

                Assert.AreEqual(20f, _player->V.DmgSave); // rogue's ammo_nails1
                Assert.AreEqual(20f, _player->V.AmmoNails); // legacy pool, weapon <= IT_LIGHTNING
            }
            finally
            {
                Com.rogue = false;
                Progs.progs = null;
                Progs.pr_fielddefs = null;
            }
        }
    }

    [TestMethod]
    public void Give_f_Rogue_NailsLetter_WeaponAboveLightning_LeavesLegacyAmmoNailsUntouched()
    {
        fixed (DDef_t* fields = new DDef_t[2])
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes("ammo_nails1\0");
            Marshal.Copy(nameBytes, 0, (nint)Progs.pr_strings + 1, nameBytes.Length);
            int ofs = Marshal.OffsetOf<EntVars_t>(nameof(EntVars_t.DmgSave)).ToInt32() / 4;
            fields[1] = new DDef_t { Type = (ushort)EType_t.Float, Ofs = (ushort)ofs, S_Name = 1 };

            DPrograms_t progsHeader = default;
            progsHeader.NumFieldDefs = 2;
            Progs.progs = &progsHeader;
            Progs.pr_fielddefs = fields;
            Com.rogue = true;
            _player->V.Weapon = 65; // above IT_LIGHTNING -- powered-up ammo pool
            _player->V.AmmoNails = 5;

            try
            {
                Cmd.TokenizeString(Ansi("give n 20\n"));

                HostCmd.Give_f();

                Assert.AreEqual(20f, _player->V.DmgSave); // rogue's ammo_nails1 still written
                Assert.AreEqual(5f, _player->V.AmmoNails); // legacy pool left alone
            }
            finally
            {
                Com.rogue = false;
                Progs.progs = null;
                Progs.pr_fielddefs = null;
            }
        }
    }

    [TestMethod]
    public void Version_f_PrintsVersionNumber()
    {
        string output = CaptureOutput(HostCmd.Version_f);

        StringAssert.Contains(output, "1.09");
    }

    [TestMethod]
    public void Viewmodel_f_NoViewthingOnMap_PrintsMessageAndDoesNothing()
    {
        Cmd.TokenizeString(Ansi("viewmodel progs/test.mdl\n"));

        string output = CaptureOutput(HostCmd.Viewmodel_f);

        StringAssert.Contains(output, "No viewthing on map");
    }

    [TestMethod]
    public void Viewmodel_f_LoadsModel_SetsPrecacheSlotAndResetsFrame()
    {
        EDict_t* e = SpawnViewthing();
        e->V.ModelIndex = 3;
        e->V.Frame = 5;
        string gamedir = WriteMdlToGamedir(BuildMdlFile());
        Com.AddGameDirectory(Ansi(gamedir));
        Cmd.TokenizeString(Ansi("viewmodel progs/test.mdl\n"));

        HostCmd.Viewmodel_f();

        Assert.AreEqual(0f, e->V.Frame);
        Assert.IsTrue((nint)Cl.model_precache[3] != 0);
        Assert.AreEqual(ModType_t.Alias, Cl.model_precache[3]->Type);
    }

    [TestMethod]
    public void Viewmodel_f_UnknownModel_PrintsCantLoad()
    {
        SpawnViewthing();
        // no search paths added (Com.com_searchpaths is null) -- ForName can't find it
        Cmd.TokenizeString(Ansi("viewmodel progs/missing.mdl\n"));

        string output = CaptureOutput(HostCmd.Viewmodel_f);

        StringAssert.Contains(output, "Can't load");
    }

    [TestMethod]
    public void Viewframe_ViewnextViewprev_ClampToModelFrameRange()
    {
        EDict_t* e = SpawnViewthing();
        e->V.ModelIndex = 1;
        string gamedir = WriteMdlToGamedir(BuildMdlFile());
        Com.AddGameDirectory(Ansi(gamedir));
        Cmd.TokenizeString(Ansi("viewmodel progs/test.mdl\n"));
        HostCmd.Viewmodel_f();

        // synthetic model only has 1 frame (index 0), so viewnext/viewframe
        // both clamp to numframes-1 == 0
        Cmd.TokenizeString(Ansi("viewframe 5\n"));
        HostCmd.Viewframe_f();
        Assert.AreEqual(0f, e->V.Frame);

        string nextOutput = CaptureOutput(HostCmd.Viewnext_f);
        Assert.AreEqual(0f, e->V.Frame);
        StringAssert.Contains(nextOutput, "frame0");

        string prevOutput = CaptureOutput(HostCmd.Viewprev_f);
        Assert.AreEqual(0f, e->V.Frame);
        StringAssert.Contains(prevOutput, "frame0");
    }

    [TestMethod]
    public void Mcache_Print_ListsLoadedModelWithName()
    {
        string gamedir = WriteMdlToGamedir(BuildMdlFile());
        Com.AddGameDirectory(Ansi(gamedir));
        Model.ForName(Ansi("progs/test.mdl"), true);

        string output = CaptureOutput(Model.Print);

        StringAssert.Contains(output, "Cached models:");
        StringAssert.Contains(output, "progs/test.mdl");
    }

    [TestMethod]
    public void Startdemos_f_Dedicated_ReturnsEarly_DoesNotTouchDemoList()
    {
        Cls.state = CActive.Dedicated;
        Sv.active = false;
        ClDemo.demonum = 0;
        Cmd.TokenizeString(Ansi("startdemos demo1 demo2\n"));

        HostCmd.Startdemos_f();

        // The dedicated branch returns before Host_Startdemos_f's normal
        // fill-in-demos-and-NextDemo logic runs (host_cmd.c).
        Assert.AreEqual(0, ClDemo.demonum);
        Assert.AreEqual(0, *ClDemo.demos);
    }

    [TestMethod]
    public void Startdemos_f_FillsDemoListAndStartsLoop()
    {
        Sv.active = false;
        ClDemo.demonum = 0;
        ClDemo.demoplayback = false;
        Cmd.TokenizeString(Ansi("startdemos demo1 demo2\n"));

        string output = CaptureOutput(HostCmd.Startdemos_f);

        StringAssert.Contains(output, "2 demo(s) in loop");
        Assert.AreEqual(0, Q.StrCmp(ClDemo.demos, Ansi("demo1")));
        Assert.AreEqual(0, Q.StrCmp(ClDemo.demos + ClDemo.MAX_DEMONAME, Ansi("demo2")));
        Assert.AreEqual(1, ClDemo.demonum); // NextDemo() incremented it past demo0
    }

    [TestMethod]
    public void Startdemos_f_ServerActive_StopsLoop()
    {
        Sv.active = true;
        Cmd.TokenizeString(Ansi("startdemos demo1\n"));

        HostCmd.Startdemos_f();

        Assert.AreEqual(-1, ClDemo.demonum);
    }

    [TestMethod]
    public void Stopdemo_f_NotPlayingBack_IsNoOp()
    {
        ClDemo.demoplayback = false;

        HostCmd.Stopdemo_f(); // must not throw

        Assert.IsFalse(ClDemo.demoplayback);
    }

    [TestMethod]
    public void Map_f_BuildsClsMapstringFromArgv()
    {
        Host.host_hunklevel = 0;
        Sv.active = false;
        ClDemo.demoplayback = false;
        ClDemo.demonum = 0;

        *Host.skill = default;
        Host.skill->Name = Ansi("skill");
        Host.skill->String = Ansi("1");
        Cvar.RegisterVariable(Host.skill);
        *Net.hostname = default;
        Net.hostname->Name = Ansi("hostname");
        Net.hostname->String = Ansi("UNNAMED");
        Cvar.RegisterVariable(Net.hostname);

        Cmd.TokenizeString(Ansi("map somemap\n"));

        // No real progs.dat in this fixture, so SV_SpawnServer throws inside
        // Progs.LoadProgs -- but only *after* Host_Map_f has already built
        // cls.mapstring from Cmd_Argv, so that's still a valid checkpoint.
        Assert.ThrowsExactly<QuakeSysErrorException>(HostCmd.Map_f);

        Assert.AreEqual("map somemap \n", Marshal.PtrToStringAnsi((nint)Cls.mapstring));
    }

    [TestMethod]
    public void Changelevel_f_ServerActive_SurvivesSendReconnectsNestedTokenize()
    {
        // Regression test: with sv.active already true (an in-progress level,
        // not a fresh "map" command), SV_SpawnServer's own "if (active)
        // SendReconnect()" fires and calls Cmd_ExecuteString("reconnect\n"),
        // a NESTED Cmd_TokenizeString that frees/reallocates every
        // cmd_argv[] slot -- including the one holding "somemap", the
        // argument this test's "changelevel somemap" command is still
        // reading. Host_Changelevel_f must defensively copy Cmd_Argv(1) into
        // its own buffer before that happens (see its own comment); without
        // that copy, sv.name ends up whatever reused that freed Zone block
        // instead of "somemap" (surfaced in the field as a garbage map name
        // like "1.000000", e.g. "FindFile: can't find maps/1.000000.bsp").
        Host.host_hunklevel = 0;
        Sv.active = true;
        ClDemo.demoplayback = false;

        // SV_SpawnServer's very first line reads Net.hostname->String[0],
        // and its "make cvars consistant" step re-derives current_skill
        // from Host.skill->Value -- both need a real registered cvar here,
        // same as SaveGameTests's own Setup (Net.Init/Host.InitLocal
        // register these for real in the actual game).
        *Host.skill = default;
        Host.skill->Name = Ansi("skill");
        Host.skill->String = Ansi("1");
        Cvar.RegisterVariable(Host.skill);
        *Net.hostname = default;
        Net.hostname->Name = Ansi("hostname");
        Net.hostname->String = Ansi("UNNAMED");
        Cvar.RegisterVariable(Net.hostname);

        Cmd.TokenizeString(Ansi("changelevel somemap\n"));

        // No real progs.dat in this fixture, so SV_SpawnServer throws inside
        // Progs.LoadProgs -- but only *after* it has already copied this
        // test's defensively-copied level name into sv.name (Sv.cs's own
        // SpawnServer sets Name before LoadProgs runs), so that's still a
        // valid checkpoint to assert against.
        Assert.ThrowsExactly<QuakeSysErrorException>(HostCmd.Changelevel_f);

        Assert.AreEqual("somemap", Marshal.PtrToStringAnsi((nint)Sv.Name));
    }

    [TestMethod]
    public void Status_f_FromConsole_PrintsHostInfoAndConnectedClient()
    {
        // src_command + sv.active routes Host_Status_f's `print` local to
        // Con_Printf (the src_client branch instead routes it through
        // SV_ClientPrintf into a client's own message buffer -- Say/Tell/Kick
        // below cover that path).
        Cmd.cmd_source = CmdSource_t.SrcCommand;
        Sv.active = true;
        Sv.Name = Ansi("e1m1");
        *Net.hostname = default;
        Net.hostname->Name = Ansi("hostname");
        Net.hostname->String = Ansi("MyServer");
        Cvar.RegisterVariable(Net.hostname);

        Net.net_activeconnections = 1;

        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));
            Svs.clients[0].Edict = _player;
            _player->V.Frags = 3;
            Svs.clients[0].NetConnection->ConnectTime = Net.net_time - 65; // 0:01:05
            Q.StrCpy(Svs.clients[0].NetConnection->Address, Ansi("127.0.0.1"));

            string output = CaptureOutput(HostCmd.Status_f);

            StringAssert.Contains(output, "MyServer");
            StringAssert.Contains(output, "e1m1");
            StringAssert.Contains(output, "Ranger");
            StringAssert.Contains(output, "127.0.0.1");
            StringAssert.Contains(output, "1 active (2 max)");
        });
    }

    [TestMethod]
    public void Status_f_ConnectedUnderTenHours_PadsHoursToWidthTwo()
    {
        // host_cmd.c Host_Status_f: "#%-2u %-16.16s  %3i  %2i:%02i:%02i\n"
        Cmd.cmd_source = CmdSource_t.SrcCommand;
        Sv.active = true;
        Sv.Name = Ansi("e1m1");
        Net.net_activeconnections = 1;

        WithClients(1, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));
            Svs.clients[0].Edict = _player;
            _player->V.Frags = 3;
            Svs.clients[0].NetConnection->ConnectTime = Net.net_time - 65; // 0:01:05
            Q.StrCpy(Svs.clients[0].NetConnection->Address, Ansi("127.0.0.1"));

            string output = CaptureOutput(HostCmd.Status_f);

            StringAssert.Contains(output, "#1  Ranger            " + "  3   0:01:");
        });
    }

    [TestMethod]
    public void Status_f_ConnectedOverTenHours_PrintsHoursWithoutPadding()
    {
        Cmd.cmd_source = CmdSource_t.SrcCommand;
        Sv.active = true;
        Sv.Name = Ansi("e1m1");
        Net.net_activeconnections = 1;

        WithClients(1, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));
            Svs.clients[0].Edict = _player;
            Svs.clients[0].NetConnection->ConnectTime = Net.net_time - (11 * 3600 + 65);
            Q.StrCpy(Svs.clients[0].NetConnection->Address, Ansi("127.0.0.1"));

            string output = CaptureOutput(HostCmd.Status_f);

            StringAssert.Contains(output, "  11:01:");
        });
    }

    [TestMethod]
    public void Status_f_FromConsole_NoServer_ForwardsToServer()
    {
        Cmd.cmd_source = CmdSource_t.SrcCommand;
        Sv.active = false;

        // No real client connection exists to forward over -- reaching here
        // without throwing proves Cmd_ForwardToServer's early-return path
        // (not the real print logic) is what ran.
        HostCmd.Status_f();
    }

    [TestMethod]
    public void Ping_f_PrintsAveragePingForActiveClients()
    {
        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));
            for (int i = 0; i < Client_t.NUM_PING_TIMES; i++)
                Svs.clients[0].PingTimes[i] = 0.05f; // 50ms average
            Svs.host_client = &Svs.clients[1]; // the requester

            HostCmd.Ping_f();

            string text = ReadAllPrints(&Svs.clients[1].Message);
            StringAssert.Contains(text, "Client ping times:");
            StringAssert.Contains(text, "50 Ranger");
        });
    }

    [TestMethod]
    public void Say_f_BroadcastsToAllSpawnedClientsAndEchoesLocally()
    {
        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Svs.clients[0].Spawned = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));
            Svs.clients[0].Edict = _player;

            Svs.clients[1].Active = true;
            Svs.clients[1].Spawned = true;
            Q.StrCpy(Svs.clients[1].Name, Ansi("Grunt"));
            Svs.clients[1].Edict = _player;

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("say hello world\n"));

            string localEcho = CaptureOutput(HostCmd.Say_f);

            StringAssert.Contains(localEcho, "Ranger: hello world");
            StringAssert.Contains(ReadAllPrints(&Svs.clients[0].Message), "Ranger: hello world");
            StringAssert.Contains(ReadAllPrints(&Svs.clients[1].Message), "Ranger: hello world");
            // host_client must be restored to the sender afterward
            Assert.IsTrue(Svs.host_client == &Svs.clients[0]);
        });
    }

    [TestMethod]
    public void Say_Team_f_Teamplay_OnlySendsToSameTeam()
    {
        Host.teamplay->Value = 1;
        EDict_t* otherEdict = (EDict_t*)NativeMemory.AllocZeroed((nuint)sizeof(EDict_t));

        try
        {
            WithClients(2, () =>
            {
                Svs.clients[0].Active = true;
                Svs.clients[0].Spawned = true;
                Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));
                Svs.clients[0].Edict = _player;
                _player->V.Team = 1;

                otherEdict->V.Team = 2;
                Svs.clients[1].Active = true;
                Svs.clients[1].Spawned = true;
                Q.StrCpy(Svs.clients[1].Name, Ansi("Grunt"));
                Svs.clients[1].Edict = otherEdict;

                Svs.host_client = &Svs.clients[0];
                Cmd.TokenizeString(Ansi("say_team teammates only\n"));

                HostCmd.Say_Team_f();

                StringAssert.Contains(ReadAllPrints(&Svs.clients[0].Message), "teammates only");
                Assert.AreEqual(0, Svs.clients[1].Message.CurSize);
            });
        }
        finally
        {
            Host.teamplay->Value = 0;
            NativeMemory.Free(otherEdict);
        }
    }

    [TestMethod]
    public void Tell_f_DeliversOnlyToNamedClient()
    {
        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Svs.clients[0].Spawned = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));

            Svs.clients[1].Active = true;
            Svs.clients[1].Spawned = true;
            Q.StrCpy(Svs.clients[1].Name, Ansi("Grunt"));

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("tell Grunt psst\n"));

            HostCmd.Tell_f();

            // Host_Tell_f's `p = Cmd_Args()` captures everything after "tell"
            // -- including the target name argument -- so the recipient's
            // own name ends up folded into the delivered message too; a
            // faithful quirk of the original, not a bug in this port.
            StringAssert.Contains(ReadAllPrints(&Svs.clients[1].Message), "Ranger: Grunt psst");
            Assert.AreEqual(0, Svs.clients[0].Message.CurSize);
            // host_client must be restored to the sender afterward
            Assert.IsTrue(Svs.host_client == &Svs.clients[0]);
        });
    }

    // The bytes of the single svc_print message in sb, without the opcode or NUL.
    private static byte[] ReadPrintBytes(SizeBuf_t* sb)
    {
        Assert.AreEqual((byte)(int)Svc.Print, sb->Data[0]);
        int len = Q.StrLen(sb->Data + 1);
        Assert.AreEqual(len + 2, sb->CurSize);
        return new ReadOnlySpan<byte>(sb->Data + 1, len).ToArray();
    }

    private static byte* RawBytes(params byte[] bytes)
    {
        byte* p = (byte*)NativeMemory.AllocZeroed((nuint)bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    [TestMethod]
    public void Say_f_KeepsHighBitCharacters()
    {
        // Quake's coloured/gold characters (0x80+) in the name and the text
        // must reach clients byte-for-byte, not narrowed to '?'.
        WithClients(1, () =>
        {
            Svs.clients[0].Active = true;
            Svs.clients[0].Spawned = true;
            Svs.clients[0].Edict = _player;
            Svs.clients[0].Name[0] = 0xD2; // gold 'R'
            Svs.clients[0].Name[1] = (byte)'x';
            Svs.clients[0].Name[2] = 0;

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(RawBytes((byte)'s', (byte)'a', (byte)'y', (byte)' ', 0xE8, 0xE9, 0xA0, (byte)'!'));

            HostCmd.Say_f();

            CollectionAssert.AreEqual(
                new byte[] { 0x01, 0xD2, (byte)'x', (byte)':', (byte)' ', 0xE8, 0xE9, 0xA0, (byte)'!', (byte)'\n' },
                ReadPrintBytes(&Svs.clients[0].Message));
        });
    }

    [TestMethod]
    public void Say_f_TruncatesTheLineToTheOriginals64ByteBuffer()
    {
        WithClients(1, () =>
        {
            Svs.clients[0].Active = true;
            Svs.clients[0].Spawned = true;
            Svs.clients[0].Edict = _player;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("say " + new string('a', 200)));

            HostCmd.Say_f();

            byte[] line = ReadPrintBytes(&Svs.clients[0].Message);
            // char text[64]: 63 chars + NUL, the last of them '\n' ("\001Ranger: " is 9 bytes)
            Assert.AreEqual(63, line.Length);
            Assert.AreEqual((byte)'\n', line[^1]);
            Assert.AreEqual(53, line.Skip(9).Count(b => b == (byte)'a')); // past the prefix
        });
    }

    [TestMethod]
    public void Say_f_FromServer_LongHostname_DoesNotOverflow()
    {
        Cmd.cmd_source = CmdSource_t.SrcCommand;
        Cls.state = CActive.Dedicated;
        *Net.hostname = default;
        Net.hostname->Name = Ansi("hostname");
        Net.hostname->String = Ansi(new string('h', 300));
        Cvar.RegisterVariable(Net.hostname);

        WithClients(1, () =>
        {
            Svs.clients[0].Active = true;
            Svs.clients[0].Spawned = true;
            Svs.clients[0].Edict = _player;
            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("say hi"));

            HostCmd.Say_f();

            byte[] line = ReadPrintBytes(&Svs.clients[0].Message);
            Assert.IsLessThanOrEqualTo(63, line.Length);
            Assert.AreEqual((byte)'\n', line[^1]);
        });
    }

    [TestMethod]
    public void Tell_f_KeepsHighBitCharacters()
    {
        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Svs.clients[0].Spawned = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Ranger"));

            Svs.clients[1].Active = true;
            Svs.clients[1].Spawned = true;
            Q.StrCpy(Svs.clients[1].Name, Ansi("Grunt"));

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(RawBytes((byte)'t', (byte)'e', (byte)'l', (byte)'l', (byte)' ',
                (byte)'G', (byte)'r', (byte)'u', (byte)'n', (byte)'t', (byte)' ', 0xE8, 0xE9));

            HostCmd.Tell_f();

            byte[] line = ReadPrintBytes(&Svs.clients[1].Message);
            Assert.AreEqual(0xE9, line[^2]);
            Assert.AreEqual(0xE8, line[^3]);
            Assert.AreEqual((byte)'\n', line[^1]);
        });
    }

    [TestMethod]
    public void Kick_f_ByName_DropsMatchingClient()
    {
        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Admin"));
            Svs.clients[1].Active = true;
            Svs.clients[1].Spawned = false; // skip ClientDisconnect's progs call
            Q.StrCpy(Svs.clients[1].Name, Ansi("Grunt"));

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("kick Grunt\n"));

            HostCmd.Kick_f();

            Assert.IsFalse(Svs.clients[1].Active);
            // host_client must be restored to the kicker afterward
            Assert.IsTrue(Svs.host_client == &Svs.clients[0]);
        });
    }

    [TestMethod]
    public void Kick_f_ByNumber_DropsMatchingClient()
    {
        WithClients(2, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Admin"));
            Svs.clients[1].Active = true;
            Svs.clients[1].Spawned = false;
            Q.StrCpy(Svs.clients[1].Name, Ansi("Grunt"));

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("kick # 2\n"));

            HostCmd.Kick_f();

            Assert.IsFalse(Svs.clients[1].Active);
        });
    }

    [TestMethod]
    public void Kick_f_CannotKickSelf()
    {
        WithClients(1, () =>
        {
            Svs.clients[0].Active = true;
            Q.StrCpy(Svs.clients[0].Name, Ansi("Admin"));

            Svs.host_client = &Svs.clients[0];
            Cmd.TokenizeString(Ansi("kick Admin\n"));

            HostCmd.Kick_f();

            Assert.IsTrue(Svs.clients[0].Active);
        });
    }
}
