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

    public const int MAX_MAP_MODELS = 256;
    public const int MAX_MAP_BRUSHES = 4096;
    public const int MAX_MAP_ENTITIES = 1024;
    public const int MAX_MAP_ENTSTRING = 65536;

    public const int MAX_MAP_PLANES = 32767;
    public const int MAX_MAP_NODES = 32767; // because negative shorts are contents
    public const int MAX_MAP_CLIPNODES = 32767;
    public const int MAX_MAP_LEAFS = (32768 * 4) - 1; // bspfile.h -- Mark V (johnfitz was 8192; 32767 before)
    public const int MAX_MAP_VERTS = 65535;
    public const int MAX_MAP_FACES = 65535;
    public const int MAX_MAP_MARKSURFACES = 65535;
    public const int MAX_MAP_TEXINFO = 4096;
    public const int MAX_MAP_EDGES = 256000;
    public const int MAX_MAP_SURFEDGES = 512000;
    public const int MAX_MAP_TEXTURES = 512;
    public const int MAX_MAP_MIPTEX = 0x200000;
    public const int MAX_MAP_LIGHTING = 0x100000;
    public const int MAX_MAP_VISIBILITY = 0x100000;

    public const int MAX_MAP_PORTALS = 65536;

    public const int MAX_KEY = 32;
    public const int MAX_VALUE = 1024;

    public const int BSPVERSION = 29;
    public const int TOOLVERSION = 2;


    public const int HEADER_LUMPS = 15;

    public const int MIPLEVELS = 4;

    // 0-2 are axial planes

    // 3-5 are non-axial planes snapped to the nearest



    public const int TEX_SPECIAL = 1; // sky or slime, no lightmap or 256 subdivision

    public const int MAXLIGHTMAPS = 4;


    public const int NUM_AMBIENTS = 4; // automatic ambient sounds
}

