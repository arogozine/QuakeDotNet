// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// net_dgrm.c -- the reliable/unreliable datagram protocol and connection
/// handshake that sits on top of a lan driver (net_landriver_t). This port
/// has exactly one lan driver (NetIp.cs), so the original's
/// net_landrivers[]/sfunc/dfunc indirection table is skipped and every
/// dfunc./sfunc. call site below calls NetIp's static methods directly
/// (CLAUDE.md rule 8: justified here by there never being a second driver
/// to dispatch to -- Net.cs's own net_drivers[]/NetDriver_t table IS kept
/// with its full delegate-pointer shape since net_driverlevel indexes real
/// per-connection state (sock->driver), where this file's dfunc/sfunc never
/// varies). CCREQ_SERVER_INFO/PLAYER_INFO/RULE_INFO are ported (small,
/// self-contained wire responders), as is Datagram_SearchForHosts (the
/// multiplayer server-browser query/reply side). BAN_TEST ("ban") and
/// Test_f/Test2_f ("test"/"test2", debug-only packet-probe tools) are
/// dropped -- see Net.cs's type-level comment. Menu.cs's connect-failure UI feedback
/// (m_return_state/m_return_onerror/m_return_reason, key_dest = key_menu) is
/// ported too, via Menu.ConnectFailed/Menu.ConnectSucceeded, called from
/// Connect below at the same points the original's ErrorReturn/ErrorReturn2
/// labels and success path do.
/// </summary>
public static unsafe class NetDgrm
{
    // NET_HEADERSIZE/NETFLAG_*/NET_PROTOCOL_VERSION/CCREQ_*/CCREP_* (net.h)
    private const int NET_HEADERSIZE = 8; // 2 * sizeof(uint)
    private const uint NETFLAG_LENGTH_MASK = 0x0000ffff;
    private const uint NETFLAG_DATA = 0x00010000;
    private const uint NETFLAG_ACK = 0x00020000;
    private const uint NETFLAG_EOM = 0x00080000;
    private const uint NETFLAG_UNRELIABLE = 0x00100000;
    private const uint NETFLAG_CTL = 0x80000000;

    private const int NET_PROTOCOL_VERSION = 3;



    // packetBuffer (net_dgrm.c's own file-scope global)
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PacketBuffer_t
    {
        public uint Length;
        public uint Sequence;
        public fixed byte Data[QuakeDef.MAX_MARK_V_DATAGRAM];
    }
    private static readonly PacketBuffer_t* packetBuffer = NativeMemory.AllocZeroed<PacketBuffer_t>();

    /* statistic counters */
    public static int packetsSent;
    public static int packetsReSent;
    public static int packetsReceived;
    public static int receivedDuplicateCount;
    public static int shortPacketCount;
    public static int droppedDatagrams;


    // Net.net_message is a native sizebuf_t (rule 9), so it is used directly
    // at this file's many write-then-send call sites.
    private static SizeBuf_t* Nm => Net.net_message;

    // MSG_ReadByte/MSG_ReadString/... read Net.net_message, i.e. whatever
    // NetIp.Read just placed there (the original has a single global net_message).
    private static void BeginReadingNetMessage()
    {
        Msg.BeginReading();
    }

    // Writes the NETFLAG_CTL-tagged length header into the front of
    // Net.net_message once its payload is built, matching the original's
    // `*((int *)net_message.data) = BigLong(NETFLAG_CTL | (cursize & MASK));`
    // in-place patch.
    private static void PatchCtlHeader()
    {
        SizeBuf_t* nm = Net.net_message;
        {
            int header = Com.BigLong((int)(NETFLAG_CTL | ((uint)nm->CurSize & NETFLAG_LENGTH_MASK)));
            *(int*)nm->Data = header;
        }
    }

