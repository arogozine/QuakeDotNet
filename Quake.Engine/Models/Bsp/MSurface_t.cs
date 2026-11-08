// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// msurface_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MSurface_t
{
    public int VisFrame; // should be drawn when node is crossed

    public int DLightFrame;
    public int DLightBits;

    public MPlane_t* Plane;
    public int Flags;

    public int FirstEdge; // look up in model->surfedges[], negative numbers
    public int NumEdges;  // are backwards edges

    // surface generation data (render-only, not populated by the loader)
    public CacheSpotArray CacheSpots;

    public fixed short TextureMins[2];
    public fixed short Extents[2];

    public MTexInfo_t* TexInfo;

    // lighting info
    public fixed byte Styles[BspFile.MAXLIGHTMAPS];
    public byte* Samples; // [numstyles*surfsize]
}
