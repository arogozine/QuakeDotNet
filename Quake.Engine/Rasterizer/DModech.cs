// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// d_modech.c: called when the video mode has just changed.
/// D_Patch (id386 code-patching) is not ported -- this build has no x86 asm.
/// </summary>
public static unsafe class DModech
{
    // D_ViewChanged
    public static void ViewChanged()
    {
        int rowbytes = DIface.r_dowarp ? DIface.WARP_WIDTH : (int)Vid.vid.RowBytes;

        DLocal.scale_for_mip = RShared.xscale;
        if (RShared.yscale > RShared.xscale)
        {
            DLocal.scale_for_mip = RShared.yscale;
        }

        DLocal.d_zrowbytes = Vid.vid.Width * 2;
        DLocal.d_zwidth = Vid.vid.Width;

        // d_pix_min/d_pix_max/d_pix_shift -- the original derives particle
        // size from vrect.width alone:
        //   d_pix_min   = width / 320
        //   d_pix_max   = width / 80 + 0.5
        //   d_pix_shift = 8 - (int)(width / 320 + 0.5)    (pix = izi >> shift)
        // Deliberately not ported as-is (Rule 8):
        //  - the shift makes size grow exponentially with width (x2 per extra
        //    320 pixels) instead of linearly like the geometry projection, so
        //    it is only right at 320 and 640 wide: 2x too big at 1280, ~5x at
        //    1920, 8x at 2560, and from ~2720 wide the shift goes negative
        //    (undefined in C; masked to a shift of 29+ in C#, so every
        //    particle collapses to d_pix_min regardless of distance);
        //  - it ignores the field of view, so with Screen.CalcHorPlusFov's
        //    wider widescreen FOV the world shrinks but particles don't.
        // Instead all three scale linearly with xscale (vrect.width /
        // horizontal FOV, the same projection scale the world, models and
        // sprites use), normalized to the original's reference 320-wide,
        // fov 90 view (xscale 160). That reproduces the original exactly at
        // 320 (min 1, max 4, >>7) and 640 (min 2, max 8, >>6) with fov 90.
        float pixScale = RShared.xscale / 160.0f;

        DLocal.d_pix_min = (int)pixScale;
        if (DLocal.d_pix_min < 1)
        {
            DLocal.d_pix_min = 1;
        }

        DLocal.d_pix_max = (int)((pixScale * 4.0f) + 0.5f);
        DLocal.d_pix_scale = pixScale / 128.0f;
        if (DLocal.d_pix_max < 1)
        {
            DLocal.d_pix_max = 1;
        }

        DLocal.d_y_aspect_shift = RShared.pixelAspect > 1.4f ? 1 : 0;

        DLocal.d_vrectx = RShared.r_refdef->Vrect.X;
        DLocal.d_vrecty = RShared.r_refdef->Vrect.Y;
        DLocal.d_vrectright_particle = RShared.r_refdef->VrectRight - DLocal.d_pix_max;
        DLocal.d_vrectbottom_particle = RShared.r_refdef->VrectBottom - (DLocal.d_pix_max << DLocal.d_y_aspect_shift);

        for (int i = 0; i < Vid.vid.Height; i++)
        {
            DLocal.d_scantable[i] = i * rowbytes;
            DLocal.zspantable[i] = DLocal.d_pzbuffer + (i * DLocal.d_zwidth);
        }
    }
}
