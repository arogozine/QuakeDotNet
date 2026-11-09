// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class FadeScreenBenchmarks
{
    #region Setup / Teardown

    private const int W = 1024, H = 768;
    private byte* _a, _b;

    [GlobalSetup]
    public void Setup()
    {
        _a = NativeMemory.AllocArray<byte>(W * H); _b = NativeMemory.AllocArray<byte>(W * H);
        Random r = new(1); for (int i = 0; i < W * H; i++) _a[i] = _b[i] = (byte)r.Next(256);
        Orig(_a); New(_b);
        if (!new ReadOnlySpan<byte>(_a, W * H).SequenceEqual(new ReadOnlySpan<byte>(_b, W * H))) throw new Exception("mismatch");
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_a); NativeMemory.Free(_b); }

    #endregion

    static void Orig(byte* buf)
    {
        for (int y = 0; y < H; y++) { byte* pbuf = buf + W * y; int t = (y & 1) << 1; for (int x = 0; x < W; x++) if ((x & 3) != t) pbuf[x] = 0; }
    }

    static void New(byte* buf) { for (int y = 0; y < H; y++) DrawSimd.FadeRow(buf + W * y, W, (y & 1) << 1); }

    [Benchmark(Baseline = true)] public void Original() => Orig(_a);

    [Benchmark] public void Vectorized() => New(_b);
}
