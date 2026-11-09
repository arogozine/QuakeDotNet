// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// mspritegroup_t (frames[1] is a flexible array of pointers, unlike the
// byte-offset scheme alias models use -- sprites are never relocated after
// loading, so real pointers are safe here, matching the original.)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MSpriteGroup_t
{
    public int NumFrames;
    public float* Intervals;
    public MSpriteFrame_t* Frames; // frames[0]
}
