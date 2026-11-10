// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class TransPicBenchmarks
{
    #region Setup / Teardown

    [Params(24, 64, 320)] public int Width;
    private const int H = 24;
    private byte* _src, _a, _b;

    [GlobalSetup]
    public void Setup()
    {
        _src = NativeMemory.AllocArray<byte>(Width * H); _a = NativeMemory.AllocArrayZeroed<byte>(Width * H); _b = NativeMemory.AllocArrayZeroed<byte>(Width * H);
        Random r = new(1); for (int i = 0; i < Width * H; i++)
        {
            _src[i] = r.Next(3) == 0 ? (byte)255 : (byte)r.Next(255);
        }

        Orig(); New();
        if (!new ReadOnlySpan<byte>(_a, Width * H).SequenceEqual(new ReadOnlySpan<byte>(_b, Width * H)))
        {
            throw new Exception("mismatch");
        }
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_src); NativeMemory.Free(_a); NativeMemory.Free(_b); }

    #endregion

    void Orig()
    {
        for (int v = 0; v < H; v++)
        {
            for (int u = 0; u < Width; u += 8)
            {
                for (int k = 0; k < 8; k++) { byte t = _src[(v * Width) + u + k]; if (t != 255)
                    {
                        _a[(v * Width) + u + k] = t;
                    }
                }
            }
        }
    }

    void New() { for (int v = 0; v < H; v++)
        {
            DrawSimd.TransRow(_b + (v * Width), _src + (v * Width), Width, 255);
        }
    }

    [Benchmark(Baseline = true)] public void Original() => Orig();

    [Benchmark] public void Vectorized() => New();
}
