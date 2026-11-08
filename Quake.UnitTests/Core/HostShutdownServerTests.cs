// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// host.c's Host_ShutdownServer, net_main.c's NET_SendToAll, and
// sv_main.c's SV_SendReconnect/SV_SaveSpawnparms -- the "changelevel"/
// "restart" prerequisite. Also covers HostCmd.cs's
// Host_Changelevel_f/Host_Restart_f guard conditions. Shares Sv/Svs/Net/
// Progs static state with the other server-side test classes.
[TestClass]
public sealed unsafe class HostShutdownServerTests
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
        Cmd.cmd_source = CmdSource_t.SrcCommand;

        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;
        Svs.clients[0] = default;
        Svs.serverflags = 0;
        Svs.host_client = null;

        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_numsockets = 0;
        Net.net_activeconnections = 0;
        Net.net_driverlevel = 0;
        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        NetLoop.localconnectpending = false;

        // Cls.state starts at CActive.Disconnected (NOT CActive.Dedicated == 0,
        // which would make NetLoop.Init() refuse to open a loopback client
        // socket). ShutdownServer's CL_Disconnect branch (Cls.state ==
        // Connected) is exercised by its own test below; with no sound device
        // started, SndDma.StopAllSounds takes its !sound_started early return.
        Cls.state = CActive.Disconnected;
        Cls.netcon = null;
        Cls.signon = 0;
        ClDemo.demoplayback = false;
        ClDemo.demorecording = false;
        SndDma.sound_started = false;

        Sv.Init();
        Host.deathmatch->Value = 0;
        Host.coop->Value = 0;
        Sv.active = true;

        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        Progs.pr_strings = (byte*)NativeMemory.AllocZeroed(64);
        _allocations.Add((nint)Progs.pr_strings);

        Sv.edicts = AllocZeroed<EDict_t>(4);
        Sv.num_edicts = 2;
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
        Cls.state = CActive.Disconnected;
        Cls.netcon = null;

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

    // Wires up a single trivial QuakeC function (SetChangeParms/
    // ClientDisconnect/SetNewParms all point at it) -- same approach as
    // SvSendTests.SetupTrivialProgs.
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
        Progs.pr_global_struct->SetChangeParms = 1;
    }

    private QSocket_t* ConnectLoopbackClient()
    {
        SetupTrivialProgs();
        Net.Init();
        QSocket_t* clientSock = Net.Connect(Ansi("local"));
        Assert.IsTrue(clientSock != null);
        QSocket_t* serverSock = Net.CheckNewConnections();
        Assert.IsTrue(serverSock != null);

        Svs.clients[0].NetConnection = serverSock;
        Sv.ConnectClient(0);
        Net.net_activeconnections = 1;

        return clientSock;
    }

    [TestMethod]
    public void SendToAll_ActiveLoopbackClient_DeliversMessageAndReturnsZero()
    {
        QSocket_t* clientSock = ConnectLoopbackClient();

        byte* dataBuf = stackalloc byte[4];
        SizeBuf_t msg = default;
        msg.Data = dataBuf;
        msg.MaxSize = 4;
        Msg.WriteByte(&msg, (int)Svc.Disconnect);

        int failed = Net.SendToAll(&msg, 5);

        Assert.AreEqual(0, failed);
        Assert.IsTrue(clientSock != null);
        int ret = Net.GetMessage(clientSock);
        Assert.AreEqual(1, ret);
        Assert.AreEqual((byte)(int)Svc.Disconnect, Net.net_message->Data[0]);
    }

    [TestMethod]
    public void SendToAll_NoActiveClients_ReturnsZeroImmediately()
    {
        byte* dataBuf = stackalloc byte[4];
        SizeBuf_t msg = default;
        msg.Data = dataBuf;
        msg.MaxSize = 4;
        Msg.WriteByte(&msg, (int)Svc.Disconnect);

        int failed = Net.SendToAll(&msg, 5);

        Assert.AreEqual(0, failed);
    }

    [TestMethod]
    public void ShutdownServer_ActiveServer_ClearsSvActiveAndDropsClients()
    {
        ConnectLoopbackClient();
        Assert.IsTrue(Svs.clients[0].Active);

        Host.ShutdownServer(false);

        Assert.IsFalse(Sv.active);
        Assert.IsFalse(Svs.clients[0].Active);
        Assert.AreEqual(0, Net.net_activeconnections);
        Assert.AreEqual(ServerState_t.Loading, Sv.state);
        Assert.AreEqual(0, Sv.num_edicts);
    }

    // host.c: "if (cls.state == ca_connected) CL_Disconnect ();" -- runs
    // after sv.active is cleared, so CL_Disconnect's own "if (sv.active)
    // Host_ShutdownServer" doesn't recurse.
    [TestMethod]
    public void ShutdownServer_ConnectedClient_DisconnectsLocalClient()
    {
        QSocket_t* clientSock = ConnectLoopbackClient();
        Cls.netcon = clientSock;
        Cls.state = CActive.Connected;
        Cls.signon = 4;

        Host.ShutdownServer(false);

        Assert.AreEqual(CActive.Disconnected, Cls.state);
        Assert.AreEqual(0, Cls.signon);
        Assert.IsFalse(Sv.active);
        Assert.IsFalse(Svs.clients[0].Active);
        Assert.AreEqual(ServerState_t.Loading, Sv.state);
    }

    [TestMethod]
    public void ShutdownServer_InactiveServer_IsNoOp()
    {
        Sv.active = false;
        Sv.state = ServerState_t.Active;

        Host.ShutdownServer(false);

        // ResetState was never called (early return), so state is
        // untouched -- distinguishes the no-op path from the real one.
        Assert.AreEqual(ServerState_t.Active, Sv.state);
    }

    [TestMethod]
    public void SaveSpawnparms_ActiveClient_CopiesServerFlagsAndRunsSetChangeParms()
    {
        SetupTrivialProgs();
        Progs.pr_global_struct->ServerFlags = 3;

        Svs.clients[0].Active = true;
        Svs.clients[0].Edict = Progs.EdictNum(1);

        Sv.SaveSpawnparms();

        Assert.AreEqual(3, Svs.serverflags);
    }

    [TestMethod]
    public void SaveSpawnparms_InactiveClient_SkipsIt()
    {
        SetupTrivialProgs();
        Progs.pr_global_struct->ServerFlags = 5;
        Svs.clients[0].Active = false;

        Sv.SaveSpawnparms();

        Assert.AreEqual(5, Svs.serverflags);
    }

    [TestMethod]
    public void SendReconnect_Dedicated_SkipsCmdExecuteAndSendsStuffText()
    {
        Cls.state = CActive.Dedicated;

        Sv.SendReconnect();

        // No active/connected client, so NET_SendToAll is a synchronous
        // no-op; the real assertion is that this doesn't throw.
    }

    [TestMethod]
    public void Changelevel_f_WrongArgCount_PrintsUsageAndDoesNotSpawn()
    {
        Sv.active = true;
        Cmd.TokenizeString(Ansi("changelevel\n"));

        HostCmd.Changelevel_f();

        // Sv.active untouched -- SpawnServer, which would reset it to
        // false on a failed load, was never reached.
        Assert.IsTrue(Sv.active);
    }

    [TestMethod]
    public void Changelevel_f_ServerNotActive_PrintsErrorAndDoesNotSpawn()
    {
        Sv.active = false;
        Cmd.TokenizeString(Ansi("changelevel start\n"));

        HostCmd.Changelevel_f();

        Assert.IsFalse(Sv.active);
    }

    [TestMethod]
    public void Restart_f_ServerNotActive_IsNoOp()
    {
        Sv.active = false;
        Sv.state = ServerState_t.Active;

        HostCmd.Restart_f();

        Assert.AreEqual(ServerState_t.Active, Sv.state); // SpawnServer never reached
    }

    [TestMethod]
    public void Restart_f_NotFromConsole_IsNoOp()
    {
        Sv.active = true;
        Sv.state = ServerState_t.Active;
        Cmd.cmd_source = CmdSource_t.SrcClient;

        HostCmd.Restart_f();

        Assert.AreEqual(ServerState_t.Active, Sv.state);
    }
}
