// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// dnode_t
// !!! if this is changed, it must be changed in asm_i386.h too !!!
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DNode_t
{
    public int PlaneNum;
    public fixed short Children[2]; // negative numbers are -(leafs+1), not nodes
    public fixed short Mins[3];     // for sphere culling
    public fixed short Maxs[3];
    public ushort FirstFace;
    public ushort NumFaces; // counting both sides
}
