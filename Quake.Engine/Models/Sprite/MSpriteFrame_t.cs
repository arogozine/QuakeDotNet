// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// mspriteframe_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MSpriteFrame_t
{
    public int Width;
    public int Height;
    public void* PCacheSpot; // remove? -- render-only, unused by the loader
    public float Up, Down, Left, Right;
    public fixed byte Pixels[4]; // flexible: actual size is width*height
}
