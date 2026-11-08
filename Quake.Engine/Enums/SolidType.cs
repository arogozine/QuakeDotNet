// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: server.h SOLID_* (World.cs, Progs.cs; BBOX/SLIDEBOX added from server.h).
// Each member's original constant name is given in its trailing comment.
public enum SolidType
{
    Not = 0, // SOLID_NOT
    Trigger = 1, // SOLID_TRIGGER
    BBox = 2, // SOLID_BBOX
    SlideBox = 3, // SOLID_SLIDEBOX
    Bsp = 4, // SOLID_BSP
}
