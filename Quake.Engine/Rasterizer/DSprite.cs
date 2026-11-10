// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;
namespace Quake.Engine;

/// <summary>
/// d_sprite.c: software top-level rasterization driver module for drawing
/// sprites. Pixel writes go through DIface.acolormap (the 256*VID_GRADES
/// light-scaled palette table) exactly as the original -- see the
/// "no software-renderer palette expansion path" note on AliasModel.cs/
/// SpriteModel.cs; DIface.acolormap is populated by RAlias.cs
/// (DIface.acolormap = currententity->ColorMap) before any pixel that
/// reads through it is drawn. Only the !id386 C fallback is ported.
/// </summary>
public static unsafe class DSprite
{
    private static int sprite_height;
    private static int minindex, maxindex;
    // D_DrawSprite: spans[] -- the original is a stack local; allocated once
    // here to avoid zero-initializing it per call and to keep sprite_spans valid.
    internal static readonly SSpan_t* sprite_spans = NativeMemory.AllocArray<SSpan_t>(RShared.MAXHEIGHT + 1);

    // D_SpriteDrawSpans
    public static void SpriteDrawSpans(SSpan_t* pspan)
    {
        int sstep = 0, tstep = 0;

        byte* pbase = RShared.cacheblock;

        float sdivz8stepu = DLocal.d_sdivzstepu * 8;
        float tdivz8stepu = DLocal.d_tdivzstepu * 8;
        float zi8stepu = DLocal.d_zistepu * 8;

        int izistep = (int)(DLocal.d_zistepu * 0x8000 * 0x10000);

        do
        {
            byte* pdest = (byte*)DLocal.d_viewbuffer + RShared.screenwidth * pspan->V + pspan->U;
            short* pz = DLocal.d_pzbuffer + DLocal.d_zwidth * pspan->V + pspan->U;

            int count = pspan->Count;

            if (count > 0)
            {
                float du = pspan->U;
                float dv = pspan->V;

                float sdivz = DLocal.d_sdivzorigin + dv * DLocal.d_sdivzstepv + du * DLocal.d_sdivzstepu;
                float tdivz = DLocal.d_tdivzorigin + dv * DLocal.d_tdivzstepv + du * DLocal.d_tdivzstepu;
                float zi = DLocal.d_ziorigin + dv * DLocal.d_zistepv + du * DLocal.d_zistepu;
                float z = 0x10000 / zi;
                int izi = (int)(zi * 0x8000 * 0x10000);

                int s = (int)(sdivz * z) + DLocal.sadjust;
                if (s > DLocal.bbextents)
                    s = DLocal.bbextents;
                else if (s < 0)
                    s = 0;

                int t = (int)(tdivz * z) + DLocal.tadjust;
                if (t > DLocal.bbextentt)
                    t = DLocal.bbextentt;
                else if (t < 0)
                    t = 0;

                do
                {
                    int spancount = count >= 8 ? 8 : count;
                    count -= spancount;

                    int snext, tnext;

                    if (count != 0)
                    {
                        sdivz += sdivz8stepu;
                        tdivz += tdivz8stepu;
                        zi += zi8stepu;
                        z = 0x10000 / zi;

                        snext = (int)(sdivz * z) + DLocal.sadjust;
                        if (snext > DLocal.bbextents)
                            snext = DLocal.bbextents;
                        else if (snext < 8)
                            snext = 8;

                        tnext = (int)(tdivz * z) + DLocal.tadjust;
                        if (tnext > DLocal.bbextentt)
                            tnext = DLocal.bbextentt;
                        else if (tnext < 8)
                            tnext = 8;

                        sstep = (snext - s) >> 3;
                        tstep = (tnext - t) >> 3;
                    }
                    else
                    {
                        float spancountminus1 = spancount - 1;
                        sdivz += DLocal.d_sdivzstepu * spancountminus1;
                        tdivz += DLocal.d_tdivzstepu * spancountminus1;
                        zi += DLocal.d_zistepu * spancountminus1;
                        z = 0x10000 / zi;
                        snext = (int)(sdivz * z) + DLocal.sadjust;
                        if (snext > DLocal.bbextents)
                            snext = DLocal.bbextents;
                        else if (snext < 8)
                            snext = 8;

                        tnext = (int)(tdivz * z) + DLocal.tadjust;
                        if (tnext > DLocal.bbextentt)
                            tnext = DLocal.bbextentt;
                        else if (tnext < 8)
                            tnext = 8;

                        if (spancount > 1)
                        {
                            sstep = (snext - s) / (spancount - 1);
                            tstep = (tnext - t) / (spancount - 1);
                        }
                    }

                    do
                    {
                        byte btemp = *(pbase + (s >> 16) + (t >> 16) * RShared.cachewidth);
                        if (btemp != 255)
                        {
                            if (*pz <= izi >> 16)
                            {
                                *pz = (short)(izi >> 16);
                                *pdest = btemp;
                            }
                        }

                        izi += izistep;
                        pdest++;
                        pz++;
                        s += sstep;
                        t += tstep;
                    } while (--spancount > 0);

                    s = snext;
                    t = tnext;

                } while (count > 0);
            }

            pspan++;

        } while (pspan->Count != DLocal.DS_SPAN_LIST_END);
    }

