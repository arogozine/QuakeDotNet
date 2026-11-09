// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Benchmarks;

// Compares DScan.DrawZSpans (d_scan.c's D_DrawZSpans) as it stands today --
// which, after the single-short alignment fixup, writes 4 shorts at a time
// via a long store when at least that many pixels remain, falling back to
// the original's paired-short (uint) store for the 0-3 leftover -- against
// the original body, which only ever paired shorts via a uint store. Both
// versions compute identical output; this isolates the quad-store change's
// cost.
[DisassemblyDiagnoser]
public unsafe class DrawZSpansBenchmarks
{
    #region Setup / Teardown

    private const int ZWidth = 320;
    private const int ZHeight = 200;
    private const int SpanCount = 400;
    private short* _zbuffer;
    private ESpan_t* _spans;

    [GlobalSetup]
    public void Setup()
    {
        _zbuffer = NativeMemory.AllocArrayZeroed<short>(ZWidth * ZHeight);
        _spans = NativeMemory.AllocArray<ESpan_t>(SpanCount);

        Random rng = new(1234);

        // A few hundred spans of varying width scattered down the screen --
        // representative of one surface's worth of scanline spans, wide
        // enough to regularly exercise the quad-store path.
        for (int i = 0; i < SpanCount; i++)
        {
            int v = i % ZHeight;
            int count = rng.Next(4, 64);
            int u = rng.Next(0, ZWidth - count);
            _spans[i] = new ESpan_t
            {
                U = u,
                V = v,
                Count = count,
                PNext = i + 1 < SpanCount ? &_spans[i + 1] : null,
            };
        }

        DLocal.d_pzbuffer = _zbuffer;
        DLocal.d_zwidth = ZWidth;
        DLocal.d_ziorigin = 0.01f;
        DLocal.d_zistepu = 0.0001f;
        DLocal.d_zistepv = 0.0002f;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_zbuffer);
        NativeMemory.Free(_spans);
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Original_DrawZSpans() => Original(&_spans[0]);

    [Benchmark]
    public void DrawZSpans() => DrawZSpans(&_spans[0]);

    [Benchmark]
    public void DrawZSpans2() => DrawZSpans2(&_spans[0]);

    [Benchmark]
    public void DrawZSpans3() => DrawZSpans3(&_spans[0]);

    // Pre-quad-store body of DScan.DrawZSpans (d_scan.c's D_DrawZSpans),
    // reproduced here only to benchmark the long-store change against the
    // current implementation.
    private static void Original(ESpan_t* pspan)
    {
        int izistep = (int)(DLocal.d_zistepu * 0x8000 * 0x10000);

        short* pzbuffer = DLocal.d_pzbuffer;
        uint zwidth = DLocal.d_zwidth;
        float d_zistepv = DLocal.d_zistepv, d_zistepu = DLocal.d_zistepu, d_ziorigin = DLocal.d_ziorigin;

        do
        {
            short* pdest = pzbuffer + zwidth * pspan->V + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            double zi = d_ziorigin + dv * d_zistepv + du * d_zistepu;
            int izi = (int)(zi * 0x8000 * 0x10000);

            if (((nint)pdest & 0x02) != 0)
            {
                *pdest++ = (short)(izi >> 16);
                izi += izistep;
                count--;
            }

            int doublecount = count >> 1;
            if (doublecount > 0)
            {
                do
                {
                    uint ltemp = (uint)(izi >> 16);
                    izi += izistep;
                    ltemp |= (uint)(izi & unchecked((int)0xFFFF0000));
                    izi += izistep;
                    *(int*)pdest = (int)ltemp;
                    pdest += 2;
                } while (--doublecount > 0);
            }

            if ((count & 1) != 0)
                *pdest = (short)(izi >> 16);

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawZSpans(ESpan_t* pspan)
    {
        int izistep = (int)(DLocal.d_zistepu * 0x8000 * 0x10000);

        short* pzbuffer = DLocal.d_pzbuffer;
        uint zwidth = DLocal.d_zwidth;
        float d_zistepv = DLocal.d_zistepv, d_zistepu = DLocal.d_zistepu, d_ziorigin = DLocal.d_ziorigin;
        Vector256<int> seq = Vector256.CreateSequence(0, izistep);

        do
        {
            short* pdest = pzbuffer + zwidth * pspan->V + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            double zi = d_ziorigin + dv * d_zistepv + du * d_zistepu;
            int izi = double.ConvertToInteger<int>(zi * 0x8000 * 0x10000);

            if (((nint)pdest & 0x02) != 0)
            {
                *pdest++ = (short)(izi >> 16);
                izi += izistep;
                count--;
            }

            while (count > Vector256<uint>.Count)
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
                *pdest = (short)(izi >> 16);

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawZSpans2(ESpan_t* pspan)
    {
        int izistep = float.ConvertToIntegerNative<int>(DLocal.d_zistepu * 0x8000 * 0x10000);

        short* pzbuffer = DLocal.d_pzbuffer;
        uint zwidth = DLocal.d_zwidth;
        float d_zistepv = DLocal.d_zistepv, d_zistepu = DLocal.d_zistepu, d_ziorigin = DLocal.d_ziorigin;
        Vector256<int> seq = Vector256.CreateSequence(0, izistep);

        do
        {
            short* pdest = pzbuffer + zwidth * pspan->V + pspan->U;

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

            while (Vector256.IsHardwareAccelerated && count > Vector256<uint>.Count)
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
                *pdest = (short)(izi >> 16);

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawZSpans3(ESpan_t* pspan)
    {
        unchecked
        {
            int izistep = float.ConvertToIntegerNative<int>(DLocal.d_zistepu * 0x8000 * 0x10000);

            short* pzbuffer = DLocal.d_pzbuffer;
            uint zwidth = DLocal.d_zwidth;
            float d_zistepv = DLocal.d_zistepv, d_zistepu = DLocal.d_zistepu, d_ziorigin = DLocal.d_ziorigin;
            Vector256<int> seq = Vector256.CreateSequence(0, izistep);

            do
            {
                short* pdest = pzbuffer + zwidth * pspan->V + pspan->U;

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

                while (Vector256.IsHardwareAccelerated && count > Vector256<uint>.Count)
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
                        uint ltemp = (uint)(izi >> 16);
                        izi += izistep;
                        ltemp |= (uint)(izi & (int)0xFFFF0000);
                        izi += izistep;
                        *(int*)pdest = (int)ltemp;
                        pdest += 2;
                    } while (--doublecount > 0);
                }

                if ((count & 1) != 0)
                    *pdest = (short)(izi >> 16);

            } while ((pspan = pspan->PNext) != null);
        }
    }
}