    /*
    ====================
    Datagram_SendMessage
    ====================
    */
    public static int SendMessage(QSocket_t* sock, SizeBuf_t* data)
    {
        Q.MemCpy(sock->SendMessage, data->Data, data->CurSize);
        sock->SendMessageLength = data->CurSize;

        uint dataLen, eom;
        if (data->CurSize <= Sv.host_protocol_datagram_maxsize)
        {
            dataLen = (uint)data->CurSize;
            eom = NETFLAG_EOM;
        }
        else
        {
            dataLen = (uint)Sv.host_protocol_datagram_maxsize;
            eom = 0;
        }
        uint packetLen = (uint)(NET_HEADERSIZE + dataLen);

        packetBuffer->Length = (uint)Com.BigLong((int)(packetLen | (NETFLAG_DATA | eom)));
        packetBuffer->Sequence = (uint)Com.BigLong((int)sock->SendSequence++);
        Q.MemCpy(packetBuffer->Data, sock->SendMessage, (int)dataLen);

        sock->CanSend = false;

        if (NetIp.Write(sock->Socket, (byte*)packetBuffer, (int)packetLen, &sock->Addr) == -1)
        {
            return -1;
        }

        sock->LastSendTime = Net.net_time;
        packetsSent++;
        return 1;
    }

    // SendMessageNext
    private static int SendMessageNext(QSocket_t* sock)
    {
        uint dataLen, eom;
        if (sock->SendMessageLength <= Sv.host_protocol_datagram_maxsize)
        {
            dataLen = (uint)sock->SendMessageLength;
            eom = NETFLAG_EOM;
        }
        else
        {
            dataLen = (uint)Sv.host_protocol_datagram_maxsize;
            eom = 0;
        }
        uint packetLen = (uint)(NET_HEADERSIZE + dataLen);

        packetBuffer->Length = (uint)Com.BigLong((int)(packetLen | (NETFLAG_DATA | eom)));
        packetBuffer->Sequence = (uint)Com.BigLong((int)sock->SendSequence++);
        Q.MemCpy(packetBuffer->Data, sock->SendMessage, (int)dataLen);

        sock->SendNext = false;

        if (NetIp.Write(sock->Socket, (byte*)packetBuffer, (int)packetLen, &sock->Addr) == -1)
        {
            return -1;
        }

        sock->LastSendTime = Net.net_time;
        packetsSent++;
        return 1;
    }

    // ReSendMessage
    private static int ReSendMessage(QSocket_t* sock)
    {
        uint dataLen, eom;
        if (sock->SendMessageLength <= Sv.host_protocol_datagram_maxsize)
        {
            dataLen = (uint)sock->SendMessageLength;
            eom = NETFLAG_EOM;
        }
        else
        {
            dataLen = (uint)Sv.host_protocol_datagram_maxsize;
            eom = 0;
        }
        uint packetLen = (uint)(NET_HEADERSIZE + dataLen);

        packetBuffer->Length = (uint)Com.BigLong((int)(packetLen | (NETFLAG_DATA | eom)));
        packetBuffer->Sequence = (uint)Com.BigLong((int)(sock->SendSequence - 1));
        Q.MemCpy(packetBuffer->Data, sock->SendMessage, (int)dataLen);

        sock->SendNext = false;

        if (NetIp.Write(sock->Socket, (byte*)packetBuffer, (int)packetLen, &sock->Addr) == -1)
        {
            return -1;
        }

        sock->LastSendTime = Net.net_time;
        packetsReSent++;
        return 1;
    }

    // Datagram_CanSendMessage
    public static bool CanSendMessage(QSocket_t* sock)
    {
        if (sock->SendNext)
        {
            SendMessageNext(sock);
        }

        return sock->CanSend;
    }

