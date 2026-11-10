// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// host.c's Host_FilterTime/Host_ServerFrame/_Host_Frame/Host_Frame and
// sv_main.c/sv_user.c's SV_ClearDatagram/SV_ConnectClient/SV_RunClients.
// Shares Sv/Progs/World's static state with SvPhysTests/ProgsTests, so it
// can't run concurrently with anything else touching that state.
[TestClass]
public sealed unsafe class MainLoopTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private List<nint> _allocations = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        _allocations = [];

        // Reset shared static state other DoNotParallelize test classes also
        // touch (same reasoning as HostTests/SvPhysTests' own Setup
        // comments): a previous class's now-freed Hunk arena can otherwise
        // leave Cmd.cmd_functions/cmd_alias holding dangling name pointers
        // that crash Sv.Init()'s Cvar.RegisterVariable -> Cmd.Exists lookup.
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

        // Same reasoning as SvSendTests' own Setup: Net.Init() Hunk-allocates
        // its socket pool, so a previous test's now-freed pool (after this
        // test's MemoryManager.Memory_Init reinit) can't be left dangling.
        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_numsockets = 0;
        Net.net_activeconnections = 0;
        Net.net_driverlevel = 0;
        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        NetLoop.localconnectpending = false;

        Sv.Init();

        Host.realtime = 0;
        Host.oldrealtime = 0;
        Host.host_frametime = 0;
        Host.host_framecount = 0;
        Host.host_time = 1.0;
        Host.host_framerate->Value = 0;
        Cls.timedemo = false;
        Cls.state = CActive.Disconnected;
        Cls.signon = 0;
        Sv.active = false;
        Sv.paused = false;
        Keys.key_dest = KeyDest.Game;

        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        Sv.edicts = AllocZeroed<EDict_t>(2);
        Sv.num_edicts = 2;
        Sv.max_edicts = 2;

        // SendServerinfo (called from ConnectClient) walks these until a
        // null entry; clear any stale byte* left over from another test
        // class's now-freed memory.
        NativeMemory.ClearPointerArray<byte>(Sv.model_precache, QuakeDef.MAX_MODELS);
        NativeMemory.ClearPointerArray<byte>(Sv.sound_precache, QuakeDef.MAX_SOUNDS);

        SetupOpenWorld();

        Host.host_frametime = 0.1f;
        Sv.time = 0;

        Svs.clients[0] = default;
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
        {
            NativeMemory.Free((void*)p);
        }

        NativeMemory.Free(_buf);
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged
    {
        T* p = NativeMemory.AllocArrayZeroed<T>(count);
        _allocations.Add((nint)p);
        return p;
    }

    // Same synthetic geometry-free world as SvPhysTests.SetupOpenWorld.
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

    // Loopback client/server QSocket_t pair (Net.Connect("local") +
    // Net.CheckNewConnections()) -- SV_ReadClientMessage now needs a real
    // NetConnection to pump (Net.GetMessage returns 0 for "no data waiting"
    // rather than crashing on a null socket).
    private QSocket_t* ConnectLoopback()
    {
        Net.Init();
        QSocket_t* clientSock = Net.Connect(Ansi("local"));
        Assert.IsTrue(clientSock != null);
        QSocket_t* serverSock = Net.CheckNewConnections();
        Assert.IsTrue(serverSock != null);
        return serverSock;
    }

    // Wires up a single trivial QuakeC function (First_Statement -> one
    // Op_t.Done statement, no parms/locals) so PR_ExecuteProgram can run
    // pr_global_struct->SetNewParms/StartFrame without a real progs.dat,
    // same synthetic-minimum-viable-progs approach as SvPhysTests' fake
    // world.
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
        Progs.pr_global_struct->StartFrame = 1;
        Progs.pr_global_struct->PlayerPreThink = 1;
        Progs.pr_global_struct->PlayerPostThink = 1;
    }

    [TestMethod]
    public void FilterTime_TooSoonAfterLastFrame_ReturnsFalse()
    {
        Host.realtime = 0;
        Host.oldrealtime = 0;

        bool result = Host.FilterTime(0.001f); // well under 1/72s

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void FilterTime_ClampsToMaxFrametime()
    {
        Host.realtime = 0;
        Host.oldrealtime = 0;

        bool result = Host.FilterTime(5.0f); // way more than 0.1s

        Assert.IsTrue(result);
        Assert.AreEqual(0.1f, Host.host_frametime);
    }

    [TestMethod]
    public void FilterTime_ClampsToMinFrametime()
    {
        Host.realtime = 0;
        Host.oldrealtime = 0;

        bool result = Host.FilterTime(1.0f / 71f); // just over the 1/72s gate

        Assert.IsTrue(result);
        Assert.IsGreaterThanOrEqualTo(0.001f, Host.host_frametime);
    }

    [TestMethod]
    public void FilterTime_HostFramerateCvarSet_PinsFrametime()
    {
        Host.realtime = 0;
        Host.oldrealtime = 0;
        Host.host_framerate->Value = 0.025f;

        Host.FilterTime(1.0f);

        Assert.AreEqual(0.025f, Host.host_frametime);
    }

    [TestMethod]
    public void ConnectClient_BindsClientToEdictAndMarksActive()
    {
        SetupTrivialProgs();
        Svs.clients[0].NetConnection = AllocZeroed<QSocket_t>(1);

        Sv.ConnectClient(0);

        Assert.IsTrue(Svs.clients[0].Active);
        Assert.IsTrue(Svs.clients[0].Edict == Progs.EdictNum(1));
    }

    [TestMethod]
    public void RunClients_InactiveClient_DoesNotSetSvPlayer()
    {
        Svs.clients[0].Active = false;
        Sv.sv_player = null;

        SvUser.RunClients();

        Assert.IsTrue(Sv.sv_player == null);
    }

    [TestMethod]
    public void RunClients_ActiveClient_DrivesClientThinkAndSetsSvPlayer()
    {
        EDict_t* ent = TestEnt;
        ent->V.MoveType = (int)MoveType.Walk;
        ent->V.Health = 1;
        ent->V.Flags = SvPhys.FL_ONGROUND;

        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = ent;
        Svs.clients[0].NetConnection = ConnectLoopback();
        Svs.clients[0].Spawned = true; // SV_ClientThink only runs for spawned clients
        Svs.clients[0].Cmd = new UserCmd_t { ForwardMove = 200 };

        SvUser.RunClients();

        Assert.IsTrue(Sv.sv_player == ent);
        // AirMove (driven via ClientThink) applies acceleration along the
        // forward wish direction for an on-ground walking player.
        Assert.IsTrue(ent->V.Velocity[0] != 0 || ent->V.Velocity[1] != 0);
    }

    [TestMethod]
    public void RunClients_UnspawnedClient_ClearsCmdAndSkipsClientThink()
    {
        EDict_t* ent = TestEnt;
        ent->V.MoveType = (int)MoveType.Walk;
        ent->V.Health = 1;
        ent->V.Flags = SvPhys.FL_ONGROUND;

        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = ent;
        Svs.clients[0].NetConnection = ConnectLoopback();
        Svs.clients[0].Spawned = false;
        Svs.clients[0].Cmd = new UserCmd_t { ForwardMove = 200 };

        SvUser.RunClients();

        Assert.AreEqual(0f, ent->V.Velocity[0]);
        Assert.AreEqual(0f, ent->V.Velocity[1]);
        Assert.AreEqual(0f, Svs.clients[0].Cmd.ForwardMove); // cleared until a new packet arrives
    }

    [TestMethod]
    public void RunClients_PausedServer_SkipsClientThink()
    {
        EDict_t* ent = TestEnt;
        ent->V.MoveType = (int)MoveType.Walk;
        ent->V.Health = 1;

        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = ent;
        Svs.clients[0].NetConnection = ConnectLoopback();
        Svs.clients[0].Spawned = true;
        Svs.clients[0].Cmd = new UserCmd_t { ForwardMove = 200 };

        Sv.paused = true;

        SvUser.RunClients();

        Assert.AreEqual(0f, ent->V.Velocity[0]);
        Assert.AreEqual(0f, ent->V.Velocity[1]);
    }

    [TestMethod]
    public void ReadClientMessage_NoDataWaiting_ReturnsTrueWithoutBlocking()
    {
        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = TestEnt;
        Svs.clients[0].NetConnection = ConnectLoopback();
        Svs.host_client = &Svs.clients[0];

        bool result = SvUser.ReadClientMessage();

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ReadClientMessage_ClcStringcmdPrespawn_QueuesSignonAndFlagsSendSignon()
    {
        SetupTrivialProgs();
        HostCmd.InitCommands();
        *Sv.signon = new SizeBuf_t { Data = NativeMemory.AllocArrayZeroed<byte>(64), MaxSize = 64 };
        _allocations.Add((nint)Sv.signon->Data);

        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = TestEnt;
        Svs.clients[0].Message.Data = NativeMemory.AllocArrayZeroed<byte>(QuakeDef.MAX_MSGLEN);
        _allocations.Add((nint)Svs.clients[0].Message.Data);
        Svs.clients[0].Message.MaxSize = QuakeDef.MAX_MSGLEN;
        Svs.host_client = &Svs.clients[0];

        QSocket_t* clientSock = ConnectLoopbackFromClientSide(out QSocket_t* serverSock);
        Svs.clients[0].NetConnection = serverSock;

        byte* data = stackalloc byte[128];
        SizeBuf_t buf = new() { Data = data, MaxSize = 128 };
        Msg.WriteByte(&buf, (int)Clc.StringCmd);
        Msg.WriteString(&buf, Ansi("prespawn"));
        Net.SendMessage(clientSock, &buf);

        bool result = SvUser.ReadClientMessage();

        Assert.IsTrue(result);
        Assert.IsTrue(Svs.clients[0].SendSignon);
    }

    private QSocket_t* ConnectLoopbackFromClientSide(out QSocket_t* serverSock)
    {
        Net.Init();
        QSocket_t* clientSock = Net.Connect(Ansi("local"));
        Assert.IsTrue(clientSock != null);
        serverSock = Net.CheckNewConnections();
        Assert.IsTrue(serverSock != null);
        return clientSock;
    }

    [TestMethod]
    public void Frame_NoActiveServer_AdvancesHostTimeAndFrameCount()
    {
        Sv.active = false;
        Screen.scr_skipupdate = true; // avoid needing a real render/video setup
        double timeBefore = Host.host_time;
        int countBefore = Host.host_framecount;

        Host.Frame(0.05f);

        Assert.IsGreaterThan(timeBefore, Host.host_time);
        Assert.AreEqual(countBefore + 1, Host.host_framecount);
    }

    [TestMethod]
    public void Frame_TooSoon_DoesNotAdvanceFrameCount()
    {
        Sv.active = false;
        Screen.scr_skipupdate = true;
        Host.realtime = 0;
        Host.oldrealtime = 0;
        int countBefore = Host.host_framecount;

        Host.Frame(0.0001f); // under the 1/72s gate

        Assert.AreEqual(countBefore, Host.host_framecount);
    }

    [TestMethod]
    public void ServerFrame_ActiveServer_ClearsDatagramAndRunsPhysics()
    {
        SetupTrivialProgs();

        EDict_t* ent = TestEnt;
        ent->V.MoveType = (int)MoveType.Walk;
        ent->V.Health = 1;
        ent->V.Flags = SvPhys.FL_ONGROUND;

        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = ent;
        Svs.clients[0].NetConnection = ConnectLoopback();
        Svs.clients[0].Spawned = true;
        Svs.clients[0].Cmd = new UserCmd_t { ForwardMove = 200 };

        // Spawned clients get a real datagram built (SV_SendClientDatagram ->
        // SV_WriteEntitiesToClient -> SV_FatPVS), which needs Sv.worldmodel's
        // render node/leaf tree -- SetupOpenWorld only populated its
        // collision hulls (World.PointContents' needs, for SvPhys.Physics()
        // below), so add a single non-solid leaf to that same worldmodel
        // rather than replacing it (replacing it would drop the hulls that
        // SvPhys.Physics's own World.PointContents call needs).
        MLeaf_t* leaf = AllocZeroed<MLeaf_t>(1);
        leaf->Contents = (int)Contents.Empty;
        Sv.worldmodel->NumLeafs = 1;
        Sv.worldmodel->Leafs = leaf;
        Sv.worldmodel->Nodes = (MNode_t*)leaf;

        Sv.active = true;
        Sv.datagram->CurSize = 5; // pretend something was queued last frame

        Host.ServerFrame();

        Assert.AreEqual(0, Sv.datagram->CurSize);
        Assert.AreEqual(Host.host_frametime, Progs.pr_global_struct->FrameTime);
        Assert.IsTrue(ent->V.Velocity[0] != 0 || ent->V.Velocity[1] != 0);
    }
}
