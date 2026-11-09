// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class SndMixBenchmarks
{
    #region Setup / Teardown

    private const int Pairs = 512;
    private int* _p;
    private short* _o16a, _o16b;
    private byte* _o8a, _o8b;
    private int* _pa, _pb;
    private short* _s16;
    private sbyte* _s8;
    private int* _table;

    [GlobalSetup]
    public void Setup()
    {
        Random r = new(4);
        _p = NativeMemory.AllocArray<int>(Pairs * 2);
        for (int i = 0; i < Pairs * 2; i++) _p[i] = r.Next(-60000, 60000) * (r.Next(8) == 0 ? 40 : 1);
        _o16a = NativeMemory.AllocArrayZeroed<short>(Pairs * 2); _o16b = NativeMemory.AllocArrayZeroed<short>(Pairs * 2);
        _o8a = NativeMemory.AllocArrayZeroed<byte>(Pairs * 2); _o8b = NativeMemory.AllocArrayZeroed<byte>(Pairs * 2);
        _pa = NativeMemory.AllocArrayZeroed<int>(Pairs * 2); _pb = NativeMemory.AllocArrayZeroed<int>(Pairs * 2);
        _s16 = NativeMemory.AllocArray<short>(Pairs); _s8 = NativeMemory.AllocArray<sbyte>(Pairs);
        for (int i = 0; i < Pairs; i++) { _s16[i] = (short)r.Next(-32768, 32768); _s8[i] = (sbyte)r.Next(-128, 128); }
        _table = NativeMemory.AllocArray<int>(32 * 256);
        for (int i = 0; i < 32; i++) for (int j = 0; j < 256; j++) _table[i * 256 + j] = (sbyte)j * i * 8;

        O16(); N16(); O8(); N8();
        if (!new ReadOnlySpan<short>(_o16a, Pairs * 2).SequenceEqual(new ReadOnlySpan<short>(_o16b, Pairs * 2))) throw new Exception("16");
        if (!new ReadOnlySpan<byte>(_o8a, Pairs * 2).SequenceEqual(new ReadOnlySpan<byte>(_o8b, Pairs * 2))) throw new Exception("8");
        new Span<int>(_pa, Pairs * 2).Clear(); new Span<int>(_pb, Pairs * 2).Clear();
        P16o(); P16n();
        if (!new ReadOnlySpan<int>(_pa, Pairs * 2).SequenceEqual(new ReadOnlySpan<int>(_pb, Pairs * 2))) throw new Exception("p16");
        new Span<int>(_pa, Pairs * 2).Clear(); new Span<int>(_pb, Pairs * 2).Clear();
        P8o(); P8n();
        if (!new ReadOnlySpan<int>(_pa, Pairs * 2).SequenceEqual(new ReadOnlySpan<int>(_pb, Pairs * 2))) throw new Exception("p8");
    }

    #endregion

    void O16()
    {
        int vol = 200;
        for (int i = 0; i < Pairs * 2; i++)
        {
            int val = (_p[i] * vol) >> 8;
            if (val > 0x7fff) _o16a[i] = 0x7fff; else if (val < -0x8000) _o16a[i] = -0x8000; else _o16a[i] = (short)val;
        }
    }

    void N16() => SndMixSimd.ScaleClamp16(_p, _o16b, Pairs * 2, 200);

    void O8()
    {
        int vol = 200;
        for (int i = 0; i < Pairs * 2; i++)
        {
            int val = (_p[i] * vol) >> 8;
            if (val > 0x7fff) val = 0x7fff; else if (val < -0x8000) val = -0x8000;
            _o8a[i] = (byte)((val >> 8) + 128);
        }
    }

    void N8() => SndMixSimd.ScaleClamp8(_p, _o8b, Pairs * 2, 200);

    void P16o() { for (int i = 0; i < Pairs; i++) { int d = _s16[i]; _pa[2 * i] += (d * 180) >> 8; _pa[2 * i + 1] += (d * 90) >> 8; } }

    void P16n() => SndMixSimd.Paint16(_pb, _s16, Pairs, 180, 90);

    void P8o()
    {
        int* l = _table + (180 >> 3) * 256, rr = _table + (90 >> 3) * 256;
        for (int i = 0; i < Pairs; i++) { int d = _s8[i]; _pa[2 * i] += l[(byte)d]; _pa[2 * i + 1] += rr[(byte)d]; }
    }

    void P8n() => SndMixSimd.Paint8(_pb, _s8, Pairs, (180 >> 3) * 8, (90 >> 3) * 8);

    [Benchmark(Baseline = true)][BenchmarkCategory("Transfer16")] public void Transfer16_Original() => O16();

    [Benchmark][BenchmarkCategory("Transfer16")] public void Transfer16_Simd() => N16();

    [Benchmark(Baseline = true)][BenchmarkCategory("Transfer8")] public void Transfer8_Original() => O8();

    [Benchmark][BenchmarkCategory("Transfer8")] public void Transfer8_Simd() => N8();

    [Benchmark(Baseline = true)][BenchmarkCategory("Paint16")] public void Paint16_Original() => P16o();

    [Benchmark][BenchmarkCategory("Paint16")] public void Paint16_Simd() => P16n();

    [Benchmark(Baseline = true)][BenchmarkCategory("Paint8")] public void Paint8_Original() => P8o();

    [Benchmark][BenchmarkCategory("Paint8")] public void Paint8_Simd() => P8n();
}
