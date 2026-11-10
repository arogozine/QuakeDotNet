// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Quake.Benchmarks;

[DisassemblyDiagnoser(printSource: true)]
public unsafe partial class DrawSpans8Benchmarks
{
    #region Setup / Teardown

    private const int ScreenWidth = 1024;
    private const int ScreenHeight = 768;
    private const int SpanCount = 400;
    private const int CacheWidth = 64;
    private const int CacheHeight = 64;
    private byte* _viewbuffer;
    private byte* _texture;
    private ESpan_t* _spans;
    // Stable, 32-byte aligned index scratch buffer for DrawSpans8StoreReload2;
    // grown on demand and reused across calls.
    private static int* s_spanIndices;
    private static int s_spanIndicesCapacity;

    [GlobalSetup]
    public void Setup()
    {
        _viewbuffer = NativeMemory.AllocArrayZeroed<byte>(ScreenWidth * ScreenHeight);
        _texture = NativeMemory.AllocArrayZeroed<byte>((CacheWidth * CacheHeight) + 4); // +4: dword texel gather may overread 3 bytes
        _spans = NativeMemory.AllocArray<ESpan_t>(SpanCount);

        Random rng = new(1234);
        for (int i = 0; i < CacheWidth * CacheHeight; i++)
        {
            _texture[i] = (byte)rng.Next(256);
        }

        // A few hundred spans of varying width scattered down the screen --
        // representative of one textured surface's worth of scanline spans.
        for (int i = 0; i < SpanCount; i++)
        {
            int v = i % ScreenHeight;
            int count = rng.Next(4, 64);
            int u = rng.Next(0, ScreenWidth - count);
            _spans[i] = new ESpan_t
            {
                U = u,
                V = v,
                Count = count,
                PNext = i + 1 < SpanCount ? &_spans[i + 1] : null,
            };
        }

        RShared.cacheblock = _texture;
        RShared.cachewidth = CacheWidth;
        RShared.screenwidth = ScreenWidth;
        DLocal.d_viewbuffer = _viewbuffer;

        DLocal.d_sdivzorigin = 5.0f;
        DLocal.d_sdivzstepu = 0.01f;
        DLocal.d_sdivzstepv = 0.02f;
        DLocal.d_tdivzorigin = 3.0f;
        DLocal.d_tdivzstepu = 0.015f;
        DLocal.d_tdivzstepv = 0.03f;
        DLocal.d_ziorigin = 0.01f;
        DLocal.d_zistepu = 0.0001f;
        DLocal.d_zistepv = 0.0002f;
        DLocal.sadjust = 0;
        DLocal.tadjust = 0;
        DLocal.bbextents = (CacheWidth << 16) - 1;
        DLocal.bbextentt = (CacheHeight << 16) - 1;

        VerifyVariants();
    }

    private void VerifyVariants()
    {
        int size = ScreenWidth * ScreenHeight;
        Span<byte> view = new(_viewbuffer, size);

        view.Clear();
        DScan.DrawSpans8(&_spans[0]);
        byte[] expected = view.ToArray();

        foreach (Action variant in new Action[] { DrawSpans8_Gather256, Batched_Gather, Batched_Scalar, BranchlessTail })
        {
            view.Clear();
            variant();
            if (!view.SequenceEqual(expected))
            {
                throw new InvalidOperationException($"{variant.Method.Name} output differs from DScan.DrawSpans8");
            }
        }

        view.Clear();
    }

    private static int* EnsureSpanIndices(int capacity)
    {
        if (capacity > s_spanIndicesCapacity)
        {
            NativeMemory.AlignedFree(s_spanIndices);
            s_spanIndices = (int*)NativeMemory.AlignedAlloc((nuint)(capacity * sizeof(int)), 32);
            s_spanIndicesCapacity = capacity;
        }
        return s_spanIndices;
    }

    private static void FreeSpanIndices()
    {
        NativeMemory.AlignedFree(s_spanIndices);
        s_spanIndices = null;
        s_spanIndicesCapacity = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_viewbuffer);
        NativeMemory.Free(_texture);
        NativeMemory.Free(_spans);
        FreeSpanIndices();
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Original() => Original(&_spans[0]);

    [Benchmark]
    public void PerspectiveCorrectPerPixel() => PerspectiveCorrectPerPixel(&_spans[0]);

    [Benchmark]
    public void PerspectiveCorrectPerPixel_Stack() => PerspectiveCorrectPerPixel_Stack(&_spans[0]);

    [Benchmark]
    public void FmaOnly() => FmaOnly(&_spans[0]);

    [Benchmark]
    public void DrawSpans8_Stack() => DrawSpans8_Stack(&_spans[0]);

