// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;

namespace Quake.Engine;

/// <summary>
/// net_loop.c -- the in-process loopback driver: single-player Quake always
/// runs an embedded server, and the local client talks to it through this
/// same-process socket pair rather than a real network transport.
/// </summary>
public static unsafe class NetLoop
{
    public static bool localconnectpending;
    public static QSocket_t* loop_client;
    public static QSocket_t* loop_server;

    // Loop_Init -- a dedicated server has no local client to loop back to.
    public static int Init() => Cls.state == CActive.Dedicated ? -1 : 0;

    // Loop_Shutdown
    public static void Shutdown()
    {
    }

    // Loop_Listen -- the loopback pair is always "listening" (it's just two
    // in-process qsocket_t's); nothing to toggle.
    public static void Listen(bool state)
    {
    }

    // Loop_SearchForHosts
    public static void SearchForHosts(bool xmit)
    {
        if (!Sv.active)
            return;

        HostCache_t* hc = Net.hostcache;
        Net.hostCacheCount = 1;
        if (Q.StrCmp(Net.hostname->String, "UNNAMED"u8) == 0)
            Q.StrCpy(hc[0].Name, "local"u8);
        else
            Q.StrCpy(hc[0].Name, Net.hostname->String);
        Q.StrCpy(hc[0].Map, Sv.Name);
        hc[0].Users = Net.net_activeconnections;
        hc[0].MaxUsers = Svs.maxclients;
        hc[0].Driver = Net.net_driverlevel;
        Q.StrCpy(hc[0].CName, "local"u8);
    }

    // Loop_Connect
    public static QSocket_t* Connect(byte* host)
    {
        if (host != null && Q.StrCmp(host, "local"u8) != 0)
            return null;

        localconnectpending = true;

        if (loop_client == null)
        {
            loop_client = Net.NewQSocket();
            if (loop_client == null)
            {
                Con.Printf("Loop_Connect: no qsocket available\n"u8);
                return null;
            }
            Q.StrCpy(loop_client->Address, "localhost"u8);
        }
        loop_client->ReceiveMessageLength = 0;
        loop_client->SendMessageLength = 0;
        loop_client->CanSend = true;

        if (loop_server == null)
        {
            loop_server = Net.NewQSocket();
            if (loop_server == null)
            {
                Con.Printf("Loop_Connect: no qsocket available\n"u8);
                return null;
            }
            Q.StrCpy(loop_server->Address, "LOCAL"u8);
        }
        loop_server->ReceiveMessageLength = 0;
        loop_server->SendMessageLength = 0;
        loop_server->CanSend = true;

        loop_client->DriverData = loop_server;
        loop_server->DriverData = loop_client;

        return loop_client;
    }

    // Loop_CheckNewConnections
    public static QSocket_t* CheckNewConnections()
    {
        if (!localconnectpending)
            return null;

        localconnectpending = false;
        loop_server->SendMessageLength = 0;
        loop_server->ReceiveMessageLength = 0;
        loop_server->CanSend = true;
        loop_client->SendMessageLength = 0;
        loop_client->ReceiveMessageLength = 0;
        loop_client->CanSend = true;
        return loop_server;
    }

    private static int IntAlign(int value) => (value + (sizeof(int) - 1)) & ~(sizeof(int) - 1);

    // Loop_GetMessage
    public static int GetMessage(QSocket_t* sock)
    {
        if (sock->ReceiveMessageLength == 0)
            return 0;

        int ret = sock->ReceiveMessage[0];
        int length = sock->ReceiveMessage[1] + (sock->ReceiveMessage[2] << 8);
        // alignment byte skipped here
        SizeBuf_t* nm = Net.net_message;
        Sz.Clear(nm);
        Sz.Write(nm, &sock->ReceiveMessage[4], length);

        length = IntAlign(length + 4);
        sock->ReceiveMessageLength -= length;

        if (sock->ReceiveMessageLength != 0)
            Q.MemCpy(sock->ReceiveMessage, &sock->ReceiveMessage[length], sock->ReceiveMessageLength);

        if (sock->DriverData != null && ret == 1)
            ((QSocket_t*)sock->DriverData)->CanSend = true;

        return ret;
    }

    // Loop_SendMessage
    public static int SendMessage(QSocket_t* sock, SizeBuf_t* data)
    {
        if (sock->DriverData == null)
            return -1;

        QSocket_t* peer = (QSocket_t*)sock->DriverData;
        int bufferLength = peer->ReceiveMessageLength;

        if (bufferLength + data->CurSize + 4 > Net.NET_MAXMESSAGE)
            Sys.Error("Loop_SendMessage: overflow\n"u8);

        byte* buffer = peer->ReceiveMessage + bufferLength;

        // message type
        *buffer++ = 1;

        // length
        *buffer++ = (byte)(data->CurSize & 0xff);
        *buffer++ = (byte)(data->CurSize >> 8);

        // align
        buffer++;

        // message
        Q.MemCpy(buffer, data->Data, data->CurSize);
        peer->ReceiveMessageLength = IntAlign(bufferLength + data->CurSize + 4);

        sock->CanSend = false;
        return 1;
    }

    // Loop_SendUnreliableMessage
    public static int SendUnreliableMessage(QSocket_t* sock, SizeBuf_t* data)
    {
        if (sock->DriverData == null)
            return -1;

        QSocket_t* peer = (QSocket_t*)sock->DriverData;
        int bufferLength = peer->ReceiveMessageLength;

        if (bufferLength + data->CurSize + sizeof(byte) + sizeof(short) > Net.NET_MAXMESSAGE)
            return 0;

        byte* buffer = peer->ReceiveMessage + bufferLength;

        // message type
        *buffer++ = 2;

        // length
        *buffer++ = (byte)(data->CurSize & 0xff);
        *buffer++ = (byte)(data->CurSize >> 8);

        // align
        buffer++;

        // message
        Q.MemCpy(buffer, data->Data, data->CurSize);
        peer->ReceiveMessageLength = IntAlign(bufferLength + data->CurSize + 4);
        return 1;
    }

    // Loop_CanSendMessage
    public static bool CanSendMessage(QSocket_t* sock)
    {
        if (sock->DriverData == null)
            return false;
        return sock->CanSend;
    }

    // Loop_Close
    public static void Close(QSocket_t* sock)
    {
        if (sock->DriverData != null)
            ((QSocket_t*)sock->DriverData)->DriverData = null;
        sock->ReceiveMessageLength = 0;
        sock->SendMessageLength = 0;
        sock->CanSend = true;
        if (sock == loop_client)
            loop_client = null;
        else
            loop_server = null;
    }

    // Populates a NetDriver_t (net.h's net_driver_t) with this driver's
    // entry points, for Net.Init to install as net_drivers[0].
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
        return d;
    }
}
