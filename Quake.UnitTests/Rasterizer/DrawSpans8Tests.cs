// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

// Copyright (C) 1996-1997 Id Software, Inc. (GNU General Public License)
// Contains a scalar transcription of D_DrawSpans8 (d_scan.c) used as the reference.

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DrawSpans8Tests
{
    private const int ScreenWidth = 256;
    private const int ScreenHeight = 8;
    private const int CacheWidth = 64;
    private const int CacheHeight = 64;

    private byte* _viewbuf;
    private byte* _tex;

    [TestInitialize]
    public void Setup()
    {
        _viewbuf = NativeMemory.AllocArrayZeroed<byte>(ScreenWidth * ScreenHeight);
        // +4: the gather path may read up to 3 bytes past the last texel.
        _tex = NativeMemory.AllocArrayZeroed<byte>(CacheWidth * CacheHeight + 4);

        DLocal.d_viewbuffer = _viewbuf;
        RShared.screenwidth = ScreenWidth;
        RShared.cacheblock = _tex;
        RShared.cachewidth = CacheWidth;

        // zero gradients so s/z, t/z, and 1/z are all constant across the span
        DLocal.d_sdivzorigin = 0;
        DLocal.d_sdivzstepu = 0;
        DLocal.d_sdivzstepv = 0;
        DLocal.d_tdivzorigin = 0;
        DLocal.d_tdivzstepu = 0;
        DLocal.d_tdivzstepv = 0;
        DLocal.d_ziorigin = 1.0f;
        DLocal.d_zistepu = 0;
        DLocal.d_zistepv = 0;
        DLocal.sadjust = 0;
        DLocal.tadjust = 0;
        DLocal.bbextents = 1 << 20;
        DLocal.bbextentt = 1 << 20;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_viewbuf);
        NativeMemory.Free(_tex);
    }

    [TestMethod]
    public void DrawSpans8_SinglePixelSpan_SamplesTextureAtOrigin()
    {
        _tex[0] = 42;
        ESpan_t span = new() { U = 0, V = 0, Count = 1, PNext = null };

        DScan.DrawSpans8(&span);

        Assert.AreEqual((byte)42, _viewbuf[0]);
    }

    [TestMethod]
    public void DrawSpans8_WalksLinkedSpanList()
    {
        _tex[0] = 42;
        ESpan_t second = new() { U = 0, V = 1, Count = 1, PNext = null };
        ESpan_t first = new() { U = 0, V = 0, Count = 1, PNext = &second };

        DScan.DrawSpans8(&first);

        Assert.AreEqual((byte)42, _viewbuf[0]);            // row 0
        Assert.AreEqual((byte)42, _viewbuf[ScreenWidth]);  // row 1
    }

    [TestMethod]
    public void DrawSpans8_SpanLongerThanEightPixels_HitsChunkAndRemainderBranches()
    {
        // Count = 10 spans two inner chunks (8 then 2), exercising both the
        // shift-step branch and the division-remainder branch.
        _tex[0] = 42;
        ESpan_t span = new() { U = 0, V = 0, Count = 10, PNext = null };

        DScan.DrawSpans8(&span);

        for (int i = 0; i < 10; i++)
            Assert.AreEqual((byte)42, _viewbuf[i], $"pixel {i}");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void DrawSpans8_RandomPerspectiveSpans_MatchesScalarReference(int seed)
    {
        Random rng = new(seed);
        for (int i = 0; i < CacheWidth * CacheHeight; i++)
            _tex[i] = (byte)rng.Next(256);

        DLocal.d_sdivzorigin = 5.0f + seed;
        DLocal.d_sdivzstepu = 0.01f;
        DLocal.d_sdivzstepv = 0.02f;
        DLocal.d_tdivzorigin = 3.0f;
        DLocal.d_tdivzstepu = 0.015f * (seed + 1);
        DLocal.d_tdivzstepv = 0.03f;
        DLocal.d_ziorigin = 0.01f;
        DLocal.d_zistepu = 0.0001f;
        DLocal.d_zistepv = 0.0002f;
        DLocal.sadjust = seed * 100;
        DLocal.tadjust = seed * 37;
        DLocal.bbextents = (CacheWidth << 16) - 1;
        DLocal.bbextentt = (CacheHeight << 16) - 1;

        // every length 1..70 (past the 32-pixel vector chunk and the 8-pixel
        // tail), one per row group, at assorted U offsets.
        const int n = 70;
        ESpan_t* spans = NativeMemory.AllocArray<ESpan_t>(n);
        byte* expected = NativeMemory.AllocArrayZeroed<byte>(ScreenWidth * ScreenHeight);
        try
        {
            for (int i = 0; i < n; i++)
            {
                spans[i] = new ESpan_t
                {
                    U = rng.Next(0, ScreenWidth - 70),
                    V = i % ScreenHeight,
                    Count = i + 1,
                    PNext = i + 1 < n ? &spans[i + 1] : null,
                };
            }

            // spans overwrite each other on shared rows; both renderers do so in
            // the same order, so the final buffers still compare exactly.
            DScan.DrawSpans8(spans);
            ReferenceDrawSpans8(spans, expected);

            CollectionAssert.AreEqual(
                new Span<byte>(expected, ScreenWidth * ScreenHeight).ToArray(),
                new Span<byte>(_viewbuf, ScreenWidth * ScreenHeight).ToArray(),
                "DrawSpans8 output differs from the scalar reference");
        }
        finally
        {
            NativeMemory.Free(spans);
            NativeMemory.Free(expected);
        }
    }

    // D_DrawSpans8 (d_scan.c), one pixel at a time. The initial s/t/z math uses
    // fused multiply-add and native float->int conversion, like the engine
    // (an intentional reordering of the original's float math).
    private void ReferenceDrawSpans8(ESpan_t* pspan, byte* viewbuffer)
    {
        byte* pbase = RShared.cacheblock;
        int cachewidth = RShared.cachewidth;

        float sdivz8stepu = DLocal.d_sdivzstepu * 8;
        float tdivz8stepu = DLocal.d_tdivzstepu * 8;
        float zi8stepu = DLocal.d_zistepu * 8;

        do
        {
            byte* pdest = viewbuffer + RShared.screenwidth * pspan->V + pspan->U;
            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = MathF.FusedMultiplyAdd(du, DLocal.d_sdivzstepu, MathF.FusedMultiplyAdd(dv, DLocal.d_sdivzstepv, DLocal.d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, DLocal.d_tdivzstepu, MathF.FusedMultiplyAdd(dv, DLocal.d_tdivzstepv, DLocal.d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, DLocal.d_zistepu, MathF.FusedMultiplyAdd(dv, DLocal.d_zistepv, DLocal.d_ziorigin));
            float z = 0x10000 / zi;

            int s = float.ConvertToIntegerNative<int>(sdivz * z) + DLocal.sadjust;
            s = Math.Clamp(s, 0, DLocal.bbextents);
            int t = float.ConvertToIntegerNative<int>(tdivz * z) + DLocal.tadjust;
            t = Math.Clamp(t, 0, DLocal.bbextentt);

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

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + DLocal.sadjust;
                    if (snext > DLocal.bbextents) snext = DLocal.bbextents;
                    else if (snext < 8) snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + DLocal.tadjust;
                    if (tnext > DLocal.bbextentt) tnext = DLocal.bbextentt;
                    else if (tnext < 8) tnext = 8;

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

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + DLocal.sadjust;
                    if (snext > DLocal.bbextents) snext = DLocal.bbextents;
                    else if (snext < 8) snext = 8;

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + DLocal.tadjust;
                    if (tnext > DLocal.bbextentt) tnext = DLocal.bbextentt;
                    else if (tnext < 8) tnext = 8;

                    if (spancount > 1)
                    {
                        sstep = (snext - s) / (spancount - 1);
                        tstep = (tnext - t) / (spancount - 1);
                    }
                }

                do
                {
                    *pdest++ = *(pbase + (s >> 16) + (t >> 16) * cachewidth);
                    s += sstep;
                    t += tstep;
                } while (--spancount > 0);

                s = snext;
                t = tnext;
            } while (count > 0);
        } while ((pspan = pspan->PNext) != null);
    }
}
