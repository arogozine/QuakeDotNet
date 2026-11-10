// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// SndMixSimd / RSurfSimd against the original scalar loops (snd_mix.c, r_surf.c).
[TestClass]
public sealed unsafe class SimdKernelTests
{
    private static int[] RandomInts(Random r, int n) => Enumerable.Range(0, n).Select(_ => r.Next(-70000, 70000) * (r.Next(6) == 0 ? 50 : 1)).ToArray();

    [TestMethod]
    public void ScaleClamp16_And_8_MatchScalar_AllLengths()
    {
        Random r = new(1);
        for (int n = 0; n <= 70; n++)
        {
            int[] src = RandomInts(r, n + 1);
            int vol = r.Next(0, 300);
            short[] a = new short[n + 1], b = new short[n + 1];
            byte[] c = new byte[n + 1], d = new byte[n + 1];
            fixed (int* p = src) fixed (short* pb = b) fixed (byte* pd = d)
            {
                for (int i = 0; i < n; i++)
                {
                    int val = (src[i] * vol) >> 8;
                    if (val > 0x7fff)
                    {
                        val = 0x7fff;
                    }
                    else if (val < -0x8000)
                    {
                        val = -0x8000;
                    }

                    a[i] = (short)val;
                    c[i] = (byte)((val >> 8) + 128);
                }
                SndMixSimd.ScaleClamp16(p, pb, n, vol);
                SndMixSimd.ScaleClamp8(p, pd, n, vol);
            }
            CollectionAssert.AreEqual(a, b, $"16-bit n={n}");
            CollectionAssert.AreEqual(c, d, $"8-bit n={n}");
        }
    }

    [TestMethod]
    public void Paint16_And_Paint8_MatchScalar_AllLengths()
    {
        Random r = new(2);
        for (int n = 0; n <= 40; n++)
        {
            short[] s16 = Enumerable.Range(0, n + 1).Select(_ => (short)r.Next(-32768, 32768)).ToArray();
            sbyte[] s8 = Enumerable.Range(0, n + 1).Select(_ => (sbyte)r.Next(-128, 128)).ToArray();
            int lv = r.Next(0, 400), rv = r.Next(0, 400);
            int[] init = RandomInts(r, (2 * n) + 2);
            int[] e16 = (int[])init.Clone(), g16 = (int[])init.Clone(), e8 = (int[])init.Clone(), g8 = (int[])init.Clone();
            int l8 = (Math.Min(lv, 255) >> 3), r8 = (Math.Min(rv, 255) >> 3);
            for (int i = 0; i < n; i++)
            {
                e16[2 * i] += (s16[i] * lv) >> 8; e16[(2 * i) + 1] += (s16[i] * rv) >> 8;
                e8[2 * i] += s8[i] * l8 * 8; e8[(2 * i) + 1] += s8[i] * r8 * 8;   // snd_scaletable[row][j] = (sbyte)j * row * 8
            }
            fixed (short* p16 = s16) fixed (sbyte* p8 = s8) fixed (int* q16 = g16) fixed (int* q8 = g8)
            {
                SndMixSimd.Paint16(q16, p16, n, lv, rv);
                SndMixSimd.Paint8(q8, p8, n, l8 * 8, r8 * 8);
            }
            CollectionAssert.AreEqual(e16, g16, $"paint16 n={n}");
            CollectionAssert.AreEqual(e8, g8, $"paint8 n={n}");
        }
    }

