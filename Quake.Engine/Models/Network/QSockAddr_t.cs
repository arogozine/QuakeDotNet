// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// qsockaddr (net.h) -- trimmed to the fields this port's IP-only driver
// (NetIp.cs) actually needs, rather than the original's raw 16-byte
// sockaddr-shaped buffer: AddrNet/PortNet hold sin_addr.s_addr/sin_port
// exactly as received (network byte order, opaque 4/2-byte blobs) so
// AddrCompare below stays a plain field-equality check, matching the
// original's byte-for-byte struct comparison without needing to duplicate
// sockaddr_in's exact memory layout.
[StructLayout(LayoutKind.Sequential)]
public struct QSockAddr_t
{
    public short Family; // sa_family / AF_INET
    public uint AddrNet; // sin_addr.s_addr, network byte order
    public ushort PortNet; // sin_port, network byte order
}
