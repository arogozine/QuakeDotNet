// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;

namespace Quake.Engine;

[InlineArray(BspFile.MAX_MAP_HULLS)]
public struct HullArray
{
    private Hull_t _element0;
}
