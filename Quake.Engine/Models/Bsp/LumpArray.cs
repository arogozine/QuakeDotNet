// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;

namespace Quake.Engine;

// 15 lump_t entries (HEADER_LUMPS); InlineArray since lump_t is a struct, not
// a primitive fixed buffers can hold.
[InlineArray(BspFile.HEADER_LUMPS)]
public struct LumpArray
{
    private Lump_t _element0;
}
