// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// affinetridesc_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AffineTriDesc_t
{
    public void* PSkin;
    public MAliasSkinDesc_t* PSkinDesc;
    public int SkinWidth;
    public int SkinHeight;
    public MTriangle_t* PTriangles;
    public FinalVert_t* PFinalVerts;
    public int NumTriangles;
    public int DrawType;
    public int SeamFixupX16;
}
