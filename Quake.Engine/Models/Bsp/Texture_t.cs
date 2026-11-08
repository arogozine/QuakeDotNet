// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// texture_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Texture_t
{
    public fixed byte Name[16];
    public uint Width, Height;
    public int AnimTotal;          // total tenths in sequence (0 = no)
    public int AnimMin, AnimMax;   // time for this frame min <=time< max
    public Texture_t* AnimNext;    // in the animation sequence
    public Texture_t* AlternateAnims; // bmodels in frame 1 use these
    public fixed uint Offsets[BspFile.MIPLEVELS]; // four mip maps stored
}
