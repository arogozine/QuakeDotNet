// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// net_driver_t (net.h) -- trimmed to the entry points NET_* actually
// dispatches through (the serial/IPX-only fields are not needed).
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NetDriver_t
{
    public bool Initialized;
    public int ControlSock;
    public delegate* managed<int> Init;
    public delegate* managed<void> Shutdown;
    public delegate* managed<bool, void> Listen;
    public delegate* managed<bool, void> SearchForHosts;
    public delegate* managed<byte*, QSocket_t*> Connect;
    public delegate* managed<QSocket_t*> CheckNewConnections;
    public delegate* managed<QSocket_t*, int> QGetMessage;
    public delegate* managed<QSocket_t*, SizeBuf_t*, int> QSendMessage;
    public delegate* managed<QSocket_t*, SizeBuf_t*, int> SendUnreliableMessage;
    public delegate* managed<QSocket_t*, bool> CanSendMessage;
    public delegate* managed<QSocket_t*, void> Close;
}
