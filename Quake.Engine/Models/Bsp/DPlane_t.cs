// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dplane_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DPlane_t
{
    public fixed float Normal[3];
    public float Dist;
    public int Type; // (int)PlaneType.X - (int)PlaneType.AnyZ ?remove? trivial to regenerate
}
