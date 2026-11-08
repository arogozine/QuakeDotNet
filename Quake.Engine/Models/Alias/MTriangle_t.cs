// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// mtriangle_t
// !!! if this is changed, it must be changed in asm_draw.h too !!!
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MTriangle_t
{
    public int FacesFront;
    public fixed int VertIndex[3];
}
