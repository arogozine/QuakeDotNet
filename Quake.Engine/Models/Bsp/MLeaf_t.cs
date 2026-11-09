// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// mleaf_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MLeaf_t
{
    // common with node
    public int Contents; // will be a negative contents number
    public int VisFrame; // node needs to be traversed if current

    public fixed short MinMaxs[6]; // for bounding box culling

    public MNode_t* Parent;

    // leaf specific
    public byte* CompressedVis;
    public EFrag_t* EFrags;

    public MSurface_t** FirstMarkSurface;
    public int NumMarkSurfaces;
    public int Key; // BSP sequence number for leaf's contents
    public fixed byte AmbientSoundLevel[BspFile.NUM_AMBIENTS];
}
