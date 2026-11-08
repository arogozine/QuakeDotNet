// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Quake.Benchmarks;

public unsafe class BlitScaledBenchmarks
{
    #region Setup / Teardown

    [Params(2, 3)] public int S;
    private const int PW = 64, PH = 24;
    private byte* _src, _a, _b;
    private int Stride => 1024;

    [GlobalSetup]
    public void Setup()
    {
        _src = (byte*)NativeMemory.AllocZeroed(PW * PH); _a = (byte*)NativeMemory.AllocZeroed(1024 * 128); _b = (byte*)NativeMemory.AllocZeroed(1024 * 128);
        Random r = new(2); for (int i = 0; i < PW * PH; i++) _src[i] = (byte)r.Next(256);
        Orig(); Fast();
        if (!new ReadOnlySpan<byte>(_a, 1024 * 128).SequenceEqual(new ReadOnlySpan<byte>(_b, 1024 * 128))) throw new Exception("mismatch");
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_src); NativeMemory.Free(_a); NativeMemory.Free(_b); }

    #endregion

    void Orig()
    {
        int s = S;
        for (int sy = 0; sy < PH; sy++)
        {
            byte* srow = _src + sy * PW;
            for (int k = 0; k < s; k++)
            {
                byte* drow = _a + (sy * s + k) * Stride;
                for (int sx = 0; sx < PW; sx++) { byte p = srow[sx]; int dx = sx * s; for (int j = 0; j < s; j++, dx++) drow[dx] = p; }
            }
        }
    }

    void Fast()
    {
        int s = S;
        for (int sy = 0; sy < PH; sy++)
        {
            byte* srow = _src + sy * PW;
            byte* drow = _b + sy * s * Stride;
            for (int sx = 0, dx = 0; sx < PW; sx++) { byte p = srow[sx]; for (int j = 0; j < s; j++) drow[dx++] = p; }
            for (int k = 1; k < s; k++) Buffer.MemoryCopy(drow, drow + k * Stride, PW * s, PW * s);
        }
    }

    [Benchmark(Baseline = true)] public void Original() => Orig();

    [Benchmark] public void ExpandThenCopyRows() => Fast();
}
