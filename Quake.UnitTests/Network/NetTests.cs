// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Net's globals (net_activeSockets/net_freeSockets/net_drivers/...) are
// Hunk-backed and tied to whichever arena was active when Init() ran, same
// concurrency/isolation rationale as CvarTests.
[TestClass]
public sealed unsafe class NetTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;

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

        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_numsockets = 0;
        Net.net_activeconnections = 0;
        Net.messagesSent = 0;
        Net.messagesReceived = 0;
        Net.unreliableMessagesSent = 0;
        Net.unreliableMessagesReceived = 0;
        Net.net_driverlevel = 0;

        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        NetLoop.localconnectpending = false;

        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void Init_DoesNotThrow_AndInitializesLoopbackDriver()
    {
        Net.Init();

        Assert.IsTrue(Net.net_drivers[0].Initialized);
        Assert.IsTrue(Net.net_freeSockets != null);
        Assert.AreEqual(Svs.maxclientslimit + 1, Net.net_numsockets);
    }

    [TestMethod]
    public void NewQSocket_ThenFreeQSocket_RoundTrips()
    {
        Net.Init();

        QSocket_t* sock = Net.NewQSocket();
        Assert.IsTrue(sock != null);
        Assert.IsFalse(sock->Disconnected);

        Net.FreeQSocket(sock);
        Assert.IsTrue(sock->Disconnected);
    }

    [TestMethod]
    public void NewQSocket_ReturnsNull_WhenAtConnectionLimit()
    {
        Net.Init();
        Net.net_activeconnections = Svs.maxclients; // at limit

        QSocket_t* sock = Net.NewQSocket();

        Assert.IsTrue(sock == null);
    }

    [TestMethod]
    public void Connect_ThenCheckNewConnections_ThenSendRoundTrips()
    {
        Net.Init();

        QSocket_t* client = Net.Connect(Ansi("local"));
        Assert.IsTrue(client != null);

        QSocket_t* server = Net.CheckNewConnections();
        Assert.IsTrue(server != null);
        Assert.IsTrue(server == NetLoop.loop_server);

        // build a small message and send client -> server
        SizeBuf_t msg = default;
        byte* data = stackalloc byte[16];
        msg.Data = data;
        msg.MaxSize = 16;
        msg.CurSize = 0;
        Sz.Write(&msg, Ansi("hi"), 3); // "hi\0"

        int sendResult = Net.SendMessage(client, &msg);
        Assert.AreEqual(1, sendResult);

        int getResult = Net.GetMessage(server);
        Assert.AreEqual(1, getResult);
        Assert.AreEqual(3, Net.net_message->CurSize);
        Assert.AreEqual((byte)'h', Net.net_message->Data[0]);
        Assert.AreEqual((byte)'i', Net.net_message->Data[1]);
    }

    [TestMethod]
    public void Connect_RejectsNonLocalHost()
    {
        Net.Init();

        QSocket_t* result = Net.Connect(Ansi("someremotehost"));

        Assert.IsTrue(result == null);
    }

    [TestMethod]
    public void CanSendMessage_FalseUntilPeerConnected()
    {
        Net.Init();

        QSocket_t* client = Net.Connect(Ansi("local"));
        Assert.IsTrue(client != null);

        // driverdata is wired synchronously by Loop_Connect, so this should
        // already be true (mirrors the original's own behavior).
        Assert.IsTrue(Net.CanSendMessage(client));
    }
}
