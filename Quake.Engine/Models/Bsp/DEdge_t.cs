// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dedge_t
// note that edge 0 is never used, because negative edge nums are used for
// counterclockwise use of the edge in a face
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DEdge_t
{
    public fixed ushort V[2]; // vertex numbers
}
