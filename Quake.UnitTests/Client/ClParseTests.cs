// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// cl_parse.c's ClParse.cs. Decodes the svc_* bytes SV_SendClientMessages
// (SvSendTests.cs) produces.
// Shares Cl/Cls/ClEntities' static state with nothing else yet (this is the
// first test class to touch it), but still isolates itself the same way the
// other DoNotParallelize classes do in case a future class shares it.
[TestClass]
public sealed unsafe class ClParseTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;
    private byte* _colormap;
    private byte* _netMessageBuf;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);

        Cvar.cvar_vars = null;

        Cl.num_entities = 0;
        Cl.num_statics = 0;
        Cl.maxclients = 4;
        Cl.mtime[0] = 0;
        Cl.mtime[1] = 0;
        Cl.onground = true;
        new Span<int>(Cl.stats, QuakeDef.MAX_CL_STATS).Clear();
        Cl.items = 0;
        new Span<ScoreBoard_t>(Cl.scores, QuakeDef.MAX_SCOREBOARD).Clear();
        Cl.cl_shownet->Value = 0;

        Cls.state = CActive.Connected;
        Cls.signon = 0;
        Cls.netcon = null;
        Sz.Clear(Cls.message);

        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;
        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_numsockets = 0;
        Net.net_activeconnections = 0;
        Net.net_driverlevel = 0;
        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        NetLoop.localconnectpending = false;

        Sv.active = true; // short-circuits KeepaliveMessage's live-network path

        _colormap = (byte*)NativeMemory.AllocZeroed((nuint)(Vid.VID_GRADES * 256));
        Vid.vid.Colormap = _colormap;

        new Span<Entity_t>(ClEntities.cl_entities, QuakeDef.MAX_EDICTS).Clear();
        new Span<Entity_t>(ClEntities.cl_static_entities, ClEntities.MAX_STATIC_ENTITIES).Clear();

        _netMessageBuf = (byte*)NativeMemory.AllocZeroed(2048);
        Net.net_message->Data = _netMessageBuf; Net.net_message->MaxSize = 2048;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_colormap);
        NativeMemory.Free(_netMessageBuf);
        NativeMemory.Free(_buf);
    }

    private static void SetMessage(params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            Msg.net_message->Data[i] = bytes[i];
        Msg.net_message->CurSize = bytes.Length;
    }

    [TestMethod]
    public void EntityNum_GrowsNumEntitiesAndSetsColormap()
    {
        Entity_t* ent = ClParse.EntityNum(5);

        Assert.AreEqual(6, Cl.num_entities);
        Assert.IsTrue(ent->ColorMap == _colormap);
        Assert.IsTrue(ent == &ClEntities.cl_entities[5]);
    }

    [TestMethod]
    public void ParseServerMessage_Nop_LeavesStateUnchanged()
    {
        SetMessage((byte)(int)Svc.Nop);

        ClParse.ParseServerMessage();

        Assert.IsFalse(Cl.onground); // reset unconditionally at the top of ParseServerMessage
    }

    [TestMethod]
    public void ParseServerMessage_Time_UpdatesMtime()
    {
        Cl.mtime[0] = 5.0;

        byte[] timeBytes = BitConverter.GetBytes(10.0f);
        SetMessage((byte)(int)Svc.Time, timeBytes[0], timeBytes[1], timeBytes[2], timeBytes[3]);

        ClParse.ParseServerMessage();

        Assert.AreEqual(5.0, Cl.mtime[1]);
        Assert.AreEqual(10.0, Cl.mtime[0], 0.0001);
    }

    [TestMethod]
    public void ParseServerMessage_SignonNum_WritesPrespawnToClsMessage()
    {
        SetMessage((byte)(int)Svc.SignOnNum, 1);

        ClParse.ParseServerMessage();

        Assert.AreEqual(1, Cls.signon);
        Assert.IsGreaterThan(0, Cls.message->CurSize);
        Assert.AreEqual((byte)(int)Clc.StringCmd, Cls.message->Data[0]);
    }

    [TestMethod]
    public void ParseServerMessage_Disconnect_SetsStateDisconnected()
    {
        // (int)Svc.Disconnect now runs the real Host.EndGame (host.c's Host_EndGame),
        // which needs a registered netcon to close and no demo loop queued so
        // it takes the plain ClMain.Disconnect() branch, matching a real
        // connected-to-a-remote-server client.
        Sv.active = false;
        ClDemo.demonum = -1;
        Net.Init();
        QSocket_t* clientSock = Net.Connect(new Utf8String("local"u8));
        Assert.IsTrue(clientSock != null);
        Assert.IsTrue(Net.CheckNewConnections() != null);
        Cls.netcon = clientSock;

        SetMessage((byte)(int)Svc.Disconnect);

        // Host_EndGame's setjmp(host_abortserver)/longjmp equivalent -- see
        // Host.QuakeHostAbortException's own comment.
        Assert.ThrowsExactly<Host.QuakeHostAbortException>(ClParse.ParseServerMessage);

        Assert.AreEqual(CActive.Disconnected, Cls.state);
        Assert.AreEqual(0, Cls.signon);
    }

    [TestMethod]
    public void ParseServerMessage_SpawnBaseline_PopulatesEntityBaseline()
    {
        // (int)Svc.SpawnBaseline, entnum(short)=3, modelindex, frame, colormap, skin,
        // then 3x (coord(short) + angle(byte))
        byte[] entnum = BitConverter.GetBytes((short)3);
        SetMessage(
            (byte)(int)Svc.SpawnBaseline, entnum[0], entnum[1],
            7, 2, 0, 0, // modelindex, frame, colormap, skin
            8, 0, 0,    // origin[0] coord = 1.0 (8/8), angle[0] = 0
            0, 0, 0,
            0, 0, 0);

        ClParse.ParseServerMessage();

        Entity_t* ent = &ClEntities.cl_entities[3];
        Assert.AreEqual(7, ent->Baseline.ModelIndex);
        Assert.AreEqual(2, ent->Baseline.Frame);
        Assert.AreEqual(1.0f, ent->Baseline.Origin[0]);
        Assert.AreEqual(4, Cl.num_entities); // EntityNum(3) forces num_entities up to 4
    }

    [TestMethod]
    public void ParseClientdata_UpdatesHealthAndItems()
    {
        byte[] itemsBytes = BitConverter.GetBytes(5);
        byte[] healthBytes = BitConverter.GetBytes((short)80);
        SetMessage(
            itemsBytes[0], itemsBytes[1], itemsBytes[2], itemsBytes[3], // items (always sent, no bit)
            healthBytes[0], healthBytes[1], // health
            10); // ammo

        // ParseClientdata reads sequentially from msg_readcount 0; call
        // BeginReading directly (bypassing the (int)Svc.ClientData dispatch byte)
        // to exercise it in isolation, matching CL_ParseClientdata's own
        // "bits" parameter contract (no SU_* bits set here).
        Msg.BeginReading();
        ClParse.ParseClientdata(0);

        Assert.AreEqual(5, Cl.items);
        Assert.AreEqual(80, Cl.stats[(int)Stat.Health]);
        Assert.AreEqual(10, Cl.stats[(int)Stat.Ammo]);
        Assert.AreEqual(Protocol.DEFAULT_VIEWHEIGHT, Cl.viewheight);
    }

    [TestMethod]
    public void NewTranslation_CopiesColormapIntoScoreTranslations()
    {
        _colormap[0] = 0xAB;
        Cl.scores[0].Colors = 0x10; // top=0x10 (<128), bottom=0

        ClParse.NewTranslation(0);

        Assert.AreEqual((byte)0xAB, Cl.scores[0].Translations[0]);
    }

    [TestMethod]
    public void ParseStatic_AddsStaticEntityAndIncrementsCount()
    {
        // (int)Svc.SpawnStatic: modelindex, frame, colormap, skin, then 3x(coord+angle)
        SetMessage(
            (byte)(int)Svc.SpawnStatic,
            0, 3, 0, 0, // modelindex(0 -> no model, so AddEfrags no-ops), frame, colormap, skin
            16, 0, 0,   // origin[0] coord = 2.0
            0, 0, 0,
            0, 0, 0);

        ClParse.ParseServerMessage();

        Assert.AreEqual(1, Cl.num_statics);
        Entity_t* ent = &ClEntities.cl_static_entities[0];
        Assert.AreEqual(3, ent->Baseline.Frame);
        Assert.AreEqual(2.0f, ent->Origin[0]);
    }

    // ---- PROTOCOL_FITZQUAKE (Mark V cl_parse.c) ----

    [TestMethod]
    public void ParseUpdate_Fitz_AlphaFrame2AndLerpFinish_AreRead()
    {
        Cl.protocol = Protocol.PROTOCOL_FITZQUAKE;
        // fast update: U_MOREBITS|U_FRAME, then bits>>8 = U_EXTEND1>>8, then bits>>16 = U_ALPHA|U_FRAME2|U_LERPFINISH
        SetMessage(
            0x80 | Protocol.U_MOREBITS | Protocol.U_FRAME,
            (byte)(Protocol.U_EXTEND1 >> 8),
            (byte)((Protocol.U_ALPHA | Protocol.U_FRAME2 | Protocol.U_LERPFINISH) >> 16),
            5,    // entnum
            44,   // frame low byte
            200,  // alpha
            1,    // frame high byte
            255); // lerpfinish -> +1.0s

        ClParse.ParseServerMessage();

        Entity_t* ent = &ClEntities.cl_entities[5];
        Assert.AreEqual(300, ent->Frame);
        Assert.AreEqual(200, ent->Alpha);
        Assert.AreEqual(1.0f, ent->LerpFinish, 0.0001f);
        Assert.AreNotEqual(0, ent->LerpFlags & LerpFlags.FINISH);
    }

    [TestMethod]
    public void ParseUpdate_Fitz_NoAlphaBit_UsesBaselineAlpha()
    {
        Cl.protocol = Protocol.PROTOCOL_FITZQUAKE;
        ClParse.EntityNum(6)->Baseline.Alpha = 77;
        SetMessage(0x80 | Protocol.U_MOREBITS, 0, 6);

        ClParse.ParseServerMessage();

        Assert.AreEqual(77, ClEntities.cl_entities[6].Alpha);
    }

    [TestMethod]
    public void ParseUpdate_Netquake_IgnoresExtendBitsAndFitzFields()
    {
        Cl.protocol = Protocol.PROTOCOL_NETQUAKE;
        // bit 15 is not U_EXTEND1 at protocol 15: no extra bits byte follows
        SetMessage(0x80 | Protocol.U_MOREBITS | Protocol.U_FRAME, (byte)(Protocol.U_EXTEND1 >> 8), 5, 44);

        ClParse.ParseServerMessage();

        Assert.AreEqual(44, ClEntities.cl_entities[5].Frame);
        Assert.AreEqual(4, Msg.msg_readcount); // whole message consumed
    }

    [TestMethod]
    public void ParseServerMessage_SpawnBaseline2_ReadsLargeFrameAndAlpha()
    {
        byte[] entnum = BitConverter.GetBytes((short)3);
        SetMessage(
            (byte)(int)Svc.SpawnBaseline2, entnum[0], entnum[1],
            (byte)(Protocol.B_LARGEMODEL | Protocol.B_LARGEFRAME | Protocol.B_ALPHA),
            0x2C, 0x01, // modelindex 300
            0x2D, 0x01, // frame 301
            0, 0,       // colormap, skin
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            128);       // alpha

        ClParse.ParseServerMessage();

        Entity_t* ent = &ClEntities.cl_entities[3];
        Assert.AreEqual(300, ent->Baseline.ModelIndex);
        Assert.AreEqual(301, ent->Baseline.Frame);
        Assert.AreEqual(128, ent->Baseline.Alpha);
    }

    [TestMethod]
    public void ParseServerMessage_SpawnBaseline2_NoBits_AlphaIsDefault()
    {
        byte[] entnum = BitConverter.GetBytes((short)3);
        ClParse.EntityNum(3)->Baseline.Alpha = 99;
        SetMessage(
            (byte)(int)Svc.SpawnBaseline2, entnum[0], entnum[1], 0,
            7, 2, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0);

        ClParse.ParseServerMessage();

        Assert.AreEqual(Protocol.ENTALPHA_DEFAULT, ClEntities.cl_entities[3].Baseline.Alpha);
    }

    [TestMethod]
    public void ParseServerMessage_SpawnStatic2_ReadsAlpha()
    {
        SetMessage(
            (byte)(int)Svc.SpawnStatic2, (byte)Protocol.B_ALPHA,
            0, 3, 0, 0,
            16, 0, 0, 0, 0, 0, 0, 0, 0,
            50);

        ClParse.ParseServerMessage();

        Assert.AreEqual(1, Cl.num_statics);
        Assert.AreEqual(50, ClEntities.cl_static_entities[0].Alpha);
        Assert.AreEqual(2.0f, ClEntities.cl_static_entities[0].Origin[0]);
    }

    [TestMethod]
    public void ParseServerMessage_SpawnStaticSound2_ReadsShortSoundNumber()
    {
        // 3 coords, short sound (300), vol, atten -- Cl.sound_precache[300] is null, which StaticSound ignores
        SetMessage((byte)(int)Svc.SpawnStaticSound2, 0, 0, 0, 0, 0, 0, 0x2C, 0x01, 255, 64);

        ClParse.ParseServerMessage();

        Assert.AreEqual(11, Msg.msg_readcount);
    }

    [TestMethod]
    public void ParseServerMessage_SoundLargeEntityAndSound_ReadsAllFields()
    {
        // field_mask, short ent, byte channel, short sound, 3 coords
        byte[] ent = BitConverter.GetBytes((short)9000);
        SetMessage(
            (byte)(int)Svc.Sound, (byte)(Protocol.SND_LARGEENTITY | Protocol.SND_LARGESOUND),
            ent[0], ent[1], 2,
            0x2C, 0x01,
            0, 0, 0, 0, 0, 0);

        ClParse.ParseServerMessage();

        Assert.AreEqual(13, Msg.msg_readcount);
    }

    [TestMethod]
    public void ParseServerMessage_Fog_StoresValues()
    {
        SetMessage((byte)(int)Svc.Fog, 255, 51, 102, 153, 100, 0);

        ClParse.ParseServerMessage();

        Assert.AreEqual(1.0f, Cl.fog_density, 0.001f);
        Assert.AreEqual(0.2f, Cl.fog_red, 0.001f);
        Assert.AreEqual(0.4f, Cl.fog_green, 0.001f);
        Assert.AreEqual(0.6f, Cl.fog_blue, 0.001f);
        Assert.AreEqual(1.0f, Cl.fog_time, 0.001f);
    }

    [TestMethod]
    public void ParseClientdata_Fitz_HighBytesAndWeaponAlpha()
    {
        // bits as seen after the short: SU_EXTEND1|SU_ARMOR; then ext1 (SU_ARMOR2 | SU_EXTEND2), ext2 (SU_WEAPONALPHA)
        byte[] items = BitConverter.GetBytes(0);
        byte[] health = BitConverter.GetBytes((short)100);
        SetMessage(
            (byte)((Protocol.SU_ARMOR2 | Protocol.SU_EXTEND2) >> 16),
            (byte)(Protocol.SU_WEAPONALPHA >> 24),
            items[0], items[1], items[2], items[3],
            0x34,                  // armor low byte
            health[0], health[1],
            0, 0, 0, 0, 0,        // ammo, shells, nails, rockets, cells
            0,                     // active weapon
            1,                     // armor high byte
            200);                  // weapon alpha

        Msg.BeginReading();
        ClParse.ParseClientdata(Protocol.SU_EXTEND1 | Protocol.SU_ARMOR);

        Assert.AreEqual(0x134, Cl.stats[(int)Stat.Armor]);
        Assert.AreEqual(200, Cl.viewent->Alpha);
        Assert.IsFalse(Msg.msg_badread);
    }

    [TestMethod]
    public void ParseClientdata_NoWeaponAlphaBit_ResetsViewentAlpha()
    {
        Cl.viewent->Alpha = 123;
        byte[] items = BitConverter.GetBytes(0);
        SetMessage(items[0], items[1], items[2], items[3], 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Msg.BeginReading();
        ClParse.ParseClientdata(0);

        Assert.AreEqual(Protocol.ENTALPHA_DEFAULT, Cl.viewent->Alpha);
    }
}
