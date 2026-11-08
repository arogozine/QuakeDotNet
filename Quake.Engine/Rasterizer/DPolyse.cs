// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Engine;

/// <summary>
/// d_polyset.c: routines for drawing sets of polygons sharing the same
/// texture (used for Alias models). Only the !id386 C fallback is ported.
/// Note: TransformAndProjectFinalVerts does not actually live in
/// d_polyse.c -- it's in r_alias.c (RAlias.cs), the alias-model
/// transform/projection step, which needs the view/frustum transform
/// machinery from the r_*.c scene layer. Everything else
/// (D_PolysetDraw, D_PolysetDrawFinalVerts) is ported here along
/// with the rest of this file's rasterizer, which turned out to be
/// self-contained.
/// </summary>
public static unsafe class DPolyse
{
    private const int DPS_MAXSPANS = RShared.MAXHEIGHT + 1;

    // r_p0/r_p1/r_p2
    internal static readonly int* r_p0 = NativeMemory.AllocArray<int>(6);
    internal static readonly int* r_p1 = NativeMemory.AllocArray<int>(6);
    internal static readonly int* r_p2 = NativeMemory.AllocArray<int>(6);

    public static byte* d_pcolormap;

    public static int d_aflatcolor;
    internal static int d_xdenom;

    // pedgetable -- a value copy of the selected edgetables[] entry (rather
    // than a pointer into the managed edgetables array, whose address isn't
    // guaranteed stable across a compacting GC once outside a `fixed`
    // scope). EdgeTable_t is cheap to copy (a handful of ints/pointers).
    private static EdgeTable_t pedgetable;

    // edgetables -- built once from the permanent r_p0/r_p1/r_p2 pointers.
    private static readonly EdgeTable_t[] edgetables = BuildEdgeTables();

