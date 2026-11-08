// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: protocol.h clc_* (Protocol.cs).
// Each member's original constant name is given in its trailing comment.
public enum Clc
{
    Bad = 0, // clc_bad
    Nop = 1, // clc_nop
    Disconnect = 2, // clc_disconnect
    Move = 3, // clc_move
    StringCmd = 4, // clc_stringcmd
}
