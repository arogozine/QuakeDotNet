// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// net_wins.c -- WinQuake's actual Windows Sockets driver (WinQuake.dsp
/// builds net_wins.c, not the BSD-flavored net_udp.c/net_bsd.c; see Net.cs's
/// type-level comment). Ported to .NET's OS-agnostic System.Net.Sockets per
/// CLAUDE.md rule 5 rather than P/Invoking wsock32.dll -- the dynamic
/// LoadLibrary/GetProcAddress dance WINS_Init does (so the game could still
/// run on Win 3.1 without Winsock statically linked) has no .NET need.
/// Each original `int socket` handle is now an index into `sockets` (a real
/// System.Net.Sockets.Socket underneath); qsocket_t.socket (int, Net.cs)
/// stores that handle exactly the way the original stores an OS fd/HANDLE.
/// PartialIPAddress is reimplemented over managed byte arrays/IPAddress
/// instead of the original's raw sin_addr bit-shifting, for the same
/// observable behavior with far less pointer arithmetic (see its own
/// comment for the one simplification: >4 dotted groups truncates to the
/// last 4 instead of silently overflowing/wrapping a 32-bit int).
/// </summary>
public static unsafe class NetIp
{
    private const short AF_INET = 2;

    private static readonly Dictionary<int, Socket> sockets = new();
    private static int nextHandle = 1;

    private static int net_acceptsocket = -1; // socket for fielding new connections
    private static int net_controlsocket = -1;
    private static int net_broadcastsocket;
    private static readonly QSockAddr_t* broadcastaddr = NativeMemory.AllocZeroed<QSockAddr_t>();

    // myAddr -- INADDR_ANY unless "-ip" names a specific local address
    // (WINS_Init); PartialIPAddress fills in missing leading octets from it.
    private static IPAddress myAddr = IPAddress.Any;

    // tcpipAvailable/my_tcpip_address (net_main.c globals) -- set by
    // WINS_Init. tcpipAvailable is read by Host_Status_f (HostCmd.cs) and by
    // Menu.cs's Multiplayer/Net screens.
    public static bool tcpipAvailable;
    public static Utf8String my_tcpip_address = Utf8String.Empty;

    // Test-only: closes every open socket and resets this file's private
    // state, so Quake.UnitTests (InternalsVisibleTo) can start each test
    // method from a clean slate instead of accumulating real OS sockets
    // (and colliding on the fixed accept-socket port) across the whole run.
    internal static void ResetForTests()
    {
        foreach (Socket sock in sockets.Values)
        {
            try { sock.Close(); } catch { /* ignore */ }
        }
        sockets.Clear();
        nextHandle = 1;
        net_acceptsocket = -1;
        net_controlsocket = -1;
        net_broadcastsocket = 0;
        *broadcastaddr = default;
        myAddr = IPAddress.Any;
        tcpipAvailable = false;
        my_tcpip_address = Utf8String.Empty;
    }


    private static void WriteAnsi(byte* dest, string s, int maxLen)
    {
        int n = Math.Min(s.Length, maxLen);
        for (int i = 0; i < n; i++)
            dest[i] = (byte)s[i];
        dest[n] = 0;
    }

    // FromEndPoint/ToEndPoint -- bridge .NET's IPEndPoint and this file's
    // qsockaddr stand-in (QSockAddr_t, Net.cs). AF_INET (IPv4) only,
    // matching the original throughout.
    public static QSockAddr_t FromEndPoint(IPEndPoint ep)
    {
        QSockAddr_t a = default;
        a.Family = AF_INET;
        byte[] b = ep.Address.GetAddressBytes();
        if (b.Length != 4)
            b = new byte[4];
        a.AddrNet = BitConverter.ToUInt32(b, 0);
        a.PortNet = (ushort)IPAddress.HostToNetworkOrder((short)ep.Port);
        return a;
    }

    public static IPEndPoint ToEndPoint(QSockAddr_t addr)
    {
        byte[] b = BitConverter.GetBytes(addr.AddrNet);
        int port = (ushort)IPAddress.NetworkToHostOrder((short)addr.PortNet);
        return new IPEndPoint(new IPAddress(b), port);
    }

