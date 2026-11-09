// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// net_dgrm.c's NetDgrm.cs. Drives the reliable/unreliable datagram protocol
// over real loopback (127.0.0.1)
// UDP sockets (NetIp.cs), single-threaded: SendMessage/GetMessage calls on
// each "side" are interleaved by hand in the exact order a real two-process
// exchange would deliver them, since NetDgrm's packetBuffer/Net.net_message
// are shared mutable statics not safe to touch from two threads at once.
[TestClass]
public sealed unsafe class NetDgrmTests
{
    private byte* _buf;
    private const int Size = 8 * 1024 * 1024; // each QSocket_t carries two NET_MAXMESSAGE (64 KB) buffers

    [TestInitialize]
    public void Setup()
    {
        Sv.host_protocol_datagram_maxsize = QuakeDef.MAX_MARK_V_DATAGRAM;
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;

        NetIp.ResetForTests();
        Net.net_hostport = 0;
        Net.net_driverlevel = 1;
        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_activeconnections = 0;
        Svs.maxclients = 8;

        SizeBuf_t* nm = Net.net_message;
        Sz.Alloc(nm, Net.NET_MAXMESSAGE);

        // NET_NewQSocket (Net.cs) needs a free-list; a handful of
        // Hunk-allocated qsocket_t's stands in for NET_Init's own pool.
        for (int i = 0; i < 8; i++)
        {
            QSocket_t* s = (QSocket_t*)Hunk.AllocName(sizeof(QSocket_t), "qsocket"u8);
            s->Next = Net.net_freeSockets;
            Net.net_freeSockets = s;
            s->Disconnected = true;
        }
    }

    [TestCleanup]
    public void Teardown()
    {
        NetIp.ResetForTests();
        NativeMemory.Free(_buf);
    }

    // Wires up a QSocket_t (from Net's free list) bound to a fresh UDP
    // socket, with Addr pointed at `peerSocket`'s bound loopback endpoint --
    // simulates two already-connected peers without running the full
    // (int)NetControlRequest.Connect handshake (covered separately below).
    private static QSocket_t* MakeConnectedSocket(out int socket)
    {
        socket = NetIp.OpenSocket(0);
        Assert.AreNotEqual(-1, socket);

        QSocket_t* sock = Net.NewQSocket();
        Assert.IsTrue(sock != null);
        sock->Socket = socket;
        sock->CanSend = true;
        return sock;
    }

