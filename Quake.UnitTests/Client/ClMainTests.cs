// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// cl_main.c's ClMain.cs: CL_ReadFromServer/CL_RelinkEntities/CL_SendCmd/
// CL_Init/CL_EstablishConnection/CL_Disconnect/CL_AllocDlight/CL_LerpPoint.
// Shares Cl/Cls/ClEntities/Net's static state with the other
// DoNotParallelize classes, so it can't run concurrently with anything else
// touching that state.
[TestClass]
public sealed unsafe class ClMainTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;
    private byte* _colormap;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);

        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

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

        Cl.num_entities = 0;
        Cl.num_statics = 0;
        Cl.time = 0;
        Cl.oldtime = 0;
        Cl.mtime[0] = 0;
        Cl.mtime[1] = 0;
        Cl.mvelocity[0] = default;
        Cl.mvelocity[1] = default;
        Cl.velocity = default;
        Cl.viewangles = default;
        Cl.viewentity = 0;
        Cl.movemessages = 0;
        Cl.cl_shownet->Value = 0;
        Cl.cl_nolerp->Value = 0;

        Cls.state = CActive.Disconnected;
        Cls.signon = 0;
        Cls.netcon = null;
        Sz.Clear(Cls.message);

        Sv.active = false;

        _colormap = NativeMemory.AllocArrayZeroed<byte>(Vid.VID_GRADES * 256);
        Vid.vid.Colormap = _colormap;

        new Span<Entity_t>(ClEntities.cl_entities, QuakeDef.MAX_EDICTS).Clear();
        new Span<DLight_t>(ClEntities.cl_dlights, ClEntities.MAX_DLIGHTS).Clear();
        ClEntities.cl_numvisedicts = 0;
        NativeMemory.ClearPointerArray(ClEntities.cl_visedicts, ClEntities.MAX_VISEDICTS);
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_colormap);
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void AllocDlight_ReusesExactKeyMatch()
    {
        DLight_t* first = ClMain.AllocDlight(42);
        first->Radius = 123;

        DLight_t* second = ClMain.AllocDlight(42);

        Assert.IsTrue(first == second);
        Assert.AreEqual(0f, second->Radius); // re-zeroed on reuse
    }

    [TestMethod]
    public void AllocDlight_PicksExpiredSlotWhenNoKeyMatch()
    {
        Cl.time = 10.0;
        DLight_t* dl = ClMain.AllocDlight(0);

        Assert.IsTrue(dl == &ClEntities.cl_dlights[0]); // Die defaults to 0 < cl.time
    }

    [TestMethod]
    public void DecayLights_ReducesRadiusByTimeTimesDecay_ClampedAtZero()
    {
        Cl.time = 10.0;
        Cl.oldtime = 9.0; // 1 second elapsed

        DLight_t* dl = ClMain.AllocDlight(1);
        dl->Radius = 100;
        dl->Decay = 40;
        dl->Die = 20; // still alive (Die >= cl.time)

        ClMain.DecayLights();

        Assert.AreEqual(60f, dl->Radius);

        ClMain.DecayLights();
        ClMain.DecayLights();

        Assert.AreEqual(0f, dl->Radius); // clamped, never negative
    }

    [TestMethod]
    public void DecayLights_SkipsExpiredOrZeroRadiusLights()
    {
        Cl.time = 10.0;
        Cl.oldtime = 9.0;

        DLight_t* expired = ClMain.AllocDlight(2);
        expired->Radius = 100;
        expired->Decay = 40;
        expired->Die = 5; // Die < cl.time -- already expired, skipped

        ClMain.DecayLights();

        Assert.AreEqual(100f, expired->Radius); // untouched
    }

    [TestMethod]
    public void LerpPoint_ServerActiveLocally_ReturnsOneAndSyncsTime()
    {
        Sv.active = true;
        Cl.mtime[0] = 5.0;

        float frac = ClMain.LerpPoint();

        Assert.AreEqual(1.0f, frac);
        Assert.AreEqual(5.0, Cl.time);
    }

    [TestMethod]
    public void RelinkEntities_RecentEntityWithModel_AddedToVisedicts()
    {
        Cl.mtime[0] = 2.0;
        Cl.num_entities = 2;
        Entity_t* ent = &ClEntities.cl_entities[1];
        // RelinkEntities reads ent->Model->Flags, so this needs a real
        // (zeroed) Model_t rather than a fake sentinel pointer.
        Model_t* realModel = NativeMemory.AllocZeroed<Model_t>();
        ent->Model = realModel;
        ent->MsgTime = 2.0;
        ent->ForceLink = true;
        ent->MsgOrigins[0] = new Vec3_t();
        ent->MsgAngles[0] = new Vec3_t();

        ClMain.RelinkEntities();

        Assert.AreEqual(1, ClEntities.cl_numvisedicts);
        Assert.IsTrue(ClEntities.cl_visedicts[0] == ent);
        Assert.IsFalse(ent->ForceLink);

        NativeMemory.Free(realModel);
    }

    [TestMethod]
    public void RelinkEntities_StaleEntity_ClearsModel()
    {
        Cl.mtime[0] = 5.0;
        Cl.num_entities = 2;
        Entity_t* ent = &ClEntities.cl_entities[1];
        Model_t* realModel = NativeMemory.AllocZeroed<Model_t>();
        ent->Model = realModel;
        ent->MsgTime = 1.0; // stale -- doesn't match cl.mtime[0]

        ClMain.RelinkEntities();

        Assert.IsTrue(ent->Model == null);
        Assert.AreEqual(0, ClEntities.cl_numvisedicts);

        NativeMemory.Free(realModel);
    }

    [TestMethod]
    public void RelinkEntities_StaleEntity_ResetsLerp()
    {
        Cl.mtime[0] = 5.0;
        Cl.num_entities = 2;
        Entity_t* ent = &ClEntities.cl_entities[1];
        Model_t* realModel = NativeMemory.AllocZeroed<Model_t>();
        ent->Model = realModel;
        ent->MsgTime = 1.0;

        ClMain.RelinkEntities();

        Assert.AreEqual(LerpFlags.RESETMOVE | LerpFlags.RESETANIM, ent->LerpFlags);

        NativeMemory.Free(realModel);
    }

    [TestMethod]
    public void RelinkEntities_Teleport_ResetsMoveLerp()
    {
        Cl.mtime[0] = 2.0;
        Cl.mtime[1] = 1.9;
        Cl.num_entities = 2;
        Entity_t* ent = &ClEntities.cl_entities[1];
        Model_t* realModel = NativeMemory.AllocZeroed<Model_t>();
        ent->Model = realModel;
        ent->MsgTime = 2.0;
        ent->MsgOrigins[0] = new Vec3_t(500, 0, 0);
        ent->MsgOrigins[1] = new Vec3_t(0, 0, 0);

        ClMain.RelinkEntities();

        Assert.AreNotEqual(0, ent->LerpFlags & LerpFlags.RESETMOVE);

        NativeMemory.Free(realModel);
    }

    [TestMethod]
    public void RelinkEntities_StepEntityWithLerpMove_UsesLatestServerOrigin()
    {
        RMain.r_lerpmove->Value = 1;
        Cl.mtime[0] = 2.0;
        Cl.mtime[1] = 1.9;
        Cl.time = 1.95; // halfway between messages, but the renderer lerps step entities instead
        Cl.num_entities = 2;
        Entity_t* ent = &ClEntities.cl_entities[1];
        Model_t* realModel = NativeMemory.AllocZeroed<Model_t>();
        ent->Model = realModel;
        ent->MsgTime = 2.0;
        ent->LerpFlags = LerpFlags.MOVESTEP;
        ent->MsgOrigins[0] = new Vec3_t(10, 0, 0);
        ent->MsgOrigins[1] = new Vec3_t(0, 0, 0);

        ClMain.RelinkEntities();

        Assert.AreEqual(10f, ent->Origin.X, 0.0001f);

        NativeMemory.Free(realModel);
    }

    [TestMethod]
    public void PrintEntities_f_PrintsEmptyForNoModel_AndDetailsWhenModelSet()
    {
        Cl.num_entities = 2;
        ClEntities.cl_entities[0].Model = null;

        Model_t* realModel = NativeMemory.AllocZeroed<Model_t>();
        byte[] name = System.Text.Encoding.ASCII.GetBytes("progs/soldier.mdl");
        for (int i = 0; i < name.Length; i++)
            realModel->Name[i] = name[i];

        Entity_t* ent = &ClEntities.cl_entities[1];
        ent->Model = realModel;
        ent->Frame = 3;
        ent->Origin[0] = 1.5f;
        ent->Origin[1] = -2.5f;
        ent->Origin[2] = 0f;
        ent->Angles[0] = 90f;

        TextWriter original = Console.Out;
        StringWriter sw = new();
        Console.SetOut(sw);
        try
        {
            ClMain.PrintEntities_f();
        }
        finally
        {
            Console.SetOut(original);
        }
        string output = sw.ToString();

        StringAssert.Contains(output, "0:EMPTY");
        StringAssert.Contains(output, "progs/soldier.mdl: 3");

        NativeMemory.Free(realModel);
    }

    [TestMethod]
    public void SendCmd_NotConnected_DoesNothing()
    {
        Cls.state = CActive.Disconnected;

        ClMain.SendCmd(); // must not throw despite Cls.netcon == null
    }

    [TestMethod]
    public void ReadFromServer_BridgesNetMessageAndAdvancesTime()
    {
        Net.Init();
        QSocket_t* clientSock = Net.Connect(new Utf8String("local"u8));
        QSocket_t* serverSock = Net.CheckNewConnections();
        Assert.IsTrue(clientSock != null && serverSock != null);

        // A real server always leads with (int)Svc.Time (Sv.SendClientDatagram);
        // without it, CL_LerpPoint's f==0 branch would reset cl.time back to
        // cl.mtime[0] (0), which is correct original behavior but would
        // defeat this test's purpose of checking the time advance itself.
        byte* data = stackalloc byte[8];
        data[0] = (byte)(int)Svc.Time;
        byte[] timeBytes = BitConverter.GetBytes(0.05f);
        for (int i = 0; i < 4; i++)
            data[1 + i] = timeBytes[i];
        SizeBuf_t msg = new() { Data = data, MaxSize = 8, CurSize = 5 };
        Net.SendMessage(serverSock, &msg);

        Cls.netcon = clientSock;
        Cls.state = CActive.Connected;
        Host.host_frametime = 0.05f;
        double timeBefore = Cl.time;

        int result = ClMain.ReadFromServer();

        Assert.AreEqual(0, result);
        Assert.AreEqual(timeBefore + 0.05, Cl.time, 0.0001);
    }

    [TestMethod]
    public void SendCmd_ConnectedAtSignon_DeliversZeroedMoveAfterWarmup()
    {
        Net.Init();
        QSocket_t* clientSock = Net.Connect(new Utf8String("local"u8));
        QSocket_t* serverSock = Net.CheckNewConnections();

        Cls.netcon = clientSock;
        Cls.state = CActive.Connected;
        Cls.signon = Cls.SIGNONS;

        // the first two CL_SendMove calls are always dropped (see SendMove's
        // own comment); the third should actually reach the server.
        ClMain.SendCmd();
        ClMain.SendCmd();
        ClMain.SendCmd();

        int result = Net.GetMessage(serverSock);

        Assert.AreEqual(2, result); // unreliable
        Assert.AreEqual((byte)(int)Clc.Move, Net.net_message->Data[0]);
    }

    [TestMethod]
    public void EstablishConnection_SetsStateConnectedAndClearsSignon()
    {
        Net.Init();
        Cls.signon = 3;

        ClMain.EstablishConnection("local"u8);

        Assert.AreEqual(CActive.Connected, Cls.state);
        Assert.AreEqual(0, Cls.signon);
        Assert.IsTrue(Cls.netcon != null);
    }

    [TestMethod]
    public void Disconnect_ConnectedState_SendsClcDisconnectAndCloses()
    {
        Net.Init();
        QSocket_t* clientSock = Net.Connect(new Utf8String("local"u8));
        QSocket_t* serverSock = Net.CheckNewConnections();
        Cls.netcon = clientSock;
        Cls.state = CActive.Connected;

        ClMain.Disconnect();

        Assert.AreEqual(CActive.Disconnected, Cls.state);

        int result = Net.GetMessage(serverSock);
        Assert.AreEqual(2, result); // unreliable (int)Clc.Disconnect
        Assert.AreEqual((byte)(int)Clc.Disconnect, Net.net_message->Data[0]);
    }
}
