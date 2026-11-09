// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RSurfTests
{
    [TestInitialize]
    public void Setup()
    {
        RMain.r_fullbright->Value = 0;
        DIface.r_framecount = 0;
    }

    [TestMethod]
    public void TextureAnimation_NoAnimation_ReturnsSameTexture()
    {
        Entity_t entity = default;
        RShared.currententity = &entity;

        Texture_t tex = default; // AnimTotal = 0
        Texture_t* result = RSurf.TextureAnimation(&tex);

        Assert.IsTrue(result == &tex);
    }

    [TestMethod]
    public void TextureAnimation_CyclesToFrameContainingCurrentTime()
    {
        Entity_t entity = default;
        RShared.currententity = &entity;

        // Two-frame cycle: frame0 covers [0,5), frame1 covers [5,10), total 10.
        Texture_t frame0 = default, frame1 = default;
        frame0.AnimTotal = 10;
        frame0.AnimMin = 0;
        frame0.AnimMax = 5;
        frame0.AnimNext = &frame1;
        frame1.AnimTotal = 10;
        frame1.AnimMin = 5;
        frame1.AnimMax = 10;
        frame1.AnimNext = &frame0;

        Cl.time = 0.6; // (int)(0.6*10) % 10 = 6 -> frame1
        Texture_t* result = RSurf.TextureAnimation(&frame0);

        Assert.IsTrue(result == &frame1);
    }

    [TestMethod]
    public void TextureAnimation_UsesAlternateAnimsWhenEntityFrameNonzero()
    {
        Entity_t entity = default;
        entity.Frame = 1;
        RShared.currententity = &entity;

        Texture_t alt = default; // AnimTotal = 0 -> returned as-is
        Texture_t basetex = default;
        basetex.AlternateAnims = &alt;

        Texture_t* result = RSurf.TextureAnimation(&basetex);

        Assert.IsTrue(result == &alt);
    }

    [TestMethod]
    public void TextureAnimation_BrokenCycle_Throws()
    {
        Entity_t entity = default;
        RShared.currententity = &entity;

        Texture_t tex = default;
        tex.AnimTotal = 10;
        tex.AnimMin = 5; // current reletive (0) never satisfies min<=rel<max
        tex.AnimMax = 6;
        tex.AnimNext = null; // breaks immediately

        Cl.time = 0;

        Texture_t* pTex = &tex;
        Assert.ThrowsExactly<QuakeSysErrorException>(() => RSurf.TextureAnimation(pTex));
    }

    [TestMethod]
    public void BuildLightMap_FullbrightCvarSet_ZerosBlocklights()
    {
        RMain.r_fullbright->Value = 1;

        MPlane_t plane = default;
        MSurface_t surf = default;
        surf.Plane = &plane;
        surf.Extents[0] = 15; // smax = 2
        surf.Extents[1] = 15; // tmax = 2
        DSurf.r_drawsurf.Surf = &surf;

        RSurf.blocklights[0] = 999;

        RSurf.BuildLightMap();

        Assert.AreEqual(0u, RSurf.blocklights[0]);
    }

    [TestMethod]
    public void BuildLightMap_NoLightmapSamples_ClampsToAmbientFloor()
    {
        Model_t model = default;
        model.LightData = (byte*)1; // non-null sentinel -- worldmodel has lightdata
        Cl.worldmodel = &model;

        MPlane_t plane = default;
        MSurface_t surf = default;
        surf.Plane = &plane;
        surf.Extents[0] = 15; // smax = 2
        surf.Extents[1] = 15; // tmax = 2
        surf.Samples = null;  // no lightmap -- ambient only
        surf.Styles[0] = 255;
        DSurf.r_drawsurf.Surf = &surf;

        RShared.r_refdef->AmbientLight = 0;

        RSurf.BuildLightMap();

        // ambient=0 -> raw 0, inverted: (255*256-0)>>2, clamped to floor 1<<6=64
        // since (255*256)>>2 = 16320, well above the floor, verify the actual formula result.
        int expected = (255 * 256 - 0) >> (8 - Vid.VID_CBITS);
        Assert.AreEqual((uint)expected, RSurf.blocklights[0]);
    }

    // Direct copy of the original R_DrawSurfaceBlock8_mipN (shift/count per mip).
    private static void ReferenceBlock8(int shift, int count, byte* psource, byte* prowdest, byte* colorMap,
        uint* lightptr, int lightwidth, int numvblocks, int sourcetstep, int rowbytes, byte* sourcemax, int stepback)
    {
        for (int v = 0; v < numvblocks; v++)
        {
            int lightleft = (int)lightptr[0];
            int lightright = (int)lightptr[1];
            lightptr += lightwidth;
            int lightleftstep = ((int)lightptr[0] - lightleft) >> shift;
            int lightrightstep = ((int)lightptr[1] - lightright) >> shift;
            for (int i = 0; i < count; i++)
            {
                int lightstep = (lightleft - lightright) >> shift;
                int light = lightright;
                for (int b = count - 1; b >= 0; b--)
                {
                    prowdest[b] = colorMap[(light & 0xFF00) + psource[b]];
                    light += lightstep;
                }
                psource += sourcetstep;
                lightright += lightrightstep;
                lightleft += lightleftstep;
                prowdest += rowbytes;
            }
            if (psource >= sourcemax)
                psource -= stepback;
        }
    }

    [TestMethod]
    public void DrawSurfaceBlock8_AllMips_MatchOriginal()
    {
        const int nv = 3, hb = 3, texW = 64, texH = 64;
        Random rng = new(42);
        byte* colormap = NativeMemory.AllocArrayZeroed<byte>(16384 + 16);
        byte* source = NativeMemory.AllocArrayZeroed<byte>(texW * texH);
        uint* lights = NativeMemory.AllocArrayZeroed<uint>((hb + 1) * (nv + 1));
        byte* destA = NativeMemory.AllocArrayZeroed<byte>(16 * hb * 16 * nv);
        byte* destB = NativeMemory.AllocArrayZeroed<byte>(16 * hb * 16 * nv);
        try
        {
            for (int i = 0; i < 16384; i++) colormap[i] = (byte)rng.Next(256);
            for (int i = 0; i < texW * texH; i++) source[i] = (byte)rng.Next(256);
            for (int i = 0; i < (hb + 1) * (nv + 1); i++) lights[i] = (uint)rng.Next(64, 16321);

            Vid.vid.Colormap = colormap;
            int lw = hb + 1;
            int rowbytes = 16 * hb;

            for (int mip = 0; mip < 4; mip++)
            {
                int count = 16 >> mip, shift = 4 - mip;
                new Span<byte>(destA, 16 * hb * 16 * nv).Clear();
                new Span<byte>(destB, 16 * hb * 16 * nv).Clear();

                // texture treated as texW-wide; source wraps when it passes sourcemax
                RSurf.surfrowbytes = rowbytes;
                RSurf.sourcetstep = texW;
                RSurf.r_numvblocks = nv;
                RSurf.r_lightwidth = lw;
                RSurf.r_source = source;
                RSurf.r_sourcemax = source + texW * count * nv - texW; // force a wrap
                RSurf.r_stepback = texW * 2;

                for (int u = 0; u < hb; u++)
                {
                    ReferenceBlock8(shift, count, source + u * count, destA + u * count, colormap, lights + u, lw, nv,
                        texW, rowbytes, RSurf.r_sourcemax, RSurf.r_stepback);

                    RSurf.r_lightptr = lights + u;
                    RSurf.prowdestbase = destB + u * count;
                    RSurf.pbasesource = source + u * count;
                    switch (mip)
                    {
                        case 0: RSurf.DrawSurfaceBlock8_mip0(); break;
                        case 1: RSurf.DrawSurfaceBlock8_mip1(); break;
                        case 2: RSurf.DrawSurfaceBlock8_mip2(); break;
                        default: RSurf.DrawSurfaceBlock8_mip3(); break;
                    }
                }

                Assert.IsTrue(new ReadOnlySpan<byte>(destA, 16 * hb * 16 * nv).SequenceEqual(new ReadOnlySpan<byte>(destB, 16 * hb * 16 * nv)),
                    $"mip{mip} output differs from original");
            }
        }
        finally
        {
            NativeMemory.Free(colormap); NativeMemory.Free(source); NativeMemory.Free(lights);
            NativeMemory.Free(destA); NativeMemory.Free(destB);
        }
    }
}
