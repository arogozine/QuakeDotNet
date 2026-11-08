// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DModechTests
{
    // Runs D_ViewChanged for a full-screen vrect of the given width with the
    // given xscale (vrect.width / horizontal FOV). vid.Height = 0 skips the
    // scantable/zspantable fill, which isn't under test here.
    private static void ViewChanged(int width, float xscale)
    {
        Vid.vid.Width = (uint)width;
        Vid.vid.Height = 0;
        Vid.vid.RowBytes = (uint)width;
        DIface.r_dowarp = false;

        RShared.r_refdef->Vrect.X = 0;
        RShared.r_refdef->Vrect.Y = 0;
        RShared.r_refdef->Vrect.Width = width;
        RShared.r_refdef->VrectRight = width;
        RShared.xscale = xscale;
        RShared.yscale = xscale;
        RShared.pixelAspect = 1;

        DModech.ViewChanged();
    }

    [TestMethod]
    public void ViewChanged_320AtFov90_MatchesOriginal()
    {
        // original: min = 320/320 = 1, max = 320/80 + 0.5 = 4, pix = izi >> 7
        ViewChanged(320, 160);

        Assert.AreEqual(1, DLocal.d_pix_min);
        Assert.AreEqual(4, DLocal.d_pix_max);
        Assert.AreEqual(1000 >> 7, (int)(1000 * DLocal.d_pix_scale));
    }

    [TestMethod]
    public void ViewChanged_640AtFov90_MatchesOriginal()
    {
        // original: min = 2, max = 8, pix = izi >> 6
        ViewChanged(640, 320);

        Assert.AreEqual(2, DLocal.d_pix_min);
        Assert.AreEqual(8, DLocal.d_pix_max);
        Assert.AreEqual(1000 >> 6, (int)(1000 * DLocal.d_pix_scale));
    }

    [TestMethod]
    public void ViewChanged_1920AtFov90_ScalesLinearlyNotExponentially()
    {
        // The original's 8 - (int)(1920/320 + 0.5) = shift 2 gives izi/4,
        // i.e. 32x the 320-wide size instead of the 6x the geometry gets.
        ViewChanged(1920, 960);

        Assert.AreEqual(6, DLocal.d_pix_min);
        Assert.AreEqual(24, DLocal.d_pix_max);
        Assert.AreEqual(6 * (1280 >> 7), (int)(1280 * DLocal.d_pix_scale));
    }

    [TestMethod]
    public void ViewChanged_UltraWideHorPlus_FollowsGeometryScale()
    {
        // 3440x1440 with Hor+ fov 90: horizontal FOV tangent 2 * 1.7917, so
        // xscale = 3440 / 3.5833 = 960 -- the same projection scale as
        // 1920x1080 at 4:3 fov 90, so particles must be the same size too
        // (the original's shift would have been 8 - 11 = -3 here).
        ViewChanged(3440, 3440 / (2 * (3440f / 1440f) / (4f / 3f)));

        Assert.AreEqual(6, DLocal.d_pix_min);
        Assert.AreEqual(24, DLocal.d_pix_max);
        Assert.AreEqual(6.0f / 128.0f, DLocal.d_pix_scale, 1e-4f);
    }
}
