// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// maliasskindesc_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MAliasSkinDesc_t
{
    public AliasSkinType_t Type;
    public void* PCacheSpot; // render-only, unused by the loader
    public int Skin; // byte offset relative to the aliashdr_t
}