    // Datagram_SendUnreliableMessage
    public static int SendUnreliableMessage(QSocket_t* sock, SizeBuf_t* data)
    {
        int packetLen = NET_HEADERSIZE + data->CurSize;

        packetBuffer->Length = (uint)Com.BigLong((int)((uint)packetLen | NETFLAG_UNRELIABLE));
        packetBuffer->Sequence = (uint)Com.BigLong((int)sock->UnreliableSendSequence++);
        Q.MemCpy(packetBuffer->Data, data->Data, data->CurSize);

        if (NetIp.Write(sock->Socket, (byte*)packetBuffer, packetLen, &sock->Addr) == -1)
        {
            return -1;
        }

        packetsSent++;
        return 1;
    }

    /*
    ====================
    Datagram_GetMessage
    ====================
    */
    public static int GetMessage(QSocket_t* sock)
    {
        if (!sock->CanSend)
        {
            if ((Net.net_time - sock->LastSendTime) > 1.0)
            {
                ReSendMessage(sock);
            }
        }

        int ret = 0;

        while (true)
        {
            QSockAddr_t readaddr;
            uint length;
            length = (uint)NetIp.Read(sock->Socket, (byte*)packetBuffer, NET_HEADERSIZE + Sv.host_protocol_datagram_maxsize, &readaddr);

            if (length == 0)
            {
                break;
            }

            if (unchecked((int)length) == -1)
            {
                Con.Printf("Read error\n"u8);
                return -1;
            }

            if (NetIp.AddrCompare(&readaddr, &sock->Addr) != 0)
            {
                continue; // forged/stray packet
            }

            if (length < NET_HEADERSIZE)
            {
                shortPacketCount++;
                continue;
            }

            uint rawLength;
            uint sequence;
            rawLength = (uint)Com.BigLong((int)packetBuffer->Length);
            sequence = (uint)Com.BigLong((int)packetBuffer->Sequence);
            uint flags = rawLength & ~NETFLAG_LENGTH_MASK;
            uint msgLength = rawLength & NETFLAG_LENGTH_MASK;

            if ((flags & NETFLAG_CTL) != 0)
            {
                continue;
            }

            packetsReceived++;

            if ((flags & NETFLAG_UNRELIABLE) != 0)
            {
                if (sequence < sock->UnreliableReceiveSequence)
                {
                    ret = 0;
                    break;
                }
                if (sequence != sock->UnreliableReceiveSequence)
                {
                    droppedDatagrams += (int)(sequence - sock->UnreliableReceiveSequence);
                }
                sock->UnreliableReceiveSequence = sequence + 1;

                msgLength -= NET_HEADERSIZE;

                SizeBuf_t* nm = Net.net_message;
                Sz.Clear(nm);
                Sz.Write(nm, packetBuffer->Data, (int)msgLength);

                ret = 2;
                break;
            }

            if ((flags & NETFLAG_ACK) != 0)
            {
                if (sequence != sock->SendSequence - 1)
                {
                    continue; // stale ACK
                }

                if (sequence == sock->AckSequence)
                {
                    sock->AckSequence++;
                }
                else
                {
                    continue; // duplicate ACK
                }

                sock->SendMessageLength -= Sv.host_protocol_datagram_maxsize;
                if (sock->SendMessageLength > 0)
                {
                    Q.MemCpy(sock->SendMessage, sock->SendMessage + Sv.host_protocol_datagram_maxsize, sock->SendMessageLength);
                    sock->SendNext = true;
                }
                else
                {
                    sock->SendMessageLength = 0;
                    sock->CanSend = true;
                }
                continue;
            }

            if ((flags & NETFLAG_DATA) != 0)
            {
                packetBuffer->Length = (uint)Com.BigLong((int)(NET_HEADERSIZE | NETFLAG_ACK));
                packetBuffer->Sequence = (uint)Com.BigLong((int)sequence);
                NetIp.Write(sock->Socket, (byte*)packetBuffer, NET_HEADERSIZE, &readaddr);

                if (sequence != sock->ReceiveSequence)
                {
                    receivedDuplicateCount++;
                    continue;
                }
                sock->ReceiveSequence++;

                msgLength -= NET_HEADERSIZE;

                if ((flags & NETFLAG_EOM) != 0)
                {
                    SizeBuf_t* nm = Net.net_message;
                    Sz.Clear(nm);
                    Sz.Write(nm, sock->ReceiveMessage, sock->ReceiveMessageLength);
                    Sz.Write(nm, packetBuffer->Data, (int)msgLength);
                    sock->ReceiveMessageLength = 0;

                    ret = 1;
                    break;
                }

                Q.MemCpy(sock->ReceiveMessage + sock->ReceiveMessageLength, packetBuffer->Data, (int)msgLength);
                sock->ReceiveMessageLength += (int)msgLength;
                continue;
            }
        }

        if (sock->SendNext)
        {
            SendMessageNext(sock);
        }

        return ret;
    }

