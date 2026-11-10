// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DPolyseTests
{
    [TestMethod]
    public void PolysetFillSpans8_FillsEachSpan_WithIncrementingFlatColor()
    {
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(16);
        try
        {
            DPolyse.d_aflatcolor = 10;

            SpanPackage_t* spans = stackalloc SpanPackage_t[3];
            spans[0] = new SpanPackage_t { PDest = buf, Count = 3 };
            spans[1] = new SpanPackage_t { PDest = buf + 8, Count = 2 };
            spans[2] = new SpanPackage_t { Count = -1 }; // terminator

            DPolyse.PolysetFillSpans8(spans);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual((byte)10, buf[i]);
            }

            for (int i = 8; i < 10; i++)
            {
                Assert.AreEqual((byte)10, buf[i]);
            }

            Assert.AreEqual(11, DPolyse.d_aflatcolor); // incremented exactly once
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void PolysetDrawFinalVerts_WritesPixel_ThroughColormapAndSkin_WhenCloserThanZBuffer()
    {
        byte* viewbuf = NativeMemory.AllocArrayZeroed<byte>(64);
        short* zrow = NativeMemory.AllocArrayZeroed<short>(16);
        byte* skin = NativeMemory.AllocArrayZeroed<byte>(64); // 8 wide x 8 tall
        byte* colormap = NativeMemory.AllocArrayZeroed<byte>(256 * 4);
        try
        {
            const int skinwidth = 8;
            skin[(1 * skinwidth) + 2] = 7; // texel at (s=2, t=1) -> palette index 7
            colormap[7] = 99; // light offset 0 -> straight palette lookup

            DIface.r_affinetridesc.SkinWidth = skinwidth;
            DIface.r_affinetridesc.PSkin = skin;
            DPolyse.PolysetUpdateTables();

            DIface.acolormap = colormap;

            DLocal.d_viewbuffer = viewbuf;
            DLocal.d_scantable[3] = 30; // row 3 starts at offset 30
            DLocal.zspantable[3] = zrow;

            RShared.r_refdef->VrectRight = 100;
            RShared.r_refdef->VrectBottom = 100;

            FinalVert_t fv = default;
            fv.V[0] = 5;          // u
            fv.V[1] = 3;          // v
            fv.V[2] = 2 << 16;    // s
            fv.V[3] = 1 << 16;    // t
            fv.V[4] = 0;          // light (colormap row 0)
            fv.V[5] = 10 << 16;   // 1/z

            DPolyse.PolysetDrawFinalVerts(&fv, 1);

            Assert.AreEqual((byte)99, viewbuf[30 + 5]);
            Assert.AreEqual((short)10, zrow[5]);
        }
        finally
        {
            NativeMemory.Free(viewbuf);
            NativeMemory.Free(zrow);
            NativeMemory.Free(skin);
            NativeMemory.Free(colormap);
        }
    }

    [TestMethod]
    public void PolysetDrawFinalVerts_SkipsPixel_WhenBehindExistingZ()
    {
        byte* viewbuf = NativeMemory.AllocArrayZeroed<byte>(64);
        short* zrow = NativeMemory.AllocArrayZeroed<short>(16);
        byte* skin = NativeMemory.AllocArrayZeroed<byte>(64);
        byte* colormap = NativeMemory.AllocArrayZeroed<byte>(256 * 4);
        try
        {
            DIface.r_affinetridesc.SkinWidth = 8;
            DIface.r_affinetridesc.PSkin = skin;
            DPolyse.PolysetUpdateTables();
            DIface.acolormap = colormap;

            DLocal.d_viewbuffer = viewbuf;
            DLocal.d_scantable[0] = 0;
            DLocal.zspantable[0] = zrow;
            zrow[0] = 50; // already closer than the incoming z=10
            viewbuf[0] = 200;

            RShared.r_refdef->VrectRight = 100;
            RShared.r_refdef->VrectBottom = 100;

            FinalVert_t fv = default;
            fv.V[5] = 10 << 16;

            DPolyse.PolysetDrawFinalVerts(&fv, 1);

            Assert.AreEqual((byte)200, viewbuf[0]); // untouched
            Assert.AreEqual((short)50, zrow[0]);
        }
        finally
        {
            NativeMemory.Free(viewbuf);
            NativeMemory.Free(zrow);
            NativeMemory.Free(skin);
            NativeMemory.Free(colormap);
        }
    }

    private struct Gradients
    {
        public int LStepX, LStepY, SStepX, SStepY, TStepX, TStepY, ZiStepX, ZiStepY;
        public int SStepXFrac, TStepXFrac, StStepXWhole;
    }

    // D_PolysetCalcGradients (d_polyse.c) reference: the original scalar form
    private static Gradients CalcGradientsReference(int* p0, int* p1, int* p2, int xdenom, int skinwidth)
    {
        float p00_minus_p20 = p0[0] - p2[0];
        float p01_minus_p21 = p0[1] - p2[1];
        float p10_minus_p20 = p1[0] - p2[0];
        float p11_minus_p21 = p1[1] - p2[1];

        float xstepdenominv = 1.0f / xdenom;
        float ystepdenominv = -xstepdenominv;

        Gradients g = default;

        float t0 = p0[4] - p2[4];
        float t1 = p1[4] - p2[4];
        g.LStepX = float.ConvertToIntegerNative<int>(MathF.Ceiling(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv));
        g.LStepY = float.ConvertToIntegerNative<int>(MathF.Ceiling(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv));

        t0 = p0[2] - p2[2];
        t1 = p1[2] - p2[2];
        g.SStepX = float.ConvertToIntegerNative<int>(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv);
        g.SStepY = float.ConvertToIntegerNative<int>(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv);

        t0 = p0[3] - p2[3];
        t1 = p1[3] - p2[3];
        g.TStepX = float.ConvertToIntegerNative<int>(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv);
        g.TStepY = float.ConvertToIntegerNative<int>(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv);

        t0 = p0[5] - p2[5];
        t1 = p1[5] - p2[5];
        g.ZiStepX = float.ConvertToIntegerNative<int>(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv);
        g.ZiStepY = float.ConvertToIntegerNative<int>(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv);

        g.SStepXFrac = g.SStepX & 0xFFFF;
        g.TStepXFrac = g.TStepX & 0xFFFF;
        g.StStepXWhole = (skinwidth * (g.TStepX >> 16)) + (g.SStepX >> 16);
        return g;
    }

    private static Gradients ReadGradients() => new()
    {
        LStepX = DPolyse.r_lstepx,
        LStepY = DPolyse.r_lstepy,
        SStepX = DPolyse.r_sstepx,
        SStepY = DPolyse.r_sstepy,
        TStepX = DPolyse.r_tstepx,
        TStepY = DPolyse.r_tstepy,
        ZiStepX = DPolyse.r_zistepx,
        ZiStepY = DPolyse.r_zistepy,
        SStepXFrac = DPolyse.a_sstepxfrac,
        TStepXFrac = DPolyse.a_tstepxfrac,
        StStepXWhole = DPolyse.a_ststepxwhole,
    };

    // Same winding test D_DrawNonSubdiv uses to compute d_xdenom
    private static int XDenom(int* p0, int* p1, int* p2) =>
        ((p0[1] - p1[1]) * (p0[0] - p2[0])) - ((p0[0] - p1[0]) * (p0[1] - p2[1]));

    private static void SetVert(int* p, int u, int v, int s, int t, int light, int zi)
    {
        p[0] = u; p[1] = v; p[2] = s; p[3] = t; p[4] = light; p[5] = zi;
    }

    // realistic ranges: screen u/v, 16.16 skin s/t, light, 16.16 zi
    private static void SetRandomVert(Random rng, int* p, int skinwidth) =>
        SetVert(p, rng.Next(0, 320), rng.Next(0, 200), rng.Next(0, skinwidth << 16),
                rng.Next(0, 200 << 16), rng.Next(0, 0xFFFF), rng.Next(0, int.MaxValue));

    [TestMethod]
    public void PolysetCalcGradients_RandomTriangles_MatchOriginal()
    {
        Random rng = new(2024);
        const int skinwidth = 296;
        int tested = 0;

        while (tested < 5000)
        {
            int* p0 = DPolyse.r_p0, p1 = DPolyse.r_p1, p2 = DPolyse.r_p2;
            SetRandomVert(rng, p0, skinwidth);
            SetRandomVert(rng, p1, skinwidth);
            SetRandomVert(rng, p2, skinwidth);

            int xdenom = XDenom(p0, p1, p2);
            if (xdenom >= 0)
            {
                continue; // back-facing or degenerate, never reaches CalcGradients
            }

            DPolyse.d_xdenom = xdenom;
            Gradients expected = CalcGradientsReference(p0, p1, p2, xdenom, skinwidth);
            DPolyse.PolysetCalcGradients(skinwidth);

            Assert.AreEqual(expected, ReadGradients(), $"triangle {tested}");
            tested++;
        }
    }

    [TestMethod]
    public void PolysetCalcGradients_KnownTriangle_ProducesExpectedSteps()
    {
        // right triangle: p2 at origin, p0 one pixel right, p1 one pixel down
        // (xdenom = -1). Each attribute rises by (dx, dy) per pixel.
        SetVert(DPolyse.r_p0, 1, 0, 3 << 16, 5 << 16, 100, 7 << 16);
        SetVert(DPolyse.r_p1, 0, 1, 2 << 16, 4 << 16, 40, 9 << 16);
        SetVert(DPolyse.r_p2, 0, 0, 0, 0, 0, 0);
        DPolyse.d_xdenom = XDenom(DPolyse.r_p0, DPolyse.r_p1, DPolyse.r_p2);
        Assert.AreEqual(-1, DPolyse.d_xdenom);

        DPolyse.PolysetCalcGradients(64);

        Assert.AreEqual(3 << 16, DPolyse.r_sstepx);
        Assert.AreEqual(2 << 16, DPolyse.r_sstepy);
        Assert.AreEqual(5 << 16, DPolyse.r_tstepx);
        Assert.AreEqual(4 << 16, DPolyse.r_tstepy);
        Assert.AreEqual(100, DPolyse.r_lstepx);
        Assert.AreEqual(40, DPolyse.r_lstepy);
        Assert.AreEqual(7 << 16, DPolyse.r_zistepx);
        Assert.AreEqual(9 << 16, DPolyse.r_zistepy);
        Assert.AreEqual(0, DPolyse.a_sstepxfrac);
        Assert.AreEqual(0, DPolyse.a_tstepxfrac);
        Assert.AreEqual((64 * 5) + 3, DPolyse.a_ststepxwhole);
    }

    [TestMethod]
    public void PolysetCalcGradients_CeilsLightOnly_TruncatesOthers()
    {
        // p0 two pixels right (xdenom = -2): x steps are half the p0 deltas.
        // Positive 3/2 -> light ceils to 2, the others truncate to 1;
        // negative -3/2 -> light ceils to -1, the others truncate to -1.
        SetVert(DPolyse.r_p0, 2, 0, 3, 3, 3, 3);
        SetVert(DPolyse.r_p1, 0, 1, 0, 0, 0, 0);
        SetVert(DPolyse.r_p2, 0, 0, 0, 0, 0, 0);
        DPolyse.d_xdenom = XDenom(DPolyse.r_p0, DPolyse.r_p1, DPolyse.r_p2);
        Assert.AreEqual(-2, DPolyse.d_xdenom);

        DPolyse.PolysetCalcGradients(64);
        Assert.AreEqual(2, DPolyse.r_lstepx);
        Assert.AreEqual(1, DPolyse.r_sstepx);
        Assert.AreEqual(1, DPolyse.r_tstepx);
        Assert.AreEqual(1, DPolyse.r_zistepx);

        SetVert(DPolyse.r_p0, 2, 0, -3, -3, -3, -3);
        DPolyse.PolysetCalcGradients(64);
        Assert.AreEqual(-1, DPolyse.r_lstepx);
        Assert.AreEqual(-1, DPolyse.r_sstepx);
        Assert.AreEqual(-1, DPolyse.r_tstepx);
        Assert.AreEqual(-1, DPolyse.r_zistepx);
    }

    [TestMethod]
    public void PolysetDrawSpans8_MatchesOriginalPerPixelStepping()
    {
        const int n = 30, max = 40, skin = 64;
        Random rng = new(11);
        SpanPackage_t* spans = NativeMemory.AllocArrayZeroed<SpanPackage_t>(n + 1);
        byte* dest = NativeMemory.AllocArrayZeroed<byte>(n * max);
        short* z = NativeMemory.AllocArrayZeroed<short>(n * max);
        byte* tex = NativeMemory.AllocArrayZeroed<byte>(skin * skin * 2);
        byte* cmap = NativeMemory.AllocArrayZeroed<byte>(16384);
        byte[] expDest = new byte[n * max];
        short[] expZ = new short[n * max];
        try
        {
            for (int i = 0; i < skin * skin * 2; i++)
            {
                tex[i] = (byte)rng.Next(256);
            }

            for (int i = 0; i < 16384; i++)
            {
                cmap[i] = (byte)rng.Next(256);
            }

            for (int i = 0; i < n * max; i++)
            {
                z[i] = (short)rng.Next(0, 6000);
            }

            for (int i = 0; i < n * max; i++)
            {
                expZ[i] = z[i];
            }

            for (int i = 0; i < n; i++)
            {
                spans[i] = new SpanPackage_t
                {
                    PDest = dest + (i * max),
                    Pz = z + (i * max),
                    Count = rng.Next(2, max - 4),
                    PTex = tex + rng.Next(0, 200),
                    Sfrac = rng.Next(0, 0x10000),
                    Tfrac = rng.Next(0, 0x10000),
                    Light = rng.Next(0, 0x3F00),
                    Zi = rng.Next(1000, 20000) << 16,
                };
            }

            spans[n].Count = -999999;

            DIface.acolormap = cmap;
            DIface.r_affinetridesc.SkinWidth = skin;
            DPolyse.r_zistepx = 3000; DPolyse.r_lstepx = 17; DPolyse.a_ststepxwhole = 1;
            DPolyse.a_sstepxfrac = 0x4000; DPolyse.a_tstepxfrac = 0x2000;
            typeof(DPolyse).GetField("d_aspancount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, max);
            typeof(DPolyse).GetField("d_countextrastep", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, 0);
            DEdge.errorterm = -5; DEdge.erroradjustup = 3; DEdge.erroradjustdown = 7; DEdge.ubasestep = 0;

            // reference: the original statement-for-statement, reading the statics every pixel
            for (int i = 0; i < n; i++)
            {
                int lcount = max - spans[i].Count;
                byte* lptex = spans[i].PTex;
                int lsfrac = spans[i].Sfrac, ltfrac = spans[i].Tfrac, llight = spans[i].Light, lzi = spans[i].Zi;
                for (int px = 0; px < lcount; px++)
                {
                    int o = (i * max) + px;
                    if (lzi >> 16 >= expZ[o]) { expDest[o] = cmap[*lptex + (llight & 0xFF00)]; expZ[o] = (short)(lzi >> 16); }
                    lzi += DPolyse.r_zistepx; llight += DPolyse.r_lstepx;
                    lptex += DPolyse.a_ststepxwhole;
                    lsfrac += DPolyse.a_sstepxfrac; lptex += lsfrac >> 16; lsfrac &= 0xFFFF;
                    ltfrac += DPolyse.a_tstepxfrac;
                    if ((ltfrac & 0x10000) != 0) { lptex += skin; ltfrac &= 0xFFFF; }
                }
            }

            DPolyse.PolysetDrawSpans8(spans);

            for (int i = 0; i < n * max; i++)
            {
                Assert.AreEqual(expDest[i], dest[i], $"dest[{i}]");
                Assert.AreEqual(expZ[i], z[i], $"z[{i}]");
            }
        }
        finally
        {
            NativeMemory.Free(spans); NativeMemory.Free(dest); NativeMemory.Free(z); NativeMemory.Free(tex); NativeMemory.Free(cmap);
        }
    }
}
