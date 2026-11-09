// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// hull_t
// !!! if this is changed, it must be changed in asm_i386.h too !!!
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Hull_t
{
    public DClipNode_t* ClipNodes;
    public MPlane_t* Planes;
    public int FirstClipNode;
    public int LastClipNode;
    public Vec3_t ClipMins;
    public Vec3_t ClipMaxs;
}