    /*
    ====================
    Datagram_Init
    ====================
    */
    public static int Init()
    {
        if (Com.CheckParm("-nolan"u8) != 0)
        {
            return -1;
        }

        int csock = NetIp.Init();
        return csock;
    }

    // Datagram_Shutdown
    public static void Shutdown() => NetIp.Shutdown();

    // Datagram_Close
    public static void Close(QSocket_t* sock) => NetIp.CloseSocket(sock->Socket);

    // Datagram_SearchForHosts/_Datagram_SearchForHosts -- merged: this port
    // has one lan driver (NetIp), so the net_landriverlevel loop collapses
    // to a single pass and hostcache[].ldriver is always 0.
    public static void SearchForHosts(bool xmit)
    {
        QSockAddr_t readaddr, myaddr;
        int controlSock = Net.net_drivers[Net.net_driverlevel].ControlSock;

        if (Net.hostCacheCount == Net.HOSTCACHESIZE)
        {
            return;
        }

        NetIp.GetSocketAddr(controlSock, &myaddr);
        if (xmit)
        {
            Sz.Clear(Nm);
            // save space for the header, filled in later
            Msg.WriteLong(Nm, 0);
            Msg.WriteByte(Nm, (int)NetControlRequest.ServerInfo);
            Msg.WriteString(Nm, "QUAKE"u8);
            Msg.WriteByte(Nm, NET_PROTOCOL_VERSION);
            PatchCtlHeader();
            NetIp.Broadcast(controlSock, Nm->Data, Nm->CurSize);
            Sz.Clear(Nm);
        }

        int ret;
        while ((ret = NetIp.Read(controlSock, Nm->Data, Nm->MaxSize, &readaddr)) > 0)
        {
            if (ret < sizeof(int))
            {
                continue;
            }

            Nm->CurSize = ret;

            // don't answer our own query
            if (NetIp.AddrCompare(&readaddr, &myaddr) >= 0)
            {
                continue;
            }

            // is the cache full?
            if (Net.hostCacheCount == Net.HOSTCACHESIZE)
            {
                continue;
            }

            BeginReadingNetMessage();
            int control = Com.BigLong(*(int*)Nm->Data);
            Msg.ReadLong();
            if (control == -1)
            {
                continue;
            }

            if ((control & ~(int)NETFLAG_LENGTH_MASK) != unchecked((int)NETFLAG_CTL))
            {
                continue;
            }

            if ((control & (int)NETFLAG_LENGTH_MASK) != ret)
            {
                continue;
            }

            if (Msg.ReadByte() != (int)NetControlReply.ServerInfo)
            {
                continue;
            }

            NetIp.GetAddrFromName(Msg.ReadString(), &readaddr);
            // search the cache for this server
            HostCache_t* hc = Net.hostcache;
            int n;
            for (n = 0; n < Net.hostCacheCount; n++)
            {
                if (NetIp.AddrCompare(&readaddr, &hc[n].Addr) == 0)
                {
                    break;
                }
            }

            // is it already there?
            if (n < Net.hostCacheCount)
            {
                continue;
            }

            // add it
            Net.hostCacheCount++;
            Q.StrCpy(hc[n].Name, Msg.ReadString());
            Q.StrCpy(hc[n].Map, Msg.ReadString());
            hc[n].Users = Msg.ReadByte();
            hc[n].MaxUsers = Msg.ReadByte();
            if (Msg.ReadByte() != NET_PROTOCOL_VERSION)
            {
                Q.StrCpy(hc[n].CName, hc[n].Name);
                hc[n].CName[14] = 0;
                Q.StrCpy(hc[n].Name, "*"u8);
                Q.StrCat(hc[n].Name, hc[n].CName);
            }
            hc[n].Addr = readaddr;
            hc[n].Driver = Net.net_driverlevel;
            hc[n].LDriver = 0;
            WriteCName(hc[n].CName, &readaddr);

            // check for a name conflict
            for (int i = 0; i < Net.hostCacheCount; i++)
            {
                if (i == n)
                {
                    continue;
                }

                if (Q.StrCaseCmp(hc[n].Name, hc[i].Name) == 0)
                {
                    i = Q.StrLen(hc[n].Name);
                    if (i < 15 && hc[n].Name[i - 1] > '8')
                    {
                        hc[n].Name[i] = (byte)'0';
                        hc[n].Name[i + 1] = 0;
                    }
                    else
                    {
                        hc[n].Name[i - 1]++;
                    }

                    i = -1;
                }
            }
        }
    }

