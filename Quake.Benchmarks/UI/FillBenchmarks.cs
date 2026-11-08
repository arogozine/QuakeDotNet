// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Quake.Benchmarks;

public unsafe class FillBenchmarks
{
    #region Setup / Teardown

    [Params(8, 40, 320)] public int W;
    private const int H = 24;
    private byte* _buf;

    [GlobalSetup] public void Setup() => _buf = (byte*)NativeMemory.AllocZeroed(1024 * 64);

    [GlobalCleanup] public void Cleanup() => NativeMemory.Free(_buf);

    #endregion

    [Benchmark(Baseline = true)]
    public void Original() { byte* dest = _buf; for (int v = 0; v < H; v++, dest += 1024) for (int u = 0; u < W; u++) dest[u] = 7; }

    [Benchmark]
    public void SpanFill() { byte* dest = _buf; for (int v = 0; v < H; v++, dest += 1024) new Span<byte>(dest, W).Fill(7); }

    [Benchmark]
    public void InitBlock() { byte* dest = _buf; for (int v = 0; v < H; v++, dest += 1024) System.Runtime.CompilerServices.Unsafe.InitBlockUnaligned(dest, 7, (uint)W); }
}
