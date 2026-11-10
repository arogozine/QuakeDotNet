// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// r_local.h -- private refresh defs. Only the constants and structs shared
/// across the r_*.c scene layer (or read by the d_*.c rasterization hooks)
/// are ported here; the many function prototypes and per-file scratch
/// globals declared alongside them belong to the r_*.c file that owns them
/// and are ported there instead (r_edge.c's edge/surface allocator state,
/// r_alias.c's numverts/ptriangles/etc, and so on).
/// </summary>
public static class RLocal
{
    // ALIAS_BASE_SIZE_RATIO -- normalizing factor so player model works out
    // to about 1 pixel per triangle.
    public const float ALIAS_BASE_SIZE_RATIO = 1.0f / 11.0f;

    // BMODEL_FULLY_CLIPPED -- value returned by R_BmodelCheckBBox() if bbox
    // is trivially rejected.
    public const int BMODEL_FULLY_CLIPPED = 0x10;

    public const float XCENTERING = 1.0f / 2.0f;
    public const float YCENTERING = 1.0f / 2.0f;

    public const float BACKFACE_EPSILON = 0.01f;


    // !!! if this is changed, it must be changed in asm_draw.h too !!!
    public const float NEAR_CLIP = 0.01f;

    // new clipped vertices when clipping bmodels to the world BSP

    public const int MAX_BTOFPOLYS = 5000; // FIXME: tune this

    // Alias models
    public const int MAXALIASVERTS = 2000; // TODO (original comment): tune this
    public const int ALIAS_Z_CLIP_PLANE = 5;

    // turbulence stuff
    public const int AMP = 8 * 0x10000;
    public const int AMP2 = 3;
    public const int SPEED = 20;
}
