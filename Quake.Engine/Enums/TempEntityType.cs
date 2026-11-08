// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: protocol.h TE_* (Protocol.cs).
// Each member's original constant name is given in its trailing comment.
public enum TempEntityType
{
    Spike = 0, // TE_SPIKE
    SuperSpike = 1, // TE_SUPERSPIKE
    Gunshot = 2, // TE_GUNSHOT
    Explosion = 3, // TE_EXPLOSION
    TarExplosion = 4, // TE_TAREXPLOSION
    Lightning1 = 5, // TE_LIGHTNING1
    Lightning2 = 6, // TE_LIGHTNING2
    WizSpike = 7, // TE_WIZSPIKE
    KnightSpike = 8, // TE_KNIGHTSPIKE
    Lightning3 = 9, // TE_LIGHTNING3
    LavaSplash = 10, // TE_LAVASPLASH
    Teleport = 11, // TE_TELEPORT
    Explosion2 = 12, // TE_EXPLOSION2
    Beam = 13, // TE_BEAM
}
