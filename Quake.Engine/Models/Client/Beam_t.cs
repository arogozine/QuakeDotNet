// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// beam_t (client.h)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Beam_t
{
    public int Entity;
    public Model_t* Model;
    public float Endtime;
    public Vec3_t Start;
    public Vec3_t End;
}
