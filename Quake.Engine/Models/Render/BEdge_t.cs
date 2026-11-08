// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// bedge_s / bedge_t -- clipped bmodel edges
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BEdge_t
{
    public MVertex_t* V0, V1; // v[2] in the original
    public BEdge_t* PNext;
}
