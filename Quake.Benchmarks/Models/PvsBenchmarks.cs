// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class PvsBenchmarks
{
    #region Setup / Teardown

    [Params(1000, 4000)] public int NumLeafs;
    private byte* _comp, _out1, _out2, _dst1, _dst2, _src;
    private int _row;

    [GlobalSetup]
    public void Setup()
    {
        _row = (NumLeafs + 7) >> 3;
        Random r = new(7);
        byte[] raw = new byte[_row];
        for (int i = 0; i < _row; i++) raw[i] = r.NextDouble() < 0.12 ? (byte)r.Next(1, 256) : (byte)0;
        List<byte> c = new();
        for (int i = 0; i < _row;)
        {
            if (raw[i] != 0) { c.Add(raw[i++]); continue; }
            int run = 0; while (i < _row && raw[i] == 0 && run < 255) { run++; i++; }
            c.Add(0); c.Add((byte)run);
        }
        _comp = (byte*)NativeMemory.AllocZeroed((nuint)(c.Count + 8)); for (int i = 0; i < c.Count; i++) _comp[i] = c[i];
        _out1 = (byte*)NativeMemory.AllocZeroed(65536); _out2 = (byte*)NativeMemory.AllocZeroed(65536);
        _dst1 = (byte*)NativeMemory.AllocZeroed(65536); _dst2 = (byte*)NativeMemory.AllocZeroed(65536); _src = (byte*)NativeMemory.AllocZeroed(65536);
        for (int i = 0; i < _row + 8; i++) _src[i] = (byte)r.Next(256);
        D1(); D2();
        if (!new ReadOnlySpan<byte>(_out1, _row).SequenceEqual(new ReadOnlySpan<byte>(_out2, _row))) throw new Exception("decomp");
        Or1(); Or2();
        if (!new ReadOnlySpan<byte>(_dst1, _row + 8).SequenceEqual(new ReadOnlySpan<byte>(_dst2, _row + 8))) throw new Exception("or");
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_comp); NativeMemory.Free(_out1); NativeMemory.Free(_out2); NativeMemory.Free(_dst1); NativeMemory.Free(_dst2); NativeMemory.Free(_src); }

    #endregion

    void D1()
    {
        byte* @in = _comp, @out = _out1;
        do
        {
            if (*@in != 0) { *@out++ = *@in++; continue; }
            int c = @in[1]; @in += 2;
            while (c != 0) { *@out++ = 0; c--; }
        } while (@out - _out1 < _row);
    }

    void D2()
    {
        byte* @in = _comp, @out = _out2;
        do
        {
            if (*@in != 0) { *@out++ = *@in++; continue; }
            int c = @in[1]; @in += 2;
            if (c != 0) { NativeMemory.Clear(@out, (nuint)c); @out += c; }
        } while (@out - _out2 < _row);
    }

    void Or1() { for (int i = 0; i < _row + 8; i++) _dst1[i] |= _src[i]; }

    void Or2() => Quake.Engine.DrawSimd.OrBytes(_dst2, _src, _row + 8);

    [Benchmark(Baseline = true)] public void Decompress_Original() => D1();

    [Benchmark] public void Decompress_ClearRuns() => D2();

    [Benchmark] public void Or_Original() => Or1();

    [Benchmark] public void Or_Vector() => Or2();
}
