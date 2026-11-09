// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// medge_t
// !!! if this is changed, it must be changed in asm_draw.h too !!!
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MEdge_t
{
    public fixed ushort V[2];
    public uint CachedEdgeOffset;
}
