// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Quake.Engine;

namespace Quake.Benchmarks;

// Compares Q.MemCpy (the ported Q_memcpy from q_shared.c/common.c, which
// picks a 4-byte word-copy loop vs. a scalar byte-copy loop based on
// pointer/count alignment) against Buffer.MemoryCopy, at a size
// (1024x768 bytes) representative of a framebuffer-sized copy in this port.
[MemoryDiagnoser]
public unsafe class MemCpyBenchmarks
{
    #region Setup / Teardown

    private const int Count = 1024 * 768;
    private byte* _src;
    private byte* _dst;

    [GlobalSetup]
    public void Setup()
    {
        _src = (byte*)NativeMemory.Alloc((nuint)Count);
        _dst = (byte*)NativeMemory.Alloc((nuint)Count);

        Random rng = new(1234);
        for (int i = 0; i < Count; i++)
            _src[i] = (byte)rng.Next(256);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_src);
        NativeMemory.Free(_dst);
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Q_MemCpy_Aligned() => MemCpy(_dst, _src, Count);

    [Benchmark]
    public void Buffer_MemoryCopy_Aligned() => Buffer.MemoryCopy(_src, _dst, Count, Count);

    // Same total byte count, but src/dst/count are all offset by 1 so
    // Q.MemCpy's alignment check fails and it falls back to its scalar loop.
    [Benchmark]
    public void Q_MemCpy_Unaligned() => MemCpy(_dst + 1, _src + 1, Count - 1);

    [Benchmark]
    public void Buffer_MemoryCopy_Unaligned() => Buffer.MemoryCopy(_src + 1, _dst + 1, Count - 1, Count - 1);

    public static void MemCpy(void* dest, void* src, int count)
    {
        if ((((long)dest | (long)src | count) & 3) == 0)
        {
            count >>= 2;
            for (int i = 0; i < count; i++)
                ((int*)dest)[i] = ((int*)src)[i];
        }
        else
        {
            for (int i = 0; i < count; i++)
                ((byte*)dest)[i] = ((byte*)src)[i];
        }
    }
}
