// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dface_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DFace_t
{
    public short PlaneNum;
    public short Side;

    public int FirstEdge; // we must support > 64k edges
    public short NumEdges;
    public short TexInfo;

    // lighting info
    public fixed byte Styles[BspFile.MAXLIGHTMAPS];
    public int LightOfs; // start of [numstyles*surfsize] samples
}
