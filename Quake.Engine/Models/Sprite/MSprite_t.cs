// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// msprite_t (frames[1] is a flexible array; only Frames (index 0) is
// declared, more slots are reached via pointer arithmetic past it)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MSprite_t
{
    public SpriteType Type; // SPR_* orientation
    public int MaxWidth;
    public int MaxHeight;
    public int NumFrames;
    public float BeamLength; // remove?
    public MSpriteFrameDesc_t Frames;
}
