// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DScanTests
{
    private byte* _viewbuf;
    private byte* _tex;

    [TestInitialize]
    public void Setup()
    {
        _viewbuf = NativeMemory.AllocArrayZeroed<byte>(16);
        _tex = NativeMemory.AllocArrayZeroed<byte>(4);
        _tex[0] = 42;

        DLocal.d_viewbuffer = _viewbuf;
        RShared.screenwidth = 8;
        RShared.cacheblock = _tex;
        RShared.cachewidth = 1;

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
    public void Turbulent8_SpanLongerThanSixteenPixels_HitsChunkAndRemainderBranches()
    {
        byte* turbTex = NativeMemory.AllocArrayZeroed<byte>(64 * 64);
        byte* viewbuf = NativeMemory.AllocArrayZeroed<byte>(32);
        try
        {
            turbTex[0] = 7;
            RShared.cacheblock = turbTex;
            RShared.cl_time = 0;
            DLocal.d_viewbuffer = viewbuf;

            // Count = 17 spans two inner chunks (16 then 1), exercising both
            // the shift-step branch and the (spancount == 1, no-op) remainder
            // branch. With zero sintable/gradients every pixel resolves to
            // sturb = tturb = 0, i.e. turbTex[0].
            ESpan_t span = new() { U = 0, V = 0, Count = 17, PNext = null };

            DScan.Turbulent8(&span);

            for (int i = 0; i < 17; i++)
                Assert.AreEqual((byte)7, viewbuf[i]);
        }
        finally
        {
            NativeMemory.Free(turbTex);
            NativeMemory.Free(viewbuf);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Turbulent8_RandomPerspectiveSpans_MatchesScalarReference(int seed)
    {
        const int W = 256, H = 8, N = 70;
        Random rng = new(seed);
        byte* tex = NativeMemory.AllocArrayZeroed<byte>(64 * 64 + 4);
        byte* view = NativeMemory.AllocArrayZeroed<byte>(W * H);
        byte* expected = NativeMemory.AllocArrayZeroed<byte>(W * H);
        ESpan_t* spans = NativeMemory.AllocArray<ESpan_t>(N);
        try
        {
            for (int i = 0; i < 64 * 64; i++) tex[i] = (byte)rng.Next(256);
            const float AMP = 8 * 0x10000;
            for (int i = 0; i < DIface.CYCLE * 2; i++)
                RShared.sintable[i] = (int)(AMP + MathF.Sin(i * MathF.PI * 2 / DIface.CYCLE) * AMP);

            RShared.cacheblock = tex;
            RShared.cl_time = 1.5f + seed;
            RShared.screenwidth = W;
            DLocal.d_sdivzorigin = 3.0f + seed; DLocal.d_sdivzstepu = 0.02f; DLocal.d_sdivzstepv = 0.013f;
            DLocal.d_tdivzorigin = 2.0f; DLocal.d_tdivzstepu = 0.011f * (seed + 1); DLocal.d_tdivzstepv = 0.017f;
            DLocal.d_ziorigin = 0.01f; DLocal.d_zistepu = 0.0001f; DLocal.d_zistepv = 0.0002f;
            DLocal.sadjust = seed * 100; DLocal.tadjust = seed * 37;
            DLocal.bbextents = (64 << 16) - 1; DLocal.bbextentt = (64 << 16) - 1;

            for (int i = 0; i < N; i++)
                spans[i] = new ESpan_t { U = rng.Next(0, W - N), V = i % H, Count = i + 1, PNext = i + 1 < N ? &spans[i + 1] : null };

            DLocal.d_viewbuffer = view;
            DScan.Turbulent8(spans);

            ReferenceTurbulent8(spans, expected, W);

            CollectionAssert.AreEqual(
                new Span<byte>(expected, W * H).ToArray(),
                new Span<byte>(view, W * H).ToArray());
        }
        finally
        {
            NativeMemory.Free(tex); NativeMemory.Free(view); NativeMemory.Free(expected); NativeMemory.Free(spans);
        }
    }

    // Turbulent8 (d_scan.c), one pixel at a time, with the engine's FMA / native conversion.
    private static void ReferenceTurbulent8(ESpan_t* pspan, byte* viewbuffer, int screenwidth)
    {
        int* turb = RShared.sintable + ((int)(RShared.cl_time * RLocal.SPEED) & (DIface.CYCLE - 1));
        byte* pbase = RShared.cacheblock;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust, bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;
        do
        {
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;
            int count = pspan->Count;
            float du = pspan->U, dv = pspan->V;
            float sdivz = MathF.FusedMultiplyAdd(du, DLocal.d_sdivzstepu, MathF.FusedMultiplyAdd(dv, DLocal.d_sdivzstepv, DLocal.d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, DLocal.d_tdivzstepu, MathF.FusedMultiplyAdd(dv, DLocal.d_tdivzstepv, DLocal.d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, DLocal.d_zistepu, MathF.FusedMultiplyAdd(dv, DLocal.d_zistepv, DLocal.d_ziorigin));
            float z = 0x10000 / zi;
            int s = Math.Clamp(float.ConvertToIntegerNative<int>(sdivz * z) + sadjust, 0, bbextents);
            int t = Math.Clamp(float.ConvertToIntegerNative<int>(tdivz * z) + tadjust, 0, bbextentt);
            int sstep = 0, tstep = 0;
            do
            {
                int spancount = count >= 16 ? 16 : count;
                count -= spancount;
                int snext, tnext;
                if (count != 0)
                {
                    sdivz += DLocal.d_sdivzstepu * 16; tdivz += DLocal.d_tdivzstepu * 16; zi += DLocal.d_zistepu * 16;
                    z = 0x10000 / zi;
                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents) snext = bbextents; else if (snext < 16) snext = 16;
                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt) tnext = bbextentt; else if (tnext < 16) tnext = 16;
                    sstep = (snext - s) >> 4; tstep = (tnext - t) >> 4;
                }
                else
                {
                    float m1 = spancount - 1;
                    sdivz += DLocal.d_sdivzstepu * m1; tdivz += DLocal.d_tdivzstepu * m1; zi += DLocal.d_zistepu * m1;
                    z = 0x10000 / zi;
                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents) snext = bbextents; else if (snext < 16) snext = 16;
                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt) tnext = bbextentt; else if (tnext < 16) tnext = 16;
                    if (spancount > 1) { sstep = (snext - s) / (spancount - 1); tstep = (tnext - t) / (spancount - 1); }
                }
                s &= (DIface.CYCLE << 16) - 1;
                t &= (DIface.CYCLE << 16) - 1;
                do
                {
                    int sturb = ((s + turb[(t >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    int tturb = ((t + turb[(s >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    *pdest++ = pbase[(tturb << 6) + sturb];
                    s += sstep; t += tstep;
                } while (--spancount > 0);
                s = snext; t = tnext;
            } while (count > 0);
        } while ((pspan = pspan->PNext) != null);
    }

    [TestMethod]
    public void DrawZSpans_OddAlignedDest_FixupThenPairedWritesMatchScalarRecurrence()
    {
        short* zbuf = NativeMemory.AllocArrayZeroed<short>(8);
        try
        {
            DLocal.d_pzbuffer = zbuf;
            DLocal.d_zwidth = 1;
            DLocal.d_zistepu = 0;
            DLocal.d_zistepv = 0;
            DLocal.d_ziorigin = 0.01f;

            // U = 1 makes pdest land 2 bytes past a (>=4-byte aligned)
            // allocation, i.e. misaligned -- forces the "(pdest & 0x02) != 0"
            // fixup branch before the paired 2-shorts-per-store loop.
            ESpan_t span = new() { U = 1, V = 0, Count = 3, PNext = null };

            DScan.DrawZSpans(&span);

            double zi = DLocal.d_ziorigin;
            int izi = (int)(zi * 0x8000 * 0x10000);
            short expected = (short)(izi >> 16);

            Assert.AreEqual(expected, zbuf[1]);
            Assert.AreEqual(expected, zbuf[2]);
            Assert.AreEqual(expected, zbuf[3]);
        }
        finally
        {
            NativeMemory.Free(zbuf);
        }
    }

    [TestMethod]
    public void DrawZSpans_LongCount_QuadWordWritesMatchScalarRecurrence()
    {
        // Count = 11 exercises all three write paths in one span: the
        // fixup (U = 1 misaligns pdest), two quadcount (4-shorts-per-store)
        // iterations, one leftover doublecount pair, and a final single --
        // i.e. 1 + 8 + 2 + 0 = 11, since the fixup consumes one before the
        // quad/double/single split of the remaining 10.
        const int count = 11;
        short* zbuf = NativeMemory.AllocArrayZeroed<short>(count + 1);
        try
        {
            DLocal.d_pzbuffer = zbuf;
            DLocal.d_zwidth = 1;
            DLocal.d_zistepu = 0.0001f;
            DLocal.d_zistepv = 0;
            DLocal.d_ziorigin = 0.01f;

            ESpan_t span = new() { U = 1, V = 0, Count = count, PNext = null };

            DScan.DrawZSpans(&span);

            int izistep = (int)(DLocal.d_zistepu * 0x8000 * 0x10000);
            double zi = DLocal.d_ziorigin + span.U * (double)DLocal.d_zistepu;
            int izi = (int)(zi * 0x8000 * 0x10000);

            for (int i = 0; i < count; i++)
            {
                short expected = (short)(izi >> 16);
                Assert.AreEqual(expected, zbuf[1 + i], $"pixel {i}");
                izi += izistep;
            }
        }
        finally
        {
            NativeMemory.Free(zbuf);
        }
    }

    [TestMethod]
    public void DrawZSpans_SecondCallWithDifferentBuffer_IsUnaffectedByFirst()
    {
        // Guards the DLocal.d_pzbuffer/d_zwidth local-caching change: the
        // cache must be scoped to a single call, not leak across calls the
        // way it would if hoisted to a wider scope.
        short* zbufA = NativeMemory.AllocArrayZeroed<short>(4);
        short* zbufB = NativeMemory.AllocArrayZeroed<short>(4);
        try
        {
            DLocal.d_pzbuffer = zbufA;
            DLocal.d_zwidth = 1;
            DLocal.d_zistepu = 0;
            DLocal.d_zistepv = 0;
            DLocal.d_ziorigin = 0.01f;
            ESpan_t spanA = new() { U = 0, V = 0, Count = 2, PNext = null };
            DScan.DrawZSpans(&spanA);

            DLocal.d_pzbuffer = zbufB;
            DLocal.d_zwidth = 1;
            DLocal.d_ziorigin = 0.02f;
            ESpan_t spanB = new() { U = 0, V = 0, Count = 2, PNext = null };
            DScan.DrawZSpans(&spanB);

            short expectedA = (short)((int)(0.01 * 0x8000 * 0x10000) >> 16);
            short expectedB = (short)((int)(0.02 * 0x8000 * 0x10000) >> 16);

            Assert.AreEqual(expectedA, zbufA[0]);
            Assert.AreEqual(expectedB, zbufB[0]);
            Assert.AreNotEqual(zbufA[0], zbufB[0]);
        }
        finally
        {
            NativeMemory.Free(zbufA);
            NativeMemory.Free(zbufB);
        }
    }
}
