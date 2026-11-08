// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Benchmarks;

// Texel-gather variants of DScan.DrawSpans8 (d_scan.c D_DrawSpans8):
//  TexGather     - full 8-pixel groups fetch texels with one dword gather
//                  (scale 1) + pshufb/vpermd pack into a single ulong store,
//                  instead of 8 scalar byte loads through a stack buffer.
//                  The <8 tail keeps the scalar loop.
//  TexGatherTail - as TexGather, plus the <8 tail uses a masked gather (only
//                  lanes < spancount load, so lanes past the span's last
//                  texel never touch memory) and a 4/2/1-byte piecewise store.
// Both read up to 3 bytes past the last texel addressed, hence the padded
// texture allocation in Setup.
public unsafe partial class DrawSpans8Benchmarks
{
    private interface IGatherOpts
    {
        static abstract bool GatherTail { get; }
    }
    private readonly struct GatherFullOnly : IGatherOpts { public static bool GatherTail => false; }
    private readonly struct GatherWithTail : IGatherOpts { public static bool GatherTail => true; }
    /*
    [Benchmark]
    public void TexGather() => DrawSpans8TexGather<GatherFullOnly>(&_spans[0]);

    [Benchmark]
    public void TexGatherTail() => DrawSpans8TexGather<GatherWithTail>(&_spans[0]);

    [Benchmark]
    public void DrawSpan8TexGather2() => DrawSpan8TexGather2(&_spans[0]);

    [Benchmark]
    public void DrawSpan8TexGather3() => DrawSpan8TexGather3(&_spans[0]);


    [Benchmark]
    public void DrawSpans8_Gather256() => DrawSpans8_Gather256(&_spans[0]);

                */

    [Benchmark]
    public void DrawSpans8_Gather256() => DrawSpans8_Gather256(&_spans[0]);


    // Byte 0 of each dword -> bytes 0..3 of each 128-bit lane; vpermd with
    // GatherOrder then joins the two lanes' dwords into the low 8 bytes.
    private static readonly Vector256<byte> GatherPack = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong PackTexels(Vector256<int> gathered, Vector256<byte> pack, Vector256<int> order) =>
        Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar();