    private static void PointAt(QSocket_t* sock, int peerSocket)
    {
        QSockAddr_t raw = default;
        Assert.AreEqual(0, NetIp.GetSocketAddr(peerSocket, &raw));
        sock->Addr = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, NetIp.GetSocketPort(&raw)));
    }

    // `data` must stay pinned for as long as the returned buffer is used.
    private static SizeBuf_t Message(byte* data, int length)
    {
        SizeBuf_t sb = default;
        sb.Data = data;
        sb.MaxSize = length;
        sb.CurSize = length;
        return sb;
    }

    [TestMethod]
    public void SendMessage_ThenGetMessage_DeliversPayloadReliably()
    {
        QSocket_t* a = MakeConnectedSocket(out int sockA);
        QSocket_t* b = MakeConnectedSocket(out int sockB);
        PointAt(a, sockB);
        PointAt(b, sockA);

        byte[] payload = [1, 2, 3, 4, 5];
        fixed (byte* p = payload)
        {
            SizeBuf_t data = Message(p, payload.Length);
            Assert.AreEqual(1, NetDgrm.SendMessage(a, &data));
        }
        Assert.IsFalse(a->CanSend); // waiting on the ACK

        TestHelpers.WaitForData(sockB);
        int ret = NetDgrm.GetMessage(b);

        Assert.AreEqual(1, ret);
        Assert.AreEqual(5, Net.net_message->CurSize);
        Assert.AreEqual((byte)1, Net.net_message->Data[0]);
        Assert.AreEqual((byte)5, Net.net_message->Data[4]);

        // the ACK NetDgrm.GetMessage(b) just sent back is now waiting for A
        TestHelpers.WaitForData(sockA);
        int ackRet = NetDgrm.GetMessage(a);

        Assert.AreEqual(0, ackRet); // no payload -- just an ACK
        Assert.IsTrue(a->CanSend); // free to send the next reliable message
    }

    [TestMethod]
    public void SendUnreliableMessage_ThenGetMessage_ReturnsTwo()
    {
        QSocket_t* a = MakeConnectedSocket(out int sockA);
        QSocket_t* b = MakeConnectedSocket(out int sockB);
        PointAt(a, sockB);
        PointAt(b, sockA);

        byte[] payload = [42, 43];
        fixed (byte* p = payload)
        {
            SizeBuf_t data = Message(p, payload.Length);
            Assert.AreEqual(1, NetDgrm.SendUnreliableMessage(a, &data));
        }

        TestHelpers.WaitForData(sockB);
        int ret = NetDgrm.GetMessage(b);

        Assert.AreEqual(2, ret);
        Assert.AreEqual(2, Net.net_message->CurSize);
        Assert.AreEqual((byte)42, Net.net_message->Data[0]);
    }

    [TestMethod]
    public void SendMessage_LargerThanMaxDatagram_FragmentsAndReassembles()
    {
        QSocket_t* a = MakeConnectedSocket(out int sockA);
        QSocket_t* b = MakeConnectedSocket(out int sockB);
        PointAt(a, sockB);
        PointAt(b, sockA);

        Sv.host_protocol_datagram_maxsize = QuakeDef.MAX_DATAGRAM; // protocol 15 size so the payload fragments (restored in Setup)
        byte[] payload = new byte[QuakeDef.MAX_DATAGRAM + 400];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i & 0xff);

        fixed (byte* p = payload)
        {
            SizeBuf_t data = Message(p, payload.Length);
            Assert.AreEqual(1, NetDgrm.SendMessage(a, &data)); // fragment 1
        }

        TestHelpers.WaitForData(sockB);
        int firstRet = NetDgrm.GetMessage(b); // buffers fragment 1, ACKs it, incomplete
        Assert.AreEqual(0, firstRet);

        TestHelpers.WaitForData(sockA);
        int ackRet = NetDgrm.GetMessage(a); // processes the ACK, sends fragment 2
        Assert.AreEqual(0, ackRet);

        TestHelpers.WaitForData(sockB);
        int finalRet = NetDgrm.GetMessage(b); // fragment 2 (EOM) -- reassembly complete

        Assert.AreEqual(1, finalRet);
        Assert.AreEqual(payload.Length, Net.net_message->CurSize);
        for (int i = 0; i < payload.Length; i++)
            Assert.AreEqual(payload[i], Net.net_message->Data[i]);
    }

    [TestMethod]
    public void CheckNewConnections_ValidConnectRequest_AcceptsAndRepliesOverRawSocket()
    {
        // Listen on a known port, as a real server does (-port / net_hostport),
        // so the raw client below knows where to send.
        Net.net_hostport = FreeUdpPort();
        NetIp.Listen(true);

        // a bare System.Net.Sockets client, standing in for a real remote
        // peer's (int)NetControlRequest.Connect -- deliberately not routed through NetIp/
        // NetDgrm, so this test doesn't just check NetDgrm against itself.
        using Socket client = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        client.SendTo(BuildConnectRequestPacket(), new IPEndPoint(IPAddress.Loopback, Net.net_hostport));

        // poll until the datagram is delivered
        Assert.IsTrue(SpinWait.SpinUntil(() => NetIp.CheckNewConnections() != -1, 2000), "CCREQ_CONNECT never arrived");
        QSocket_t* accepted = NetDgrm.CheckNewConnections();
        Assert.IsTrue(accepted != null, "server rejected a well-formed CCREQ_CONNECT");
        Assert.AreEqual(0, accepted->LanDriver);

        // the (int)NetControlReply.Accept reply should now be waiting on the raw client socket
        client.ReceiveTimeout = 2000;
        byte[] reply = new byte[512];
        int n = client.Receive(reply);
        Assert.AreEqual(9, n); // header + CCREP_ACCEPT + the new socket's port (long)
        // byte layout: 4-byte BigLong header, then the (int)NetControlReply.Accept command byte
        Assert.AreEqual(unchecked((byte)0x81), reply[4]);
    }

    private static int FreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    // Hand-builds a (int)NetControlRequest.Connect packet, matching what
    // _Datagram_Connect (net_dgrm.c) sends: a NETFLAG_CTL|length BigLong header,
    // then byte CCREQ_CONNECT(0x01), MSG_WriteString "QUAKE" (which includes
    // the terminating NUL -- MSG_ReadString on the server stops there), byte
    // NET_PROTOCOL_VERSION(3).
    private static byte[] BuildConnectRequestPacket()
    {
        byte[] body = [0x01, .. "QUAKE\0"u8.ToArray(), 0x03];
        int totalLen = 4 + body.Length;
        uint header = 0x80000000u | (uint)totalLen; // NETFLAG_CTL | length
        byte[] headerBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)header));
        return [.. headerBytes, .. body];
    }

    // ---- server browser (Datagram_SearchForHosts / NET_Slist_f) ----

    // A CCREP_SERVER_INFO reply, as Datagram_CheckNewConnections would send.
    private static byte[] BuildServerInfoReply(string addr, string name, string map, int users, int max)
    {
        List<byte> body = [131]; // CCREP_SERVER_INFO
        foreach (string str in new[] { addr, name, map })
        {
            body.AddRange(System.Text.Encoding.ASCII.GetBytes(str));
            body.Add(0);
        }
        body.Add((byte)users);
        body.Add((byte)max);
        body.Add(3); // NET_PROTOCOL_VERSION
        // body order in the original: addr, hostname, map, users, max, version
        int totalLen = 4 + body.Count;
        uint header = 0x80000000u | (uint)totalLen;
        byte[] headerBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)header));
        return [.. headerBytes, .. body];
    }

    private static int OpenBrowserControlSocket(out int port)
    {
        int control = NetIp.OpenSocket(0);
        Assert.AreNotEqual(-1, control);
        Net.net_drivers[1].ControlSock = control;
        QSockAddr_t raw = default;
        NetIp.GetSocketAddr(control, &raw);
        port = NetIp.GetSocketPort(&raw);
        Net.ResetSlistForTests();
        return control;
    }

    private static void Deliver(int control, int port, byte[] packet)
    {
        using UdpClient udp = new();
        udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
        TestHelpers.WaitForData(control);
        NetDgrm.SearchForHosts(false);
    }

    [TestMethod]
    public void SearchForHosts_AddsServerInfoReplyToHostcache()
    {
        int control = OpenBrowserControlSocket(out int port);

        Deliver(control, port, BuildServerInfoReply("127.0.0.1:26001", "Srv", "e1m1", 2, 8));

        Assert.AreEqual(1, Net.hostCacheCount);
        Assert.AreEqual("Srv", CStr.ToString(Net.hostcache[0].Name));
        Assert.AreEqual("e1m1", CStr.ToString(Net.hostcache[0].Map));
        Assert.AreEqual(2, Net.hostcache[0].Users);
        Assert.AreEqual(8, Net.hostcache[0].MaxUsers);
        Assert.AreEqual("127.0.0.1:26001", CStr.ToString(Net.hostcache[0].CName));
        Assert.AreEqual("  Srv             e1m1             2/ 8".TrimStart(), CStr.ToString(Net.FormatSlistEntry(0)));
    }

    [TestMethod]
    public void SearchForHosts_IgnoresDuplicateAddress_AndRenamesNameConflict()
    {
        int control = OpenBrowserControlSocket(out int port);

        Deliver(control, port, BuildServerInfoReply("127.0.0.1:26001", "Srv", "e1m1", 1, 4));
        Deliver(control, port, BuildServerInfoReply("127.0.0.1:26001", "Srv", "e1m1", 1, 4)); // same server
        Assert.AreEqual(1, Net.hostCacheCount);

        Deliver(control, port, BuildServerInfoReply("127.0.0.1:26002", "Srv", "e1m2", 1, 4)); // same name, other server
        Assert.AreEqual(2, Net.hostCacheCount);
        Assert.AreEqual("Srv0", CStr.ToString(Net.hostcache[1].Name));
    }

    [TestMethod]
    public void SearchForHosts_IgnoresMalformedReply()
    {
        int control = OpenBrowserControlSocket(out int port);

        byte[] reply = BuildServerInfoReply("127.0.0.1:26001", "Srv", "e1m1", 1, 4);
        reply[3]++; // length field no longer matches the datagram
        Deliver(control, port, reply);

        Assert.AreEqual(0, Net.hostCacheCount);
    }

    [TestMethod]
    public void LoopSearchForHosts_ListsLocalServer_OnlyWhenActive()
    {
        byte* oldName = Net.hostname->String;
        bool oldActive = Sv.active;
        byte* oldSvName = Sv.Name;
        Net.ResetSlistForTests();
        try
        {
            Net.hostname->String = Ansi("UNNAMED");
            Sv.Name = Ansi("e1m3");
            Svs.maxclients = 4;

            Sv.active = false;
            NetLoop.SearchForHosts(true);
            Assert.AreEqual(0, Net.hostCacheCount);

            Sv.active = true;
            NetLoop.SearchForHosts(true);
            Assert.AreEqual(1, Net.hostCacheCount);
            Assert.AreEqual("local", CStr.ToString(Net.hostcache[0].Name));
            Assert.AreEqual("e1m3", CStr.ToString(Net.hostcache[0].Map));
            Assert.AreEqual(4, Net.hostcache[0].MaxUsers);
            Assert.AreEqual("local", CStr.ToString(Net.hostcache[0].CName));
        }
        finally
        {
            Net.hostname->String = oldName;
            Sv.active = oldActive;
            Sv.Name = oldSvName;
            Net.ResetSlistForTests();
        }
    }

    private static readonly List<string> pollOrder = [];
    private static void PollA() => pollOrder.Add("A");
    private static void PollB() => pollOrder.Add("B");

    [TestMethod]
    public void Poll_RunsDueProceduresInTimeOrder()
    {
        Net.ResetSlistForTests();
        pollOrder.Clear();
        PollProcedure_t* a = NativeMemory.AllocZeroed<PollProcedure_t>();
        PollProcedure_t* b = NativeMemory.AllocZeroed<PollProcedure_t>();
        PollProcedure_t* later = NativeMemory.AllocZeroed<PollProcedure_t>();
        try
        {
            a->Procedure = &PollA;
            b->Procedure = &PollB;
            later->Procedure = &PollA;
            Net.SchedulePollProcedure(a, 0.0);
            Net.SchedulePollProcedure(b, -1.0); // scheduled later, but due earlier
            Net.SchedulePollProcedure(later, 3600.0); // not due yet

            Net.Poll();

            CollectionAssert.AreEqual(new[] { "B", "A" }, pollOrder);
        }
        finally
        {
            Net.ResetSlistForTests();
            NativeMemory.Free(a);
            NativeMemory.Free(b);
            NativeMemory.Free(later);
        }
    }

    [TestMethod]
    public void Slist_RunsToCompletion_ThenResetsFlags()
    {
        Net.ResetSlistForTests();
        Net.net_numdrivers = 2;
        Net.net_drivers[0] = default;
        Net.net_drivers[1] = default; // neither driver initialized: nothing to search

        Net.slistSilent = true;
        Net.Slist_f();
        Assert.IsTrue(Net.slistInProgress);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (Net.slistInProgress && DateTime.UtcNow < deadline)
            Net.Poll();

        Assert.IsFalse(Net.slistInProgress);
        Assert.IsFalse(Net.slistSilent);
        Assert.IsTrue(Net.slistLocal);
        Assert.AreEqual(0, Net.hostCacheCount);
    }
}
