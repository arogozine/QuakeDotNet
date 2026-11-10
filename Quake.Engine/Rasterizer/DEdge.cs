// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Quake.Engine;

/// <summary>
/// d_edge.c: edge-list to span conversion support. D_DrawSurfaces and
/// D_CalcGradients (the top-level per-frame surface walk and its texture-
/// axis gradient setup) need the active-edge-table surface stack,
/// currententity/cl_entities, R_RotateBmodel, and TransformVector, all of
/// which live in the r_*.c scene layer (RBsp.cs/REdge.cs/RMain.cs); r_edge.c's
/// R_ScanEdges (REdge.cs) is D_DrawSurfaces' only caller.
/// </summary>
public static unsafe class DEdge
{
    // ubasestep, errorterm, erroradjustup, erroradjustdown
    // Used by D_PolysetSetUpForLineScan/D_PolysetScanLeftEdge (d_polyse.c)
    // and the active-edge-table walk in r_edge.c (REdge.cs).
    public static int ubasestep, errorterm, erroradjustup, erroradjustdown;

    // transformed_modelorg
    public static Vec3_t transformed_modelorg;

    // miplevel (static in the original)
    private static int miplevel;

    // r_skymade
    public static bool r_skymade;

    // D_Sky_uv_To_st
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SkyUvToSt(int u, int v, int* s, int* t)
    {
        float temp = RShared.r_refdef->Vrect.Width >= RShared.r_refdef->Vrect.Height
            ? RShared.r_refdef->Vrect.Width
            : RShared.r_refdef->Vrect.Height;

        float wu = 8192.0f * (u - ((int)Vid.vid.Width >> 1)) / temp;
        float wv = 8192.0f * (((int)Vid.vid.Height >> 1) - v) / temp;

        Vec3_t end = 4096 * RShared.vpn + wu * RShared.vright + wv * RShared.vup;
        end[2] *= 3;
        MathLib.VectorNormalize(&end);

        temp = RSky.skytime * RSky.skyspeed; // TODO (original comment): add D_SetupFrame & set this there

        *s = float.ConvertToInteger<int>((temp + 6 * (128 / 2 - 1) * end[0]) * 0x10000);
        *t = float.ConvertToInteger<int>((temp + 6 * (128 / 2 - 1) * end[1]) * 0x10000);
    }

    // D_DrawSkyScans8
    public static void DrawSkyScans8(ESpan_t* pspan)
    {
        const int SKY_SPAN_SHIFT = 5;
        const int SKY_SPAN_MAX = 1 << SKY_SPAN_SHIFT;

        int sstep = 0;
        int tstep = 0;
        int s = 0, t = 0, snext = 0, tnext = 0;

        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        byte* skysource = RSky.r_skysource;

        Vector256<int> indexV = Vector256.CreateSequence(0, 1);
        Vector256<int> rSkyTMaskV = Vector256.Create(DLocal.R_SKY_TMASK);
        Vector256<int> rSkySMaskV = Vector256.Create(DLocal.R_SKY_SMASK);

        int* idx = stackalloc int[Vector256<int>.Count];
        ulong* idx64 = (ulong*)idx;

        do
        {
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;

            int count = pspan->Count;

            // calculate the initial s & t
            int u = pspan->U;
            int v = pspan->V;
            SkyUvToSt(u, v, &s, &t);

            do
            {
                int spancount = count >= SKY_SPAN_MAX ? SKY_SPAN_MAX : count;

                count -= spancount;

                if (count != 0)
                {
                    u += spancount;

                    // calculate s and t at far end of span,
                    // calculate s and t steps across span by shifting
                    SkyUvToSt(u, v, &snext, &tnext);

                    sstep = (snext - s) >> SKY_SPAN_SHIFT;
                    tstep = (tnext - t) >> SKY_SPAN_SHIFT;
                }
                else
                {
                    // calculate s and t at last pixel in span,
                    // calculate s and t steps across span by division
                    int spancountminus1 = spancount - 1;

                    if (spancountminus1 > 0)
                    {
                        u += spancountminus1;
                        SkyUvToSt(u, v, &snext, &tnext);

                        sstep = (snext - s) / spancountminus1;
                        tstep = (tnext - t) / spancountminus1;
                    }
                }

                if (Vector256.IsHardwareAccelerated && spancount == SKY_SPAN_MAX)
                {
                    do
                    {
                        Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                        Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                        (((tV & rSkyTMaskV) >> 8) + ((sV & rSkySMaskV) >> 16)).Store(idx);

                        ulong i01 = idx64[0], i23 = idx64[1], i45 = idx64[2], i67 = idx64[3];

                        ulong texels =
                              skysource[(uint)i01]
                            | (ulong)skysource[(uint)(i01 >> 32)] << 8
                            | (ulong)skysource[(uint)i23] << 16
                            | (ulong)skysource[(uint)(i23 >> 32)] << 24
                            | (ulong)skysource[(uint)i45] << 32
                            | (ulong)skysource[(uint)(i45 >> 32)] << 40
                            | (ulong)skysource[(uint)i67] << 48
                            | (ulong)skysource[(uint)(i67 >> 32)] << 56;

                        Unsafe.WriteUnaligned(pdest, texels);
                        pdest += Vector256<int>.Count;

                        s += sstep * Vector256<int>.Count;
                        t += tstep * Vector256<int>.Count;

                        spancount -= Vector256<int>.Count;
                    } while (spancount > 0);
                }
                else
                {
                    do
                    {
                        *pdest++ = skysource[((t & DLocal.R_SKY_TMASK) >> 8) + ((s & DLocal.R_SKY_SMASK) >> 16)];
                        s += sstep;
                        t += tstep;
                    } while (--spancount > 0);
                }

                s = snext;
                t = tnext;
            } while (count > 0);
        } while ((pspan = pspan->PNext) != null);
    }

