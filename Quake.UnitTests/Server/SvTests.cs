// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// Sv/World/Progs share static state, so, like SvPhysTests/ProgsTests, these
// can't run concurrently with anything else touching that state.
[TestClass]
public sealed unsafe class SvTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private List<nint> _allocations = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        _allocations = [];

        Cvar.cvar_vars = null;
        Sv.Init();
        Sv.loadgame = false;

        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        // Two edicts: [0] the world, [1] a generic test entity.
        Sv.edicts = AllocZeroed<EDict_t>(2);
        Sv.num_edicts = 2;
        Sv.max_edicts = 2;

        SetupOpenWorld();

        // Svs.clients is reallocated fresh every test (rather than reusing
        // whatever the static field currently points at) so a previous
        // test's Svs.AllocateClients()-style resize never leaves this test
        // writing into memory that Teardown already freed.
        Svs.maxclientslimit = 1;
        Svs.maxclients = 1;
        Svs.clients = AllocZeroed<Client_t>(1);

        Host.host_frametime = 0.1f;
        Sv.time = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
            NativeMemory.Free((void*)p);
        NativeMemory.Free(_buf);
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged => TestHelpers.AllocZeroed<T>(_allocations, count);

    // Builds a worldmodel whose collision hulls report (int)Contents.Empty for
    // every point -- see SvPhysTests.cs's own copy of this fixture for the
    // full rationale.
    private void SetupOpenWorld()
    {
        DClipNode_t* clipnodes = AllocZeroed<DClipNode_t>(1);
        clipnodes[0].PlaneNum = 0;
        clipnodes[0].Children[0] = (int)Contents.Empty;
        clipnodes[0].Children[1] = (int)Contents.Empty;

        MPlane_t* planes = AllocZeroed<MPlane_t>(1);
        planes[0].Normal[0] = 1;
        planes[0].Type = 0;
        planes[0].Dist = 0;

        Model_t* worldmodel = AllocZeroed<Model_t>(1);
        worldmodel->Type = ModType_t.Brush;
        for (int i = 0; i < BspFile.MAX_MAP_HULLS; i++)
        {
            worldmodel->Hulls[i].ClipNodes = clipnodes;
            worldmodel->Hulls[i].Planes = planes;
            worldmodel->Hulls[i].FirstClipNode = 0;
            worldmodel->Hulls[i].LastClipNode = 0;
        }

        Sv.worldmodel = worldmodel;
        Sv.models[0] = worldmodel;

        Sv.edicts[0].V.Solid = (int)SolidType.Bsp;
        Sv.edicts[0].V.MoveType = (int)MoveType.Push;
        Sv.edicts[0].V.ModelIndex = 0;

        World.ClearWorld();
    }

    private EDict_t* TestEnt => Progs.EdictNum(1);

    // A minimal loopback-style QSocket_t, just enough for ConnectClient's
    // Con.DPrintf(client->NetConnection->Address) call.
    private QSocket_t* MakeSocket()
    {
        QSocket_t* sock = AllocZeroed<QSocket_t>(1);
        Q.StrCpy(sock->Address, TestHelpers.Ansi("localhost"));
        return sock;
    }

    [TestMethod]
    public void ModelIndex_NamePresent_ReturnsItsIndex()
    {
        Sv.model_precache[0] = TestHelpers.Ansi("maps/start.bsp");
        Sv.model_precache[1] = TestHelpers.Ansi("progs/player.mdl");

        Assert.AreEqual(1, Sv.ModelIndex(TestHelpers.Ansi("progs/player.mdl")));
    }

    [TestMethod]
    public void ModelIndex_NullOrEmptyName_ReturnsZero()
    {
        Assert.AreEqual(0, Sv.ModelIndex(null));
        Assert.AreEqual(0, Sv.ModelIndex(TestHelpers.Ansi("")));
    }

    [TestMethod]
    public void ModelIndex_NameNotFound_ReturnsZero()
    {
        Sv.model_precache[0] = TestHelpers.Ansi("maps/start.bsp");

        Assert.AreEqual(0, Sv.ModelIndex(TestHelpers.Ansi("progs/missing.mdl")));
    }

    [TestMethod]
    public void ClearDatagram_ResetsCurSizeToZero()
    {
        byte* dataBuf = stackalloc byte[64];
        *Sv.datagram = new SizeBuf_t { Data = dataBuf, MaxSize = 64 };
        SizeBuf_t* dg = Sv.datagram;
        Msg.WriteByte(dg, 42);
        Assert.AreNotEqual(0, Sv.datagram->CurSize);

        Sv.ClearDatagram();

        Assert.AreEqual(0, Sv.datagram->CurSize);
    }

    [TestMethod]
    public void StartParticle_WritesSvcParticleToDatagram()
    {
        byte* dataBuf = stackalloc byte[1024];
        *Sv.datagram = new SizeBuf_t { Data = dataBuf, MaxSize = 1024 };

        Vec3_t org = default;
        org[0] = 1; org[1] = 2; org[2] = 3;
        Vec3_t dir = default;
        dir[0] = 1; // *16 = 16, well within [-128,127]

        Sv.StartParticle(org, dir, 5, 10);

        byte* readBuf = stackalloc byte[1024];
        new Span<byte>(Sv.datagram->Data, Sv.datagram->CurSize).CopyTo(new Span<byte>(readBuf, Sv.datagram->CurSize));
        Net.net_message->Data = readBuf; Net.net_message->MaxSize = Sv.datagram->CurSize; Msg.net_message->CurSize = Sv.datagram->CurSize;
        Msg.BeginReading();

        Assert.AreEqual((int)Svc.Particle, Msg.ReadByte());
        Assert.AreEqual(1f, Msg.ReadCoord());
        Assert.AreEqual(2f, Msg.ReadCoord());
        Assert.AreEqual(3f, Msg.ReadCoord());
        Assert.AreEqual(16, Msg.ReadChar()); // dir[0]*16
        Assert.AreEqual(0, Msg.ReadChar());
        Assert.AreEqual(0, Msg.ReadChar());
        Assert.AreEqual(10, Msg.ReadByte()); // count
        Assert.AreEqual(5, Msg.ReadByte());  // color
    }

    [TestMethod]
    public void StartParticle_DatagramNearlyFull_EarlyReturnsWithoutWriting()
    {
        byte* dataBuf = stackalloc byte[1024];
        *Sv.datagram = new SizeBuf_t { Data = dataBuf, MaxSize = 1024, CurSize = QuakeDef.MAX_DATAGRAM - 15 };
        int before = Sv.datagram->CurSize;
        int oldMax = Sv.host_protocol_datagram_maxsize;
        Sv.host_protocol_datagram_maxsize = QuakeDef.MAX_DATAGRAM; // protocol 15 size

        Sv.StartParticle(default, default, 0, 0);

        Sv.host_protocol_datagram_maxsize = oldMax;

        Assert.AreEqual(before, Sv.datagram->CurSize);
    }

    [TestMethod]
    public void StartParticle_DirectionClampedToSignedByteRange()
    {
        byte* dataBuf = stackalloc byte[1024];
        *Sv.datagram = new SizeBuf_t { Data = dataBuf, MaxSize = 1024 };

        Vec3_t dir = default;
        dir[0] = 100;  // *16 = 1600 -> clamps to 127
        dir[1] = -100; // *16 = -1600 -> clamps to -128

        Sv.StartParticle(default, dir, 0, 0);

        byte* readBuf = stackalloc byte[1024];
        new Span<byte>(Sv.datagram->Data, Sv.datagram->CurSize).CopyTo(new Span<byte>(readBuf, Sv.datagram->CurSize));
        Net.net_message->Data = readBuf; Net.net_message->MaxSize = Sv.datagram->CurSize; Msg.net_message->CurSize = Sv.datagram->CurSize;
        Msg.BeginReading();

        Msg.ReadByte(); // (int)Svc.Particle
        Msg.ReadCoord(); Msg.ReadCoord(); Msg.ReadCoord(); // org
        Assert.AreEqual(127, Msg.ReadChar());
        Assert.AreEqual(-128, Msg.ReadChar());
    }

    [TestMethod]
    public void CleanupEnts_ClearsMuzzleflashButPreservesOtherEffectBits()
    {
        EDict_t* ent = TestEnt;
        ent->V.Effects = Sv.EF_MUZZLEFLASH | Sv.EF_DIMLIGHT;

        Sv.CleanupEnts();

        Assert.AreEqual((float)Sv.EF_DIMLIGHT, ent->V.Effects);
    }

    [TestMethod]
    public void ConnectClient_Loadgame_PreservesSpawnParmsAndSetsUpClient()
    {
        Sv.loadgame = true;
        Client_t* client = &Svs.clients[0];
        client->NetConnection = MakeSocket();
        for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
            client->spawn_parms[i] = i + 1;

        Sv.ConnectClient(0);

        Assert.IsTrue(client->Active);
        Assert.IsFalse(client->Spawned);
        Assert.AreEqual("unconnected", Marshal.PtrToStringAnsi((nint)client->Name));
        Assert.IsTrue(client->Edict == Progs.EdictNum(1));
        Assert.IsTrue(client->SendSignon);
        for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
            Assert.AreEqual(i + 1f, client->spawn_parms[i]);
    }

    [TestMethod]
    public void SendServerinfo_WritesExpectedMessageSequence()
    {
        Host.coop->Value = 0;
        Host.deathmatch->Value = 0;
        Svs.maxclients = 1;
        Client_t* client = &Svs.clients[0];
        client->Message.Data = client->MsgBuf;
        client->Message.MaxSize = QuakeDef.MAX_MSGLEN;
        client->Edict = Progs.EdictNum(1);

        Sv.SendServerinfo(client);

        Net.net_message->Data = client->Message.Data; Net.net_message->MaxSize = client->Message.CurSize; Msg.net_message->CurSize = client->Message.CurSize;
        Msg.BeginReading();

        Assert.AreEqual((int)Svc.Print, Msg.ReadByte());
        string version = Marshal.PtrToStringAnsi((nint)Msg.ReadString())!;
        StringAssert.Contains(version, "VERSION");
        Assert.AreEqual((int)Svc.ServerInfo, Msg.ReadByte());
        Assert.AreEqual(Sv.current_protocol, Msg.ReadLong());
        Assert.AreEqual(1, Msg.ReadByte()); // maxclients
        Assert.AreEqual((int)GameType.Coop, Msg.ReadByte());
        Assert.IsTrue(client->SendSignon);
        Assert.IsFalse(client->Spawned);
    }

    [TestMethod]
    [DataRow(Protocol.PROTOCOL_NETQUAKE)]
    [DataRow(Protocol.PROTOCOL_FITZQUAKE)]
    public void SendServerinfo_WritesSvProtocol(int protocol)
    {
        Svs.maxclients = 1;
        Client_t* client = &Svs.clients[0];
        client->Message.Data = client->MsgBuf;
        client->Message.MaxSize = QuakeDef.MAX_MSGLEN;
        client->Edict = Progs.EdictNum(1);
        Sv.current_protocol = protocol;

        Sv.SendServerinfo(client);

        Net.net_message->Data = client->Message.Data; Net.net_message->MaxSize = client->Message.CurSize; Msg.net_message->CurSize = client->Message.CurSize;
        Msg.BeginReading();
        Msg.ReadByte();   // svc_print
        Msg.ReadString();
        Msg.ReadByte();   // svc_serverinfo
        Assert.AreEqual(protocol, Msg.ReadLong());
    }

    [TestMethod]
    public void SendServerinfo_DeathmatchWithoutCoop_WritesGameDeathmatch()
    {
        Host.coop->Value = 0;
        Host.deathmatch->Value = 1;
        Svs.maxclients = 1;
        Client_t* client = &Svs.clients[0];
        client->Message.Data = client->MsgBuf;
        client->Message.MaxSize = QuakeDef.MAX_MSGLEN;
        client->Edict = Progs.EdictNum(1);

        Sv.SendServerinfo(client);

        Net.net_message->Data = client->Message.Data; Net.net_message->MaxSize = client->Message.CurSize; Msg.net_message->CurSize = client->Message.CurSize;
        Msg.BeginReading();

        Msg.ReadByte();     // (int)Svc.Print
        Msg.ReadString();   // version line
        Msg.ReadByte();     // (int)Svc.ServerInfo
        Msg.ReadLong();     // protocol version
        Msg.ReadByte();     // maxclients
        Assert.AreEqual((int)GameType.Deathmatch, Msg.ReadByte());
    }

    [TestMethod]
    public void UpdateToReliableMessages_FragsChanged_BroadcastsToAllActiveClients()
    {
        // Allocated directly (not via Svs.AllocateClients(), which is
        // Hunk-based and tied to this test's arena -- see Setup()'s comment
        // on Svs.clients) so Teardown's NativeMemory.Free/_allocations
        // bookkeeping stays the single source of truth for this buffer.
        Svs.maxclientslimit = 2;
        Svs.maxclients = 2;
        Svs.clients = AllocZeroed<Client_t>(2);

        Svs.clients[0].Active = true;
        Svs.clients[0].Message.Data = Svs.clients[0].MsgBuf;
        Svs.clients[0].Message.MaxSize = QuakeDef.MAX_MSGLEN;
        Svs.clients[0].Edict = TestEnt;
        Svs.clients[0].OldFrags = 0;

        Svs.clients[1].Active = true;
        Svs.clients[1].Message.Data = Svs.clients[1].MsgBuf;
        Svs.clients[1].Message.MaxSize = QuakeDef.MAX_MSGLEN;
        Svs.clients[1].Edict = TestEnt;

        TestEnt->V.Frags = 3;

        Sv.UpdateToReliableMessages();

        Assert.AreEqual(3, Svs.clients[0].OldFrags);
        foreach (int i in new[] { 0, 1 })
        {
            Net.net_message->Data = Svs.clients[i].Message.Data; Net.net_message->MaxSize = Svs.clients[i].Message.CurSize; Msg.net_message->CurSize = Svs.clients[i].Message.CurSize;
            Msg.BeginReading();
            Assert.AreEqual((int)Svc.UpdateFrags, Msg.ReadByte());
            Assert.AreEqual(0, Msg.ReadByte()); // player index 0's frags changed
            Assert.AreEqual(3, Msg.ReadShort());
        }
    }

    [TestMethod]
    public void UpdateToReliableMessages_InactiveClient_NeitherSourceNorTarget()
    {
        Svs.maxclientslimit = 2;
        Svs.maxclients = 2;
        Svs.clients = AllocZeroed<Client_t>(2); // both already-zeroed, i.e. inactive

        // UpdateToReliableMessages's outer loop reads through Edict even for
        // inactive slots (it only gates the broadcast loop on Active) --
        // every real client slot always has a valid Edict once allocated, so
        // both need one here too, matching that invariant.
        Svs.clients[0].Edict = TestEnt;
        TestEnt->V.Frags = 99;

        Svs.clients[1].Edict = Progs.EdictNum(0);
        Svs.clients[1].Message.Data = Svs.clients[1].MsgBuf;
        Svs.clients[1].Message.MaxSize = QuakeDef.MAX_MSGLEN;

        Sv.UpdateToReliableMessages();

        Assert.AreEqual(0, Svs.clients[1].Message.CurSize);
    }

    [TestMethod]
    public void CreateBaseline_NonFreeEdictWithModel_WritesSpawnbaseline()
    {
        byte* sigBuf = stackalloc byte[1024];
        *Sv.signon = new SizeBuf_t { Data = sigBuf, MaxSize = 1024 };
        // entnum 1 is within [1, maxclients] -- CreateBaseline looks up
        // "progs/player.mdl" for that branch regardless of the edict's own
        // V.Model, so that's what needs to be in model_precache here.
        Sv.model_precache[1] = TestHelpers.Ansi("progs/player.mdl");
        Svs.maxclients = 1;

        EDict_t* ent = TestEnt;
        ent->V.ModelIndex = 1;
        ent->V.Origin = new Vec3_t { [0] = 1, [1] = 2, [2] = 3 };
        ent->V.Frame = 4;
        ent->V.Skin = 5;

        Sv.CreateBaseline();

        Assert.AreEqual(ent->V.Origin[0], ent->Baseline.Origin[0]);
        Assert.AreEqual(4, ent->Baseline.Frame);
        Assert.AreEqual(5, ent->Baseline.Skin);
        Assert.AreEqual(1, ent->Baseline.ColorMap);

        Net.net_message->Data = Sv.signon->Data; Net.net_message->MaxSize = Sv.signon->CurSize; Msg.net_message->CurSize = Sv.signon->CurSize;
        Msg.BeginReading();
        // entnum 0 (the world edict) is never Free and always precedes our
        // test entity in CreateBaseline's loop, so its own spawnbaseline
        // entry has to be consumed first.
        Assert.AreEqual((int)Svc.SpawnBaseline, Msg.ReadByte());
        Assert.AreEqual(0, Msg.ReadShort()); // entnum 0 (world)
        Msg.ReadByte(); Msg.ReadByte(); Msg.ReadByte(); Msg.ReadByte(); // modelindex/frame/colormap/skin
        for (int i = 0; i < 3; i++) { Msg.ReadCoord(); Msg.ReadAngle(); }

        Assert.AreEqual((int)Svc.SpawnBaseline, Msg.ReadByte());
        Assert.AreEqual(1, Msg.ReadShort()); // entnum 1 (our test entity)
    }

    [TestMethod]
    public void CreateBaseline_FreeEdict_IsSkipped()
    {
        byte* sigBuf = stackalloc byte[1024];
        *Sv.signon = new SizeBuf_t { Data = sigBuf, MaxSize = 1024 };
        Sv.edicts[0].Free = 1; // isolate: the world edict is skipped too

        EDict_t* ent = TestEnt;
        ent->Free = 1;
        ent->V.ModelIndex = 1;

        Sv.CreateBaseline();

        Assert.AreEqual(0, Sv.signon->CurSize);
    }

    [TestMethod]
    public void CreateBaseline_BeyondMaxclientsWithNoModel_IsSkipped()
    {
        byte* sigBuf = stackalloc byte[1024];
        *Sv.signon = new SizeBuf_t { Data = sigBuf, MaxSize = 1024 };
        Sv.edicts[0].Free = 1; // isolate: the world edict is skipped too
        Svs.maxclients = 0; // entnum 1 > maxclients(0)

        EDict_t* ent = TestEnt;
        ent->V.ModelIndex = 0;

        Sv.CreateBaseline();

        Assert.AreEqual(0, Sv.signon->CurSize);
    }

    [TestMethod]
    public void ResetState_ZeroesDocumentedFields()
    {
        Sv.paused = true;
        Sv.loadgame = true;
        Sv.time = 123;
        Sv.lastcheck = 5;
        Sv.lastchecktime = 5;
        Sv.modelname = TestHelpers.Ansi("maps/start.bsp");
        Sv.worldmodel = (Model_t*)1;
        Sv.model_precache[0] = TestHelpers.Ansi("x");
        Sv.num_edicts = 99;
        Sv.max_edicts = 99;
        Sv.edicts = (EDict_t*)1;
        Sv.sv_player = (EDict_t*)1;
        Sv.state = ServerState_t.Active;
        Sv.active = true;

        Sv.ResetState();

        Assert.IsFalse(Sv.paused);
        Assert.IsFalse(Sv.loadgame);
        Assert.AreEqual(0, Sv.time);
        Assert.AreEqual(0, Sv.lastcheck);
        Assert.AreEqual(0, Sv.lastchecktime);
        Assert.IsTrue(Sv.modelname == null);
        Assert.IsTrue(Sv.worldmodel == null);
        Assert.IsTrue(Sv.model_precache[0] == null);
        Assert.AreEqual(0, Sv.num_edicts);
        Assert.AreEqual(0, Sv.max_edicts);
        Assert.IsTrue(Sv.edicts == null);
        Assert.IsTrue(Sv.sv_player == null);
        Assert.AreEqual(ServerState_t.Loading, Sv.state);
        Assert.IsFalse(Sv.active);
    }
}
