// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Engine;

/// <summary>
/// r_surf.c: surface-related refresh code -- builds/lights the surface
/// texture cache tiles that D_CacheSurface (DSurf.cs) hands off to the
/// scanline rasterizer.
/// </summary>
public static unsafe class RSurf
{
    public static int lightleft, sourcesstep, blocksize, sourcetstep;
    public static int lightright, lightleftstep, lightrightstep, blockdivshift;
    public static uint blockdivmask;
    public static void* prowdestbase;
    public static byte* pbasesource;
    public static int surfrowbytes; // used by ASM files
    public static uint* r_lightptr;
    public static int r_stepback;
    public static int r_lightwidth;
    public static int r_numhblocks, r_numvblocks;
    public static byte* r_source, r_sourcemax;

    private static readonly delegate* managed<void>[] surfmiptable =
    {
        &DrawSurfaceBlock8_mip0,
        &DrawSurfaceBlock8_mip1,
        &DrawSurfaceBlock8_mip2,
        &DrawSurfaceBlock8_mip3,
    };

    // blocklights[18*18]
    public static readonly uint* blocklights = NativeMemory.AllocArrayZeroed<uint>(18 * 18);

    // R_AddDynamicLights
    public static void AddDynamicLights()
    {
        MSurface_t* surf = DSurf.r_drawsurf.Surf;
        int smax = (surf->Extents[0] >> 4) + 1;
        int tmax = (surf->Extents[1] >> 4) + 1;
        MTexInfo_t* tex = surf->TexInfo;

        for (int lnum = 0; lnum < ClEntities.MAX_DLIGHTS; lnum++)
        {
            if ((surf->DLightBits & (1 << lnum)) == 0)
            {
                continue; // not lit by this light
            }

            float rad = ClEntities.cl_dlights[lnum].Radius;
            Vec3_t origin = ClEntities.cl_dlights[lnum].Origin;
            Vec3_t normal = surf->Plane->Normal;
            float dist = Vector3.Dot(origin, normal) - surf->Plane->Dist;
            rad -= MathF.Abs(dist);
            float minlight = ClEntities.cl_dlights[lnum].MinLight;
            if (rad < minlight)
            {
                continue;
            }

            minlight = rad - minlight;

            Vec3_t impact = origin - (normal * dist);

            Vec3_t local = default;
            local[0] = Vector3.Dot(impact, Unsafe.ReadUnaligned<Vec3_t>(tex->Vecs)) + tex->Vecs[3];
            local[1] = Vector3.Dot(impact, Unsafe.ReadUnaligned<Vec3_t>(tex->Vecs + 4)) + tex->Vecs[7];

            local[0] -= surf->TextureMins[0];
            local[1] -= surf->TextureMins[1];

            RSurfSimd.AddDlightLuxels(blocklights, smax, tmax, local[0], local[1], rad, minlight);
        }
    }

    // R_BuildLightMap
    public static void BuildLightMap()
    {
        MSurface_t* surf = DSurf.r_drawsurf.Surf;

        int smax = (surf->Extents[0] >> 4) + 1;
        int tmax = (surf->Extents[1] >> 4) + 1;
        int size = smax * tmax;
        byte* lightmap = surf->Samples;

        if (RMain.r_fullbright->Value != 0 || Cl.worldmodel->LightData == null)
        {
            new Span<uint>(blocklights, size).Clear();
            return;
        }

        // clear to ambient
        uint blockLightClearValue = (uint)(RShared.r_refdef->AmbientLight << 8);
        new Span<uint>(blocklights, size).Fill(blockLightClearValue);

        // add all the lightmaps
        if (lightmap != null)
        {
            for (int maps = 0; maps < BspFile.MAXLIGHTMAPS && surf->Styles[maps] != 255; maps++)
            {
                uint scale = (uint)DSurf.r_drawsurf.LightAdj[maps]; // 8.8 fraction
                RSurfSimd.AddScaled(blocklights, lightmap, size, scale);
                lightmap += size; // skip to next lightmap
            }
        }

        // add all the dynamic lights
        if (surf->DLightFrame == DIface.r_framecount)
        {
            AddDynamicLights();
        }

        // bound, invert, and shift
        RSurfSimd.BoundInvertShift(blocklights, size, 8 - Vid.VID_CBITS);
    }

