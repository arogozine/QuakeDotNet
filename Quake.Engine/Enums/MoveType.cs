// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: server.h MOVETYPE_* (SvPhys.cs, World.cs). All server.h values are declared, including those SvPhys.cs does not implement.
// Each member's original constant name is given in its trailing comment.
public enum MoveType
{
    None = 0, // MOVETYPE_NONE
    AngleNoClip = 1, // MOVETYPE_ANGLENOCLIP
    AngleClip = 2, // MOVETYPE_ANGLECLIP
    Walk = 3, // MOVETYPE_WALK
    Step = 4, // MOVETYPE_STEP
    Fly = 5, // MOVETYPE_FLY
    Toss = 6, // MOVETYPE_TOSS
    Push = 7, // MOVETYPE_PUSH
    NoClip = 8, // MOVETYPE_NOCLIP
    FlyMissile = 9, // MOVETYPE_FLYMISSILE
    Bounce = 10, // MOVETYPE_BOUNCE
    BounceMissile = 11, // MOVETYPE_BOUNCEMISSILE
    Follow = 12, // MOVETYPE_FOLLOW
}
