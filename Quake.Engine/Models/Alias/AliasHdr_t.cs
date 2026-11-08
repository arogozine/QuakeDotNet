// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// aliashdr_t
// Alias models are position independent, so the cache manager can move them:
// fields that look like pointers elsewhere in this port (Model, Skin,
// Intervals, Frame) are byte offsets relative to the aliashdr_t itself, not
// real pointers, so relocating the whole blob (Cache_Alloc + memcpy) doesn't
// invalidate them. frames[1] is a flexible-array member -- see Frames.
[StructLayout(LayoutKind.Sequential)]
public struct AliasHdr_t
{
    public int Model;     // byte offset to the mdl_t
    public int StVerts;   // byte offset to the stvert_t array
    public int SkinDesc;  // byte offset to the maliasskindesc_t array
    public int Triangles; // byte offset to the mtriangle_t array
    public MAliasFrameDesc_t Frames;
}
