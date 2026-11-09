// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dmodel_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DModel_t
{
    public fixed float Mins[3], Maxs[3];
    public fixed float Origin[3];
    public fixed int HeadNode[BspFile.MAX_MAP_HULLS];
    public int VisLeafs; // not including the solid leaf 0
    public int FirstFace, NumFaces;
}
