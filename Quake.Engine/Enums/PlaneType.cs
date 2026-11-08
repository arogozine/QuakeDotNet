// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: bspfile.h PLANE_* (BspFile.cs).
// Each member's original constant name is given in its trailing comment.
public enum PlaneType
{
    X = 0, // PLANE_X
    Y = 1, // PLANE_Y
    Z = 2, // PLANE_Z
    AnyX = 3, // PLANE_ANYX
    AnyY = 4, // PLANE_ANYY
    AnyZ = 5, // PLANE_ANYZ
}