    private static void DrawSpans8TexGather<TOpt>(ESpan_t* pspan) where TOpt : struct, IGatherOpts
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
        Vector256<byte> pack = GatherPack;
        Vector256<int> order = Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0);

        int* idx = stackalloc int[Vector256<int>.Count];

        do
        {
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;

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
                        snext = bbextents;
                    else if (snext < 8)
                        snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                        tnext = bbextentt;
                    else if (tnext < 8)
                        tnext = 8;

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
                        snext = bbextents;
                    else if (snext < 8)
                        snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                        tnext = bbextentt;
                    else if (tnext < 8)
                        tnext = 8;

                    if (spancount > 1)
                    {
                        sstep = (snext - s) / (spancount - 1);
                        tstep = (tnext - t) / (spancount - 1);
                    }
                }

                Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                Vector256<int> idxV = (sV >> 16) + (tV >> 16) * cachewidthV;

                if (spancount == 8)
                {
                    Unsafe.WriteUnaligned(pdest, PackTexels(Avx2.GatherVector256((int*)pbase, idxV, 1), pack, order));
                    pdest += 8;
                }
                else if (TOpt.GatherTail)
                {
                    // Lanes >= spancount are masked off: their s/t run past
                    // snext/tnext and may index outside the texture.
                    Vector256<int> mask = Vector256.LessThan(indexV, Vector256.Create(spancount));
                    ulong texels = PackTexels(
                        Avx2.GatherMaskVector256(Vector256<int>.Zero, (int*)pbase, idxV, mask, 1), pack, order);

                    if ((spancount & 4) != 0)
                    {
                        Unsafe.WriteUnaligned(pdest, (uint)texels);
                        pdest += 4;
                        texels >>= 32;
                    }
                    if ((spancount & 2) != 0)
                    {
                        Unsafe.WriteUnaligned(pdest, (ushort)texels);
                        pdest += 2;
                        texels >>= 16;
                    }
                    if ((spancount & 1) != 0)
                        *pdest++ = (byte)texels;
                }
                else
                {
                    idxV.Store(idx);
                    for (int j = 0; j < spancount; j++)
                        *pdest++ = pbase[idx[j]];
                }

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong PackTexels(byte* pbase, ulong* idx64, Vector256<int> idxV, Vector256<byte> pack, Vector256<int> order)
    {
        if (Avx2.IsSupported)
        {
            Vector256<int> gathered = Avx2.GatherVector256((int*)pbase, idxV, 1);

            return Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar();
        }
        else
        {
            ulong i01 = idx64[0], i23 = idx64[1], i45 = idx64[2], i67 = idx64[3];

            ulong texels =
                  pbase[(uint)i01]
                | (ulong)pbase[(uint)(i01 >> 32)] << 8
                | (ulong)pbase[(uint)i23] << 16
                | (ulong)pbase[(uint)(i23 >> 32)] << 24
                | (ulong)pbase[(uint)i45] << 32
                | (ulong)pbase[(uint)(i45 >> 32)] << 40
                | (ulong)pbase[(uint)i67] << 48
                | (ulong)pbase[(uint)(i67 >> 32)] << 56;

            return texels;
        }
    }

    private static void DrawSpan8TexGather2(ESpan_t* pspan)
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
        Vector256<byte> pack = GatherPack;
        Vector256<int> order = Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0);

        int* idx = stackalloc int[Vector256<int>.Count];
        ulong* idx64 = (ulong*)idx;

        do
        {
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;

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
                        snext = bbextents;
                    else if (snext < 8)
                        snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                        tnext = bbextentt;
                    else if (tnext < 8)
                        tnext = 8;

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
                        snext = bbextents;
                    else if (snext < 8)
                        snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                        tnext = bbextentt;
                    else if (tnext < 8)
                        tnext = 8;

                    if (spancount > 1)
                    {
                        sstep = (snext - s) / (spancount - 1);
                        tstep = (tnext - t) / (spancount - 1);
                    }
                }

                Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                Vector256<int> idxV = (sV >> 16) + (tV >> 16) * cachewidthV;

                if (spancount == 8)
                {
                    Unsafe.WriteUnaligned(pdest, PackTexels(pbase, idx64, idxV, pack, order));
                    pdest += 8;
                }
                else
                {
                    idxV.Store(idx);
                    for (int j = 0; j < spancount; j++)
                        *pdest++ = pbase[idx[j]];
                }

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawSpan8TexGather3(ESpan_t* pspan)
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

        // A span never exceeds the screen width; pad by one vector since the
        // final partial group still stores a full Vector256.
        int* indices = EnsureSpanIndices(screenwidth + Vector256<int>.Count);
        Vector256<int> order = Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0);
        Vector256<byte> pack = GatherPack;

        do
        {
            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            // Pass 1: compute every texel index for the span into the stable buffer.
            int* idx = indices;

            float sdivz = MathF.FusedMultiplyAdd(du, d_sdivzstepu, MathF.FusedMultiplyAdd(dv, d_sdivzstepv, d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, d_tdivzstepu, MathF.FusedMultiplyAdd(dv, d_tdivzstepv, d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));
            float z = 0x10000 / zi;

            int s = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
            s = Math.Clamp(s, 0, bbextents);

            int t = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
            t = Math.Clamp(t, 0, bbextentt);

            int sstep = 0, tstep = 0;
            int snext, tnext;

            do
            {
                sdivz += sdivz8stepu;
                tdivz += tdivz8stepu;
                zi += zi8stepu;
                z = 0x10000 / zi;

                snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                if (snext > bbextents) // snext | bbextents
                    snext = bbextents;
                else if (snext < 8)
                    snext = 8;

                tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                if (tnext > bbextentt)
                    tnext = bbextentt;
                else if (tnext < 8)
                    tnext = 8;

                sstep = (snext - s) >> 3;
                tstep = (tnext - t) >> 3;


                Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                ((sV >> 16) + (tV >> 16) * cachewidthV).StoreAligned(idx);






                idx += 8;

                s = snext;
                t = tnext;
                count -= 8;
            }
            while (count >= 8);

            if (count != 0)
            {
                int spancount = count;

                float spancountminus1 = spancount - 1;
                sdivz += d_sdivzstepu * spancountminus1;
                tdivz += d_tdivzstepu * spancountminus1;
                zi += d_zistepu * spancountminus1;
                z = 0x10000 / zi;

                snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                if (snext > bbextents)
                    snext = bbextents;
                else if (snext < 8)
                    snext = 8;

                tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                if (tnext > bbextentt)
                    tnext = bbextentt;
                else if (tnext < 8)
                    tnext = 8;

                if (spancount > 1)
                {
                    sstep = (snext - s) / (spancount - 1);
                    tstep = (tnext - t) / (spancount - 1);
                }


                Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                ((sV >> 16) + (tV >> 16) * cachewidthV).StoreAligned(idx);
            }


            // Pass 2: gather texels through the precomputed indices into pdest.
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;
            idx = indices;
            count = pspan->Count;

            for (; count >= 8; count -= 8)
            {
                ulong texels;

                if (Avx2.IsSupported)
                {
                    Vector256<int> idxV = Vector256.LoadAligned(idx);
                    Vector256<int> gathered = Avx2.GatherVector256((int*)pbase, idxV, 1);

                    texels = Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar();
                }
                else
                {
                    ulong* idx64 = (ulong*)idx;
                    ulong i01 = idx64[0], i23 = idx64[1], i45 = idx64[2], i67 = idx64[3];

                    texels =
                          pbase[(uint)i01]
                        | (ulong)pbase[(uint)(i01 >> 32)] << 8
                        | (ulong)pbase[(uint)i23] << 16
                        | (ulong)pbase[(uint)(i23 >> 32)] << 24
                        | (ulong)pbase[(uint)i45] << 32
                        | (ulong)pbase[(uint)(i45 >> 32)] << 40
                        | (ulong)pbase[(uint)i67] << 48
                        | (ulong)pbase[(uint)(i67 >> 32)] << 56;
                }

                Unsafe.WriteUnaligned(pdest, texels);
                pdest += 8;
                idx += 8;
            }

            for (; count > 0; count--)
                *pdest++ = pbase[*idx++];

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawSpans8_Gather256(ESpan_t* pspan)
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
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;

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
                            snext = bbextents;
                        else if (snext < 8)
                            snext = 8;

                        tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                        if (tnext > bbextentt)
                            tnext = bbextentt;
                        else if (tnext < 8)
                            tnext = 8;

                        sstep = (snext - s) >> 3;
                        tstep = (tnext - t) >> 3;


                        sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                        tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                        idxV = (sV >> 16) + (tV >> 16) * cachewidthV;

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
                        snext = bbextents;
                    else if (snext < 8)
                        snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                        tnext = bbextentt;
                    else if (tnext < 8)
                        tnext = 8;

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
                        snext = bbextents;
                    else if (snext < 8)
                        snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                        tnext = bbextentt;
                    else if (tnext < 8)
                        tnext = 8;

                    if (spancount > 1)
                    {
                        sstep = (snext - s) / (spancount - 1);
                        tstep = (tnext - t) / (spancount - 1);
                    }
                }

                sV = Vector256.Create(s) + Vector256.Create(sstep) * indexV;
                tV = Vector256.Create(t) + Vector256.Create(tstep) * indexV;
                idxV = (sV >> 16) + (tV >> 16) * cachewidthV;

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
                        *pdest++ = pbase[idx[j]];
                }

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    // Reference variant of DScan.DrawSpans8 (d_scan.c D_DrawSpans8): exact perspective
    // at every pixel (one divide per pixel) instead of every 8th with linear
    // interpolation in between. Not a speed variant; it is the ground truth the
    // 8-pixel approximation can be compared against.
    private static void PerspectiveCorrectPerPixel(ESpan_t* pspan)
    {
        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        int cachewidth = RShared.cachewidth;
        byte* pbase = RShared.cacheblock;

        float sdivzstepu = DLocal.d_sdivzstepu, tdivzstepu = DLocal.d_tdivzstepu, zistepu = DLocal.d_zistepu;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        do
        {
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;
            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = DLocal.d_sdivzorigin + dv * DLocal.d_sdivzstepv + du * sdivzstepu;
            float tdivz = DLocal.d_tdivzorigin + dv * DLocal.d_tdivzstepv + du * tdivzstepu;
            float zi = DLocal.d_ziorigin + dv * DLocal.d_zistepv + du * zistepu;

            do
            {
                float z = 0x10000 / zi;

                int s = (int)(sdivz * z) + sadjust;
                if (s > bbextents)
                    s = bbextents;
                else if (s < 0)
                    s = 0;

                int t = (int)(tdivz * z) + tadjust;
                if (t > bbextentt)
                    t = bbextentt;
                else if (t < 0)
                    t = 0;

                *pdest++ = pbase[(s >> 16) + (t >> 16) * cachewidth];

                sdivz += sdivzstepu;
                tdivz += tdivzstepu;
                zi += zistepu;
            } while (--count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    // PerspectiveCorrectPerPixel with the DrawSpans8_Gather256 optimizations: hoisted
    // globals, FMA span setup, native float->int, 8 pixels per Vector256 divide, and
    // the texels of a full group fetched with one dword gather (scale 1) packed by
    // pshufb/vpermd into a ulong. Runs of 4 groups (32 pixels) are staged in a
    // stackalloc buffer and written with a single 256-bit store. The <8 tail is
    // scalar. Lanes past the span's end are computed but never read. Reads up to 3
    // bytes past the last texel addressed (padded texture allocation in Setup).
    //[SkipLocalsInit]
    private static void PerspectiveCorrectPerPixel_Stack(ESpan_t* pspan)
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

        Vector256<float> indexF = Vector256.CreateSequence(0f, 1f);
        Vector256<float> sdivzLane = indexF * Vector256.Create(d_sdivzstepu);
        Vector256<float> tdivzLane = indexF * Vector256.Create(d_tdivzstepu);
        Vector256<float> ziLane = indexF * Vector256.Create(d_zistepu);
        Vector256<float> oneV = Vector256.Create((float)0x10000);

        Vector256<int> cachewidthV = Vector256.Create(cachewidth);
        Vector256<int> sadjustV = Vector256.Create(sadjust), tadjustV = Vector256.Create(tadjust);
        Vector256<int> zeroV = Vector256<int>.Zero;
        Vector256<int> bbextentsV = Vector256.Create(bbextents), bbextenttV = Vector256.Create(bbextentt);

        Vector256<int> order = Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0);
        Vector256<byte> pack = Vector256.Create(
            0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
            0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);

        int* idx = stackalloc int[Vector256<int>.Count];

        do
        {
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = MathF.FusedMultiplyAdd(du, d_sdivzstepu, MathF.FusedMultiplyAdd(dv, d_sdivzstepv, d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, d_tdivzstepu, MathF.FusedMultiplyAdd(dv, d_tdivzstepv, d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));

            do
            {
                Vector256<float> zV;
                Vector256<int> sV, tV, idxV;

                while (count >= 32)
                {
                    for (int i = 0; i < Vector256<ulong>.Count; i++)
                    {
                        zV = oneV / (Vector256.Create(zi) + ziLane);
                        sV = Vector256.ConvertToInt32Native((Vector256.Create(sdivz) + sdivzLane) * zV) + sadjustV;
                        tV = Vector256.ConvertToInt32Native((Vector256.Create(tdivz) + tdivzLane) * zV) + tadjustV;
                        sV = Vector256.ClampNative(sV, zeroV, bbextentsV);
                        tV = Vector256.ClampNative(tV, zeroV, bbextenttV);
                        idxV = (sV >> 16) + (tV >> 16) * cachewidthV;

                        Vector256<int> gathered = Avx2.GatherVector256((int*)pbase, idxV, 1);

                        ((ulong*)idx)[i] = Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar();

                        sdivz += sdivz8stepu;
                        tdivz += tdivz8stepu;
                        zi += zi8stepu;
                    }

                    Vector256.Load(idx).AsByte().Store(pdest);

                    count -= 32;
                    pdest += 32;
                }

                if (count == 0)
                    break;

                int spancount = count >= 8 ? 8 : count;
                count -= spancount;

                zV = oneV / (Vector256.Create(zi) + ziLane);
                sV = Vector256.ConvertToInt32Native((Vector256.Create(sdivz) + sdivzLane) * zV) + sadjustV;
                tV = Vector256.ConvertToInt32Native((Vector256.Create(tdivz) + tdivzLane) * zV) + tadjustV;
                sV = Vector256.ClampNative(sV, zeroV, bbextentsV);
                tV = Vector256.ClampNative(tV, zeroV, bbextenttV);
                idxV = (sV >> 16) + (tV >> 16) * cachewidthV;

                if (spancount == 8)
                {
                    Vector256<int> gathered = Avx2.GatherVector256((int*)pbase, idxV, 1);

                    Unsafe.WriteUnaligned(pdest, Avx2.PermuteVar8x32(Avx2.Shuffle(gathered.AsByte(), pack).AsInt32(), order).AsUInt64().ToScalar());
                    pdest += 8;
                }
                else
                {
                    idxV.Store(idx);
                    for (int j = 0; j < spancount; j++)
                        *pdest++ = pbase[idx[j]];
                }

                sdivz += sdivz8stepu;
                tdivz += tdivz8stepu;
                zi += zi8stepu;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

}