    /*
    ====================
    WINS_Init
    ====================
    */
    public static int Init()
    {
        if (Com.CheckParm("-noudp"u8) != 0)
            return -1;

        // determine my name
        string hostName;
        try { hostName = Dns.GetHostName(); }
        catch { return -1; }

        // if the quake hostname isn't set, set it to the machine name
        if (Q.StrCmp(Net.hostname->String, "UNNAMED"u8) == 0)
        {
            string trimmed = hostName.Length > 15 ? hostName[..15] : hostName;
            byte* buf = stackalloc byte[16];
            WriteAnsi(buf, trimmed, 15);
            Cvar.Set("hostname"u8, buf);
        }

        int i = Com.CheckParm("-ip"u8);
        if (i != 0)
        {
            if (i < Com.com_argc - 1)
            {
                byte* ipArg = Com.com_argv[i + 1];
                if (!IPAddress.TryParse(CStr.ToString(ipArg), out myAddr!))
                    Sys.Error($"{ipArg} is not a valid IP address");
                my_tcpip_address = ipArg;
            }
            else
                Sys.Error("NET_Init: you must specify an IP address after -ip"u8);
        }
        else
        {
            myAddr = IPAddress.Any;
            my_tcpip_address = "INADDR_ANY"u8;
        }

        net_controlsocket = OpenSocket(0);
        if (net_controlsocket == -1)
        {
            Con.Printf("WINS_Init: Unable to open control socket\n"u8);
            return -1;
        }

        *broadcastaddr = default;
        broadcastaddr->Family = AF_INET;
        broadcastaddr->AddrNet = BitConverter.ToUInt32(IPAddress.Broadcast.GetAddressBytes(), 0);
        broadcastaddr->PortNet = (ushort)IPAddress.HostToNetworkOrder((short)Net.net_hostport);

        Con.Printf("Winsock TCP/IP Initialized\n"u8);
        tcpipAvailable = true;

        return net_controlsocket;
    }

    // WINS_Shutdown
    public static void Shutdown()
    {
        Listen(false);
        CloseSocket(net_controlsocket);
    }

    // WINS_Listen
    public static void Listen(bool state)
    {
        if (state)
        {
            if (net_acceptsocket != -1)
                return;
            if ((net_acceptsocket = OpenSocket(Net.net_hostport)) == -1)
                Sys.Error("WINS_Listen: Unable to open accept socket\n"u8);
            return;
        }

        if (net_acceptsocket == -1)
            return;
        CloseSocket(net_acceptsocket);
        net_acceptsocket = -1;
    }

    // WINS_OpenSocket
    public static int OpenSocket(int port)
    {
        Socket sock;
        try
        {
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Blocking = false;
            sock.Bind(new IPEndPoint(myAddr, port));
        }
        catch
        {
            return -1;
        }

        int handle = nextHandle++;
        sockets[handle] = sock;
        return handle;
    }

    // WINS_CloseSocket
    public static int CloseSocket(int socket)
    {
        if (socket == net_broadcastsocket)
            net_broadcastsocket = 0;

        if (sockets.Remove(socket, out Socket? sock))
        {
            try { sock.Close(); } catch { /* already gone */ }
        }
        return 0;
    }

    /*
    ============
    PartialIPAddress

    This lets you type only as much of the net address as required, using
    the local network components to fill in the rest (e.g. ".5" or "5" means
    "my subnet's host 5"; "1.2.3.4" is a full address). Up to 4 dot-groups
    are honored; more than 4 truncates to the last 4 rather than wrapping a
    32-bit accumulator the way the original's `addr = (addr<<8)+num` would
    (an edge case no real command line hits).
    ============
    */
    private static int PartialIPAddress(string input, QSockAddr_t* hostaddr)
    {
        string s = input.Length > 0 && input[0] == '.' ? input : "." + input;

        List<int> octets = [];
        int idx = 0;
        while (idx < s.Length && s[idx] == '.')
        {
            idx++;
            int num = 0, digits = 0;
            while (idx < s.Length && s[idx] is >= '0' and <= '9')
            {
                num = num * 10 + (s[idx] - '0');
                idx++;
                if (++digits > 3)
                    return -1;
            }
            if (idx < s.Length && s[idx] != '.' && s[idx] != ':' && (s[idx] is < '0' or > '9'))
                return -1;
            if (num is < 0 or > 255)
                return -1;
            octets.Add(num);
        }

        if (octets.Count > 4)
            octets = octets[^4..];

        int port = Net.net_hostport;
        if (idx < s.Length && s[idx] == ':')
        {
            if (!int.TryParse(s[(idx + 1)..], out port))
                port = Net.net_hostport;
        }

        byte[] baseOctets = myAddr.GetAddressBytes();
        if (baseOctets.Length != 4)
            baseOctets = new byte[4];
        byte[] result = (byte[])baseOctets.Clone();
        int fillStart = 4 - octets.Count;
        for (int i = 0; i < octets.Count; i++)
            result[fillStart + i] = (byte)octets[i];

        *hostaddr = FromEndPoint(new IPEndPoint(new IPAddress(result), port));
        return 0;
    }

    // WINS_Connect -- a true no-op, matching the original exactly: UDP
    // sockets here are never OS-connect()'d, every send/receive names its
    // peer address explicitly (sock->addr, validated per-packet by
    // NetDgrm.cs).
    public static int Connect(int socket, QSockAddr_t* addr) => 0;

    // WINS_CheckNewConnections
    public static int CheckNewConnections()
    {
        if (net_acceptsocket == -1)
            return -1;
        if (sockets.TryGetValue(net_acceptsocket, out Socket? sock) && sock.Available > 0)
            return net_acceptsocket;
        return -1;
    }