    // R_TextureAnimation
    public static Texture_t* TextureAnimation(Texture_t* @base)
    {
        if (RShared.currententity->Frame != 0)
        {
            if (@base->AlternateAnims != null)
            {
                @base = @base->AlternateAnims;
            }
        }

        if (@base->AnimTotal == 0)
        {
            return @base;
        }

        int reletive = (int)(Cl.time * 10) % @base->AnimTotal;

        int count = 0;
        while (@base->AnimMin > reletive || @base->AnimMax <= reletive)
        {
            @base = @base->AnimNext;
            if (@base == null)
            {
                Sys.Error("R_TextureAnimation: broken cycle"u8);
            }

            if (++count > 100)
            {
                Sys.Error("R_TextureAnimation: infinite cycle"u8);
            }
        }

        return @base;
    }

    // R_DrawSurface
    public static void DrawSurface()
    {
        // calculate the lightings
        BuildLightMap();

        surfrowbytes = DSurf.r_drawsurf.RowBytes;

        Texture_t* mt = DSurf.r_drawsurf.Texture;

        r_source = (byte*)mt + mt->Offsets[DSurf.r_drawsurf.SurfMip];

        // the fractional light values should range from 0 to (VID_GRADES - 1) << 16
        // from a source range of 0 - 255

        int texwidth = (int)(mt->Width >> DSurf.r_drawsurf.SurfMip);

        blocksize = 16 >> DSurf.r_drawsurf.SurfMip;
        blockdivshift = 4 - DSurf.r_drawsurf.SurfMip;
        blockdivmask = (uint)((1 << blockdivshift) - 1);

        r_lightwidth = (DSurf.r_drawsurf.Surf->Extents[0] >> 4) + 1;

        r_numhblocks = DSurf.r_drawsurf.SurfWidth >> blockdivshift;
        r_numvblocks = DSurf.r_drawsurf.SurfHeight >> blockdivshift;

        //==============================

        delegate* managed<void> pblockdrawer;
        int horzblockstep;

        if (DIface.r_pixbytes == 1)
        {
            pblockdrawer = surfmiptable[DSurf.r_drawsurf.SurfMip];
            // TODO (original comment): only needs to be set when there is a display settings change
            horzblockstep = blocksize;
        }
        else
        {
            pblockdrawer = &DrawSurfaceBlock16;
            // TODO (original comment): only needs to be set when there is a display settings change
            horzblockstep = blocksize << 1;
        }

        int smax = (int)(mt->Width >> DSurf.r_drawsurf.SurfMip);
        int twidth = texwidth;
        int tmax = (int)(mt->Height >> DSurf.r_drawsurf.SurfMip);
        sourcetstep = texwidth;
        r_stepback = tmax * twidth;

        r_sourcemax = r_source + (tmax * smax);

        int soffset = DSurf.r_drawsurf.Surf->TextureMins[0];
        int basetoffset = DSurf.r_drawsurf.Surf->TextureMins[1];

        // << 16 components are to guarantee positive values for %
        soffset = ((soffset >> DSurf.r_drawsurf.SurfMip) + (smax << 16)) % smax;
        byte* basetptr = &r_source[
            (((basetoffset >> DSurf.r_drawsurf.SurfMip) + (tmax << 16)) % tmax) * twidth];

        byte* pcolumndest = DSurf.r_drawsurf.SurfDat;

        for (int u = 0; u < r_numhblocks; u++)
        {
            r_lightptr = blocklights + u;

            prowdestbase = pcolumndest;

            pbasesource = basetptr + soffset;

            pblockdrawer();

            soffset += blocksize;
            if (soffset >= smax)
            {
                soffset = 0;
            }

            pcolumndest += horzblockstep;
        }
    }

