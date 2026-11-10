// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// ptype_t
public enum PType_t
{
    Static,
    Grav,
    SlowGrav,
    Fire,
    Explode,
    Explode2,
    Blob,
    Blob2,
}

/// <summary>
/// d_iface.h: interface header for rasterization driver modules.
/// Only the structs, constants, and globals actually consumed by the ported
/// d_*.c rasterization bodies are included here -- the many r_local.h/
/// r_shared.h scene-traversal globals declared in the original d_iface.h's
/// neighborhood belong to the r_*.c scene layer (RShared.cs/RLocal.cs) and
/// are intentionally left out.
/// </summary>
public static unsafe class DIface
{
    public const int WARP_WIDTH = 320;
    public const int WARP_HEIGHT = 200;

    public const int MAX_LBM_HEIGHT = 480;

    public const float PARTICLE_Z_CLIP = 8.0f;

    // flags in finalvert_t.flags
    public const int ALIAS_LEFT_CLIP = 0x0001;
    public const int ALIAS_TOP_CLIP = 0x0002;
    public const int ALIAS_RIGHT_CLIP = 0x0004;
    public const int ALIAS_BOTTOM_CLIP = 0x0008;
    public const int ALIAS_Z_CLIP = 0x0010;
    public const int ALIAS_ONSEAM = 0x0020;
    public const int ALIAS_XY_CLIP_MASK = 0x000F;

    public const int CYCLE = 128;        // turbulent cycle size

    // transparency types for D_DrawRect ()

    // !!! must be kept the same as in quakeasm.h !!!
    public const int TRANSPARENT_COLOR = 0xFF;

    // r_zpointdesc
    public static ZPointDesc_t r_zpointdesc;

    // r_spritedesc
    public static SpriteDesc_t r_spritedesc;

    // r_affinetridesc
    public static AffineTriDesc_t r_affinetridesc;

    // r_polydesc
    public static PolyDesc_t r_polydesc;

    // r_pright, r_pup, r_ppn
    public static Vec3_t r_pright, r_pup, r_ppn;

    // acolormap -- FIXME (original comment): should go away. 256*VID_GRADES
    // light-scaled palette table; set each alias-model draw by RAlias.cs
    // (DIface.acolormap = currententity->ColorMap) before any pixel that
    // reads through it (D_PolysetDrawFinalVerts, D_PolysetDrawSpans8,
    // D_DrawSprite) is drawn.
    public static byte* acolormap;

    // r_pixbytes
    public static int r_pixbytes;

    // r_drawpolys, r_drawculledpolys, r_worldpolysbacktofront,
    // r_recursiveaffinetriangles, r_aliasuvscale, r_dowarp
    public static bool r_drawpolys;
    public static bool r_drawculledpolys;
    public static bool r_worldpolysbacktofront;
    public static bool r_recursiveaffinetriangles;
    public static float r_aliasuvscale;
    public static bool r_dowarp;

    // r_warpbuffer -- screen warp target buffer; set by RMain.cs each frame.
    public static byte* r_warpbuffer;

    // r_framecount -- sequence # of current frame since Quake started;
    // incremented each frame by r_main.c's RMisc.cs (R_SetupFrame), needed
    // directly by D_CacheSurface's animation/dlight cache-invalidation check.
    public static int r_framecount;
}
