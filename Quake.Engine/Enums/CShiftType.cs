// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: view.c CSHIFT_* (View.cs).
// Each member's original constant name is given in its trailing comment.
public enum CShiftType
{
    Contents = 0, // CSHIFT_CONTENTS
    Damage = 1, // CSHIFT_DAMAGE
    Bonus = 2, // CSHIFT_BONUS
    Powerup = 3, // CSHIFT_POWERUP
}
