// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// mplane_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MPlane_t
{
    // public fixed float Normal[3];
    public Vec3_t Normal;
    public float Dist;
    public byte Type;      // for texture axis selection and fast side tests
    public byte SignBits;  // signx + signy<<1 + signz<<1
    public fixed byte Pad[2];
}
