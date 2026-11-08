// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DEdgeTests
{
    [TestInitialize]
    public void Setup()
    {
        DLocal.d_scalemip[0] = 1.0f;
        DLocal.d_scalemip[1] = 0.4f;
        DLocal.d_scalemip[2] = 0.2f;
        DLocal.d_minmip = 0;
    }

    [TestMethod]
    [DataRow(1.0f, 0)]
    [DataRow(0.5f, 1)]
    [DataRow(0.3f, 2)]
    [DataRow(0.25f, 2)]
    [DataRow(0.01f, 3)]
    public void MipLevelForScale_PicksBracketingLevel(float scale, int expected)
    {
        Assert.AreEqual(expected, DEdge.MipLevelForScale(scale));
    }

    [TestMethod]
    public void MipLevelForScale_ClampsToMinMip()
    {
        DLocal.d_minmip = 2;

        Assert.AreEqual(2, DEdge.MipLevelForScale(1.0f));
    }

    [TestMethod]
    public void DrawSolidSurface_FillsSpanRun_WithLowByteOfColor()
    {
        byte* viewbuf = (byte*)NativeMemory.AllocZeroed(32);
        try
        {
            DLocal.d_viewbuffer = viewbuf;
            RShared.screenwidth = 16;

            ESpan_t span = new() { U = 2, V = 0, Count = 5, PNext = null };
            Surf_t surf = new() { Spans = &span };

            DEdge.DrawSolidSurface(&surf, 0x55);

            for (int i = 0; i < 5; i++)
                Assert.AreEqual((byte)0x55, viewbuf[2 + i]);
            Assert.AreEqual(0, viewbuf[1]);
            Assert.AreEqual(0, viewbuf[7]);
        }
        finally
        {
            NativeMemory.Free(viewbuf);
        }
    }

    [TestMethod]
    public void CalcGradients_ReadsSAxisAndTAxisAtCorrect4FloatStride()
    {
        // Vecs = [sx,sy,sz,sdist, tx,ty,tz,tdist]; distinct values catch any
        // stride mistake (e.g. reading the t-axis at a 3-float offset).
        MTexInfo_t texInfo = default;
        texInfo.Vecs[0] = 0; texInfo.Vecs[1] = 1; texInfo.Vecs[2] = 2; texInfo.Vecs[3] = 3;
        texInfo.Vecs[4] = 4; texInfo.Vecs[5] = 5; texInfo.Vecs[6] = 6; texInfo.Vecs[7] = 7;

        MSurface_t surface = default;
        surface.TexInfo = &texInfo;
        surface.TextureMins[0] = 0;
        surface.TextureMins[1] = 0;
        surface.Extents[0] = 100;
        surface.Extents[1] = 200;

        // Identity view basis so TransformVector(in) == in, isolating the
        // axis-read stride from the rotation math.
        RShared.vright = new Vec3_t(1, 0, 0);
        RShared.vup = new Vec3_t(0, 1, 0);
        RShared.vpn = new Vec3_t(0, 0, 1);
        RShared.xscaleinv = 1;
        RShared.yscaleinv = 1;
        RShared.xcenter = 0;
        RShared.ycenter = 0;
        DEdge.transformed_modelorg = default;

        DEdge.CalcGradients(&surface);

        // s-axis == {0,1,2}, sdist == 3
        Assert.AreEqual(0f, DLocal.d_sdivzstepu);
        Assert.AreEqual(-1f, DLocal.d_sdivzstepv);
        Assert.AreEqual(2f, DLocal.d_sdivzorigin);
        Assert.AreEqual(3 * 0x10000, DLocal.sadjust);

        // t-axis == {4,5,6}, tdist == 7
        Assert.AreEqual(4f, DLocal.d_tdivzstepu);
        Assert.AreEqual(-5f, DLocal.d_tdivzstepv);
        Assert.AreEqual(6f, DLocal.d_tdivzorigin);
        Assert.AreEqual(7 * 0x10000, DLocal.tadjust);

        Assert.AreEqual(100 * 0x10000 - 1, DLocal.bbextents);
        Assert.AreEqual(200 * 0x10000 - 1, DLocal.bbextentt);
    }

    [TestMethod]
    public void DrawSkyScans8_SinglePixelSpan_SamplesSkySourceAtComputedUv()
    {
        byte* viewbuf = (byte*)NativeMemory.AllocZeroed(16);
        byte* skysource = (byte*)NativeMemory.AllocZeroed(128 * 128);
        try
        {
            DLocal.d_viewbuffer = viewbuf;
            RShared.screenwidth = 8;
            RSky.r_skysource = skysource;
            RSky.skytime = 0;
            RSky.skyspeed = 0;

            RShared.vpn = new Vec3_t(0, 0, 1);
            RShared.vup = new Vec3_t(0, 1, 0);
            RShared.vright = new Vec3_t(1, 0, 0);
            RShared.r_refdef->Vrect = new VRect_t { X = 0, Y = 0, Width = 8, Height = 8 };
            Vid.vid.Width = 8;
            Vid.vid.Height = 8;

            int s = 0, t = 0;
            DEdge.SkyUvToSt(0, 0, &s, &t);
            int expectedIndex = ((t & DLocal.R_SKY_TMASK) >> 8) + ((s & DLocal.R_SKY_SMASK) >> 16);
            skysource[expectedIndex] = 99;

            ESpan_t span = new() { U = 0, V = 0, Count = 1, PNext = null };

            DEdge.DrawSkyScans8(&span);

            Assert.AreEqual((byte)99, viewbuf[0]);
        }
        finally
        {
            NativeMemory.Free(viewbuf);
            NativeMemory.Free(skysource);
        }
    }
}
