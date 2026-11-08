// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

// server_static_t (server.h) -- the rest of the struct is in Sv.cs; the two
// fields NET_Init/NET_NewQSocket read (the client-count ceiling used to size
// the socket pool and gate new connections) are declared here, their earliest
// consumer. maxclients/maxclientslimit default to 1 (the single-player value)
// until Host.FindMaxClients (host.c, Host.cs) parses -dedicated/-listen.
public static partial class Svs
{
    public static int maxclients = 1;
    public static int maxclientslimit = 1;
}

/// <summary>
/// net_main.c. Real network transports are ported: net_dgrm.c (NetDgrm.cs,
/// the reliable/unreliable datagram protocol, connection handshake and
/// server-browser SearchForHosts) sits on top of net_wins.c (NetIp.cs, this
/// WinQuake tree's actual Windows-socket driver per its own .dsp --
/// net_udp.c/net_bsd.c are the Linux/BSD sibling, not built here) migrated
/// to .NET's OS-agnostic System.Net.Sockets per CLAUDE.md rule 5, rather
/// than P/Invoked WinSock. The multiplayer server browser is ported too:
/// NET_Slist_f/"slist"/hostcache/Slist_Send/Slist_Poll/PollProcedure
/// scheduling, and NET_Connect's hostcache name lookup. net_wipx.c (IPX) is
/// dropped -- IPX is a dead protocol no modern OS/router carries. Still
/// dropped: VCR record/playback (net_vcr.c; not demo recording, which is
/// ClDemo.cs), serial/modem config (config_com_*/config_modem_*,
/// GetComPortConfig/SetComPortConfig/GetModemConfig/SetModemConfig), and
/// NetDgrm's admin-only debug tooling (Test_f/Test2_f/"test"/"test2"/
/// BAN_TEST's "ban"), which stays out of scope the same way Host_InitVCR's
/// debug tooling does elsewhere in this port. "listen"/"maxplayers"/"port"
/// ARE ported (NET_Listen_f/MaxPlayers_f/NET_Port_f) -- svs.maxclients can
/// really exceed 1. IDGODS is a dead debug-only branch even in the original
/// (never defined) and is dropped.
/// </summary>
public static unsafe class Net
{
    public const int NET_NAMELEN = 64;
    public const int NET_MAXMESSAGE = 65536; // net.h -- Mark V NET_MARK_V_MAXMESSAGE (was 8192)

    public const int MAX_NET_DRIVERS = 2;
    public static readonly NetDriver_t* net_drivers = NativeMemory.AllocArrayZeroed<NetDriver_t>(MAX_NET_DRIVERS);
    public static int net_numdrivers = 2;
    public static int net_driverlevel;

    // net_hostport/DEFAULTnet_hostport -- the UDP port NetIp.cs's accept
    // socket listens on (NetIp.Listen(true)), and the port a bare numeric
    // host implicitly connects to (NetIp.GetAddrFromName).
    public static int net_hostport;
    public static int DEFAULTnet_hostport = 26000;

    // listening (net_main.c's own file-scope static) -- NET_Listen_f
    // toggles it and re-broadcasts to every initialized driver's Listen;
    // NET_Init seeds it from "-listen"/dedicated-state, matching the
    // original exactly.
    public static bool listening;

    public static QSocket_t* net_activeSockets;
    public static QSocket_t* net_freeSockets;
    public static int net_numsockets;

    public static readonly SizeBuf_t* net_message = NativeMemory.AllocZeroed<SizeBuf_t>();
    public static int net_activeconnections;

    public static int messagesSent;
    public static int messagesReceived;
    public static int unreliableMessagesSent;
    public static int unreliableMessagesReceived;

    public static readonly Cvar_t* net_messagetimeout = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* hostname = NativeMemory.AllocZeroed<Cvar_t>();

    public static double net_time;

    // hostcache_t hostcache[HOSTCACHESIZE]/hostCacheCount (net.h/net_main.c)
    public const int HOSTCACHESIZE = 8;
    public static int hostCacheCount;
    public static readonly HostCache_t* hostcache = NativeMemory.AllocArrayZeroed<HostCache_t>(HOSTCACHESIZE);

