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

// DScan.DrawSpans8 with the per-8-pixel divide chain broken: the sdivz/tdivz/zi
// running sums are cheap serial adds, but every 1/zi divide depends only on its
// own sum, so the (up to 8) non-final chunks of a span are divided, scaled,
// converted and clamped as one vdivps/vcvttps2dq batch instead of one latency
// chain per chunk. Results are bit-identical to D_DrawSpans8's chunk math.
public unsafe partial class DrawSpans8Benchmarks
{
    private interface IFetch
    {
        static abstract bool Gather { get; }
        static abstract bool Fma { get; }
    }
    private readonly struct FetchGather : IFetch { public static bool Gather => true; public static bool Fma => false; }
    private readonly struct FetchGatherFma : IFetch { public static bool Gather => true; public static bool Fma => true; }
    private readonly struct FetchScalar : IFetch { public static bool Gather => false; public static bool Fma => false; }

    [Benchmark]
    public void Batched_Gather() => DrawSpans8_Batched<FetchGather>(&_spans[0]);

    [Benchmark]
    public void Batched_GatherFma() => DrawSpans8_Batched<FetchGatherFma>(&_spans[0]);

    [Benchmark]
    public void Batched_Scalar() => DrawSpans8_Batched<FetchScalar>(&_spans[0]);

    // pixels [0, n) of one chunk; n == 8 is the common full store
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitChunk<TF>(byte* pbase, byte* pdest, int s, int t, int sstep, int tstep, int n,
        Vector256<int> iota, Vector256<int> cwV, int* ib) where TF : struct, IFetch
    {
        Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * iota;
        Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * iota;
        Vector256<int> idxV = (sV >> 16) + (tV >> 16) * cwV;

        if (n != 8)
            idxV = Vector256.AndNot(idxV, Vector256.GreaterThanOrEqual(iota, Vector256.Create(n)));

        ulong texels;
        if (TF.Gather)
        {
            Vector256<int> g = Avx2.GatherVector256((int*)pbase, idxV, 1);
            texels = Avx2.PermuteVar8x32(
                Avx2.Shuffle(g.AsByte(), GatherPack).AsInt32(),
                Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0)).AsUInt64().ToScalar();
        }
        else
        {
            idxV.Store(ib);
            ulong* i64 = (ulong*)ib;
            ulong i01 = i64[0], i23 = i64[1], i45 = i64[2], i67 = i64[3];
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

        if (n == 8)
        {
            Unsafe.WriteUnaligned(pdest, texels);
            return;
        }
        if ((n & 4) != 0) { Unsafe.WriteUnaligned(pdest, (uint)texels); texels >>= 32; pdest += 4; }
        if ((n & 2) != 0) { Unsafe.WriteUnaligned(pdest, (ushort)texels); texels >>= 16; pdest += 2; }
        if ((n & 1) != 0) *pdest = (byte)texels;
    }

    private static void DrawSpans8_Batched<TF>(ESpan_t* pspan) where TF : struct, IFetch
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

        Vector256<int> iota = Vector256.CreateSequence(0, 1);
        Vector256<int> cwV = Vector256.Create(cachewidth);
        Vector256<int> bbsV = Vector256.Create(bbextents), bbtV = Vector256.Create(bbextentt);
        Vector256<int> eight = Vector256.Create(8);
        Vector256<int> sadjV = Vector256.Create(sadjust), tadjV = Vector256.Create(tadjust);
        Vector256<float> one16 = Vector256.Create(65536.0f);
        Vector256<int> rot = Vector256.Create(7, 0, 1, 2, 3, 4, 5, 6);

        float* fx = stackalloc float[24];
        float* fy = fx + 8;
        float* fw = fx + 16;
        int* ib = stackalloc int[56];          // [0,8): texel index scratch for the scalar fetch
        int* sb = ib + 8, ss = ib + 16, tb = ib + 24, ts = ib + 32, snb = ib + 40, tnb = ib + 48;
        new Span<float>(fx, 24).Clear();
        new Span<int>(ib, 56).Clear();

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

            // chunks after which more pixels remain; the last chunk (1..8 px) is handled below
            int nfull = (count - 1) >> 3;
            int spancount = count - (nfull << 3);

