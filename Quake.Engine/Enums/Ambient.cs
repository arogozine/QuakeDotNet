// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: bspfile.h AMBIENT_* (BspFile.cs).
// Each member's original constant name is given in its trailing comment.
public enum Ambient
{
    Water = 0, // AMBIENT_WATER
    Sky = 1, // AMBIENT_SKY
    Slime = 2, // AMBIENT_SLIME
    Lava = 3, // AMBIENT_LAVA
}