    // R_DrawSurfaceBlock8_mip0
    public static void DrawSurfaceBlock8_mip0()
    {
        if (Avx2.IsSupported)
        {
            DrawSurfaceBlock8_Avx2<Mip0>();
        }
        else
        {
            DrawSurfaceBlock8<Mip0>();
        }
    }

    // R_DrawSurfaceBlock8_mip1
    public static void DrawSurfaceBlock8_mip1()
    {
        if (Avx2.IsSupported)
        {
            DrawSurfaceBlock8_Avx2<Mip1>();
        }
        else
        {
            DrawSurfaceBlock8<Mip1>();
        }
    }

    // R_DrawSurfaceBlock8_mip2
    public static void DrawSurfaceBlock8_mip2() => DrawSurfaceBlock8<Mip2>();

    // R_DrawSurfaceBlock8_mip3
    public static void DrawSurfaceBlock8_mip3() => DrawSurfaceBlock8<Mip3>();

    // The four originals are identical except for the light-step shift and
    // inner loop counts (16/8/4/2); the mip level is a generic struct so the
    // JIT specializes each instantiation with constant shift/count.
    private interface IMip
    {
        static abstract int Shift { get; }
        static abstract int Count { get; }
    }
    private readonly struct Mip0 : IMip { public static int Shift => 4; public static int Count => 16; }
    private readonly struct Mip1 : IMip { public static int Shift => 3; public static int Count => 8; }
    private readonly struct Mip2 : IMip { public static int Shift => 2; public static int Count => 4; }
    private readonly struct Mip3 : IMip { public static int Shift => 1; public static int Count => 2; }

    // Scalar body of R_DrawSurfaceBlock8_mipN, with the lightleft/lightright/
    // step statics kept in locals (the statics are written back on exit, as
    // they are the original's globals shared with the ASM).
    private static void DrawSurfaceBlock8<TMip>() where TMip : IMip
    {
        int shift = TMip.Shift;
        int count = TMip.Count;
        byte* psource = pbasesource;
        byte* prowdest = (byte*)prowdestbase;
        byte* colorMap = Vid.vid.Colormap;
        uint* lightptr = r_lightptr;
        int sourcetstepL = sourcetstep, rowbytes = surfrowbytes, lightwidth = r_lightwidth;
        int lightleftL = lightleft, lightrightL = lightright, lightleftstepL = lightleftstep, lightrightstepL = lightrightstep;

        for (int v = 0; v < r_numvblocks; v++)
        {
            lightleftL = (int)lightptr[0];
            lightrightL = (int)lightptr[1];
            lightptr += lightwidth;
            lightleftstepL = ((int)lightptr[0] - lightleftL) >> shift;
            lightrightstepL = ((int)lightptr[1] - lightrightL) >> shift;

            for (int i = 0; i < count; i++)
            {
                int lightstep = (lightleftL - lightrightL) >> shift;
                int light = lightrightL;

                for (int b = count - 1; b >= 0; b--)
                {
                    prowdest[b] = colorMap[(light & 0xFF00) + psource[b]];
                    light += lightstep;
                }

                psource += sourcetstepL;
                lightrightL += lightrightstepL;
                lightleftL += lightleftstepL;
                prowdest += rowbytes;
            }

            if (psource >= r_sourcemax)
            {
                psource -= r_stepback;
            }
        }

        r_lightptr = lightptr;
        lightleft = lightleftL;
        lightright = lightrightL;
        lightleftstep = lightleftstepL;
        lightrightstep = lightrightstepL;
    }

