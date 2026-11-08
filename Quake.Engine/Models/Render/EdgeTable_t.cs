// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// edgetable (d_polyse.c)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EdgeTable_t
{
    public int IsFlatTop;
    public int NumLeftEdges;
    public int* PLeftEdgeVert0;
    public int* PLeftEdgeVert1;
    public int* PLeftEdgeVert2;
    public int NumRightEdges;
    public int* PRightEdgeVert0;
    public int* PRightEdgeVert1;
    public int* PRightEdgeVert2;
}
