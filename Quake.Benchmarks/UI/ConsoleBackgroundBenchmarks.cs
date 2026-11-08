// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Quake.Benchmarks;

public unsafe class ConsoleBackgroundBenchmarks
{
    #region Setup / Teardown

    [Params(640, 1024, 1920)] public int W;
    private const int Rows = 300;
    private byte* _conback, _a, _b;
    private ushort* _tab;

    [GlobalSetup]
    public void Setup()
    {
        _conback = (byte*)NativeMemory.AllocZeroed(320 * 200 + 8); // +8 slack for gather overread
        Random r = new(2); for (int i = 0; i < 320 * 200; i++) _conback[i] = (byte)r.Next(256);
        _a = (byte*)NativeMemory.AllocZeroed((nuint)(W * Rows)); _b = (byte*)NativeMemory.AllocZeroed((nuint)(W * Rows));
        _tab = (ushort*)NativeMemory.AllocZeroed((nuint)(W * 2));
        int fstep = (int)(320L * 0x10000 / W);
        for (int x = 0; x < W; x++) _tab[x] = (ushort)((x * fstep) >> 16);
        Orig(); Table();
        if (!new ReadOnlySpan<byte>(_a, W * Rows).SequenceEqual(new ReadOnlySpan<byte>(_b, W * Rows))) throw new Exception("mismatch");
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_conback); NativeMemory.Free(_a); NativeMemory.Free(_b); NativeMemory.Free(_tab); }

    #endregion

    void Orig()
    {
        int fstep = (int)(320L * 0x10000 / W);
        for (int y = 0; y < Rows; y++)
        {
            byte* dest = _a + y * W; int v = y * 200 / Rows; byte* src = _conback + v * 320; int f = 0;
            for (int x = 0; x < W; x++) { dest[x] = src[f >> 16]; f += fstep; }
        }
    }

    void Table()
    {
        for (int y = 0; y < Rows; y++)
        {
            byte* dest = _b + y * W; int v = y * 200 / Rows; byte* src = _conback + v * 320;
            for (int x = 0; x < W; x++) dest[x] = src[_tab[x]];
        }
    }

    void TableRepeat()
    {
        int prev = -1;
        for (int y = 0; y < Rows; y++)
        {
            byte* dest = _b + y * W; int v = y * 200 / Rows; byte* src = _conback + v * 320;
            if (v == prev) { Buffer.MemoryCopy(dest - W, dest, W, W); continue; }
            prev = v;
            for (int x = 0; x < W; x++) dest[x] = src[_tab[x]];
        }
    }

    [Benchmark(Baseline = true)] public void Original() => Orig();

    [Benchmark] public void TableLookup() => Table();

    [Benchmark] public void TableLookupPlusRowRepeat() => TableRepeat();
}