    // Q_strcpy(hostcache[n].cname, dfunc.AddrToString(&readaddr)) -- cname is
    // char[32], so the string is clipped to fit.
    private static void WriteCName(byte* dest, QSockAddr_t* addr)
    {
        string s = NetIp.AddrToString(addr);
        int len = Math.Min(s.Length, 31);
        for (int k = 0; k < len; k++)
        {
            dest[k] = (byte)s[k];
        }

        dest[len] = 0;
    }

    // Datagram_Listen
    public static void Listen(bool state) => NetIp.Listen(state);

    /*
    ====================
    _Datagram_CheckNewConnections / Datagram_CheckNewConnections
    ====================
    */
    public static QSocket_t* CheckNewConnections()
    {
        int acceptsock = NetIp.CheckNewConnections();
        if (acceptsock == -1)
        {
            return null;
        }

        Sz.Clear(Nm);

        QSockAddr_t clientaddr;
        int len = NetIp.Read(acceptsock, Nm->Data, Nm->MaxSize, &clientaddr);
        if (len < sizeof(int))
        {
            return null;
        }

        Nm->CurSize = len;

        BeginReadingNetMessage();
        int control = Com.BigLong(*(int*)Net.net_message->Data);
        Msg.ReadLong();
        if (control == -1)
        {
            return null;
        }

        if ((control & ~(int)NETFLAG_LENGTH_MASK) != unchecked((int)NETFLAG_CTL))
        {
            return null;
        }

        if ((control & (int)NETFLAG_LENGTH_MASK) != len)
        {
            return null;
        }

        int command = Msg.ReadByte();

        if (command == (int)NetControlRequest.ServerInfo)
        {
            if (Q.StrCmp(Msg.ReadString(), "QUAKE"u8) != 0)
            {
                return null;
            }

            Sz.Clear(Nm);
            Msg.WriteLong(Nm, 0);
            Msg.WriteByte(Nm, (int)NetControlReply.ServerInfo);
            QSockAddr_t newaddr;
            NetIp.GetSocketAddr(acceptsock, &newaddr);
            Msg.WriteString(Nm, Com.Va(NetIp.AddrToString(&newaddr)));
            Msg.WriteString(Nm, Net.hostname->String);
            Msg.WriteString(Nm, Sv.Name);
            Msg.WriteByte(Nm, Net.net_activeconnections);
            Msg.WriteByte(Nm, Svs.maxclients);
            Msg.WriteByte(Nm, NET_PROTOCOL_VERSION);
            PatchCtlHeader();
            NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
            Sz.Clear(Nm);
            return null;
        }

        if (command == (int)NetControlRequest.PlayerInfo)
        {
            int playerNumber = Msg.ReadByte();
            int activeNumber = -1;
            int clientNumber;
            Client_t* client = null;
            for (clientNumber = 0; clientNumber < Svs.maxclients; clientNumber++)
            {
                client = &Svs.clients[clientNumber];
                if (client->Active)
                {
                    activeNumber++;
                    if (activeNumber == playerNumber)
                    {
                        break;
                    }
                }
            }
            if (clientNumber == Svs.maxclients)
            {
                return null;
            }

            Sz.Clear(Nm);
            SizeBuf_t* nmp = Nm;
            Msg.WriteLong(nmp, 0);
            Msg.WriteByte(nmp, (int)NetControlReply.PlayerInfo);
            Msg.WriteByte(nmp, playerNumber);
            Msg.WriteString(nmp, client->Name);
            Msg.WriteLong(nmp, client->Colors);
            Msg.WriteLong(nmp, (int)client->Edict->V.Frags);
            Msg.WriteLong(nmp, (int)(Net.net_time - client->NetConnection->ConnectTime));
            Msg.WriteString(nmp, client->NetConnection->Address);
            PatchCtlHeader();
            NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
            Sz.Clear(Nm);
            return null;
        }

        if (command == (int)NetControlRequest.RuleInfo)
        {
            byte* prevCvarName = Msg.ReadString();
            Cvar_t* var;
            if (*prevCvarName != 0)
            {
                var = Cvar.FindVar(prevCvarName);
                if (var == null)
                {
                    return null;
                }

                var = var->Next;
            }
            else
            {
                var = Cvar.cvar_vars;
            }

            while (var != null)
            {
                if (var->Server)
                {
                    break;
                }

                var = var->Next;
            }

            Sz.Clear(Nm);
            SizeBuf_t* nmp = Nm;
            Msg.WriteLong(nmp, 0);
            Msg.WriteByte(nmp, (int)NetControlReply.RuleInfo);
            if (var != null)
            {
                Msg.WriteString(nmp, var->Name);
                Msg.WriteString(nmp, var->String);
            }
            PatchCtlHeader();
            NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
            Sz.Clear(Nm);
            return null;
        }

        if (command != (int)NetControlRequest.Connect)
        {
            return null;
        }

        if (Q.StrCmp(Msg.ReadString(), "QUAKE"u8) != 0)
        {
            return null;
        }

        if (Msg.ReadByte() != NET_PROTOCOL_VERSION)
        {
            Sz.Clear(Nm);
            SizeBuf_t* nmp = Nm;
            Msg.WriteLong(nmp, 0);
            Msg.WriteByte(nmp, (int)NetControlReply.Reject);
            Msg.WriteString(nmp, "Incompatible version.\n"u8);
            PatchCtlHeader();
            NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
            Sz.Clear(Nm);
            return null;
        }

        // see if this guy is already connected
        for (QSocket_t* s = Net.net_activeSockets; s != null; s = s->Next)
        {
            if (s->Driver != Net.net_driverlevel)
            {
                continue;
            }

            int ret = NetIp.AddrCompare(&clientaddr, &s->Addr);
            if (ret >= 0)
            {
                if (ret == 0 && Net.net_time - s->ConnectTime < 2.0)
                {
                    // duplicate connection request -- send a duplicate reply
                    Sz.Clear(Nm);
                    SizeBuf_t* nmp = Nm;
                    Msg.WriteLong(nmp, 0);
                    Msg.WriteByte(nmp, (int)NetControlReply.Accept);
                    QSockAddr_t newaddr;
                    NetIp.GetSocketAddr(s->Socket, &newaddr);
                    Msg.WriteLong(nmp, NetIp.GetSocketPort(&newaddr));
                    PatchCtlHeader();
                    NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
                    Sz.Clear(Nm);
                    return null;
                }
                // reconnecting after a crash/disconnect -- close the old
                // socket and let their retry get them back in
                Net.Close(s);
                return null;
            }
        }

        // allocate a QSocket
        QSocket_t* sock = Net.NewQSocket();
        if (sock == null)
        {
            Sz.Clear(Nm);
            SizeBuf_t* nmp = Nm;
            Msg.WriteLong(nmp, 0);
            Msg.WriteByte(nmp, (int)NetControlReply.Reject);
            Msg.WriteString(nmp, "Server is full.\n"u8);
            PatchCtlHeader();
            NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
            Sz.Clear(Nm);
            return null;
        }

        // allocate a network socket, connect to the client
        int newsock = NetIp.OpenSocket(0);
        if (newsock == -1)
        {
            Net.FreeQSocket(sock);
            return null;
        }
        if (NetIp.Connect(newsock, &clientaddr) == -1)
        {
            NetIp.CloseSocket(newsock);
            Net.FreeQSocket(sock);
            return null;
        }

        sock->Socket = newsock;
        sock->LanDriver = 0;
        sock->Addr = clientaddr;
        byte* addrStr = Com.Va(NetIp.AddrToString(&clientaddr));
        Q.StrCpy(sock->Address, addrStr);

        // send him back the info about the connection he has been allocated
        Sz.Clear(Nm);
        {
            SizeBuf_t* nmp = Nm;
            Msg.WriteLong(nmp, 0);
            Msg.WriteByte(nmp, (int)NetControlReply.Accept);
            QSockAddr_t newaddr;
            NetIp.GetSocketAddr(newsock, &newaddr);
            Msg.WriteLong(nmp, NetIp.GetSocketPort(&newaddr));
            PatchCtlHeader();
        }
        NetIp.Write(acceptsock, Nm->Data, Nm->CurSize, &clientaddr);
        Sz.Clear(Nm);

        return sock;
    }