    [Benchmark]
    public void DrawSpans8_VectorIndex() => DrawSpans8_VectorIndex(&_spans[0]);


    private static void Original(ESpan_t* pspan)
    {
        byte* pbase = RShared.cacheblock;

        float sdivz8stepu = DLocal.d_sdivzstepu * 8;
        float tdivz8stepu = DLocal.d_tdivzstepu * 8;
        float zi8stepu = DLocal.d_zistepu * 8;

        do
        {
            byte* pdest = DLocal.d_viewbuffer + (RShared.screenwidth * pspan->V) + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = DLocal.d_sdivzorigin + (dv * DLocal.d_sdivzstepv) + (du * DLocal.d_sdivzstepu);
            float tdivz = DLocal.d_tdivzorigin + (dv * DLocal.d_tdivzstepv) + (du * DLocal.d_tdivzstepu);
            float zi = DLocal.d_ziorigin + (dv * DLocal.d_zistepv) + (du * DLocal.d_zistepu);
            float z = 0x10000 / zi;

            int s = (int)(sdivz * z) + DLocal.sadjust;
            if (s > DLocal.bbextents)
            {
                s = DLocal.bbextents;
            }
            else if (s < 0)
            {
                s = 0;
            }

            int t = (int)(tdivz * z) + DLocal.tadjust;
            if (t > DLocal.bbextentt)
            {
                t = DLocal.bbextentt;
            }
            else if (t < 0)
            {
                t = 0;
            }

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

                    snext = (int)(sdivz * z) + DLocal.sadjust;
                    if (snext > DLocal.bbextents)
                    {
                        snext = DLocal.bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = (int)(tdivz * z) + DLocal.tadjust;
                    if (tnext > DLocal.bbextentt)
                    {
                        tnext = DLocal.bbextentt;
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
                    sdivz += DLocal.d_sdivzstepu * spancountminus1;
                    tdivz += DLocal.d_tdivzstepu * spancountminus1;
                    zi += DLocal.d_zistepu * spancountminus1;
                    z = 0x10000 / zi;

                    snext = (int)(sdivz * z) + DLocal.sadjust;
                    if (snext > DLocal.bbextents)
                    {
                        snext = DLocal.bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = (int)(tdivz * z) + DLocal.tadjust;
                    if (tnext > DLocal.bbextentt)
                    {
                        tnext = DLocal.bbextentt;
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

                do
                {
                    *pdest++ = *(pbase + (s >> 16) + ((t >> 16) * RShared.cachewidth));
                    s += sstep;
                    t += tstep;
                } while (--spancount > 0);

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    private static void FmaOnly(ESpan_t* pspan)
    {
        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        int cachewidth = RShared.cachewidth;
        float d_sdivzstepu = DLocal.d_sdivzstepu, d_sdivzstepv = DLocal.d_sdivzstepv, d_sdivzorigin = DLocal.d_sdivzorigin;
        float d_tdivzstepu = DLocal.d_tdivzstepu, d_tdivzstepv = DLocal.d_tdivzstepv, d_tdivzorigin = DLocal.d_tdivzorigin;
        float d_zistepu = DLocal.d_zistepu, d_zistepv = DLocal.d_zistepv, d_ziorigin = DLocal.d_ziorigin;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        byte* pbase = RShared.cacheblock;

        float sdivz8stepu = DLocal.d_sdivzstepu * 8;
        float tdivz8stepu = DLocal.d_tdivzstepu * 8;
        float zi8stepu = DLocal.d_zistepu * 8;

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

                    snext = (int)(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 8)
                    {
                        snext = 8;
                    }

                    tnext = (int)(tdivz * z) + tadjust;
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

                do
                {
                    *pdest++ = *(pbase + (s >> 16) + ((t >> 16) * cachewidth));
                    s += sstep;
                    t += tstep;
                } while (--spancount > 0);

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

    public static void DrawSpans8_Stack(ESpan_t* pspan)
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
                    // Indices are non-negative (s/t are clamped), so (uint) is a
                    // free zero-extension instead of a movsxd.
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

    public static void DrawSpans8_VectorIndex(ESpan_t* pspan)
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
                Vector256<ulong> idxV = ((sV >> 16) + ((tV >> 16) * cachewidthV)).AsUInt64();

                if (spancount == 8)
                {
                    // Indices are non-negative (s/t are clamped), so (uint) is a
                    // free zero-extension instead of a movsxd.
                    ulong i01 = idxV[0], i23 = idxV[1], i45 = idxV[2], i67 = idxV[3];

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
                        *pdest++ = pbase[idxV.AsInt32()[j]];
                    }
                }

                s = snext;
                t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }

}
