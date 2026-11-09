// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Quake.Engine;

namespace Quake.Benchmarks;

public unsafe class PaletteBenchmarks
{
    #region Setup / Teardown

    private byte* _pal;
    private uint* _a, _b;
    private byte* _out;
    private byte[] _base = new byte[768];

    [GlobalSetup]
    public void Setup()
    {
        _pal = NativeMemory.AllocArrayZeroed<byte>(768 + 16); _a = NativeMemory.AllocArrayZeroed<uint>(256); _b = NativeMemory.AllocArrayZeroed<uint>(256);
        _out = NativeMemory.AllocArrayZeroed<byte>(768);
        Random r = new(5); for (int i = 0; i < 768; i++) { _pal[i] = (byte)r.Next(256); _base[i] = _pal[i]; }
        Orig(); Simd();
        if (!new ReadOnlySpan<uint>(_a, 256).SequenceEqual(new ReadOnlySpan<uint>(_b, 256))) throw new Exception("mismatch");
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_pal); NativeMemory.Free(_a); NativeMemory.Free(_b); NativeMemory.Free(_out); }

    #endregion

    void Orig() { for (int i = 0; i < 256; i++) { byte r = _pal[i * 3], g = _pal[i * 3 + 1], b = _pal[i * 3 + 2]; _a[i] = (uint)(r | (g << 8) | (b << 16) | (0xFFu << 24)); } }

    void Simd()
    {
        System.Runtime.Intrinsics.Vector128<byte> idx = System.Runtime.Intrinsics.Vector128.Create((byte)0, 1, 2, 0x80, 3, 4, 5, 0x80, 6, 7, 8, 0x80, 9, 10, 11, 0x80);
        System.Runtime.Intrinsics.Vector128<uint> alpha = System.Runtime.Intrinsics.Vector128.Create(0xFF000000u);
        int i = 0;
        for (; i < 252; i += 4)
        {
            var v = System.Runtime.Intrinsics.Vector128.Load(_pal + i * 3);
            (System.Runtime.Intrinsics.Vector128.AsUInt32(System.Runtime.Intrinsics.Vector128.Shuffle(v, idx)) | alpha).Store(_b + i);
        }
        for (; i < 256; i++) { byte r = _pal[i * 3], g = _pal[i * 3 + 1], b = _pal[i * 3 + 2]; _b[i] = (uint)(r | (g << 8) | (b << 16) | (0xFFu << 24)); }
    }

    [Benchmark(Baseline = true)] public void SetPaletteOriginal() => Orig();

    [Benchmark] public void SetPaletteShuffle() => Simd();
}
