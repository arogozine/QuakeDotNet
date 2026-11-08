// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// dsprite_t
// TODO (original comment): shorten these?
[StructLayout(LayoutKind.Sequential)]
public struct DSprite_t
{
    public int Ident;
    public int Version;
    public SpriteType Type; // SPR_* orientation
    public float BoundingRadius;
    public int Width;
    public int Height;
    public int NumFrames;
    public float BeamLength;
    public SyncType_t SyncType;
}
