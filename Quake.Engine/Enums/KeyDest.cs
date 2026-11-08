// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: keys.h keydest_t (Keys.cs KEY_*; original enumerators key_game/key_console/key_message/key_menu).
// Each member's original constant name is given in its trailing comment.
public enum KeyDest
{
    Game = 0, // KEY_GAME
    Console = 1, // KEY_CONSOLE
    Message = 2, // KEY_MESSAGE
    Menu = 3, // KEY_MENU
}
