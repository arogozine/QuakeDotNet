// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Quake.Engine;

namespace Quake.Benchmarks;

// D_WarpScreen main loop: row[turb[u]][col[u]] vs offset tables + 2 gathers / 8 px.
public unsafe class WarpScreenBenchmarks
{
    #region Setup / Teardown

    [Params(320, 800)] public int W;
    private int H => W * 5 / 8;
    private const int Amp2 = 3, Stride = 1024;
    private byte* _view, _a, _b;
    private byte** _rowptr;
    private int* _rowoff, _column, _turbBase;

    [GlobalSetup]
    public void Setup()
    {
        int h = H;
        _view = NativeMemory.AllocArrayZeroed<byte>((Stride * (h + (2 * Amp2))) + 16);
        _a = NativeMemory.AllocArrayZeroed<byte>(Stride * h); _b = NativeMemory.AllocArrayZeroed<byte>(Stride * h);
        Random r = new(9); for (int i = 0; i < Stride * (h + (2 * Amp2)); i++)
        {
            _view[i] = (byte)r.Next(256);
        }

        _rowptr = NativeMemory.AllocPointerArray<byte>(h + (2 * Amp2));
        _rowoff = NativeMemory.AllocArrayZeroed<int>(h + (2 * Amp2));
        _column = NativeMemory.AllocArrayZeroed<int>(W + (2 * Amp2));
        _turbBase = NativeMemory.AllocArrayZeroed<int>(2048 + 128);
        for (int i = 0; i < 2048 + 128; i++)
        {
            _turbBase[i] = (int)(Amp2 + (MathF.Sin(i * 3.14159f * 2 / 128) * Amp2));
        }

        for (int v = 0; v < h + (2 * Amp2); v++) { _rowoff[v] = Stride * v; _rowptr[v] = _view + (Stride * v); }
        for (int u = 0; u < W + (2 * Amp2); u++)
        {
            _column[u] = u;
        }

        Orig(); Gather();
        for (int v = 0; v < h; v++)
        {
            if (!new ReadOnlySpan<byte>(_a + (v * Stride), W).SequenceEqual(new ReadOnlySpan<byte>(_b + (v * Stride), W)))
            {
                throw new Exception("mismatch row " + v);
            }
        }
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_view); NativeMemory.Free(_a); NativeMemory.Free(_b); NativeMemory.Free(_rowptr); NativeMemory.Free(_rowoff); NativeMemory.Free(_column); NativeMemory.Free(_turbBase); }

    #endregion

    void Orig()
    {
        int* turb = _turbBase + 17; byte* dest = _a;
        for (int v = 0; v < H; v++, dest += Stride)
        {
            int* col = &_column[turb[v]]; byte** row = &_rowptr[v];
            for (int u = 0; u < W; u += 4)
            {
                dest[u + 0] = row[turb[u + 0]][col[u + 0]]; dest[u + 1] = row[turb[u + 1]][col[u + 1]];
                dest[u + 2] = row[turb[u + 2]][col[u + 2]]; dest[u + 3] = row[turb[u + 3]][col[u + 3]];
            }
        }
    }

    void Gather()
    {
        int* turb = _turbBase + 17; byte* dest = _b;
        Vector256<int> ff = Vector256.Create(0xFF);
        for (int v = 0; v < H; v++, dest += Stride)
        {
            int* col = &_column[turb[v]]; int* roff = &_rowoff[v];
            int u = 0;
            for (; u + 16 <= W; u += 16)
            {
                Vector256<int> t0 = Vector256.Load(turb + u), t1 = Vector256.Load(turb + u + 8);
                Vector256<int> i0 = Avx2.GatherVector256(roff, t0, 4) + Vector256.Load(col + u);
                Vector256<int> i1 = Avx2.GatherVector256(roff, t1, 4) + Vector256.Load(col + u + 8);
                Vector256<int> g0 = Avx2.GatherVector256((int*)_view, i0, 1) & ff;
                Vector256<int> g1 = Avx2.GatherVector256((int*)_view, i1, 1) & ff;
                Vector128<short> s0 = Sse2.PackSignedSaturate(g0.GetLower(), g0.GetUpper());
                Vector128<short> s1 = Sse2.PackSignedSaturate(g1.GetLower(), g1.GetUpper());
                Sse2.Store(dest + u, Sse2.PackUnsignedSaturate(s0, s1));
            }
            for (; u < W; u++)
            {
                dest[u] = _view[roff[turb[u]] + col[u]];
            }
        }
    }

    [Benchmark(Baseline = true)] public void Original() => Orig();

    [Benchmark] public void GatherOffsets() => Gather();
}
