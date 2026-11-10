// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class LightmapBenchmarks
{
    #region Setup / Teardown

    [Params(36, 100, 324)] public int Size;
    private uint* _a, _b;
    private byte* _lm;

    [GlobalSetup]
    public void Setup()
    {
        _a = NativeMemory.AllocArrayZeroed<uint>(324 + 16); _b = NativeMemory.AllocArrayZeroed<uint>(324 + 16); _lm = NativeMemory.AllocArrayZeroed<byte>(324 + 16);
        Random r = new(3); for (int i = 0; i < 324; i++)
        {
            _lm[i] = (byte)r.Next(256);
        }

        for (int i = 0; i < 324; i++)
        {
            _a[i] = _b[i] = (uint)r.Next(0, 60000);
        }

        AddO(); AddN(); BoundO(); BoundN();
        if (!new ReadOnlySpan<uint>(_a, 324).SequenceEqual(new ReadOnlySpan<uint>(_b, 324)))
        {
            throw new Exception("mismatch");
        }
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_a); NativeMemory.Free(_b); NativeMemory.Free(_lm); }

    #endregion

    void AddO() { for (int i = 0; i < Size; i++)
        {
            _a[i] += _lm[i] * 200u;
        }
    }

    void AddN() => RSurfSimd.AddScaled(_b, _lm, Size, 200u);

    void BoundO() { for (int i = 0; i < Size; i++) { int t = (int)((255 * 256) - _a[i]) >> 2; if (t < 64) { t = 64; } _a[i] = (uint)t; } }

    void BoundN() => RSurfSimd.BoundInvertShift(_b, Size, 2);

    [Benchmark(Baseline = true)] public void AddScaled_Original() => AddO();

    [Benchmark] public void AddScaled_Simd() => AddN();

    [Benchmark] public void Bound_Original() => BoundO();

    [Benchmark] public void Bound_Simd() => BoundN();
}