    // D_SpriteScanLeftEdge
    public static void SpriteScanLeftEdge()
    {
        SSpan_t* pspan = sprite_spans;
        int i = minindex;
        if (i == 0)
            i = DIface.r_spritedesc.Nump;

        int lmaxindex = maxindex;
        if (lmaxindex == 0)
            lmaxindex = DIface.r_spritedesc.Nump;

        float vtop = MathF.Ceiling(DIface.r_spritedesc.PVerts[i].V);

        do
        {
            EmitPoint_t* pvert = &DIface.r_spritedesc.PVerts[i];
            EmitPoint_t* pnext = pvert - 1;

            float vbottom = MathF.Ceiling(pnext->V);

            if (vtop < vbottom)
            {
                float du = pnext->U - pvert->U;
                float dv = pnext->V - pvert->V;
                float slope = du / dv;
                int u_step = (int)(slope * 0x10000);
                int u = (int)((pvert->U + slope * (vtop - pvert->V)) * 0x10000) + (0x10000 - 1);
                int itop = (int)vtop;
                int ibottom = (int)vbottom;

                for (int v = itop; v < ibottom; v++)
                {
                    pspan->U = u >> 16;
                    pspan->V = v;
                    u += u_step;
                    pspan++;
                }
            }

            vtop = vbottom;

            i--;
            if (i == 0)
                i = DIface.r_spritedesc.Nump;

        } while (i != lmaxindex);
    }

    // D_SpriteScanRightEdge
    public static void SpriteScanRightEdge()
    {
        SSpan_t* pspan = sprite_spans;
        int i = minindex;

        float vvert = DIface.r_spritedesc.PVerts[i].V;
        if (vvert < RShared.r_refdef->FVrectyAdj)
            vvert = RShared.r_refdef->FVrectyAdj;
        if (vvert > RShared.r_refdef->FVrectBottomAdj)
            vvert = RShared.r_refdef->FVrectBottomAdj;

        float vtop = MathF.Ceiling(vvert);

        do
        {
            EmitPoint_t* pvert = &DIface.r_spritedesc.PVerts[i];
            EmitPoint_t* pnext = pvert + 1;

            float vnext = pnext->V;
            if (vnext < RShared.r_refdef->FVrectyAdj)
                vnext = RShared.r_refdef->FVrectyAdj;
            if (vnext > RShared.r_refdef->FVrectBottomAdj)
                vnext = RShared.r_refdef->FVrectBottomAdj;

            float vbottom = MathF.Ceiling(vnext);

            if (vtop < vbottom)
            {
                float uvert = pvert->U;
                if (uvert < RShared.r_refdef->FVrectxAdj)
                    uvert = RShared.r_refdef->FVrectxAdj;
                if (uvert > RShared.r_refdef->FVrectRightAdj)
                    uvert = RShared.r_refdef->FVrectRightAdj;

                float unext = pnext->U;
                if (unext < RShared.r_refdef->FVrectxAdj)
                    unext = RShared.r_refdef->FVrectxAdj;
                if (unext > RShared.r_refdef->FVrectRightAdj)
                    unext = RShared.r_refdef->FVrectRightAdj;

                float du = unext - uvert;
                float dv = vnext - vvert;
                float slope = du / dv;
                int u_step = (int)(slope * 0x10000);
                int u = (int)((uvert + slope * (vtop - vvert)) * 0x10000) + (0x10000 - 1);
                int itop = (int)vtop;
                int ibottom = (int)vbottom;

                for (int v = itop; v < ibottom; v++)
                {
                    pspan->Count = (u >> 16) - pspan->U;
                    u += u_step;
                    pspan++;
                }
            }

            vtop = vbottom;
            vvert = vnext;

            i++;
            if (i == DIface.r_spritedesc.Nump)
                i = 0;

        } while (i != maxindex);

        pspan->Count = DLocal.DS_SPAN_LIST_END;
    }