    // Same computation as DrawSurfaceBlock8<TMip> for mip0/mip1 (8 or 16 px
    // rows): lane b gets light = lightright + (count-1-b) * lightstep, and the
    // colormap lookups are done with AVX2 dword gathers (low byte kept). The
    // gather may read up to 3 bytes past colormap[index]; the colormap is a
    // padded hunk allocation so this stays inside it.
    private static void DrawSurfaceBlock8_Avx2<TMip>()
        where TMip : IMip
    {
        int shift = TMip.Shift;
        int count = TMip.Count;
        byte* psource = pbasesource;
        byte* prowdest = (byte*)prowdestbase;
        byte* colorMap = Vid.vid.Colormap;
        uint* lightptr = r_lightptr;
        int sourcetstepL = sourcetstep, rowbytes = surfrowbytes, lightwidth = r_lightwidth;
        int lightleftL = lightleft, lightrightL = lightright, lightleftstepL = lightleftstep, lightrightstepL = lightrightstep;

        Vector256<int> ff00 = Vector256.Create(0xFF00);
        Vector256<int> ff = Vector256.Create(0xFF);
        // lanes 0..7 hold pixels count-1-b ... in pixel order
        Vector256<int> iotaLo = Vector256.CreateSequence(count - 1, -1);
        Vector256<int> iotaHi = Vector256.CreateSequence(7, -1);

        for (int v = 0; v < r_numvblocks; v++)
        {
            lightleftL = (int)lightptr[0];
            lightrightL = (int)lightptr[1];
            lightptr += lightwidth;
            lightleftstepL = ((int)lightptr[0] - lightleftL) >> shift;
            lightrightstepL = ((int)lightptr[1] - lightrightL) >> shift;

            for (int i = 0; i < count; i++)
            {
                int lightstep = (lightleftL - lightrightL) >> shift;
                Vector256<int> vstep = Vector256.Create(lightstep);
                Vector256<int> vbase = Vector256.Create(lightrightL);

                Vector256<int> idx0 = ((vbase + (vstep * iotaLo)) & ff00) + Avx2.ConvertToVector256Int32(psource);
                Vector256<int> g0 = Avx2.GatherVector256((int*)colorMap, idx0, 1) & ff;

                if (count == 16)
                {
                    Vector256<int> idx1 = ((vbase + (vstep * iotaHi)) & ff00) + Avx2.ConvertToVector256Int32(psource + 8);
                    Vector256<int> g1 = Avx2.GatherVector256((int*)colorMap, idx1, 1) & ff;
                    Vector128<short> s0 = Sse2.PackSignedSaturate(g0.GetLower(), g0.GetUpper());
                    Vector128<short> s1 = Sse2.PackSignedSaturate(g1.GetLower(), g1.GetUpper());
                    Sse2.Store(prowdest, Sse2.PackUnsignedSaturate(s0, s1));
                }
                else
                {
                    Vector128<short> s0 = Sse2.PackSignedSaturate(g0.GetLower(), g0.GetUpper());
                    *(ulong*)prowdest = Sse2.PackUnsignedSaturate(s0, s0).AsUInt64().ToScalar();
                }

                psource += sourcetstepL;
                lightrightL += lightrightstepL;
                lightleftL += lightleftstepL;
                prowdest += rowbytes;
            }

            if (psource >= r_sourcemax)
            {
                psource -= r_stepback;
            }
        }

        r_lightptr = lightptr;
        lightleft = lightleftL;
        lightright = lightrightL;
        lightleftstep = lightleftstepL;
        lightrightstep = lightrightstepL;
    }

    // R_DrawSurfaceBlock16
    public static void DrawSurfaceBlock16()
    {
        ushort* prowdest = (ushort*)prowdestbase;

        for (int k = 0; k < blocksize; k++)
        {
            byte* psource = pbasesource;
            int lighttemp = lightright - lightleft;
            int lightstep = lighttemp >> blockdivshift;

            int light = lightleft;
            ushort* pdest = prowdest;

            for (int b = 0; b < blocksize; b++)
            {
                byte pix = *psource;
                *pdest = Vid.vid.Colormap16[(light & 0xFF00) + pix];
                psource += sourcesstep;
                pdest++;
                light += lightstep;
            }

            pbasesource += sourcetstep;
            lightright += lightrightstep;
            lightleft += lightleftstep;
            prowdest = (ushort*)((byte*)prowdest + surfrowbytes);
        }

        prowdestbase = prowdest;
    }

    // R_GenTurbTile

    // R_GenTurbTile16

}
