// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: spritegn.h SPR_* (SpriteModel.cs).
// Each member's original constant name is given in its trailing comment.
public enum SpriteType
{
    VpParallelUpright = 0, // SPR_VP_PARALLEL_UPRIGHT
    FacingUpright = 1, // SPR_FACING_UPRIGHT
    VpParallel = 2, // SPR_VP_PARALLEL
    Oriented = 3, // SPR_ORIENTED
    VpParallelOriented = 4, // SPR_VP_PARALLEL_ORIENTED
}
