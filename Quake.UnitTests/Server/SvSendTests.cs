// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// sv_main.c's frame-update/message-sending block (SV_SendServerinfo/
// SV_ConnectClient/SV_CreateBaseline/SV_SendClientDatagram/
// SV_SendClientMessages/Host_DropClient). Shares Sv/Progs/Net/World's static
// state with the other server-side test classes, so it can't run
// concurrently with anything else touching that state.
[TestClass]
public sealed unsafe class SvSendTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private List<nint> _allocations = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        _allocations = [];

        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;
        Svs.clients[0] = default;
        Svs.serverflags = 0;

        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_numsockets = 0;
        Net.net_activeconnections = 0;
        Net.net_driverlevel = 0;
        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        NetLoop.localconnectpending = false;

        Sv.Init();
        Host.deathmatch->Value = 0;
        Host.coop->Value = 0;

        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        // A single "" string at pr_strings[0], enough for every string_t
        // field this file's code paths read when left at its zero default
        // (v.message, v.model, v.weaponmodel, ...).
        Progs.pr_strings = (byte*)NativeMemory.AllocZeroed(64);
        _allocations.Add((nint)Progs.pr_strings);

        Sv.edicts = AllocZeroed<EDict_t>(4);
        Sv.num_edicts = 2; // world + the one reserved client slot
        Sv.max_edicts = 4;
        Sv.time = 1.0;

        NativeMemory.ClearPointerArray<byte>(Sv.model_precache, QuakeDef.MAX_MODELS);
        NativeMemory.ClearPointerArray<byte>(Sv.sound_precache, QuakeDef.MAX_SOUNDS);
        Sv.model_precache[0] = Progs.pr_strings;
        Sv.sound_precache[0] = Progs.pr_strings;

        SizeBuf_t* sig = Sv.signon;
            Sz.Alloc(sig, 8192);
        SizeBuf_t* dg = Sv.datagram;
            Sz.Alloc(dg, QuakeDef.MAX_DATAGRAM);
        SizeBuf_t* rdg = Sv.reliable_datagram;
            Sz.Alloc(rdg, QuakeDef.MAX_DATAGRAM);
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
            NativeMemory.Free((void*)p);
        NativeMemory.Free(_buf);
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged
    {
        T* p = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
        _allocations.Add((nint)p);
        return p;
    }

    // Wires up a single trivial QuakeC function (First_Statement -> one
    // Op_t.Done statement, no parms/locals) so PR_ExecuteProgram can run
    // pr_global_struct->SetNewParms/ClientDisconnect without a real
    // progs.dat, same approach as MainLoopTests.SetupTrivialProgs.
    private void SetupTrivialProgs()
    {
        DPrograms_t* progs = AllocZeroed<DPrograms_t>(1);
        progs->NumFunctions = 2;
        Progs.progs = progs;

        DFunction_t* functions = AllocZeroed<DFunction_t>(2);
        functions[1].First_Statement = 1;

        DStatement_t* statements = AllocZeroed<DStatement_t>(2);
        statements[1].Op = (ushort)Op_t.Done;

        Progs.pr_functions = functions;
        Progs.pr_statements = statements;

        Progs.pr_global_struct->SetNewParms = 1;
        Progs.pr_global_struct->ClientDisconnect = 1;
    }

    // ConnectClient (called by every test below except the SendServerinfo
    // ones) writes straight into client->Message via client->MsgBuf; the
    // Message.Data/MaxSize/AllowOverflow wiring itself is normally done by
    // ConnectClient, but calling SendServerinfo directly (as these two tests
    // do) skips that setup, so it's done here instead.
    private static void WireMessageBuffer(Client_t* client)
    {
        client->Message.Data = client->MsgBuf;
        client->Message.MaxSize = QuakeDef.MAX_MSGLEN;
        client->Message.AllowOverflow = true;
    }

    [TestMethod]
    public void SendServerinfo_WritesLeadingSvcPrintAndSetsSignonFlags()
    {
        Client_t* client = &Svs.clients[0];
        client->Edict = Progs.EdictNum(1);
        WireMessageBuffer(client);

        Sv.SendServerinfo(client);

        Assert.IsGreaterThan(0, client->Message.CurSize);
        Assert.AreEqual((byte)(int)Svc.Print, client->MsgBuf[0]);
        Assert.IsTrue(client->SendSignon);
        Assert.IsFalse(client->Spawned);
    }

    [TestMethod]
    public void SendServerinfo_DeathmatchWithoutCoop_WritesGameDeathmatch()
    {
        Host.deathmatch->Value = 1;
        Client_t* client = &Svs.clients[0];
        client->Edict = Progs.EdictNum(1);
        WireMessageBuffer(client);

        Sv.SendServerinfo(client);

        // Decode just enough of the message by hand to find the game-type
        // byte: (int)Svc.Print + string, (int)Svc.ServerInfo(1) + long(4) + byte(1),
        // then the game-type byte.
        byte* data = client->MsgBuf;
        int i = 1; // past (int)Svc.Print
        while (data[i] != 0) i++; // version string
        i++; // NUL
        i++; // (int)Svc.ServerInfo
        i += 4; // protocol version long
        i += 1; // maxclients byte
        Assert.AreEqual((byte)(int)GameType.Deathmatch, data[i]);
    }

    [TestMethod]
    public void CreateBaseline_PopulatesBaselineAndWritesToSignonBuffer()
    {
        EDict_t* world = Progs.EdictNum(0);
        world->V.Origin[0] = 12;
        world->V.Angles[1] = 45;
        world->V.ModelIndex = 1;

        Sv.CreateBaseline();

        Assert.AreEqual(12f, world->Baseline.Origin[0]);
        Assert.AreEqual(45f, world->Baseline.Angles[1]);
        Assert.IsGreaterThan(0, Sv.signon->CurSize);
        Assert.AreEqual((byte)(int)Svc.SpawnBaseline, Sv.signon->Data[0]);
    }

    [TestMethod]
    public void CreateBaseline_Fitz_LargeFrameAndAlpha_WritesSpawnBaseline2()
    {
        Sv.current_protocol = Protocol.PROTOCOL_FITZQUAKE;
        EDict_t* world = Progs.EdictNum(0);
        world->V.ModelIndex = 1;
        world->V.Frame = 300;
        world->Alpha = 128; // no progs loaded: UpdateEntAlpha keeps ent->Alpha

        Sv.CreateBaseline();

        byte* d = Sv.signon->Data;
        Assert.AreEqual((byte)(int)Svc.SpawnBaseline2, d[0]);
        Assert.AreEqual(0, d[1] | d[2]);          // entnum 0
        Assert.AreEqual(Protocol.B_LARGEFRAME | Protocol.B_ALPHA, d[3]);
        Assert.AreEqual(0, d[4]);                 // modelindex byte ("" model -> 0)
        Assert.AreEqual(300, d[5] | (d[6] << 8)); // frame short
        Assert.AreEqual(128, d[18]); // alpha trails the first message (19 bytes)
    }

    [TestMethod]
    public void CreateBaseline_Netquake_ResetsLargeFrame()
    {
        Sv.current_protocol = Protocol.PROTOCOL_NETQUAKE;
        EDict_t* world = Progs.EdictNum(0);
        world->V.ModelIndex = 1;
        world->V.Frame = 300;
        world->Alpha = 128;

        Sv.CreateBaseline();

        Assert.AreEqual((byte)(int)Svc.SpawnBaseline, Sv.signon->Data[0]);
        Assert.AreEqual(0, world->Baseline.Frame);
        Assert.AreEqual(Protocol.ENTALPHA_DEFAULT, world->Baseline.Alpha);
    }

    [TestMethod]
    [DataRow(Protocol.PROTOCOL_FITZQUAKE, 0x134, 0x201)]
    [DataRow(Protocol.PROTOCOL_NETQUAKE, 0x34, 0x01)]
    public void WriteClientdataToMessage_RoundTripsThroughParseClientdata(int protocol, int expectedArmor, int expectedAmmo)
    {
        Sv.current_protocol = protocol;
        EDict_t* ent = Progs.EdictNum(1);
        ent->V.ArmorValue = 0x134;
        ent->V.CurrentAmmo = 0x201;
        ent->V.Health = 90;
        Sv.sv_player = ent; // SV_SetIdealPitch reads it
        DPrograms_t* oldProgs = Progs.progs;
        DPrograms_t* fakeProgs = (DPrograms_t*)NativeMemory.AllocZeroed((nuint)sizeof(DPrograms_t)); // no field defs
        Progs.progs = fakeProgs;
        ent->Alpha = 0;

        byte* buf = stackalloc byte[256];
        SizeBuf_t msg = default;
        msg.Data = buf;
        msg.MaxSize = 256;
        Sv.WriteClientdataToMessage(ent, &msg);

        byte* netBuf = (byte*)NativeMemory.AllocZeroed(256);
        try
        {
            Net.net_message->Data = netBuf; Net.net_message->MaxSize = 256;
            new Span<byte>(buf, msg.CurSize).CopyTo(new Span<byte>(netBuf, msg.CurSize));
            Msg.net_message->CurSize = msg.CurSize;
            Msg.BeginReading();

            Assert.AreEqual((int)Svc.ClientData, Msg.ReadByte());
            int bits = Msg.ReadShort() & 0xFFFF;
            ClParse.ParseClientdata(bits);

            Assert.AreEqual(msg.CurSize, Msg.msg_readcount); // every byte the server wrote was consumed
            Assert.AreEqual(expectedArmor, Cl.stats[(int)Stat.Armor]);
            Assert.AreEqual(expectedAmmo, Cl.stats[(int)Stat.Ammo]);
            Assert.AreEqual(90, Cl.stats[(int)Stat.Health]);
        }
        finally
        {
            Progs.progs = oldProgs;
            NativeMemory.Free(fakeProgs);
            NativeMemory.Free(netBuf);
        }
    }

    [TestMethod]
    public void CreateBaseline_SkipsFreeEdicts()
    {
        EDict_t* ent = Progs.EdictNum(1);
        ent->Free = 1;
        ent->V.ModelIndex = 1;
        ent->V.Origin[0] = 999;

        Sv.CreateBaseline();

        Assert.AreEqual(0f, ent->Baseline.Origin[0]); // never touched
    }

    [TestMethod]
    public void ConnectClient_ThenSendClientMessages_DeliversReliableSignonOverLoopback()
    {
        SetupTrivialProgs();
        Net.Init();
        QSocket_t* clientSock = Net.Connect(Ansi("local"));
        Assert.IsTrue(clientSock != null);
        QSocket_t* serverSock = Net.CheckNewConnections();
        Assert.IsTrue(serverSock != null);

        Svs.clients[0].NetConnection = serverSock;
        Sv.ConnectClient(0);

        Assert.IsTrue(Svs.clients[0].Active);
        Assert.IsTrue(Svs.clients[0].SendSignon);
        Assert.IsGreaterThan(0, Svs.clients[0].Message.CurSize);

        Sv.SendClientMessages();

        // the client side of the loopback pair should now have received the
        // signon message reliably.
        int result = Net.GetMessage(clientSock);
        Assert.AreEqual(1, result);
        Assert.IsGreaterThan(0, Net.net_message->CurSize);
        Assert.AreEqual((byte)(int)Svc.Print, Net.net_message->Data[0]);

        // SendClientMessages cleared the outgoing buffer and the signon flag
        // once the reliable send succeeded.
        Assert.AreEqual(0, Svs.clients[0].Message.CurSize);
        Assert.IsFalse(Svs.clients[0].SendSignon);
    }

    [TestMethod]
    public void DropClient_MarksInactiveAndClosesConnection()
    {
        SetupTrivialProgs();
        Net.Init();
        QSocket_t* clientSock = Net.Connect(Ansi("local"));
        QSocket_t* serverSock = Net.CheckNewConnections();
        Svs.clients[0].NetConnection = serverSock;
        Sv.ConnectClient(0);
        Net.net_activeconnections = 1;

        Svs.host_client = &Svs.clients[0];
        Host.DropClient(true); // crash path: skip the disconnect message/ClientDisconnect call

        Assert.IsFalse(Svs.clients[0].Active);
        Assert.AreEqual(-999999, Svs.clients[0].OldFrags);
        Assert.AreEqual(0, Net.net_activeconnections);
    }
}
