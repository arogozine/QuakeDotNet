// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: protocol.h GAME_* (Protocol.cs, duplicated in Cl.cs).
// Each member's original constant name is given in its trailing comment.
public enum GameType
{
    Coop = 0, // GAME_COOP
    Deathmatch = 1, // GAME_DEATHMATCH
}
