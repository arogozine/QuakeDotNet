// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// finalvert_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FinalVert_t
{
    public fixed int V[6]; // u, v, s, t, l, 1/z
    public int Flags;
    public float Reserved;
}
