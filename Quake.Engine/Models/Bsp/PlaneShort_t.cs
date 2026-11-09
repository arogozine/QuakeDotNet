// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// world.h's trace_t.plane is a plane_t (normal + dist only, no type/signbits)
[StructLayout(LayoutKind.Sequential)]
public struct PlaneShort_t
{
    public Vec3_t Normal;
    public float Dist;
}