    // slist (server browser) state -- net_main.c's file-scope globals
    public static bool slistInProgress;
    public static bool slistSilent;
    public static bool slistLocal = true;
    private static double slistStartTime;
    private static int slistLastShown;
    private static readonly PollProcedure_t* slistSendProcedure = NativeMemory.AllocZeroed<PollProcedure_t>();
    private static readonly PollProcedure_t* slistPollProcedure = NativeMemory.AllocZeroed<PollProcedure_t>();
    private static PollProcedure_t* pollProcedureList;

    // these two macros are to make the code more readable (sfunc/dfunc)
    private static ref NetDriver_t Sfunc(QSocket_t* sock) => ref net_drivers[sock->Driver];
    private static ref NetDriver_t Dfunc() => ref net_drivers[net_driverlevel];

    // SetNetTime
    public static double SetNetTime()
    {
        net_time = Sys.FloatTime();
        return net_time;
    }

    /*
    ===================
    NET_NewQSocket

    Called by drivers when a new communications endpoint is required
    The sequence and buffer fields will be filled in properly
    ===================
    */
    public static QSocket_t* NewQSocket()
    {
        if (net_freeSockets == null)
            return null;

        if (net_activeconnections >= Svs.maxclients)
            return null;

        // get one from free list
        QSocket_t* sock = net_freeSockets;
        net_freeSockets = sock->Next;

        // add it to active list
        sock->Next = net_activeSockets;
        net_activeSockets = sock;

        sock->Disconnected = false;
        sock->ConnectTime = net_time;
        Q.StrCpy(sock->Address, "UNSET ADDRESS"u8);
        sock->Driver = net_driverlevel;
        sock->Socket = 0;
        sock->DriverData = null;
        sock->CanSend = true;
        sock->SendNext = false;
        sock->LastMessageTime = net_time;
        sock->AckSequence = 0;
        sock->SendSequence = 0;
        sock->UnreliableSendSequence = 0;
        sock->SendMessageLength = 0;
        sock->ReceiveSequence = 0;
        sock->UnreliableReceiveSequence = 0;
        sock->ReceiveMessageLength = 0;

        return sock;
    }

    // NET_FreeQSocket
    public static void FreeQSocket(QSocket_t* sock)
    {
        // remove it from active list
        if (sock == net_activeSockets)
        {
            net_activeSockets = net_activeSockets->Next;
        }
        else
        {
            QSocket_t* s;
            for (s = net_activeSockets; s != null; s = s->Next)
            {
                if (s->Next == sock)
                {
                    s->Next = sock->Next;
                    break;
                }
            }
            if (s == null)
                Sys.Error("NET_FreeQSocket: not active\n"u8);
        }

        // add it to free list
        sock->Next = net_freeSockets;
        net_freeSockets = sock;
        sock->Disconnected = true;
    }

    /*
    ===================
    NET_Connect
    ===================
    */
    public static QSocket_t* Connect(byte* host)
    {
        int numdrivers = net_numdrivers;

        SetNetTime();

        if (host != null && *host == 0)
            host = null;

        bool justDoIt = false;
        if (host != null)
        {
            if (Q.StrCaseCmp(host, "local"u8) == 0)
            {
                numdrivers = 1;
                justDoIt = true;
            }
            else if (hostCacheCount != 0)
            {
                int n;
                for (n = 0; n < hostCacheCount; n++)
                    if (Q.StrCaseCmp(host, hostcache[n].Name) == 0)
                    {
                        host = hostcache[n].CName;
                        break;
                    }
                if (n < hostCacheCount)
                    justDoIt = true;
            }
        }

        if (!justDoIt)
        {
            slistSilent = host != null;
            Slist_f();

            while (slistInProgress)
                Poll();

            if (host == null)
            {
                if (hostCacheCount != 1)
                    return null;
                host = hostcache[0].CName;
                Con.Printf($"Connecting to...\n{hostcache[0].Name} @ {host}\n\n");
            }

            if (hostCacheCount != 0)
            {
                for (int n = 0; n < hostCacheCount; n++)
                    if (Q.StrCaseCmp(host, hostcache[n].Name) == 0)
                    {
                        host = hostcache[n].CName;
                        break;
                    }
            }
        }

        // JustDoIt:
        for (net_driverlevel = 0; net_driverlevel < numdrivers; net_driverlevel++)
        {
            if (!net_drivers[net_driverlevel].Initialized)
                continue;
            QSocket_t* ret = net_drivers[net_driverlevel].Connect(host);
            if (ret != null)
                return ret;
        }

        if (host != null)
        {
            Con.Printf("\n"u8);
            PrintSlistHeader();
            PrintSlist();
            PrintSlistTrailer();
        }

        return null;
    }

