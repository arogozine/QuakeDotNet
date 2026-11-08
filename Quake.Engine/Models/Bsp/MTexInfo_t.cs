// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// mtexinfo_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MTexInfo_t
{
    public fixed float Vecs[2 * 4];
    public float MipAdjust;
    public Texture_t* Texture;
    public int Flags;
}
