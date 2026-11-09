// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dleaf_t
// leaf 0 is the generic CONTENTS_SOLID leaf, used for all solid areas
// all other leafs need visibility info
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DLeaf_t
{
    public int Contents;
    public int VisOfs; // -1 = no visibility info

    public fixed short Mins[3]; // for frustum culling
    public fixed short Maxs[3];

    public ushort FirstMarkSurface;
    public ushort NumMarkSurfaces;

    public fixed byte AmbientLevel[BspFile.NUM_AMBIENTS];
}