    /*
    ===================
    NET_CheckNewConnections

    Driver 1+ (anything but loopback) is skipped unless `listening` is set,
    matching the original -- NetIp.CheckNewConnections would just report "no
    accept socket" anyway (Listen(true) is what opens it), but this avoids
    the per-frame syscall/Available check entirely while not listening.
    ===================
    */
    public static QSocket_t* CheckNewConnections()
    {
        SetNetTime();

        for (net_driverlevel = 0; net_driverlevel < net_numdrivers; net_driverlevel++)
        {
            if (!net_drivers[net_driverlevel].Initialized)
                continue;
            if (net_driverlevel != 0 && !listening)
                continue;
            QSocket_t* ret = net_drivers[net_driverlevel].CheckNewConnections();
            if (ret != null)
                return ret;
        }

        return null;
    }

    // NET_Close
    public static void Close(QSocket_t* sock)
    {
        if (sock == null)
            return;

        if (sock->Disconnected)
            return;

        SetNetTime();

        Sfunc(sock).Close(sock);

        FreeQSocket(sock);
    }

    /*
    =================
    NET_GetMessage

    If there is a complete message, return it in net_message

    returns 0 if no data is waiting
    returns 1 if a message was received
    returns -1 if connection is invalid
    =================
    */
    public static int GetMessage(QSocket_t* sock)
    {
        if (sock == null)
            return -1;

        if (sock->Disconnected)
        {
            Con.Printf("NET_GetMessage: disconnected socket\n"u8);
            return -1;
        }

        SetNetTime();

        int ret = Sfunc(sock).QGetMessage(sock);

        // see if this connection has timed out
        if (ret == 0 && sock->Driver != 0)
        {
            if (net_time - sock->LastMessageTime > net_messagetimeout->Value)
            {
                Close(sock);
                return -1;
            }
        }

        if (ret > 0)
        {
            if (sock->Driver != 0)
            {
                sock->LastMessageTime = net_time;
                if (ret == 1)
                    messagesReceived++;
                else if (ret == 2)
                    unreliableMessagesReceived++;
            }
        }

        return ret;
    }

    /*
    ==================
    NET_SendMessage

    Try to send a complete length+message unit over the reliable stream.
    returns 0 if the message cannot be delivered reliably, but the connection
            is still considered valid
    returns 1 if the message was sent properly
    returns -1 if the connection died
    ==================
    */
    public static int SendMessage(QSocket_t* sock, SizeBuf_t* data)
    {
        if (sock == null)
            return -1;

        if (sock->Disconnected)
        {
            Con.Printf("NET_SendMessage: disconnected socket\n"u8);
            return -1;
        }

        SetNetTime();
        int r = Sfunc(sock).QSendMessage(sock, data);
        if (r == 1 && sock->Driver != 0)
            messagesSent++;

        return r;
    }

    public static int SendUnreliableMessage(QSocket_t* sock, SizeBuf_t* data)
    {
        if (sock == null)
            return -1;

        if (sock->Disconnected)
        {
            Con.Printf("NET_SendMessage: disconnected socket\n"u8);
            return -1;
        }

        SetNetTime();
        int r = Sfunc(sock).SendUnreliableMessage(sock, data);
        if (r == 1 && sock->Driver != 0)
            unreliableMessagesSent++;

        return r;
    }

    /*
    ==================
    NET_CanSendMessage

    Returns true or false if the given qsocket can currently accept a
    message to be transmitted.
    ==================
    */
    public static bool CanSendMessage(QSocket_t* sock)
    {
        if (sock == null)
            return false;

        if (sock->Disconnected)
            return false;

        SetNetTime();

        return Sfunc(sock).CanSendMessage(sock);
    }

