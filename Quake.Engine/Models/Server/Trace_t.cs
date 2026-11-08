// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Quake.Engine;

// trace_t (world.h)
[StructLayout(LayoutKind.Sequential)]
public struct Trace_t
{
    public bool AllSolid;   // if true, plane is not valid
    public bool StartSolid; // if true, the initial point was in a solid area
    public bool InOpen, InWater;
    public float Fraction;  // time completed, 1.0 = didn't hit anything
    public Vec3_t EndPos;   // final position
    public PlaneShort_t Plane;
    public unsafe EDict_t* Ent; // entity the surface is on
}
