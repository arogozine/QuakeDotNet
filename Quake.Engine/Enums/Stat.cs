// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: quakedef.h STAT_* (Cl.cs, View.cs).
// Each member's original constant name is given in its trailing comment.
public enum Stat
{
    Health = 0, // STAT_HEALTH
    Frags = 1, // STAT_FRAGS
    Weapon = 2, // STAT_WEAPON
    Ammo = 3, // STAT_AMMO
    Armor = 4, // STAT_ARMOR
    WeaponFrame = 5, // STAT_WEAPONFRAME
    Shells = 6, // STAT_SHELLS
    Nails = 7, // STAT_NAILS
    Rockets = 8, // STAT_ROCKETS
    Cells = 9, // STAT_CELLS
    ActiveWeapon = 10, // STAT_ACTIVEWEAPON
    TotalSecrets = 11, // STAT_TOTALSECRETS
    TotalMonsters = 12, // STAT_TOTALMONSTERS
    Secrets = 13, // STAT_SECRETS
    Monsters = 14, // STAT_MONSTERS
}