    [TestMethod]
    public void TransferPaintBuffer_Stereo8Bit_WrapsRingBufferLikeOriginal()
    {
        const int samples = 64;
        Dma_t shm = default;
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(samples);
        byte[] expected = new byte[samples];
        try
        {
            shm.Buffer = buf; shm.Samples = samples; shm.SampleBits = 8; shm.Channels = 2;
            SndDma.shm = (Dma_t*)Unsafe.AsPointer(ref shm);
            SndDma.volume->Value = 0.8f;
            Random r = new(3);
            SndDma.paintedtime = 20;   // out_idx = 40, count 40 -> wraps
            int endtime = 40;
            int* pb = (int*)SndMix.paintbuffer;
            for (int i = 0; i < 2 * (endtime - 20); i++)
            {
                pb[i] = r.Next(-60000, 60000);
            }

            int vol = (int)(0.8f * 256);
            int outIdx = (20 * 2) & (samples - 1);
            for (int i = 0; i < 2 * (endtime - 20); i++)
            {
                int val = (pb[i] * vol) >> 8;
                if (val > 0x7fff)
                {
                    val = 0x7fff;
                }
                else if (val < -0x8000)
                {
                    val = -0x8000;
                }

                expected[outIdx] = (byte)((val >> 8) + 128);
                outIdx = (outIdx + 1) & (samples - 1);
            }

            SndMix.TransferPaintBuffer(endtime);

            for (int i = 0; i < samples; i++)
            {
                Assert.AreEqual(expected[i], buf[i], $"byte {i}");
            }
        }
        finally { NativeMemory.Free(buf); }
    }

    [TestMethod]
    public void RSurfSimd_LightmapLoops_MatchScalar_AllSizes()
    {
        Random r = new(4);
        for (int size = 1; size <= 324; size += (size < 40 ? 1 : 37))
        {
            uint[] init = Enumerable.Range(0, size).Select(_ => (uint)r.Next(0, 90000)).ToArray();
            byte[] lm = new byte[size + 1]; r.NextBytes(lm);
            uint scale = (uint)r.Next(0, 600);
            uint[] e = (uint[])init.Clone(), g = (uint[])init.Clone();
            for (int i = 0; i < size; i++)
            {
                e[i] += lm[i] * scale;
            }

            for (int i = 0; i < size; i++) { int t = (int)((255 * 256) - e[i]) >> 2; if (t < 64) { t = 64; } e[i] = (uint)t; }
            fixed (uint* pg = g) fixed (byte* pl = lm)
            {
                RSurfSimd.AddScaled(pg, pl, size, scale);
                RSurfSimd.BoundInvertShift(pg, size, 2);
            }
            CollectionAssert.AreEqual(e, g, $"size {size}");
        }
    }

    [TestMethod]
    public void AddDlightLuxels_MatchesScalar()
    {
        Random r = new(5);
        for (int trial = 0; trial < 200; trial++)
        {
            int smax = r.Next(1, 19), tmax = r.Next(1, 19);
            float l0 = (float)((r.NextDouble() * 300) - 40), l1 = (float)((r.NextDouble() * 300) - 40);
            float rad = (float)((r.NextDouble() * 400) + 50), minl = rad - (float)(r.NextDouble() * rad);
            uint[] e = new uint[324], g = new uint[324];
            for (int i = 0; i < 324; i++)
            {
                e[i] = g[i] = (uint)r.Next(0, 5000);
            }

            for (int t = 0; t < tmax; t++)
            {
                int td = float.ConvertToIntegerNative<int>(l1 - (t * 16)); if (td < 0)
                {
                    td = -td;
                }

                for (int s = 0; s < smax; s++)
                {
                    int sd = float.ConvertToIntegerNative<int>(l0 - (s * 16)); if (sd < 0)
                    {
                        sd = -sd;
                    }

                    float dist = sd > td ? sd + (td >> 1) : td + (sd >> 1);
                    if (dist < minl)
                    {
                        e[(t * smax) + s] += float.ConvertToIntegerNative<uint>((rad - dist) * 256);
                    }
                }
            }
            fixed (uint* pg = g)
            {
                RSurfSimd.AddDlightLuxels(pg, smax, tmax, l0, l1, rad, minl);
            }

            CollectionAssert.AreEqual(e, g, $"trial {trial} {smax}x{tmax}");
        }
    }
}