    /*
    ====================
    _Datagram_Connect / Datagram_Connect
    ====================
    */
    public static QSocket_t* Connect(byte* host)
    {
        QSockAddr_t sendaddr;
        if (NetIp.GetAddrFromName(host, &sendaddr) == -1)
        {
            return null;
        }

        int newsock = NetIp.OpenSocket(0);
        if (newsock == -1)
        {
            return null;
        }

        QSocket_t* sock = Net.NewQSocket();
        if (sock == null)
        {
            NetIp.CloseSocket(newsock);
            Menu.ConnectFailed(null);
            return null;
        }
        sock->Socket = newsock;
        sock->LanDriver = 0;

        if (NetIp.Connect(newsock, &sendaddr) == -1)
        {
            Net.FreeQSocket(sock);
            NetIp.CloseSocket(newsock);
            Menu.ConnectFailed(null);
            return null;
        }

        Con.Printf("trying...\n"u8);
        double startTime = Net.net_time;

        int ret = 0;
        for (int reps = 0; reps < 3; reps++)
        {
            Sz.Clear(Nm);
            {
                SizeBuf_t* nmp = Nm;
                Msg.WriteLong(nmp, 0);
                Msg.WriteByte(nmp, (int)NetControlRequest.Connect);
                Msg.WriteString(nmp, "QUAKE"u8);
                Msg.WriteByte(nmp, NET_PROTOCOL_VERSION);
                PatchCtlHeader();
            }
            NetIp.Write(newsock, Nm->Data, Nm->CurSize, &sendaddr);
            Sz.Clear(Nm);

            do
            {
                QSockAddr_t readaddr;
                ret = NetIp.Read(newsock, Nm->Data, Nm->MaxSize, &readaddr);

                if (ret > 0)
                {
                    if (NetIp.AddrCompare(&readaddr, &sendaddr) != 0)
                    {
                        ret = 0;
                        continue;
                    }
                    if (ret < sizeof(int))
                    {
                        ret = 0;
                        continue;
                    }

                    Nm->CurSize = ret;
                    BeginReadingNetMessage();

                    int control = Com.BigLong(*(int*)Net.net_message->Data);
                    Msg.ReadLong();
                    if (control == -1 ||
                        (control & ~(int)NETFLAG_LENGTH_MASK) != unchecked((int)NETFLAG_CTL) ||
                        (control & (int)NETFLAG_LENGTH_MASK) != ret)
                    {
                        ret = 0;
                        continue;
                    }
                }
            }
            while (ret == 0 && (Net.SetNetTime() - startTime) < 2.5);

            if (ret != 0)
            {
                break;
            }

            Con.Printf("still trying...\n"u8);
            startTime = Net.SetNetTime();
        }

        byte* reason;
        if (ret == 0)
        {
            reason = new Utf8String("No Response"u8);
            Con.Printf($"{reason}\n");
            goto ErrorReturn;
        }

        if (ret == -1)
        {
            reason = new Utf8String("Network Error"u8);
            Con.Printf($"{reason}\n");
            goto ErrorReturn;
        }

        ret = Msg.ReadByte();
        if (ret == (int)NetControlReply.Reject)
        {
            reason = Msg.ReadString();
            Con.Printf($"{reason}");
            goto ErrorReturn;
        }

        if (ret == (int)NetControlReply.Accept)
        {
            sock->Addr = sendaddr;
            NetIp.SetSocketPort(&sock->Addr, Msg.ReadLong());
        }
        else
        {
            reason = new Utf8String("Bad Response"u8);
            Con.Printf($"{reason}\n");
            goto ErrorReturn;
        }

        NetIp.GetNameFromAddr(&sock->Addr, sock->Address);

        Con.Printf("Connection accepted\n"u8);
        sock->LastMessageTime = Net.SetNetTime();

        // switch the connection to the specified address
        if (NetIp.Connect(newsock, &sock->Addr) == -1)
        {
            reason = new Utf8String("Connect to Game failed"u8);
            Con.Printf($"{reason}\n");
            goto ErrorReturn;
        }

        Menu.ConnectSucceeded();
        return sock;

    ErrorReturn:
        Net.FreeQSocket(sock);
        NetIp.CloseSocket(newsock);
        Menu.ConnectFailed(reason);
        return null;
    }