    /*
    NET_SendToAll

    This is a reliable *blocking* send to all attached clients. Ported now
    that Host.ShutdownServer (host.c) needs it; `state1`/`state2` are
    stackalloc'd (QuakeDef.MAX_SCOREBOARD-sized, matching the original's
    qboolean[MAX_SCOREBOARD]) rather than allocated, matching this port's
    other bounded-size stack buffers.
    */
    public static int SendToAll(SizeBuf_t* data, int blocktime)
    {
        bool* state1 = stackalloc bool[QuakeDef.MAX_SCOREBOARD];
        bool* state2 = stackalloc bool[QuakeDef.MAX_SCOREBOARD];
        int count = 0;

        for (int i = 0; i < Svs.maxclients; i++)
        {
            Client_t* host_client = &Svs.clients[i];
            if (host_client->NetConnection == null)
                continue;
            if (host_client->Active)
            {
                if (host_client->NetConnection->Driver == 0)
                {
                    SendMessage(host_client->NetConnection, data);
                    state1[i] = true;
                    state2[i] = true;
                    continue;
                }
                count++;
                state1[i] = false;
                state2[i] = false;
            }
            else
            {
                state1[i] = true;
                state2[i] = true;
            }
        }

        double start = Sys.FloatTime();
        while (count != 0)
        {
            count = 0;
            for (int i = 0; i < Svs.maxclients; i++)
            {
                Client_t* host_client = &Svs.clients[i];
                if (!state1[i])
                {
                    if (CanSendMessage(host_client->NetConnection))
                    {
                        state1[i] = true;
                        SendMessage(host_client->NetConnection, data);
                    }
                    else
                    {
                        GetMessage(host_client->NetConnection);
                    }
                    count++;
                    continue;
                }

                if (!state2[i])
                {
                    if (CanSendMessage(host_client->NetConnection))
                    {
                        state2[i] = true;
                    }
                    else
                    {
                        GetMessage(host_client->NetConnection);
                    }
                    count++;
                    continue;
                }
            }
            if ((Sys.FloatTime() - start) > blocktime)
                break;
        }
        return count;
    }

