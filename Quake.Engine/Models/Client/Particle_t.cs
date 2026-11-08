// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// particle_t (particle_s)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Particle_t
{
    // driver-usable fields
    public Vec3_t Org;
    public float Color;
    // drivers never touch the following fields
    public Particle_t* Next;
    public Vec3_t Vel;
    public float Ramp;
    public float Die;
    public PType_t Type;
}
