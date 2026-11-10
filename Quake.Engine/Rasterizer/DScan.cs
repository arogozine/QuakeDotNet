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
/// d_scan.c: portable C scan-level rasterization code (span filling for
/// textured/turbulent world surfaces and the z-buffer span writer), plus
/// D_WarpScreen (the underwater screen-space sine-warp post-process),
/// called from RMain.RenderView_ when DIface.r_dowarp is set.
/// Only the !id386 C fallback bodies are ported (this build has no x86 asm).
/// </summary>
public static unsafe class DScan
{
    private static int* r_turb_turb;

    // Turbulent8
    public static void Turbulent8(ESpan_t* pspan)
    {
        r_turb_turb = RShared.sintable + ((int)(RShared.cl_time * RLocal.SPEED) & (DIface.CYCLE - 1));

        Turbulent8Inner(pspan);
    }

    // sturb/tturb -> (tturb << 6) + sturb for 8 consecutive pixels.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> TexIndex8(int* turb, Vector256<int> sV, Vector256<int> tV)
    {
        Vector256<int> cyc = Vector256.Create(DIface.CYCLE - 1);
        Vector256<int> m63 = Vector256.Create(63);
        Vector256<int> sturb = ((sV + Avx2.GatherVector256(turb, (tV >> 16) & cyc, sizeof(int))) >> 16) & m63;
        Vector256<int> tturb = ((tV + Avx2.GatherVector256(turb, (sV >> 16) & cyc, sizeof(int))) >> 16) & m63;
        return (tturb << 6) + sturb;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Turbulent8Inner(ESpan_t* pspan)
    {
        int r_turb_sstep = 0;
        int r_turb_tstep = 0;
        int r_turb_s = 0;
        int r_turb_t = 0;
        int r_turb_spancount;

        byte* r_turb_pbase = RShared.cacheblock;

        // Hoisted out of the per-surface span walk below: none of these are
        // reassigned until DEdge.DrawSurfaces moves on to the next surface
        // (single-threaded, non-reentrant renderer), so caching them as
        // locals avoids repeated static-field reads per span/pixel.
        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        float d_sdivzstepu = DLocal.d_sdivzstepu, d_sdivzstepv = DLocal.d_sdivzstepv, d_sdivzorigin = DLocal.d_sdivzorigin;
        float d_tdivzstepu = DLocal.d_tdivzstepu, d_tdivzstepv = DLocal.d_tdivzstepv, d_tdivzorigin = DLocal.d_tdivzorigin;
        float d_zistepu = DLocal.d_zistepu, d_zistepv = DLocal.d_zistepv, d_ziorigin = DLocal.d_ziorigin;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        float sdivz16stepu = d_sdivzstepu * 16;
        float tdivz16stepu = d_tdivzstepu * 16;
        float zi16stepu = d_zistepu * 16;

        Vector256<int> indexV = Vector256.CreateSequence(0, 1);
        Vector256<byte> packLo = Vector256.Create(
            0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
            0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);
        Vector256<byte> packHi = Vector256.Create(
            0x80, 0x80, 0x80, 0x80, 0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
            0x80, 0x80, 0x80, 0x80, 0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);
        Vector256<int> order = Vector256.Create(0, 4, 1, 5, 0, 0, 0, 0);

        do
        {
            byte* r_turb_pdest = viewbuffer + (screenwidth * pspan->V) + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = MathF.FusedMultiplyAdd(du, d_sdivzstepu, MathF.FusedMultiplyAdd(dv, d_sdivzstepv, d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, d_tdivzstepu, MathF.FusedMultiplyAdd(dv, d_tdivzstepv, d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));
            float z = 0x10000 / zi;

            r_turb_s = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
            if (r_turb_s > bbextents)
            {
                r_turb_s = bbextents;
            }
            else if (r_turb_s < 0)
            {
                r_turb_s = 0;
            }

            r_turb_t = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
            if (r_turb_t > bbextentt)
            {
                r_turb_t = bbextentt;
            }
            else if (r_turb_t < 0)
            {
                r_turb_t = 0;
            }

            int snext, tnext;

            do
            {
                r_turb_spancount = count >= 16 ? 16 : count;
                count -= r_turb_spancount;

                if (count != 0)
                {
                    sdivz += sdivz16stepu;
                    tdivz += tdivz16stepu;
                    zi += zi16stepu;
                    z = 0x10000 / zi;

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 16)
                    {
                        snext = 16;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 16)
                    {
                        tnext = 16;
                    }

                    r_turb_sstep = (snext - r_turb_s) >> 4;
                    r_turb_tstep = (tnext - r_turb_t) >> 4;
                }
                else
                {
                    float spancountminus1 = r_turb_spancount - 1;
                    sdivz += d_sdivzstepu * spancountminus1;
                    tdivz += d_tdivzstepu * spancountminus1;
                    zi += d_zistepu * spancountminus1;
                    z = 0x10000 / zi;
                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 16)
                    {
                        snext = 16;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 16)
                    {
                        tnext = 16;
                    }

                    if (r_turb_spancount > 1)
                    {
                        r_turb_sstep = (snext - r_turb_s) / (r_turb_spancount - 1);
                        r_turb_tstep = (tnext - r_turb_t) / (r_turb_spancount - 1);
                    }
                }

                r_turb_s &= (DIface.CYCLE << 16) - 1;
                r_turb_t &= (DIface.CYCLE << 16) - 1;

                // 8 (or 16) pixels at a time: two sintable gathers per axis, then one
                // dword texel gather per 8 pixels packed to bytes (the gather reads up
                // to 3 bytes past the last texel; warp textures are followed by their
                // lower mips).
                if (Avx2.IsSupported && r_turb_spancount >= 8)
                {
                    Vector256<int> sstepV = Vector256.Create(r_turb_sstep);
                    Vector256<int> tstepV = Vector256.Create(r_turb_tstep);
                    Vector256<int> sV = Vector256.Create(r_turb_s) + (indexV * sstepV);
                    Vector256<int> tV = Vector256.Create(r_turb_t) + (indexV * tstepV);
                    Vector256<byte> g0 = Avx2.GatherVector256((int*)r_turb_pbase, TexIndex8(r_turb_turb, sV, tV), 1).AsByte();

                    if (r_turb_spancount == 16)
                    {
                        Vector256<byte> g1 = Avx2.GatherVector256((int*)r_turb_pbase, TexIndex8(r_turb_turb, sV + (sstepV << 3), tV + (tstepV << 3)), 1).AsByte();
                        Vector256<byte> packed = Avx2.Shuffle(g0, packLo) | Avx2.Shuffle(g1, packHi);
                        Avx2.PermuteVar8x32(packed.AsInt32(), order).GetLower().AsByte().Store(r_turb_pdest);
                        r_turb_pdest += 16;
                        r_turb_spancount = 0;
                    }
                    else
                    {
                        Unsafe.WriteUnaligned(r_turb_pdest, Avx2.PermuteVar8x32(Avx2.Shuffle(g0, packLo).AsInt32(), order).AsUInt64().ToScalar());
                        r_turb_pdest += 8;
                        r_turb_s += r_turb_sstep << 3;
                        r_turb_t += r_turb_tstep << 3;
                        r_turb_spancount -= 8;
                    }
                }

                while (r_turb_spancount > 0)
                {
                    int sturb = ((r_turb_s + r_turb_turb[(r_turb_t >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    int tturb = ((r_turb_t + r_turb_turb[(r_turb_s >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    *r_turb_pdest++ = *(r_turb_pbase + (tturb << 6) + sturb);
                    r_turb_s += r_turb_sstep;
                    r_turb_t += r_turb_tstep;
                    r_turb_spancount--;
                }

                r_turb_s = snext;
                r_turb_t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    // D_DrawSpans8
    public static void DrawSpans8(ESpan_t* pspan)
    {
        if (Avx2.IsSupported)
        {
            DrawSpans8_SIMD(pspan);
            return;
        }

        byte* pbase = RShared.cacheblock;

        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        int cachewidth = RShared.cachewidth;
        float d_sdivzstepu = DLocal.d_sdivzstepu, d_sdivzstepv = DLocal.d_sdivzstepv, d_sdivzorigin = DLocal.d_sdivzorigin;
        float d_tdivzstepu = DLocal.d_tdivzstepu, d_tdivzstepv = DLocal.d_tdivzstepv, d_tdivzorigin = DLocal.d_tdivzorigin;
        float d_zistepu = DLocal.d_zistepu, d_zistepv = DLocal.d_zistepv, d_ziorigin = DLocal.d_ziorigin;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        float sdivz8stepu = d_sdivzstepu * 8;
        float tdivz8stepu = d_tdivzstepu * 8;
        float zi8stepu = d_zistepu * 8;

        Vector256<int> indexV = Vector256.CreateSequence(0, 1);
        Vector256<int> cachewidthV = Vector256.Create(cachewidth);

        int* idx = stackalloc int[Vector256<int>.Count];
        ulong* idx64 = (ulong*)idx;

        do
        {
            byte* pdest = viewbuffer + (screenwidth * pspan->V) + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = MathF.FusedMultiplyAdd(du, d_sdivzstepu, MathF.FusedMultiplyAdd(dv, d_sdivzstepv, d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, d_tdivzstepu, MathF.FusedMultiplyAdd(dv, d_tdivzstepv, d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));
            float z = 0x10000 / zi;

            int s = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
            s = Math.Clamp(s, 0, bbextents);

            int t = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
            t = Math.Clamp(t, 0, bbextentt);

            do
            {
                int sstep = 0, tstep = 0;
                int snext, tnext;

                int spancount = count >= 8 ? 8 : count;
                count -= spancount;

                if (count != 0)
                {
                    sdivz += sdivz8stepu;
                    tdivz += tdivz8stepu;
                    zi += zi8stepu;
                    z = 0x10000 / zi;

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 8)
                    {
                        tnext = 8;
                    }

                    sstep = (snext - s) >> 3;
                    tstep = (tnext - t) >> 3;
                }
                else
                {
                    float spancountminus1 = spancount - 1;
                    sdivz += d_sdivzstepu * spancountminus1;
                    tdivz += d_tdivzstepu * spancountminus1;
                    zi += d_zistepu * spancountminus1;
                    z = 0x10000 / zi;

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 8)
                    {
                        tnext = 8;
                    }

                    if (spancount > 1)
                    {
                        sstep = (snext - s) / (spancount - 1);
                        tstep = (tnext - t) / (spancount - 1);
                    }
                }

                Vector256<int> sV = Vector256.Create(s) + (Vector256.Create(sstep) * indexV);
                Vector256<int> tV = Vector256.Create(t) + (Vector256.Create(tstep) * indexV);
                ((sV >> 16) + ((tV >> 16) * cachewidthV)).Store(idx);

                if (spancount == 8)
                {
                    ulong i01 = idx64[0], i23 = idx64[1], i45 = idx64[2], i67 = idx64[3];

                    ulong texels =
                          pbase[(uint)i01]
                        | ((ulong)pbase[(uint)(i01 >> 32)] << 8)
                        | ((ulong)pbase[(uint)i23] << 16)
                        | ((ulong)pbase[(uint)(i23 >> 32)] << 24)
                        | ((ulong)pbase[(uint)i45] << 32)
                        | ((ulong)pbase[(uint)(i45 >> 32)] << 40)
                        | ((ulong)pbase[(uint)i67] << 48)
                        | ((ulong)pbase[(uint)(i67 >> 32)] << 56);

                    Unsafe.WriteUnaligned(pdest, texels);
                    pdest += 8;
                }
                else
                {
                    for (int j = 0; j < spancount; j++)
                    {
                        *pdest++ = pbase[idx[j]];
                    }
                }

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawSpans8_SIMD(ESpan_t* pspan)
    {
        byte* pbase = RShared.cacheblock;

        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        int cachewidth = RShared.cachewidth;
        float d_sdivzstepu = DLocal.d_sdivzstepu, d_sdivzstepv = DLocal.d_sdivzstepv, d_sdivzorigin = DLocal.d_sdivzorigin;
        float d_tdivzstepu = DLocal.d_tdivzstepu, d_tdivzstepv = DLocal.d_tdivzstepv, d_tdivzorigin = DLocal.d_tdivzorigin;
        float d_zistepu = DLocal.d_zistepu, d_zistepv = DLocal.d_zistepv, d_ziorigin = DLocal.d_ziorigin;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        float sdivz8stepu = d_sdivzstepu * 8;
        float tdivz8stepu = d_tdivzstepu * 8;
        float zi8stepu = d_zistepu * 8;

        Vector256<int> indexV = Vector256.CreateSequence(0, 1);
        Vector256<int> cachewidthV = Vector256.Create(cachewidth);
        Vector256<int> order = Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0);
        Vector256<byte> pack = Vector256.Create(
            0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
            0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);

        int* idx = stackalloc int[Vector256<int>.Count];

        do
        {
            byte* pdest = viewbuffer + (screenwidth * pspan->V) + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = MathF.FusedMultiplyAdd(du, d_sdivzstepu, MathF.FusedMultiplyAdd(dv, d_sdivzstepv, d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, d_tdivzstepu, MathF.FusedMultiplyAdd(dv, d_tdivzstepv, d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));
            float z = 0x10000 / zi;

            int s = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
            s = Math.Clamp(s, 0, bbextents);

            int t = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
            t = Math.Clamp(t, 0, bbextentt);

            do
            {
                Vector256<int> sV, tV, idxV;

                int sstep = 0, tstep = 0;
                int snext, tnext;

                while (count >= 32)
                {
                    for (int i = 0; i < Vector256<ulong>.Count; i++)
                    {
                        sdivz += sdivz8stepu;
                        tdivz += tdivz8stepu;
                        zi += zi8stepu;
                        z = 0x10000 / zi;

                        snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                        if (snext > bbextents)
                        {
                            snext = bbextents;
                        }
                        else if (snext < 8)
                        {
                            snext = 8;
                        }

                        tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                        if (tnext > bbextentt)
                        {
                            tnext = bbextentt;
                        }
                        else if (tnext < 8)
                        {
                            tnext = 8;
                        }

                        sstep = (snext - s) >> 3;
                        tstep = (tnext - t) >> 3;


                        sV = Vector256.Create(s) + (Vector256.Create(sstep) * indexV);
                        tV = Vector256.Create(t) + (Vector256.Create(tstep) * indexV);
                        idxV = (sV >> 16) + ((tV >> 16) * cachewidthV);

                        Vector256<int> gathered = Avx2.GatherVector256((int*)pbase, idxV, 1);

                        ulong val = Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar();

                        ((ulong*)idx)[i] = val;

                        s = snext;
                        t = tnext;
                    }

                    Vector256.Load(idx).AsByte().Store(pdest);

                    count -= 32;
                    pdest += 32;
                }

                int spancount = count >= 8 ? 8 : count;
                count -= spancount;

                if (count != 0)
                {
                    sdivz += sdivz8stepu;
                    tdivz += tdivz8stepu;
                    zi += zi8stepu;
                    z = 0x10000 / zi;

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 8)
                    {
                        tnext = 8;
                    }

                    sstep = (snext - s) >> 3;
                    tstep = (tnext - t) >> 3;
                }
                else
                {
                    float spancountminus1 = spancount - 1;
                    sdivz += d_sdivzstepu * spancountminus1;
                    tdivz += d_tdivzstepu * spancountminus1;
                    zi += d_zistepu * spancountminus1;
                    z = 0x10000 / zi;

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 8)
                    {
                        tnext = 8;
                    }

                    if (spancount > 1)
                    {
                        sstep = (snext - s) / (spancount - 1);
                        tstep = (tnext - t) / (spancount - 1);
                    }
                }

                sV = Vector256.Create(s) + (Vector256.Create(sstep) * indexV);
                tV = Vector256.Create(t) + (Vector256.Create(tstep) * indexV);
                idxV = (sV >> 16) + ((tV >> 16) * cachewidthV);

                if (spancount == 8)
                {
                    Vector256<int> gathered = Avx2.GatherVector256((int*)pbase, idxV, 1);

                    ulong val = Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar();
                    Unsafe.WriteUnaligned(pdest, val);

                    pdest += 8;
                }
                else
                {
                    idxV.Store(idx);
                    for (int j = 0; j < spancount; j++)
                    {
                        *pdest++ = pbase[idx[j]];
                    }
                }

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }



    // D_WarpScreen: rowptr[], column[] -- the original's are stack locals;
    // allocated once here to avoid zero-initializing ~33 KB per call. Every
    // entry read is written earlier in the same call.
    internal static readonly byte** warp_rowptr = NativeMemory.AllocPointerArray<byte>(RShared.MAXHEIGHT + (RLocal.AMP2 * 2));
    internal static readonly int* warp_column = NativeMemory.AllocArray<int>(RShared.MAXWIDTH + (RLocal.AMP2 * 2));

    private record struct WarpKey_t(int W, int H, int VrectX, int VrectY, int ScrW, int ScrH, int ScreenWidth, nint ViewBuffer);
    private static WarpKey_t warpKey;
    private static bool warpKeyValid;

    /*
    =============
    D_WarpScreen

    this performs a slight compression of the screen at the same time as
    the sine warp, to keep the edges from wrapping
    =============
    */
    public static void WarpScreen()
    {
        int w = RShared.r_refdef->Vrect.Width;
        int h = RShared.r_refdef->Vrect.Height;

        float wratio = w / (float)Screen.scr_vrect->Width;
        float hratio = h / (float)Screen.scr_vrect->Height;

        byte** rowptr = warp_rowptr;
        int* column = warp_column;

        // rowptr[]/column[] only depend on the view geometry and the source
        // buffer, so they are rebuilt only when one of those changes (the
        // original recomputes them every call).
        WarpKey_t key = default;
        key.W = w; key.H = h;
        key.VrectX = RShared.r_refdef->Vrect.X; key.VrectY = RShared.r_refdef->Vrect.Y;
        key.ScrW = Screen.scr_vrect->Width; key.ScrH = Screen.scr_vrect->Height;
        key.ScreenWidth = RShared.screenwidth; key.ViewBuffer = (nint)DLocal.d_viewbuffer;

        if (!warpKeyValid || !key.Equals(warpKey))
        {
            for (int v = 0; v < Screen.scr_vrect->Height + (RLocal.AMP2 * 2); v++)
            {
                rowptr[v] = DLocal.d_viewbuffer + (RShared.r_refdef->Vrect.Y * RShared.screenwidth) +
                    (RShared.screenwidth * (int)(v * hratio * h / (h + (RLocal.AMP2 * 2))));
            }

            for (int u = 0; u < Screen.scr_vrect->Width + (RLocal.AMP2 * 2); u++)
            {
                column[u] = RShared.r_refdef->Vrect.X +
                    (int)(u * wratio * w / (w + (RLocal.AMP2 * 2)));
            }

            warpKey = key;
            warpKeyValid = true;
        }

        {
            int* turb = RShared.intsintable + ((int)(RShared.cl_time * RLocal.SPEED) & (DIface.CYCLE - 1));
            byte* dest = Vid.vid.Buffer + (Screen.scr_vrect->Y * Vid.vid.RowBytes) + Screen.scr_vrect->X;

            for (int v = 0; v < Screen.scr_vrect->Height; v++, dest += Vid.vid.RowBytes)
            {
                int* col = &column[turb[v]];
                byte** row = &rowptr[v];

                for (int u = 0; u < Screen.scr_vrect->Width; u += 4)
                {
                    dest[u + 0] = row[turb[u + 0]][col[u + 0]];
                    dest[u + 1] = row[turb[u + 1]][col[u + 1]];
                    dest[u + 2] = row[turb[u + 2]][col[u + 2]];
                    dest[u + 3] = row[turb[u + 3]][col[u + 3]];
                }
            }
        }
    }

    // D_DrawZSpans
    public static void DrawZSpans(ESpan_t* pspan)
    {
        int izistep = float.ConvertToIntegerNative<int>(DLocal.d_zistepu * 0x8000 * 0x10000);

        short* pzbuffer = DLocal.d_pzbuffer;
        uint zwidth = DLocal.d_zwidth;
        float d_zistepv = DLocal.d_zistepv, d_zistepu = DLocal.d_zistepu, d_ziorigin = DLocal.d_ziorigin;
        Vector256<int> seq = Vector256.CreateSequence(0, izistep);

        do
        {
            short* pdest = pzbuffer + (zwidth * pspan->V) + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            double zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));
            int izi = double.ConvertToIntegerNative<int>(zi * 0x8000 * 0x10000);

            if (((nint)pdest & 0x02) != 0)
            {
                *pdest++ = (short)(izi >> 16);
                izi += izistep;
                count--;
            }

            while (Sse41.IsSupported && count > Vector256<uint>.Count)
            {
                Vector256<int> iziV = Vector256.Create(izi) + seq;
                iziV >>= 16;

                Vector128<short> t2 = Sse41.PackUnsignedSaturate(iziV.GetLower(), iziV.GetUpper())
                    .AsInt16();

                t2.Store(pdest);

                izi += izistep * Vector256<uint>.Count;
                count -= Vector256<uint>.Count;
                pdest += Vector256<uint>.Count;
            }

            int doublecount = count >> 1;
            if (doublecount > 0)
            {
                do
                {
                    unchecked
                    {
                        uint ltemp = (uint)(izi >> 16);
                        izi += izistep;
                        ltemp |= (uint)(izi & (int)0xFFFF0000);
                        izi += izistep;
                        *(int*)pdest = (int)ltemp;
                        pdest += 2;
                    }
                } while (--doublecount > 0);
            }

            if ((count & 1) != 0)
            {
                *pdest = (short)(izi >> 16);
            }
        } while ((pspan = pspan->PNext) != null);
    }

}
