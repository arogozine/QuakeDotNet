// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// spritedesc_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SpriteDesc_t
{
    public int Nump;
    public EmitPoint_t* PVerts; // room for an extra element at [nump] (duplicate of [0])
    public MSpriteFrame_t* PSpriteFrame;
    public Vec3_t Vup, Vright, Vpn; // in worldspace
    public float NearZi;
}