    private static EdgeTable_t[] BuildEdgeTables()
    {
        int* p0 = r_p0, p1 = r_p1, p2 = r_p2;

        return
        [
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 1, PLeftEdgeVert0 = p0, PLeftEdgeVert1 = p2, PLeftEdgeVert2 = null, NumRightEdges = 2, PRightEdgeVert0 = p0, PRightEdgeVert1 = p1, PRightEdgeVert2 = p2 },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 2, PLeftEdgeVert0 = p1, PLeftEdgeVert1 = p0, PLeftEdgeVert2 = p2, NumRightEdges = 1, PRightEdgeVert0 = p1, PRightEdgeVert1 = p2, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 1, NumLeftEdges = 1, PLeftEdgeVert0 = p0, PLeftEdgeVert1 = p2, PLeftEdgeVert2 = null, NumRightEdges = 1, PRightEdgeVert0 = p1, PRightEdgeVert1 = p2, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 1, PLeftEdgeVert0 = p1, PLeftEdgeVert1 = p0, PLeftEdgeVert2 = null, NumRightEdges = 2, PRightEdgeVert0 = p1, PRightEdgeVert1 = p2, PRightEdgeVert2 = p0 },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 2, PLeftEdgeVert0 = p0, PLeftEdgeVert1 = p2, PLeftEdgeVert2 = p1, NumRightEdges = 1, PRightEdgeVert0 = p0, PRightEdgeVert1 = p1, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 1, PLeftEdgeVert0 = p2, PLeftEdgeVert1 = p1, PLeftEdgeVert2 = null, NumRightEdges = 1, PRightEdgeVert0 = p2, PRightEdgeVert1 = p0, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 1, PLeftEdgeVert0 = p2, PLeftEdgeVert1 = p1, PLeftEdgeVert2 = null, NumRightEdges = 2, PRightEdgeVert0 = p2, PRightEdgeVert1 = p0, PRightEdgeVert2 = p1 },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 2, PLeftEdgeVert0 = p2, PLeftEdgeVert1 = p1, PLeftEdgeVert2 = p0, NumRightEdges = 1, PRightEdgeVert0 = p2, PRightEdgeVert1 = p0, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 1, PLeftEdgeVert0 = p1, PLeftEdgeVert1 = p0, PLeftEdgeVert2 = null, NumRightEdges = 1, PRightEdgeVert0 = p1, PRightEdgeVert1 = p2, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 1, NumLeftEdges = 1, PLeftEdgeVert0 = p2, PLeftEdgeVert1 = p1, PLeftEdgeVert2 = null, NumRightEdges = 1, PRightEdgeVert0 = p0, PRightEdgeVert1 = p1, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 1, NumLeftEdges = 1, PLeftEdgeVert0 = p1, PLeftEdgeVert1 = p0, PLeftEdgeVert2 = null, NumRightEdges = 1, PRightEdgeVert0 = p2, PRightEdgeVert1 = p0, PRightEdgeVert2 = null },
            new EdgeTable_t { IsFlatTop = 0, NumLeftEdges = 1, PLeftEdgeVert0 = p0, PLeftEdgeVert1 = p2, PLeftEdgeVert2 = null, NumRightEdges = 1, PRightEdgeVert0 = p0, PRightEdgeVert1 = p1, PRightEdgeVert2 = null },
        ];
    }

    internal static int a_sstepxfrac, a_tstepxfrac, r_lstepx, a_ststepxwhole;
    internal static int r_sstepx, r_tstepx, r_lstepy, r_sstepy, r_tstepy;
    internal static int r_zistepx, r_zistepy;
    private static int d_aspancount, d_countextrastep;

    // D_PolysetDraw: spans[] -- the original is a stack local; allocated once
    // here to avoid zero-initializing it per call and to keep a_spans valid.
    // The cache-line alignment/pretouch padding (asm only) is not reproduced.
    internal static readonly SpanPackage_t* a_spans = NativeMemory.AllocArray<SpanPackage_t>(DPS_MAXSPANS + 2);
    private static SpanPackage_t* d_pedgespanpackage;
    private static int ystart;
    private static byte* d_pdest, d_ptex;
    private static short* d_pz;
    private static int d_sfrac, d_tfrac, d_light, d_zi;
    private static int d_ptexextrastep, d_sfracextrastep;
    private static int d_tfracextrastep, d_lightextrastep, d_pdestextrastep;
    private static int d_lightbasestep, d_pdestbasestep, d_ptexbasestep;
    private static int d_sfracbasestep, d_tfracbasestep;
    private static int d_ziextrastep, d_zibasestep;
    private static int d_pzextrastep, d_pzbasestep;

    // adivtab -- table of quotients and remainders for [-15...16] / [-15...16].
    // The original ships this as a precomputed data file (adivtab.h);
    // generated here from MathLib.FloorDivMod instead of transcribing 1024
    // literal values. Only denom > 0 entries are ever read at runtime (an
    // edge being scanned always increases in v), so denom <= 0 rows -- which
    // FloorDivMod refuses -- are left as (0, 0), matching the fact that the
    // original table's values there are never used either.
    private static readonly (int Quotient, int Remainder)[] adivtab = BuildAdivTab();

    private static (int, int)[] BuildAdivTab()
    {
        var table = new (int, int)[32 * 32];
        for (int tm = -15; tm <= 16; tm++)
        {
            for (int tn = -15; tn <= 16; tn++)
            {
                int idx = ((tm + 15) << 5) + (tn + 15);
                if (tn <= 0)
                {
                    table[idx] = (0, 0);
                    continue;
                }

                int q, r;
                MathLib.FloorDivMod(tm, tn, &q, &r);
                table[idx] = (q, r);
            }
        }
        return table;
    }

    internal static readonly byte** skintable = NativeMemory.AllocPointerArray<byte>(DIface.MAX_LBM_HEIGHT);
    private static int skinwidth;
    private static byte* skinstart;

    // D_PolysetDraw
    public static void PolysetDraw()
    {
        if (DIface.r_affinetridesc.DrawType != 0)
            DrawSubdiv();
        else
            DrawNonSubdiv();
    }

    // D_PolysetDrawFinalVerts
    public static void PolysetDrawFinalVerts(FinalVert_t* fv, int numverts)
    {
        for (int i = 0; i < numverts; i++, fv++)
        {
            // valid triangle coordinates for filling can include the bottom and
            // right clip edges, due to the fill rule; these shouldn't be drawn
            if (fv->V[0] < RShared.r_refdef->VrectRight && fv->V[1] < RShared.r_refdef->VrectBottom)
            {
                // Mark V d_polyse.c D_PolysetDrawFinalVerts: skip negative coordinates (would write before the buffers)
                if (fv->V[0] < 0 || fv->V[1] < 0)
                {
                    Con.DPrintf($"D_PolysetDrawFinalVerts: Wanted to draw a vert {fv->V[0]} {fv->V[1]} with negative offset\n");
                    continue;
                }

                int z = fv->V[5] >> 16;
                short* zbuf = DLocal.zspantable[fv->V[1]] + fv->V[0];
                if (z >= *zbuf)
                {
                    *zbuf = (short)z;
                    int pix = skintable[fv->V[3] >> 16][fv->V[2] >> 16];
                    pix = DIface.acolormap[pix + (fv->V[4] & 0xFF00)];
                    DLocal.d_viewbuffer[DLocal.d_scantable[fv->V[1]] + fv->V[0]] = (byte)pix;
                }
            }
        }
    }

    // D_DrawSubdiv
    private static void DrawSubdiv()
    {
        FinalVert_t* pfv = DIface.r_affinetridesc.PFinalVerts;
        MTriangle_t* ptri = DIface.r_affinetridesc.PTriangles;
        int lnumtriangles = DIface.r_affinetridesc.NumTriangles;

        for (int i = 0; i < lnumtriangles; i++)
        {
            FinalVert_t* index0 = pfv + ptri[i].VertIndex[0];
            FinalVert_t* index1 = pfv + ptri[i].VertIndex[1];
            FinalVert_t* index2 = pfv + ptri[i].VertIndex[2];

            if ((index0->V[1] - index1->V[1]) * (index0->V[0] - index2->V[0]) -
                (index0->V[0] - index1->V[0]) * (index0->V[1] - index2->V[1]) >= 0)
            {
                continue;
            }

            d_pcolormap = &DIface.acolormap[index0->V[4] & 0xFF00];

            if (ptri[i].FacesFront != 0)
            {
                PolysetRecursiveTriangle(index0->V, index1->V, index2->V);
            }
            else
            {
                int s0 = index0->V[2];
                int s1 = index1->V[2];
                int s2 = index2->V[2];

                if ((index0->Flags & DIface.ALIAS_ONSEAM) != 0)
                    index0->V[2] += DIface.r_affinetridesc.SeamFixupX16;
                if ((index1->Flags & DIface.ALIAS_ONSEAM) != 0)
                    index1->V[2] += DIface.r_affinetridesc.SeamFixupX16;
                if ((index2->Flags & DIface.ALIAS_ONSEAM) != 0)
                    index2->V[2] += DIface.r_affinetridesc.SeamFixupX16;

                PolysetRecursiveTriangle(index0->V, index1->V, index2->V);

                index0->V[2] = s0;
                index1->V[2] = s1;
                index2->V[2] = s2;
            }
        }
    }

    // D_DrawNonSubdiv
    private static void DrawNonSubdiv()
    {
        FinalVert_t* pfv = DIface.r_affinetridesc.PFinalVerts;
        MTriangle_t* ptri = DIface.r_affinetridesc.PTriangles;
        int lnumtriangles = DIface.r_affinetridesc.NumTriangles;

        for (int i = 0; i < lnumtriangles; i++, ptri++)
        {
            FinalVert_t* index0 = pfv + ptri->VertIndex[0];
            FinalVert_t* index1 = pfv + ptri->VertIndex[1];
            FinalVert_t* index2 = pfv + ptri->VertIndex[2];

            d_xdenom = (index0->V[1] - index1->V[1]) * (index0->V[0] - index2->V[0]) -
                       (index0->V[0] - index1->V[0]) * (index0->V[1] - index2->V[1]);

            if (d_xdenom >= 0)
                continue;

            for (int k = 0; k < 3; k++)
            {
                ((ulong*)r_p0)[k] = ((ulong*)index0->V)[k];
                ((ulong*)r_p1)[k] = ((ulong*)index1->V)[k];
                ((ulong*)r_p2)[k] = ((ulong*)index2->V)[k];
            }

            if (ptri->FacesFront == 0)
            {
                if ((index0->Flags & DIface.ALIAS_ONSEAM) != 0)
                    r_p0[2] += DIface.r_affinetridesc.SeamFixupX16;
                if ((index1->Flags & DIface.ALIAS_ONSEAM) != 0)
                    r_p1[2] += DIface.r_affinetridesc.SeamFixupX16;
                if ((index2->Flags & DIface.ALIAS_ONSEAM) != 0)
                    r_p2[2] += DIface.r_affinetridesc.SeamFixupX16;
            }

            PolysetSetEdgeTable();
            RasterizeAliasPolySmooth();
        }
    }

    // D_PolysetRecursiveTriangle
    [SkipLocalsInit]
    private static void PolysetRecursiveTriangle(int* lp1, int* lp2, int* lp3)
    {
        int d;

        d = lp2[0] - lp1[0];
        if (d < -1 || d > 1)
            goto split;
        d = lp2[1] - lp1[1];
        if (d < -1 || d > 1)
            goto split;

        d = lp3[0] - lp2[0];
        if (d < -1 || d > 1)
            goto split2;
        d = lp3[1] - lp2[1];
        if (d < -1 || d > 1)
            goto split2;

        d = lp1[0] - lp3[0];
        if (d < -1 || d > 1)
            goto split3;
        d = lp1[1] - lp3[1];
        if (d >= -1 && d <= 1)
            return; // entire tri is filled

    split3:
        int* temp3 = lp1;
        lp1 = lp3;
        lp3 = lp2;
        lp2 = temp3;

        goto split;

    split2:
        int* temp2 = lp1;
        lp1 = lp2;
        lp2 = lp3;
        lp3 = temp2;

    split:
        // split this edge
        int* newv = stackalloc int[6];
        newv[0] = (lp1[0] + lp2[0]) >> 1;
        newv[1] = (lp1[1] + lp2[1]) >> 1;
        newv[2] = (lp1[2] + lp2[2]) >> 1;
        newv[3] = (lp1[3] + lp2[3]) >> 1;
        newv[5] = (lp1[5] + lp2[5]) >> 1;

        // draw the point if splitting a leading edge
        if (lp2[1] > lp1[1])
            goto nodraw;
        if (lp2[1] == lp1[1] && lp2[0] < lp1[0])
            goto nodraw;

        int z = newv[5] >> 16;
        short* zbuf = DLocal.zspantable[newv[1]] + newv[0];
        if (z >= *zbuf)
        {
            *zbuf = (short)z;
            int pix = d_pcolormap[skintable[newv[3] >> 16][newv[2] >> 16]];
            DLocal.d_viewbuffer[DLocal.d_scantable[newv[1]] + newv[0]] = (byte)pix;
        }

    nodraw:
        // recursively continue
        PolysetRecursiveTriangle(lp3, lp1, newv);
        PolysetRecursiveTriangle(lp3, newv, lp2);
    }

    // D_PolysetUpdateTables
    public static void PolysetUpdateTables()
    {
        if (DIface.r_affinetridesc.SkinWidth != skinwidth || DIface.r_affinetridesc.PSkin != skinstart)
        {
            skinwidth = DIface.r_affinetridesc.SkinWidth;
            skinstart = (byte*)DIface.r_affinetridesc.PSkin;
            byte* s = skinstart;
            for (int i = 0; i < DIface.MAX_LBM_HEIGHT; i++, s += skinwidth)
                skintable[i] = s;
        }
    }

    // D_PolysetScanLeftEdge
    private static void PolysetScanLeftEdge(int height)
    {
        do
        {
            d_pedgespanpackage->PDest = d_pdest;
            d_pedgespanpackage->Pz = d_pz;
            d_pedgespanpackage->Count = d_aspancount;
            d_pedgespanpackage->PTex = d_ptex;

            d_pedgespanpackage->Sfrac = d_sfrac;
            d_pedgespanpackage->Tfrac = d_tfrac;

            d_pedgespanpackage->Light = d_light;
            d_pedgespanpackage->Zi = d_zi;

            d_pedgespanpackage++;

            DEdge.errorterm += DEdge.erroradjustup;
            if (DEdge.errorterm >= 0)
            {
                d_pdest += d_pdestextrastep;
                d_pz += d_pzextrastep;
                d_aspancount += d_countextrastep;
                d_ptex += d_ptexextrastep;
                d_sfrac += d_sfracextrastep;
                d_ptex += d_sfrac >> 16;

                d_sfrac &= 0xFFFF;
                d_tfrac += d_tfracextrastep;
                if ((d_tfrac & 0x10000) != 0)
                {
                    d_ptex += DIface.r_affinetridesc.SkinWidth;
                    d_tfrac &= 0xFFFF;
                }
                d_light += d_lightextrastep;
                d_zi += d_ziextrastep;
                DEdge.errorterm -= DEdge.erroradjustdown;
            }
            else
            {
                d_pdest += d_pdestbasestep;
                d_pz += d_pzbasestep;
                d_aspancount += DEdge.ubasestep;
                d_ptex += d_ptexbasestep;
                d_sfrac += d_sfracbasestep;
                d_ptex += d_sfrac >> 16;
                d_sfrac &= 0xFFFF;
                d_tfrac += d_tfracbasestep;
                if ((d_tfrac & 0x10000) != 0)
                {
                    d_ptex += DIface.r_affinetridesc.SkinWidth;
                    d_tfrac &= 0xFFFF;
                }
                d_light += d_lightbasestep;
                d_zi += d_zibasestep;
            }
        } while (--height != 0);
    }

    // D_PolysetSetUpForLineScan
    private static void PolysetSetUpForLineScan(int startvertu, int startvertv, int endvertu, int endvertv)
    {
        DEdge.errorterm = -1;

        int tm = endvertu - startvertu;
        int tn = endvertv - startvertv;

        if (tm <= 16 && tm >= -15 && tn <= 16 && tn >= -15)
        {
            (int q, int r) = adivtab[((tm + 15) << 5) + (tn + 15)];
            DEdge.ubasestep = q;
            DEdge.erroradjustup = r;
            DEdge.erroradjustdown = tn;
        }
        else
        {
            int q, r;
            MathLib.FloorDivMod(tm, tn, &q, &r);
            DEdge.ubasestep = q;
            DEdge.erroradjustup = r;
            DEdge.erroradjustdown = tn;
        }
    }

    // Lane mask selecting the light gradient (vertex component 4, lane 2 of
    // the [s, t, light, zi] vector loaded from component 2 onwards).
    private static readonly Vector128<float> LightLaneMask = Vector128.Create(0, 0, -1, 0).AsSingle();

    // D_PolysetCalcGradients
    // The original computes four identical gradient expressions, one per
    // vertex component s (2), t (3), light (4) and zi (5). Components 2..5 are
    // contiguous, so all four run at once as Vector128 lanes [s, t, light, zi].
    // Per lane the operations and their order match the original exactly --
    // int subtract, int->float, (t1 * a - t0 * b) * inv with no FMA
    // contraction, then a truncating native conversion -- so results are
    // bit-identical. ceil() applies to the light lane only, as in the original.
    internal static void PolysetCalcGradients(int skinwidthParam)
    {
        int* r_p0 = DPolyse.r_p0, r_p1 = DPolyse.r_p1, r_p2 = DPolyse.r_p2;

        float p00_minus_p20 = r_p0[0] - r_p2[0];
        float p01_minus_p21 = r_p0[1] - r_p2[1];
        float p10_minus_p20 = r_p1[0] - r_p2[0];
        float p11_minus_p21 = r_p1[1] - r_p2[1];

        float xstepdenominv = 1.0f / d_xdenom;
        float ystepdenominv = -xstepdenominv;

        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<int> p2 = Vector128.Load(r_p2 + 2);
            Vector128<float> t0 = Vector128.ConvertToSingle(Vector128.Load(r_p0 + 2) - p2);
            Vector128<float> t1 = Vector128.ConvertToSingle(Vector128.Load(r_p1 + 2) - p2);

            Vector128<float> stepx = (t1 * p01_minus_p21 - t0 * p11_minus_p21) * xstepdenominv;
            Vector128<float> stepy = (t1 * p00_minus_p20 - t0 * p10_minus_p20) * ystepdenominv;

            // ceil () for light so positive steps are exaggerated, negative steps
            // diminished,  pushing us away from underflow toward overflow. Underflow is
            // very visible, overflow is very unlikely, because of ambient lighting
            stepx = Vector128.ConditionalSelect(LightLaneMask, Vector128.Ceiling(stepx), stepx);
            stepy = Vector128.ConditionalSelect(LightLaneMask, Vector128.Ceiling(stepy), stepy);

            Vector128<int> istepx = Vector128.ConvertToInt32Native(stepx);
            Vector128<int> istepy = Vector128.ConvertToInt32Native(stepy);

            r_sstepx = istepx.GetElement(0);
            r_tstepx = istepx.GetElement(1);
            r_lstepx = istepx.GetElement(2);
            r_zistepx = istepx.GetElement(3);

            r_sstepy = istepy.GetElement(0);
            r_tstepy = istepy.GetElement(1);
            r_lstepy = istepy.GetElement(2);
            r_zistepy = istepy.GetElement(3);

            a_sstepxfrac = r_sstepx & 0xFFFF;
            a_tstepxfrac = r_tstepx & 0xFFFF;

            a_ststepxwhole = skinwidthParam * (r_tstepx >> 16) + (r_sstepx >> 16);
        }
        else
        {

            // ceil () for light so positive steps are exaggerated, negative steps
            // diminished,  pushing us away from underflow toward overflow. Underflow is
            // very visible, overflow is very unlikely, because of ambient lighting
            float t0 = r_p0[4] - r_p2[4];
            float t1 = r_p1[4] - r_p2[4];
            r_lstepx = float.ConvertToIntegerNative<int>(MathF.Ceiling((t1 * p01_minus_p21 - t0 * p11_minus_p21) * xstepdenominv));
            r_lstepy = float.ConvertToIntegerNative<int>(MathF.Ceiling((t1 * p00_minus_p20 - t0 * p10_minus_p20) * ystepdenominv));

            t0 = r_p0[2] - r_p2[2];
            t1 = r_p1[2] - r_p2[2];
            r_sstepx = float.ConvertToIntegerNative<int>((t1 * p01_minus_p21 - t0 * p11_minus_p21) * xstepdenominv);
            r_sstepy = float.ConvertToIntegerNative<int>((t1 * p00_minus_p20 - t0 * p10_minus_p20) * ystepdenominv);

            t0 = r_p0[3] - r_p2[3];
            t1 = r_p1[3] - r_p2[3];
            r_tstepx = float.ConvertToIntegerNative<int>((t1 * p01_minus_p21 - t0 * p11_minus_p21) * xstepdenominv);
            r_tstepy = float.ConvertToIntegerNative<int>((t1 * p00_minus_p20 - t0 * p10_minus_p20) * ystepdenominv);

            t0 = r_p0[5] - r_p2[5];
            t1 = r_p1[5] - r_p2[5];
            r_zistepx = float.ConvertToIntegerNative<int>((t1 * p01_minus_p21 - t0 * p11_minus_p21) * xstepdenominv);
            r_zistepy = float.ConvertToIntegerNative<int>((t1 * p00_minus_p20 - t0 * p10_minus_p20) * ystepdenominv);

            a_sstepxfrac = r_sstepx & 0xFFFF;
            a_tstepxfrac = r_tstepx & 0xFFFF;

            a_ststepxwhole = skinwidthParam * (r_tstepx >> 16) + (r_sstepx >> 16);
        }
    }

    // D_PolysetDrawSpans8
    // The per-pixel step statics are hoisted into locals: the stores through
    // lpdest/lpz may alias them as far as the JIT knows, so otherwise each is
    // reloaded from memory every pixel.
    internal static void PolysetDrawSpans8(SpanPackage_t* pspanpackage)
    {
        byte* acolormap = DIface.acolormap;
        int zistepx = r_zistepx, lstepx = r_lstepx, ststepxwhole = a_ststepxwhole;
        int sstepxfrac = a_sstepxfrac, tstepxfrac = a_tstepxfrac, skinwidth = DIface.r_affinetridesc.SkinWidth;

        do
        {
            int lcount = d_aspancount - pspanpackage->Count;

            DEdge.errorterm += DEdge.erroradjustup;
            if (DEdge.errorterm >= 0)
            {
                d_aspancount += d_countextrastep;
                DEdge.errorterm -= DEdge.erroradjustdown;
            }
            else
            {
                d_aspancount += DEdge.ubasestep;
            }

            if (lcount != 0)
            {
                byte* lpdest = (byte*)pspanpackage->PDest;
                byte* lptex = pspanpackage->PTex;
                short* lpz = pspanpackage->Pz;
                int lsfrac = pspanpackage->Sfrac;
                int ltfrac = pspanpackage->Tfrac;
                int llight = pspanpackage->Light;
                int lzi = pspanpackage->Zi;

                do
                {
                    if (lzi >> 16 >= *lpz)
                    {
                        *lpdest = acolormap[*lptex + (llight & 0xFF00)];
                        *lpz = (short)(lzi >> 16);
                    }

                    lpdest++;
                    lzi += zistepx;
                    lpz++;
                    llight += lstepx;
                    lptex += ststepxwhole;
                    lsfrac += sstepxfrac;
                    lptex += lsfrac >> 16;
                    lsfrac &= 0xFFFF;
                    ltfrac += tstepxfrac;

                    if ((ltfrac & 0x10000) != 0)
                    {
                        lptex += skinwidth;
                        ltfrac &= 0xFFFF;
                    }
                } while (--lcount != 0);
            }

            pspanpackage++;
        } while (pspanpackage->Count != -999999);
    }

    // D_PolysetFillSpans8
    public static void PolysetFillSpans8(SpanPackage_t* pspanpackage)
    {
        int color = d_aflatcolor++;

        while (true)
        {
            int lcount = pspanpackage->Count;

            if (lcount == -1)
                return;

            if (lcount != 0)
            {
                byte* lpdest = (byte*)pspanpackage->PDest;
                do
                {
                    *lpdest++ = (byte)color;
                } while (--lcount != 0);
            }

            pspanpackage++;
        }
    }

    // D_RasterizeAliasPolySmooth
    private static void RasterizeAliasPolySmooth()
    {
        int* plefttop = pedgetable.PLeftEdgeVert0;
        int* prighttop = pedgetable.PRightEdgeVert0;

        int* pleftbottom = pedgetable.PLeftEdgeVert1;
        int* prightbottom = pedgetable.PRightEdgeVert1;

        int initialleftheight = pleftbottom[1] - plefttop[1];
        int initialrightheight = prightbottom[1] - prighttop[1];

        // set the s, t, and light gradients, which are consistent across the
        // triangle because being a triangle, things are affine
        PolysetCalcGradients(DIface.r_affinetridesc.SkinWidth);

        // rasterize the polygon

        // scan out the top (and possibly only) part of the left edge
        d_pedgespanpackage = a_spans;

        ystart = plefttop[1];
        d_aspancount = plefttop[0] - prighttop[0];

        d_ptex = (byte*)DIface.r_affinetridesc.PSkin + (plefttop[2] >> 16) + (plefttop[3] >> 16) * DIface.r_affinetridesc.SkinWidth;
        d_sfrac = plefttop[2] & 0xFFFF;
        d_tfrac = plefttop[3] & 0xFFFF;
        d_light = plefttop[4];
        d_zi = plefttop[5];

        d_pdest = DLocal.d_viewbuffer + ystart * RShared.screenwidth + plefttop[0];
        d_pz = DLocal.d_pzbuffer + ystart * DLocal.d_zwidth + plefttop[0];

        int working_lstepx;

        if (initialleftheight == 1)
        {
            d_pedgespanpackage->PDest = d_pdest;
            d_pedgespanpackage->Pz = d_pz;
            d_pedgespanpackage->Count = d_aspancount;
            d_pedgespanpackage->PTex = d_ptex;

            d_pedgespanpackage->Sfrac = d_sfrac;
            d_pedgespanpackage->Tfrac = d_tfrac;

            d_pedgespanpackage->Light = d_light;
            d_pedgespanpackage->Zi = d_zi;

            d_pedgespanpackage++;
        }
        else
        {
            PolysetSetUpForLineScan(plefttop[0], plefttop[1], pleftbottom[0], pleftbottom[1]);

            d_pzbasestep = (int)DLocal.d_zwidth + DEdge.ubasestep;
            d_pzextrastep = d_pzbasestep + 1;

            d_pdestbasestep = RShared.screenwidth + DEdge.ubasestep;
            d_pdestextrastep = d_pdestbasestep + 1;

            working_lstepx = DEdge.ubasestep < 0 ? r_lstepx - 1 : r_lstepx;

            d_countextrastep = DEdge.ubasestep + 1;
            d_ptexbasestep = ((r_sstepy + r_sstepx * DEdge.ubasestep) >> 16) +
                              ((r_tstepy + r_tstepx * DEdge.ubasestep) >> 16) * DIface.r_affinetridesc.SkinWidth;
            d_sfracbasestep = (r_sstepy + r_sstepx * DEdge.ubasestep) & 0xFFFF;
            d_tfracbasestep = (r_tstepy + r_tstepx * DEdge.ubasestep) & 0xFFFF;
            d_lightbasestep = r_lstepy + working_lstepx * DEdge.ubasestep;
            d_zibasestep = r_zistepy + r_zistepx * DEdge.ubasestep;

            d_ptexextrastep = ((r_sstepy + r_sstepx * d_countextrastep) >> 16) +
                               ((r_tstepy + r_tstepx * d_countextrastep) >> 16) * DIface.r_affinetridesc.SkinWidth;
            d_sfracextrastep = (r_sstepy + r_sstepx * d_countextrastep) & 0xFFFF;
            d_tfracextrastep = (r_tstepy + r_tstepx * d_countextrastep) & 0xFFFF;
            d_lightextrastep = d_lightbasestep + working_lstepx;
            d_ziextrastep = d_zibasestep + r_zistepx;

            PolysetScanLeftEdge(initialleftheight);
        }

        // scan out the bottom part of the left edge, if it exists
        if (pedgetable.NumLeftEdges == 2)
        {
            plefttop = pleftbottom;
            pleftbottom = pedgetable.PLeftEdgeVert2;

            int height = pleftbottom[1] - plefttop[1];

            ystart = plefttop[1];
            d_aspancount = plefttop[0] - prighttop[0];
            d_ptex = (byte*)DIface.r_affinetridesc.PSkin + (plefttop[2] >> 16) + (plefttop[3] >> 16) * DIface.r_affinetridesc.SkinWidth;
            d_sfrac = 0;
            d_tfrac = 0;
            d_light = plefttop[4];
            d_zi = plefttop[5];

            d_pdest = DLocal.d_viewbuffer + ystart * RShared.screenwidth + plefttop[0];
            d_pz = DLocal.d_pzbuffer + ystart * DLocal.d_zwidth + plefttop[0];

            if (height == 1)
            {
                d_pedgespanpackage->PDest = d_pdest;
                d_pedgespanpackage->Pz = d_pz;
                d_pedgespanpackage->Count = d_aspancount;
                d_pedgespanpackage->PTex = d_ptex;

                d_pedgespanpackage->Sfrac = d_sfrac;
                d_pedgespanpackage->Tfrac = d_tfrac;

                d_pedgespanpackage->Light = d_light;
                d_pedgespanpackage->Zi = d_zi;

                d_pedgespanpackage++;
            }
            else
            {
                PolysetSetUpForLineScan(plefttop[0], plefttop[1], pleftbottom[0], pleftbottom[1]);

                d_pdestbasestep = RShared.screenwidth + DEdge.ubasestep;
                d_pdestextrastep = d_pdestbasestep + 1;

                d_pzbasestep = (int)DLocal.d_zwidth + DEdge.ubasestep;
                d_pzextrastep = d_pzbasestep + 1;

                working_lstepx = DEdge.ubasestep < 0 ? r_lstepx - 1 : r_lstepx;

                d_countextrastep = DEdge.ubasestep + 1;
                d_ptexbasestep = ((r_sstepy + r_sstepx * DEdge.ubasestep) >> 16) +
                                  ((r_tstepy + r_tstepx * DEdge.ubasestep) >> 16) * DIface.r_affinetridesc.SkinWidth;
                d_sfracbasestep = (r_sstepy + r_sstepx * DEdge.ubasestep) & 0xFFFF;
                d_tfracbasestep = (r_tstepy + r_tstepx * DEdge.ubasestep) & 0xFFFF;
                d_lightbasestep = r_lstepy + working_lstepx * DEdge.ubasestep;
                d_zibasestep = r_zistepy + r_zistepx * DEdge.ubasestep;

                d_ptexextrastep = ((r_sstepy + r_sstepx * d_countextrastep) >> 16) +
                                   ((r_tstepy + r_tstepx * d_countextrastep) >> 16) * DIface.r_affinetridesc.SkinWidth;
                d_sfracextrastep = (r_sstepy + r_sstepx * d_countextrastep) & 0xFFFF;
                d_tfracextrastep = (r_tstepy + r_tstepx * d_countextrastep) & 0xFFFF;
                d_lightextrastep = d_lightbasestep + working_lstepx;
                d_ziextrastep = d_zibasestep + r_zistepx;

                PolysetScanLeftEdge(height);
            }
        }

        // scan out the top (and possibly only) part of the right edge, updating the count field
        d_pedgespanpackage = a_spans;

        PolysetSetUpForLineScan(prighttop[0], prighttop[1], prightbottom[0], prightbottom[1]);
        d_aspancount = 0;
        d_countextrastep = DEdge.ubasestep + 1;
        int originalcount = a_spans[initialrightheight].Count;
        a_spans[initialrightheight].Count = -999999; // mark end of the spanpackages
        PolysetDrawSpans8(a_spans);

        // scan out the bottom part of the right edge, if it exists
        if (pedgetable.NumRightEdges == 2)
        {
            SpanPackage_t* pstart = a_spans + initialrightheight;
            pstart->Count = originalcount;

            d_aspancount = prightbottom[0] - prighttop[0];

            prighttop = prightbottom;
            prightbottom = pedgetable.PRightEdgeVert2;

            int height = prightbottom[1] - prighttop[1];

            PolysetSetUpForLineScan(prighttop[0], prighttop[1], prightbottom[0], prightbottom[1]);

            d_countextrastep = DEdge.ubasestep + 1;
            a_spans[initialrightheight + height].Count = -999999; // mark end of the spanpackages
            PolysetDrawSpans8(pstart);
        }
    }

    // D_PolysetSetEdgeTable
    private static void PolysetSetEdgeTable()
    {
        int edgetableindex = 0; // assume the vertices are already in top to bottom order

        if (r_p0[1] >= r_p1[1])
        {
            if (r_p0[1] == r_p1[1])
            {
                pedgetable = r_p0[1] < r_p2[1] ? edgetables[2] : edgetables[5];
                return;
            }

            edgetableindex = 1;
        }

        if (r_p0[1] == r_p2[1])
        {
            pedgetable = edgetableindex != 0 ? edgetables[8] : edgetables[9];
            return;
        }

        if (r_p1[1] == r_p2[1])
        {
            pedgetable = edgetableindex != 0 ? edgetables[10] : edgetables[11];
            return;
        }

        if (r_p0[1] > r_p2[1])
            edgetableindex += 2;

        if (r_p1[1] > r_p2[1])
            edgetableindex += 4;

        pedgetable = edgetables[edgetableindex];
    }
}
