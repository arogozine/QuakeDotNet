// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// bspfile.h -- on-disk BSP structures and constants.
/// </summary>
public static class BspFile
{
    public const int MAX_MAP_HULLS = 4;

    public const int MAX_MAP_LEAFS = (32768 * 4) - 1; // bspfile.h -- Mark V (johnfitz was 8192; 32767 before)

    public const int BSPVERSION = 29;


    public const int HEADER_LUMPS = 15;

    public const int MIPLEVELS = 4;

    // 0-2 are axial planes

    // 3-5 are non-axial planes snapped to the nearest



    public const int TEX_SPECIAL = 1; // sky or slime, no lightmap or 256 subdivision

    public const int MAXLIGHTMAPS = 4;


    public const int NUM_AMBIENTS = 4; // automatic ambient sounds
}

