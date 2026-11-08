// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// d_local.h: private rasterization driver defs. Only the structs/globals the
/// ported d_*.c bodies actually touch are included -- see DIface.cs/RShared.cs
/// for the same scoping rule.
/// </summary>
public static unsafe class DLocal
{
    public const int DS_SPAN_LIST_END = -128;

    public const int SURFCACHE_SIZE_AT_320X200 = 600 * 1024;

    // R_SKY_SMASK, R_SKY_TMASK (d_local.h) -- D_DrawSkyScans8's texel mask.
    public const int R_SKY_SMASK = 0x007F0000;
    public const int R_SKY_TMASK = 0x007F0000;

    // d_sdivzstepu, d_tdivzstepu, d_zistepu / d_sdivzstepv, d_tdivzstepv, d_zistepv
    // / d_sdivzorigin, d_tdivzorigin, d_ziorigin
    public static float d_sdivzstepu, d_tdivzstepu, d_zistepu;
    public static float d_sdivzstepv, d_tdivzstepv, d_zistepv;
    public static float d_sdivzorigin, d_tdivzorigin, d_ziorigin;

    // sadjust, tadjust / bbextents, bbextentt (fixed16_t)
    public static int sadjust, tadjust;
    public static int bbextents, bbextentt;

    public static float scale_for_mip;

    public static bool d_roverwrapped;
    public static SurfCache_t* sc_rover;
    public static SurfCache_t* d_initial_rover;

    public static readonly int* d_scantable = NativeMemory.AllocArrayZeroed<int>(RShared.MAXHEIGHT);

    public static int d_vrectx, d_vrecty, d_vrectright_particle, d_vrectbottom_particle;

    public static int d_y_aspect_shift, d_pix_min, d_pix_max;

    // d_pix_scale -- replaces the original's int d_pix_shift (see
    // DModech.ViewChanged's own comment): particle size in pixels per unit
    // of izi, i.e. pix = (int)(izi * d_pix_scale) instead of izi >> d_pix_shift.
    public static float d_pix_scale;

    public static byte* d_viewbuffer;

    public static readonly short** zspantable = NativeMemory.AllocPointerArray<short>(RShared.MAXHEIGHT);

    public static int d_minmip;
    public static readonly float* d_scalemip = NativeMemory.AllocArrayZeroed<float>(3);

    public static short* d_pzbuffer;
    public static uint d_zrowbytes, d_zwidth;

    public static delegate* managed<ESpan_t*, void> d_drawspans;
}