    /*
    ====================
    NET_Init
    ====================
    */
    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value)
    {
        cvar->Name = name;
        cvar->String = value;
        Cvar.RegisterVariable(cvar);
    }

    private static void MaxPlayersCmd() => MaxPlayers_f();

    private static void MaxPlayers_f()
    {
        if (Cmd.Argc() != 2)
        {
            Con.Printf($"\"maxplayers\" is \"{Svs.maxclients}\"\n");
            return;
        }

        if (Sv.active)
        {
            Con.Printf("maxplayers can not be changed while a server is running.\n"u8);
            return;
        }

        int n = Q.Atoi(Cmd.Argv(1));
        if (n < 1)
            n = 1;
        if (n > Svs.maxclientslimit)
        {
            n = Svs.maxclientslimit;
            Con.Printf($"\"maxplayers\" set to \"{n}\"\n");
        }

        if (n == 1 && listening)
            Cmd.AddText("listen 0\n"u8);
        if (n > 1 && !listening)
            Cmd.AddText("listen 1\n"u8);

        Svs.maxclients = n;
        Cvar.Set("deathmatch"u8, n == 1 ? "0"u8 : "1"u8);
    }

    /*
    ==================
    NET_Listen_f
    ==================
    */
    private static void ListenCmd() => Listen_f();

    public static void Listen_f()
    {
        if (Cmd.Argc() != 2)
        {
            Con.Printf($"\"listen\" is \"{(listening ? 1 : 0)}\"\n");
            return;
        }

        listening = Q.Atoi(Cmd.Argv(1)) != 0;

        for (net_driverlevel = 0; net_driverlevel < net_numdrivers; net_driverlevel++)
        {
            if (!net_drivers[net_driverlevel].Initialized)
                continue;
            net_drivers[net_driverlevel].Listen(listening);
        }
    }

    /*
    ==================
    NET_Port_f
    ==================
    */
    private static void PortCmd() => Port_f();

    public static void Port_f()
    {
        if (Cmd.Argc() != 2)
        {
            Con.Printf($"\"port\" is \"{net_hostport}\"\n");
            return;
        }

        int n = Q.Atoi(Cmd.Argv(1));
        if (n < 1 || n > 65534)
        {
            Con.Printf("Bad value, must be between 1 and 65534\n"u8);
            return;
        }

        DEFAULTnet_hostport = n;
        net_hostport = n;

        if (listening)
        {
            // force a change to the new port
            Cmd.AddText("listen 0\n"u8);
            Cmd.AddText("listen 1\n"u8);
        }
    }

    /*
    ====================
    NET_Init
    ====================
    */
    public static void Init()
    {
        int i = Com.CheckParm("-port"u8);
        if (i == 0)
            i = Com.CheckParm("-udpport"u8);
        if (i != 0)
        {
            if (i < Com.com_argc - 1)
                DEFAULTnet_hostport = Q.Atoi(Com.com_argv[i + 1]);
            else
                Sys.Error("NET_Init: you must specify a number after -port"u8);
        }
        net_hostport = DEFAULTnet_hostport;

        listening = Com.CheckParm("-listen"u8) != 0 || Cls.state == CActive.Dedicated;

        net_numsockets = Svs.maxclientslimit;
        if (Cls.state != CActive.Dedicated)
            net_numsockets++; // the local client's own socket

        SetNetTime();

        for (int n = 0; n < net_numsockets; n++)
        {
            QSocket_t* s = (QSocket_t*)Hunk.AllocName(sizeof(QSocket_t), "qsocket"u8);
            s->Next = net_freeSockets;
            net_freeSockets = s;
            s->Disconnected = true;
        }

        // allocate space for network message buffer
        SizeBuf_t* nm = net_message;
        Sz.Alloc(nm, NET_MAXMESSAGE);

        RegisterCvar(net_messagetimeout, "net_messagetimeout"u8, "300"u8);
        RegisterCvar(hostname, "hostname"u8, "UNNAMED"u8);

        Cmd.AddCommand("listen"u8, &ListenCmd);
        Cmd.AddCommand("maxplayers"u8, &MaxPlayersCmd);
        Cmd.AddCommand("port"u8, &PortCmd);
        Cmd.AddCommand("slist"u8, &SlistCmd);

        // initialize all the drivers
        net_drivers[0] = NetLoop.MakeDriver();
        net_drivers[1] = NetDgrm.MakeDriver();

        for (net_driverlevel = 0; net_driverlevel < net_numdrivers; net_driverlevel++)
        {
            int controlSocket = net_drivers[net_driverlevel].Init();
            if (controlSocket == -1)
                continue;
            net_drivers[net_driverlevel].Initialized = true;
            net_drivers[net_driverlevel].ControlSock = controlSocket;
            if (listening)
                net_drivers[net_driverlevel].Listen(true);
        }
    }

    // NET_Shutdown
    public static void Shutdown()
    {
        SetNetTime();

        for (QSocket_t* sock = net_activeSockets; sock != null; sock = sock->Next)
            Close(sock);

        for (net_driverlevel = 0; net_driverlevel < net_numdrivers; net_driverlevel++)
        {
            if (net_drivers[net_driverlevel].Initialized)
            {
                net_drivers[net_driverlevel].Shutdown();
                net_drivers[net_driverlevel].Initialized = false;
            }
        }
    }

    // NET_Poll -- the serial/modem config-restore block (configRestored) is
    // dropped with the serial driver; only the PollProcedure dispatch remains.
    public static void Poll()
    {
        SetNetTime();

        for (PollProcedure_t* pp = pollProcedureList; pp != null; pp = pp->Next)
        {
            if (pp->NextTime > net_time)
                break;
            pollProcedureList = pp->Next;
            pp->Procedure();
        }
    }

    // SchedulePollProcedure
    public static void SchedulePollProcedure(PollProcedure_t* proc, double timeOffset)
    {
        proc->NextTime = Sys.FloatTime() + timeOffset;
        PollProcedure_t* pp, prev;
        for (pp = pollProcedureList, prev = null; pp != null; pp = pp->Next)
        {
            if (pp->NextTime >= proc->NextTime)
                break;
            prev = pp;
        }

        if (prev == null)
        {
            proc->Next = pollProcedureList;
            pollProcedureList = proc;
            return;
        }

        proc->Next = pp;
        prev->Next = proc;
    }

    // PrintSlistHeader
    public static void PrintSlistHeader()
    {
        Con.Printf("Server          Map             Users\n"u8);
        Con.Printf("--------------- --------------- -----\n"u8);
        slistLastShown = 0;
    }

    // PrintSlist
    public static void PrintSlist()
    {
        int n;
        for (n = slistLastShown; n < hostCacheCount; n++)
            Con.Printf($"{FormatSlistEntry(n)}\n");
        slistLastShown = n;
    }

    /*
    FormatSlistEntry -- the "%-15.15s %-15.15s %2u/%2u" line (without the
    user counts when maxusers is 0) shared by PrintSlist and the slist menu
    (M_ServerList_Draw).
    */
    public static Utf8String FormatSlistEntry(int n)
    {
        ReadOnlySpan<byte> name = Clip(hostcache[n].Name, 15);
        ReadOnlySpan<byte> map = Clip(hostcache[n].Map, 15);
        if (hostcache[n].MaxUsers != 0)
        {
            Utf8StringHandler full = $"{name,-15} {map,-15} {hostcache[n].Users,2}/{hostcache[n].MaxUsers,2}";
            return full;
        }
        Utf8StringHandler line = $"{name,-15} {map,-15}";
        return line;
    }

    // "%.15s"
    private static ReadOnlySpan<byte> Clip(byte* s, int max)
    {
        ReadOnlySpan<byte> span = new(s, max);
        int len = span.IndexOf((byte)0);
        return len < 0 ? span : span[..len];
    }

    // PrintSlistTrailer
    public static void PrintSlistTrailer()
    {
        if (hostCacheCount != 0)
            Con.Printf("== end list ==\n\n"u8);
        else
            Con.Printf("No Quake servers found.\n\n"u8);
    }

    // NET_Slist_f
    public static void Slist_f()
    {
        if (slistInProgress)
            return;

        if (!slistSilent)
        {
            Con.Printf("Looking for Quake servers...\n"u8);
            PrintSlistHeader();
        }

        slistInProgress = true;
        slistStartTime = Sys.FloatTime();

        slistSendProcedure->Procedure = &Slist_Send;
        slistPollProcedure->Procedure = &Slist_Poll;
        SchedulePollProcedure(slistSendProcedure, 0.0);
        SchedulePollProcedure(slistPollProcedure, 0.1);

        hostCacheCount = 0;
    }

    // Slist_Send
    private static void Slist_Send()
    {
        for (net_driverlevel = 0; net_driverlevel < net_numdrivers; net_driverlevel++)
        {
            if (!slistLocal && net_driverlevel == 0)
                continue;
            if (!net_drivers[net_driverlevel].Initialized)
                continue;
            net_drivers[net_driverlevel].SearchForHosts(true);
        }

        if ((Sys.FloatTime() - slistStartTime) < 0.5)
            SchedulePollProcedure(slistSendProcedure, 0.75);
    }

    // Slist_Poll
    private static void Slist_Poll()
    {
        for (net_driverlevel = 0; net_driverlevel < net_numdrivers; net_driverlevel++)
        {
            if (!slistLocal && net_driverlevel == 0)
                continue;
            if (!net_drivers[net_driverlevel].Initialized)
                continue;
            net_drivers[net_driverlevel].SearchForHosts(false);
        }

        if (!slistSilent)
            PrintSlist();

        if ((Sys.FloatTime() - slistStartTime) < 1.5)
        {
            SchedulePollProcedure(slistPollProcedure, 0.1);
            return;
        }

        if (!slistSilent)
            PrintSlistTrailer();
        slistInProgress = false;
        slistSilent = false;
        slistLocal = true;
    }

    private static void SlistCmd() => Slist_f();

    // Test-only: clears the browser/poll state so Quake.UnitTests
    // (InternalsVisibleTo) can start from a clean slate.
    internal static void ResetSlistForTests()
    {
        pollProcedureList = null;
        hostCacheCount = 0;
        slistInProgress = false;
        slistSilent = false;
        slistLocal = true;
    }
}
