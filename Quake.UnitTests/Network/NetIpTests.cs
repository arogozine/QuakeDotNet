// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// net_wins.c's NetIp.cs -- the .NET-Socket-backed IP driver. Uses real
// loopback (127.0.0.1) UDP sockets, so
// (like RealSoundTests/RealPakFileTests) this exercises the actual OS
// networking stack rather than a pure in-memory fake. NetIp.ResetForTests()
// (internal, InternalsVisibleTo) closes every socket between tests so the
// fixed accept-socket port doesn't collide across test methods.
[TestClass]
public sealed unsafe class NetIpTests
{
    private byte* _buf;
    private const int Size = 512 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;

        NetIp.ResetForTests();
        Net.net_hostport = 0; // OpenSocket(0) picks an ephemeral port for every test below
    }

    [TestCleanup]
    public void Teardown()
    {
        NetIp.ResetForTests();
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void OpenSocket_ThenWriteRead_DeliversPayloadAndSenderAddress()
    {
        int a = NetIp.OpenSocket(0);
        int b = NetIp.OpenSocket(0);
        Assert.AreNotEqual(-1, a);
        Assert.AreNotEqual(-1, b);

        // GetSocketAddr's INADDR_ANY/127.0.0.1 substitution falls back to
        // myAddr, which is itself INADDR_ANY when "-ip" isn't given (as in
        // this test) -- not a valid sendto() target. Build the target
        // address from the real loopback address plus the bound port
        // instead of trusting that substitution, same as any real client
        // would when told to connect to "127.0.0.1".
        QSockAddr_t addrARaw = default, addrBRaw = default;
        Assert.AreEqual(0, NetIp.GetSocketAddr(a, &addrARaw));
        Assert.AreEqual(0, NetIp.GetSocketAddr(b, &addrBRaw));
        QSockAddr_t addrA = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, NetIp.GetSocketPort(&addrARaw)));
        QSockAddr_t addrB = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, NetIp.GetSocketPort(&addrBRaw)));

        byte[] payload = "hello"u8.ToArray();
        fixed (byte* p = payload)
            Assert.AreEqual(5, NetIp.Write(a, p, 5, &addrB));

        TestHelpers.WaitForData(b);

        byte* recvBuf = stackalloc byte[32];
        QSockAddr_t from = default;
        int n = NetIp.Read(b, recvBuf, 32, &from);

        Assert.AreEqual(5, n);
        Assert.AreEqual((byte)'h', recvBuf[0]);
        Assert.AreEqual((byte)'o', recvBuf[4]);
        Assert.AreEqual(0, NetIp.AddrCompare(&from, &addrA));
    }

    [TestMethod]
    public void Read_NoDataAvailable_ReturnsZero()
    {
        int a = NetIp.OpenSocket(0);

        byte* buf = stackalloc byte[32];
        QSockAddr_t from = default;
        int n = NetIp.Read(a, buf, 32, &from);

        Assert.AreEqual(0, n);
    }

    [TestMethod]
    public void CloseSocket_ThenRead_ReturnsMinusOne()
    {
        int a = NetIp.OpenSocket(0);
        NetIp.CloseSocket(a);

        byte* buf = stackalloc byte[32];
        QSockAddr_t from = default;
        int n = NetIp.Read(a, buf, 32, &from);

        Assert.AreEqual(-1, n);
    }

    [TestMethod]
    public void AddrCompare_SameAddrDifferentPort_ReturnsOne()
    {
        QSockAddr_t a = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, 1000));
        QSockAddr_t b = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, 2000));

        Assert.AreEqual(1, NetIp.AddrCompare(&a, &b));
    }

    [TestMethod]
    public void AddrCompare_DifferentAddr_ReturnsMinusOne()
    {
        QSockAddr_t a = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, 1000));
        QSockAddr_t b = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1000));

        Assert.AreEqual(-1, NetIp.AddrCompare(&a, &b));
    }

    [TestMethod]
    public void AddrCompare_SameAddrAndPort_ReturnsZero()
    {
        QSockAddr_t a = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, 1000));
        QSockAddr_t b = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Loopback, 1000));

        Assert.AreEqual(0, NetIp.AddrCompare(&a, &b));
    }

    [TestMethod]
    public void AddrToString_RoundTripsDottedQuadAndPort()
    {
        QSockAddr_t a = NetIp.FromEndPoint(new IPEndPoint(IPAddress.Parse("192.168.1.5"), 26000));

        Assert.AreEqual("192.168.1.5:26000", NetIp.AddrToString(&a));
    }

    [TestMethod]
    public void GetAddrFromName_FullNumericAddress_ParsesDirectly()
    {
        Net.net_hostport = 26000;
        QSockAddr_t addr = default;

        int result = NetIp.GetAddrFromName(Ansi("192.168.1.5"), &addr);

        Assert.AreEqual(0, result);
        Assert.AreEqual("192.168.1.5:26000", NetIp.AddrToString(&addr));
    }

    [TestMethod]
    public void GetAddrFromName_NumericWithExplicitPort_UsesThatPort()
    {
        QSockAddr_t addr = default;

        int result = NetIp.GetAddrFromName(Ansi("10.0.0.9:4321"), &addr);

        Assert.AreEqual(0, result);
        Assert.AreEqual("10.0.0.9:4321", NetIp.AddrToString(&addr));
    }

    [TestMethod]
    public void SetSocketPort_ThenGetSocketPort_RoundTrips()
    {
        QSockAddr_t addr = default;
        NetIp.SetSocketPort(&addr, 12345);

        Assert.AreEqual(12345, NetIp.GetSocketPort(&addr));
    }

    [TestMethod]
    public void Listen_OpensAndClosesAcceptSocket()
    {
        Net.net_hostport = 0;
        // OpenSocket(0) inside Listen binds an ephemeral port when
        // net_hostport is 0, same as any other OpenSocket(0) call --
        // enough to prove Listen(true)/Listen(false) don't throw and
        // CheckNewConnections reports "nothing pending" once open.
        NetIp.Listen(true);
        Assert.AreEqual(-1, NetIp.CheckNewConnections()); // open, but nothing sent yet

        NetIp.Listen(false);
    }
}