    // D_SpriteCalculateGradients
    public static void SpriteCalculateGradients()
    {
        RMisc.TransformVector(DIface.r_spritedesc.Vpn, out Vec3_t p_normal);
        RMisc.TransformVector(DIface.r_spritedesc.Vright, out Vec3_t p_saxis);
        RMisc.TransformVector(DIface.r_spritedesc.Vup, out Vec3_t p_taxis);
        p_taxis = -p_taxis;

        float distinv = 1.0f / -Vector3.Dot(RShared.modelorg, DIface.r_spritedesc.Vpn);

        DLocal.d_sdivzstepu = p_saxis[0] * RShared.xscaleinv;
        DLocal.d_tdivzstepu = p_taxis[0] * RShared.xscaleinv;

        DLocal.d_sdivzstepv = -p_saxis[1] * RShared.yscaleinv;
        DLocal.d_tdivzstepv = -p_taxis[1] * RShared.yscaleinv;

        DLocal.d_zistepu = p_normal[0] * RShared.xscaleinv * distinv;
        DLocal.d_zistepv = -p_normal[1] * RShared.yscaleinv * distinv;

        DLocal.d_sdivzorigin = p_saxis[2] - RShared.xcenter * DLocal.d_sdivzstepu - RShared.ycenter * DLocal.d_sdivzstepv;
        DLocal.d_tdivzorigin = p_taxis[2] - RShared.xcenter * DLocal.d_tdivzstepu - RShared.ycenter * DLocal.d_tdivzstepv;
        DLocal.d_ziorigin = p_normal[2] * distinv - RShared.xcenter * DLocal.d_zistepu - RShared.ycenter * DLocal.d_zistepv;

        RMisc.TransformVector(RShared.modelorg, out Vec3_t p_temp1);

        DLocal.sadjust = (int)(Vector3.Dot(p_temp1, p_saxis) * 0x10000 + 0.5f) - (-(RShared.cachewidth >> 1) << 16);
        DLocal.tadjust = (int)(Vector3.Dot(p_temp1, p_taxis) * 0x10000 + 0.5f) - (-(sprite_height >> 1) << 16);

        // -1 (-epsilon) so we never wander off the edge of the texture
        DLocal.bbextents = (RShared.cachewidth << 16) - 1;
        DLocal.bbextentt = (sprite_height << 16) - 1;
    }

    // D_DrawSprite
    public static void DrawSprite()
    {
        // find the top and bottom vertices, and make sure there's at least one scan to draw
        float ymin = 999999.9f;
        float ymax = -999999.9f;
        EmitPoint_t* pverts = DIface.r_spritedesc.PVerts;

        for (int i = 0; i < DIface.r_spritedesc.Nump; i++)
        {
            if (pverts->V < ymin)
            {
                ymin = pverts->V;
                minindex = i;
            }

            if (pverts->V > ymax)
            {
                ymax = pverts->V;
                maxindex = i;
            }

            pverts++;
        }

        ymin = MathF.Ceiling(ymin);
        ymax = MathF.Ceiling(ymax);

        if (ymin >= ymax)
            return; // doesn't cross any scans at all

        // Mark V d_sprite.c D_DrawSprite: catch sprite bug (negative ymin would index spans out of range)
        if (ymin < 0)
        {
            Con.DPrintf("Sprite draw with bad minimum (ymin) < 0\n"u8);
            return;
        }

        RShared.cachewidth = DIface.r_spritedesc.PSpriteFrame->Width;
        sprite_height = DIface.r_spritedesc.PSpriteFrame->Height;
        RShared.cacheblock = &DIface.r_spritedesc.PSpriteFrame->Pixels[0];

        // copy the first vertex to the last vertex, so we don't have to deal with wrapping
        int nump = DIface.r_spritedesc.Nump;
        pverts = DIface.r_spritedesc.PVerts;
        pverts[nump] = pverts[0];

        SpriteCalculateGradients();
        SpriteScanLeftEdge();
        SpriteScanRightEdge();
        SpriteDrawSpans(sprite_spans);
    }
}
