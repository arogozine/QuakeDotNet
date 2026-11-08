// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// refdef_t (render.h)
// !!! if this is changed, it must be changed in asm_draw.h too !!!
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RRefDef_t
{
    public VRect_t Vrect;                  // subwindow in video for refresh
                                            // FIXME: not need vrect next field here?
    public VRect_t AliasVrect;             // scaled Alias version
    public int VrectRight, VrectBottom;    // right & bottom screen coords
    public int AliasVrectRight, AliasVrectBottom; // scaled Alias versions
    public float VrectRightEdge;           // rightmost right edge we care about,
                                            //  for use in edge list
    public float FVrectx, FVrecty;         // for floating-point compares
    public float FVrectxAdj, FVrectyAdj;   // left and top edges, for clamping
    // int in the original; long here to match Edge_t.U's widening (see
    // that field's comment) -- vrectright << 20 overflows int past x = 2047.
    public long VrectXAdjShift20;          // (vrect.x + 0.5 - epsilon) << 20
    public long VrectRightAdjShift20;      // (vrectright + 0.5 - epsilon) << 20
    public float FVrectRightAdj, FVrectBottomAdj;
                                            // right and bottom edges, for clamping
    public float FVrectRight;              // rightmost edge, for Alias clamping
    public float FVrectBottom;             // bottommost edge, for Alias clamping
    public float HorizontalFieldOfView;    // at Z = 1.0, this many X is visible
                                            // 2.0 = 90 degrees
    public float XOrigin;                  // should probably allways be 0.5
    public float YOrigin;                  // between be around 0.3 to 0.5

    public Vec3_t Vieworg;
    public Vec3_t ViewAngles;

    public float FovX, FovY;

    public int AmbientLight;
}