            while (nfull > 0)
            {
                int m = nfull < 8 ? nfull : 8;
                nfull -= m;

                Vector256<float> X, Y, W;
                if (TF.Fma)
                {
                    Vector256<float> kV = Vector256.ConvertToSingle(iota + Vector256<int>.One);
                    X = Vector256.FusedMultiplyAdd(kV, Vector256.Create(sdivz8stepu), Vector256.Create(sdivz));
                    Y = Vector256.FusedMultiplyAdd(kV, Vector256.Create(tdivz8stepu), Vector256.Create(tdivz));
                    W = Vector256.FusedMultiplyAdd(kV, Vector256.Create(zi8stepu), Vector256.Create(zi));
                    sdivz = X.GetElement(m - 1); tdivz = Y.GetElement(m - 1); zi = W.GetElement(m - 1);
                }
                else
                {
                    for (int k = 0; k < m; k++)
                    {
                        sdivz += sdivz8stepu;
                        tdivz += tdivz8stepu;
                        zi += zi8stepu;
                        fx[k] = sdivz;
                        fy[k] = tdivz;
                        fw[k] = zi;
                    }
                    X = Vector256.Load(fx); Y = Vector256.Load(fy); W = Vector256.Load(fw);
                }

                Vector256<float> zV = one16 / W;
                Vector256<int> sn = Vector256.ConvertToInt32Native(X * zV) + sadjV;
                Vector256<int> tn = Vector256.ConvertToInt32Native(Y * zV) + tadjV;
                sn = Vector256.ConditionalSelect(Vector256.GreaterThan(sn, bbsV), bbsV, Vector256.Max(sn, eight));
                tn = Vector256.ConditionalSelect(Vector256.GreaterThan(tn, bbtV), bbtV, Vector256.Max(tn, eight));

                // chunk j starts at the previous chunk's end: lane j of sp/tp
                Vector256<int> sp = Avx2.Blend(Avx2.PermuteVar8x32(sn, rot), Vector256.Create(s), 0x01);
                Vector256<int> tp = Avx2.Blend(Avx2.PermuteVar8x32(tn, rot), Vector256.Create(t), 0x01);
                sp.Store(sb);
                tp.Store(tb);
                ((sn - sp) >> 3).Store(ss);
                ((tn - tp) >> 3).Store(ts);
                sn.Store(snb);
                tn.Store(tnb);

                for (int j = 0; j < m; j++)
                {
                    EmitChunk<TF>(pbase, pdest, sb[j], tb[j], ss[j], ts[j], 8, iota, cwV, ib);
                    pdest += 8;
                }

                s = snb[m - 1];
                t = tnb[m - 1];
            }

            // last chunk: spancount - 1 pixel steps from the running sums
            {
                int sstep = 0, tstep = 0;

                float spancountminus1 = spancount - 1;
                sdivz += d_sdivzstepu * spancountminus1;
                tdivz += d_tdivzstepu * spancountminus1;
                zi += d_zistepu * spancountminus1;
                z = 0x10000 / zi;

                int snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                if (snext > bbextents)
                    snext = bbextents;
                else if (snext < 8)
                    snext = 8;

                int tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                if (tnext > bbextentt)
                    tnext = bbextentt;
                else if (tnext < 8)
                    tnext = 8;

                if (spancount > 1)
                {
                    sstep = (snext - s) / (spancount - 1);
                    tstep = (tnext - t) / (spancount - 1);
                }

                EmitChunk<TF>(pbase, pdest, s, t, sstep, tstep, spancount, iota, cwV, ib);
            }

        } while ((pspan = pspan->PNext) != null);
    }

    [Benchmark]
    public void BranchlessTail() => DrawSpans8_BranchlessTail(&_spans[0]);

    [Benchmark]
    public void Engine() => DScan.DrawSpans8(&_spans[0]);

    // Same chunk math as D_DrawSpans8, but the final 1..8 pixel chunk has no
    // data-dependent branches: lanes past the span end are zeroed out of the
    // gather, and the result is merged into the 8 bytes ending at the span end.
    private static void DrawSpans8_BranchlessTail(ESpan_t* pspan)
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

        Vector256<int> iota = Vector256.CreateSequence(0, 1);
        Vector256<int> cwV = Vector256.Create(cachewidth);
        Vector256<int> order = Vector256.Create(0, 4, 0, 0, 0, 0, 0, 0);

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

            int snext, tnext;

            while (count > 8)
            {
                count -= 8;

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

                int sstep = (snext - s) >> 3;
                int tstep = (tnext - t) >> 3;

                Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * iota;
                Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * iota;
                Vector256<int> idxV = (sV >> 16) + (tV >> 16) * cwV;

                Vector256<int> g = Avx2.GatherVector256((int*)pbase, idxV, 1);
                Unsafe.WriteUnaligned(pdest, Avx2.PermuteVar8x32(Avx2.Shuffle(g.AsByte(), GatherPack).AsInt32(), order).AsUInt64().ToScalar());
                pdest += 8;

                s = snext;
                t = tnext;
            }

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

                // pixel 0 is the only one used when spancount == 1, so the step is don't-care there
                int divisor = spancount > 1 ? spancount - 1 : 1;
                int sstep = (snext - s) / divisor;
                int tstep = (tnext - t) / divisor;

                Vector256<int> sV = Vector256.Create(s) + Vector256.Create(sstep) * iota;
                Vector256<int> tV = Vector256.Create(t) + Vector256.Create(tstep) * iota;
                Vector256<int> idxV = (sV >> 16) + (tV >> 16) * cwV;
                idxV = Vector256.AndNot(idxV, Vector256.GreaterThanOrEqual(iota, Vector256.Create(spancount)));

                Vector256<int> g = Avx2.GatherVector256((int*)pbase, idxV, 1);
                ulong texels = Avx2.PermuteVar8x32(Avx2.Shuffle(g.AsByte(), GatherPack).AsInt32(), order).AsUInt64().ToScalar();

                // merge into the 8 bytes ending at the span end
                int sh = (8 - spancount) << 3;
                byte* w = pdest + spancount - 8;
                if (w >= viewbuffer)
                {
                    ulong old = Unsafe.ReadUnaligned<ulong>(w);
                    Unsafe.WriteUnaligned(w, (old & ((1UL << sh) - 1)) | (texels << sh));
                }
                else
                {
                    for (int j = 0; j < spancount; j++)
                        pdest[j] = (byte)(texels >> (j << 3));
                }
            }

        } while ((pspan = pspan->PNext) != null);
    }
}
