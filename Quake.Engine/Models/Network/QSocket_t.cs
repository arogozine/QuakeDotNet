// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// qsocket_t (net.h). `driverdata`-as-void* is kept since Loop.Connect wires
// the paired loopback socket through it; `addr` (struct qsockaddr) is now
// kept too (QSockAddr_t above) -- NetDgrm.cs's connection handshake and
// per-packet sender validation need it now that a real (non-loopback) net
// driver exists.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct QSocket_t
{
    public QSocket_t* Next;
    public double ConnectTime;
    public double LastMessageTime;
    public double LastSendTime;

    public bool Disconnected;
    public bool CanSend;
    public bool SendNext;

    public int Driver;
    public int LanDriver;
    public int Socket;
    public void* DriverData;

    public uint AckSequence;
    public uint SendSequence;
    public uint UnreliableSendSequence;
    public int SendMessageLength;
    public fixed byte SendMessage[Net.NET_MAXMESSAGE];

    public uint ReceiveSequence;
    public uint UnreliableReceiveSequence;
    public int ReceiveMessageLength;
    public fixed byte ReceiveMessage[Net.NET_MAXMESSAGE];

    public QSockAddr_t Addr;
    public fixed byte Address[Net.NET_NAMELEN];
}