    /*
    ==================
    NET_Stats_f
    ==================
    */
    private static void StatsCmd() => Stats_f();

    public static void Stats_f()
    {
        if (Cmd.Argc() == 1)
        {
            Con.Printf($"unreliable messages sent   = {Net.unreliableMessagesSent}\n");
            Con.Printf($"unreliable messages recv   = {Net.unreliableMessagesReceived}\n");
            Con.Printf($"reliable messages sent     = {Net.messagesSent}\n");
            Con.Printf($"reliable messages received = {Net.messagesReceived}\n");
            Con.Printf($"packetsSent                = {packetsSent}\n");
            Con.Printf($"packetsReSent              = {packetsReSent}\n");
            Con.Printf($"packetsReceived            = {packetsReceived}\n");
            Con.Printf($"receivedDuplicateCount     = {receivedDuplicateCount}\n");
            Con.Printf($"shortPacketCount           = {shortPacketCount}\n");
            Con.Printf($"droppedDatagrams           = {droppedDatagrams}\n");
        }
    }

    // Populates a NetDriver_t (net.h's net_driver_t) with this file's entry
    // points, for Net.Init to install as net_drivers[1].
    public static NetDriver_t MakeDriver()
    {
        NetDriver_t d = default;
        d.Init = &Init;
        d.Shutdown = &Shutdown;
        d.Listen = &Listen;
        d.SearchForHosts = &SearchForHosts;
        d.Connect = &Connect;
        d.CheckNewConnections = &CheckNewConnections;
        d.QGetMessage = &GetMessage;
        d.QSendMessage = &SendMessage;
        d.SendUnreliableMessage = &SendUnreliableMessage;
        d.CanSendMessage = &CanSendMessage;
        d.Close = &Close;
        Cmd.AddCommand("net_stats"u8, &StatsCmd);
        return d;
    }
}
