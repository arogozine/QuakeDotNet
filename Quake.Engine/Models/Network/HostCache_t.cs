// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// hostcache_t (net.h) -- one server-browser entry, filled by the drivers'
// SearchForHosts and read by NET_Connect/NET_Slist_f and the slist menu.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HostCache_t
{
    public fixed byte Name[16];
    public fixed byte Map[16];
    public fixed byte CName[32];
    public int Users;
    public int MaxUsers;
    public int Driver;
    public int LDriver;
    public QSockAddr_t Addr;
}

// PollProcedure (net.h) -- a deferred call run by NET_Poll once its NextTime
// has passed. The original's `void *arg` is dropped: every PollProcedure left
// in this port (slist send/poll) ignores it.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PollProcedure_t
{
    public PollProcedure_t* Next;
    public double NextTime;
    public delegate* managed<void> Procedure;
}
