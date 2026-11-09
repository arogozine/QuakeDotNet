// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;

namespace Quake.Engine;

// Vec3_t[2] -- fixed buffers can't hold struct elements, so entity_t's
// msg_origins/msg_angles (each vec3_t[2]) use this InlineArray instead.
[InlineArray(2)]
public struct Vec3Array2
{
    private Vec3_t _element0;
}