    // D_DrawPoly
    public static void DrawPoly()
    {
        // this driver takes spans, not polygons -- a permanent no-op in the
        // original itself (r_draw.c's R_RenderPoly calls it purely for
        // interface symmetry with the ASM/GL polygon rasterizers).
    }

    // D_MipLevelForScale
    public static int MipLevelForScale(float scale)
    {
        int lmiplevel;

        if (scale >= DLocal.d_scalemip[0])
            lmiplevel = 0;
        else if (scale >= DLocal.d_scalemip[1])
            lmiplevel = 1;
        else if (scale >= DLocal.d_scalemip[2])
            lmiplevel = 2;
        else
            lmiplevel = 3;

        if (lmiplevel < DLocal.d_minmip)
            lmiplevel = DLocal.d_minmip;

        return lmiplevel;
    }

    // D_DrawSolidSurface
    public static void DrawSolidSurface(Surf_t* surf, int color)
    {
        int pix = (color << 24) | (color << 16) | (color << 8) | color;

        for (ESpan_t* span = surf->Spans; span != null; span = span->PNext)
        {
            byte* pdest = (byte*)DLocal.d_viewbuffer + RShared.screenwidth * span->V;
            int u = span->U;
            int u2 = span->U + span->Count - 1;
            pdest[u] = (byte)pix;

            if (u2 - u < 8)
            {
                for (u++; u <= u2; u++)
                    pdest[u] = (byte)pix;
            }
            else
            {
                for (u++; (u & 3) != 0; u++)
                    pdest[u] = (byte)pix;

                u2 -= 4;
                for (; u <= u2; u += 4)
                    *(int*)(pdest + u) = pix;
                u2 += 4;
                for (; u <= u2; u++)
                    pdest[u] = (byte)pix;
            }
        }
    }

    // D_CalcGradients
    public static void CalcGradients(MSurface_t* pface)
    {
        float mipscale = 1.0f / (1 << miplevel);

        Vec3_t sAxisIn = Unsafe.ReadUnaligned<Vec3_t>(pface->TexInfo->Vecs);
        Vec3_t tAxisIn = Unsafe.ReadUnaligned<Vec3_t>(pface->TexInfo->Vecs + 4);

        RMisc.TransformVector(in sAxisIn, out Vec3_t p_saxis);
        RMisc.TransformVector(in tAxisIn, out Vec3_t p_taxis);

        float t = RShared.xscaleinv * mipscale;
        DLocal.d_sdivzstepu = p_saxis[0] * t;
        DLocal.d_tdivzstepu = p_taxis[0] * t;

        t = RShared.yscaleinv * mipscale;
        DLocal.d_sdivzstepv = -p_saxis[1] * t;
        DLocal.d_tdivzstepv = -p_taxis[1] * t;

        DLocal.d_sdivzorigin = p_saxis[2] * mipscale - RShared.xcenter * DLocal.d_sdivzstepu -
                                RShared.ycenter * DLocal.d_sdivzstepv;
        DLocal.d_tdivzorigin = p_taxis[2] * mipscale - RShared.xcenter * DLocal.d_tdivzstepu -
                                RShared.ycenter * DLocal.d_tdivzstepv;

        Vec3_t p_temp1 = transformed_modelorg * mipscale;

        t = 0x10000 * mipscale;
        DLocal.sadjust = float.ConvertToIntegerNative<int>(Vector3.Dot(p_temp1, p_saxis) * 0x10000 + 0.5f)
                         - ((pface->TextureMins[0] << 16) >> miplevel)
                         + float.ConvertToIntegerNative<int>(pface->TexInfo->Vecs[3] * t);
        DLocal.tadjust = float.ConvertToIntegerNative<int>(Vector3.Dot(p_temp1, p_taxis) * 0x10000 + 0.5f)
                         - ((pface->TextureMins[1] << 16) >> miplevel)
                         + float.ConvertToIntegerNative<int>(pface->TexInfo->Vecs[7] * t);

        // -1 (-epsilon) so we never wander off the edge of the texture
        DLocal.bbextents = ((pface->Extents[0] << 16) >> miplevel) - 1;
        DLocal.bbextentt = ((pface->Extents[1] << 16) >> miplevel) - 1;
    }