[TestClass]
public sealed unsafe class PvsSimdTests
{
    [TestMethod]
    public void MarkVisibleLeaves_MatchesPerBitLoop_IgnoringPaddingBits()
    {
        Random r = new(9);
        foreach (int n in new[] { 1, 7, 8, 9, 63, 64, 65, 100, 128, 129, 1000 })
        {
            MLeaf_t* leaves = NativeMemory.AllocArrayZeroed<MLeaf_t>(n + 2);
            MNode_t* nodes = NativeMemory.AllocArrayZeroed<MNode_t>(n + 2);
            int rowBytes = ((n + 7) >> 3) + 16;
            byte* vis = NativeMemory.AllocArrayZeroed<byte>(rowBytes);
            try
            {
                for (int i = 0; i <= n; i++)
                {
                    leaves[i].Parent = &nodes[1 + (i / 3)];
                }

                for (int k = 1; k <= n; k++)
                {
                    nodes[k].Parent = k > 1 ? &nodes[k / 2] : null;
                }

                for (int i = 0; i < rowBytes; i++)
                {
                    vis[i] = (byte)r.Next(256);   // includes bits >= n
                }

                int[] expLeaf = new int[n + 1], expNode = new int[n + 2];
                for (int i = 0; i < n; i++)
                {
                    if ((vis[i >> 3] & (1 << (i & 7))) != 0)
                    {
                        // original: stamp leaf and parents until already stamped
                        MNode_t* node = (MNode_t*)&leaves[i + 1];
                        do { if (node->VisFrame == 5) { break; } node->VisFrame = 5; node = node->Parent; } while (node != null);
                    }
                }

                for (int i = 0; i <= n; i++)
                {
                    expLeaf[i] = leaves[i].VisFrame;
                }

                for (int k = 0; k <= n; k++)
                {
                    expNode[k] = nodes[k].VisFrame;
                }

                for (int i = 0; i <= n; i++)
                {
                    leaves[i].VisFrame = 0;
                }

                for (int k = 0; k <= n; k++)
                {
                    nodes[k].VisFrame = 0;
                }

                RMain.MarkVisibleLeaves(vis, n, leaves, 5);

                for (int i = 0; i <= n; i++)
                {
                    Assert.AreEqual(expLeaf[i], leaves[i].VisFrame, $"n={n} leaf {i}");
                }

                for (int k = 0; k <= n; k++)
                {
                    Assert.AreEqual(expNode[k], nodes[k].VisFrame, $"n={n} node {k}");
                }
            }
            finally { NativeMemory.Free(leaves); NativeMemory.Free(nodes); NativeMemory.Free(vis); }
        }
    }

    [TestMethod]
    public void DrawSimd_OrBytes_MatchesScalar()
    {
        Random r = new(10);
        for (int n = 0; n < 100; n++)
        {
            byte[] a = new byte[n + 1], b = new byte[n + 1]; r.NextBytes(a); r.NextBytes(b);
            byte[] e = (byte[])a.Clone(); for (int i = 0; i < n; i++)
            {
                e[i] |= b[i];
            }

            fixed (byte* pa = a) fixed (byte* pb = b)
            {
                DrawSimd.OrBytes(pa, pb, n);
            }

            CollectionAssert.AreEqual(e, a, $"n={n}");
        }
    }

    [TestMethod]
    public void DecompressVis_ZeroRunsAndLiterals_MatchOriginal()
    {
        Model_t model = default;
        model.NumLeafs = 200;                       // row = 25 bytes
        byte[] comp = { 0x81, 0, 5, 0x02, 0, 1, 0x40, 0, 14, 0x08, 0xFF };   // 1+5+1+1+1+14+... = 25 bytes
        byte[] expected = new byte[25];
        // decode with the original byte-at-a-time loop
        int o = 0, ip = 0;
        do
        {
            if (comp[ip] != 0) { expected[o++] = comp[ip++]; continue; }
            int c = comp[ip + 1]; ip += 2; while (c != 0) { expected[o++] = 0; c--; }
        } while (o < 25);

        fixed (byte* pc = comp)
        {
            byte* res = Model.DecompressVis(pc, &model);
            for (int i = 0; i < 25; i++)
            {
                Assert.AreEqual(expected[i], res[i], $"byte {i}");
            }
        }
    }
}
