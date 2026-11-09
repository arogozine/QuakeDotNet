// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// mnode_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MNode_t
{
    // common with leaf
    public int Contents; // 0, to differentiate from leafs
    public int VisFrame; // node needs to be traversed if current

    public fixed short MinMaxs[6]; // for bounding box culling

    public MNode_t* Parent;

    // node specific
    public MPlane_t* Plane;
    public MNodeChildArray Children;

    public ushort FirstSurface;
    public ushort NumSurfaces;
}