    // WINS_Read
    public static int Read(int socket, byte* buf, int len, QSockAddr_t* addr)
    {
        if (!sockets.TryGetValue(socket, out Socket? sock))
            return -1;

        if (sock.Available == 0)
            return 0;

        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int ret;
        try
        {
            // recvfrom straight into the caller's buffer, as the original does.
            ret = sock.ReceiveFrom(new Span<byte>(buf, len), SocketFlags.None, ref remote);
        }
        catch (SocketException ex)
        {
            // UDP_Read: only EWOULDBLOCK/ECONNREFUSED are benign; anything else
            // (e.g. WSAEMSGSIZE for an oversized datagram) returns -1 rather
            // than crashing the process.
            return ex.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset ? 0 : -1;
        }

        if (addr != null)
            *addr = FromEndPoint((IPEndPoint)remote);

        return ret;
    }

    private static int MakeSocketBroadcastCapable(int socket)
    {
        if (!sockets.TryGetValue(socket, out Socket? sock))
            return -1;
        try { sock.EnableBroadcast = true; }
        catch { return -1; }
        net_broadcastsocket = socket;
        return 0;
    }

    // WINS_Broadcast -- used by NetDgrm.SearchForHosts to send the server-info query.
    public static int Broadcast(int socket, byte* buf, int len)
    {
        if (socket != net_broadcastsocket)
        {
            if (net_broadcastsocket != 0)
                Sys.Error("Attempted to use multiple broadcasts sockets\n"u8);
            if (MakeSocketBroadcastCapable(socket) == -1)
            {
                Con.Printf("Unable to make socket broadcast capable\n"u8);
                return -1;
            }
        }

        return Write(socket, buf, len, broadcastaddr);
    }

    // WINS_Write
    public static int Write(int socket, byte* buf, int len, QSockAddr_t* addr)
    {
        if (!sockets.TryGetValue(socket, out Socket? sock))
            return -1;

        IPEndPoint ep = ToEndPoint(*addr);
        try
        {
            return sock.SendTo(new ReadOnlySpan<byte>(buf, len), SocketFlags.None, ep);
        }
        catch (SocketException ex)
        {
            // UDP_Write: only EWOULDBLOCK is benign; other failures return -1.
            return ex.SocketErrorCode == SocketError.WouldBlock ? 0 : -1;
        }
    }

    // WINS_AddrToString
    public static string AddrToString(QSockAddr_t* addr)
    {
        IPEndPoint ep = ToEndPoint(*addr);
        return $"{ep.Address}:{ep.Port}";
    }

    // WINS_GetSocketAddr
    public static int GetSocketAddr(int socket, QSockAddr_t* addr)
    {
        *addr = default;
        if (sockets.TryGetValue(socket, out Socket? sock) && sock.LocalEndPoint is IPEndPoint ep)
        {
            *addr = FromEndPoint(ep);
            if (addr->AddrNet == 0 || Equals(ep.Address, IPAddress.Loopback))
                addr->AddrNet = BitConverter.ToUInt32(myAddr.GetAddressBytes(), 0);
        }
        return 0;
    }

    // WINS_GetNameFromAddr
    public static void GetNameFromAddr(QSockAddr_t* addr, byte* name)
    {
        try
        {
            IPAddress ip = ToEndPoint(*addr).Address;
            IPHostEntry entry = Dns.GetHostEntry(ip);
            WriteAnsi(name, entry.HostName, Net.NET_NAMELEN - 1);
            return;
        }
        catch
        {
            // fall through to the address-string fallback, matching the
            // original's gethostbyaddr-failed branch.
        }

        WriteAnsi(name, AddrToString(addr), Net.NET_NAMELEN - 1);
    }

    // WINS_GetAddrFromName
    public static int GetAddrFromName(byte* nameBytes, QSockAddr_t* addr)
    {
        string name = CStr.ToString(nameBytes);
        if (name.Length > 0 && name[0] is >= '0' and <= '9')
            return PartialIPAddress(name, addr);

        try
        {
            IPAddress[] addrs = Dns.GetHostAddresses(name);
            IPAddress? v4 = Array.Find(addrs, a => a.AddressFamily == AddressFamily.InterNetwork);
            if (v4 == null)
                return -1;
            *addr = FromEndPoint(new IPEndPoint(v4, Net.net_hostport));
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    // WINS_AddrCompare
    public static int AddrCompare(QSockAddr_t* addr1, QSockAddr_t* addr2)
    {
        if (addr1->Family != addr2->Family)
            return -1;
        if (addr1->AddrNet != addr2->AddrNet)
            return -1;
        if (addr1->PortNet != addr2->PortNet)
            return 1;
        return 0;
    }

    // WINS_GetSocketPort
    public static int GetSocketPort(QSockAddr_t* addr) => (ushort)IPAddress.NetworkToHostOrder((short)addr->PortNet);

    // WINS_SetSocketPort
    public static int SetSocketPort(QSockAddr_t* addr, int port)
    {
        addr->PortNet = (ushort)IPAddress.HostToNetworkOrder((short)port);
        return 0;
    }
}
