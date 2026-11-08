// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: world.h MOVE_* (World.cs).
// Each member's original constant name is given in its trailing comment.
public enum ClipMoveType
{
    Normal = 0, // MOVE_NORMAL
    NoMonsters = 1, // MOVE_NOMONSTERS
    Missile = 2, // MOVE_MISSILE
}
