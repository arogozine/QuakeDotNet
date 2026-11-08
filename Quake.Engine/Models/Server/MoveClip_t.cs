// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Quake.Engine;

// moveclip_t (world.c)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MoveClip_t
{
    public Vec3_t BoxMins, BoxMaxs; // enclose the test object along entire move
    public Vec3_t Mins, Maxs;       // size of the moving object (world.c takes float*; stored by value here)
    public Vec3_t Mins2, Maxs2;     // size when clipping against monsters
    public Vec3_t Start, End;
    public Trace_t Trace;
    public int Type;
    public EDict_t* PassEdict;
}