    // D_DrawSurfaces
    public static void DrawSurfaces()
    {
        RShared.currententity = ClEntities.cl_entities; // &cl_entities[0]
        RMisc.TransformVector(RShared.modelorg, out transformed_modelorg);

        Vec3_t world_transformed_modelorg = transformed_modelorg;

        // TODO (original comment): could preset a lot of this at mode set time
        if (RMain.r_drawflat->Value != 0)
        {
            for (Surf_t* s = &RShared.surfaces[1]; s < RShared.surface_p; s++)
            {
                if (s->Spans == null)
                    continue;

                DLocal.d_zistepu = s->DZiStepU;
                DLocal.d_zistepv = s->DZiStepV;
                DLocal.d_ziorigin = s->DZiOrigin;

                DrawSolidSurface(s, (int)(nint)s->Data & 0xFF);
                DScan.DrawZSpans(s->Spans);
            }

            return;
        }

        for (Surf_t* s = &RShared.surfaces[1]; s < RShared.surface_p; s++)
        {
            if (s->Spans == null)
                continue;

            RShared.r_drawnpolycount++;

            DLocal.d_zistepu = s->DZiStepU;
            DLocal.d_zistepv = s->DZiStepV;
            DLocal.d_ziorigin = s->DZiOrigin;

            if ((s->Flags & Surf.DRAWSKY) != 0)
            {
                if (!r_skymade)
                    RSky.MakeSky();

                DrawSkyScans8(s->Spans);
                DScan.DrawZSpans(s->Spans);
            }
            else if ((s->Flags & Surf.DRAWBACKGROUND) != 0)
            {
                // set up a gradient for the background surface that places
                // it effectively at infinity distance from the viewpoint
                DLocal.d_zistepu = 0;
                DLocal.d_zistepv = 0;
                DLocal.d_ziorigin = -0.9f;

                DrawSolidSurface(s, (int)RMain.r_clearcolor->Value & 0xFF);
                DScan.DrawZSpans(s->Spans);
            }
            else if ((s->Flags & Surf.DRAWTURB) != 0)
            {
                MSurface_t* pface = (MSurface_t*)s->Data;
                miplevel = 0;
                RShared.cacheblock = (byte*)pface->TexInfo->Texture + pface->TexInfo->Texture->Offsets[0];
                RShared.cachewidth = 64;

                if (s->InSubmodel)
                {
                    // FIXME (original comment): we don't want to do all this for every polygon!
                    // TODO (original comment): store once at start of frame
                    RShared.currententity = s->Entity; // FIXME (original comment): make this passed in to R_RotateBmodel()
                    Vec3_t local_modelorg = RShared.r_origin - RShared.currententity->Origin;
                    RMisc.TransformVector(local_modelorg, out transformed_modelorg);

                    RBsp.RotateBmodel(); // FIXME (original comment): don't mess with the frustum, make entity passed in
                }

                CalcGradients(pface);
                DScan.Turbulent8(s->Spans);
                DScan.DrawZSpans(s->Spans);

                if (s->InSubmodel)
                {
                    // restore the old drawing state
                    // FIXME (original comment): we don't want to do this every time! TODO (original comment): speed up
                    RShared.currententity = ClEntities.cl_entities;

                    transformed_modelorg = world_transformed_modelorg;
                    RShared.vpn = RShared.base_vpn;
                    RShared.vup = RShared.base_vup;
                    RShared.vright = RShared.base_vright;
                    RShared.modelorg = RShared.base_modelorg;

                    RMisc.TransformFrustum();
                }
            }
            else
            {
                if (s->InSubmodel)
                {
                    // FIXME (original comment): we don't want to do all this for every polygon!
                    // TODO (original comment): store once at start of frame
                    RShared.currententity = s->Entity; // FIXME (original comment): make this passed in to R_RotateBmodel()
                    Vec3_t local_modelorg = RShared.r_origin - RShared.currententity->Origin;
                    RMisc.TransformVector(local_modelorg, out transformed_modelorg);

                    RBsp.RotateBmodel(); // FIXME (original comment): don't mess with the frustum, make entity passed in
                }

                MSurface_t* pface = (MSurface_t*)s->Data;
                miplevel = MipLevelForScale(s->NearZi * DLocal.scale_for_mip * pface->TexInfo->MipAdjust);

                // FIXME (original comment): make this passed in to D_CacheSurface
                SurfCache_t* pcurrentcache = DSurf.CacheSurface(pface, miplevel);

                RShared.cacheblock = pcurrentcache->Data;
                RShared.cachewidth = (int)pcurrentcache->Width;

                CalcGradients(pface);

                DLocal.d_drawspans(s->Spans);

                DScan.DrawZSpans(s->Spans);

                if (s->InSubmodel)
                {
                    // restore the old drawing state
                    // FIXME (original comment): we don't want to do this every time! TODO (original comment): speed up
                    RShared.currententity = ClEntities.cl_entities;

                    transformed_modelorg = world_transformed_modelorg;
                    RShared.vpn = RShared.base_vpn;
                    RShared.vup = RShared.base_vup;
                    RShared.vright = RShared.base_vright;
                    RShared.modelorg = RShared.base_modelorg;

                    RMisc.TransformFrustum();
                }
            }
        }
    }
}
