// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class CharacterBenchmarks
{
    #region Setup / Teardown

    private byte* _src, _dst;

    [GlobalSetup]
    public void Setup()
    {
        _src = NativeMemory.AllocArrayZeroed<byte>(128 * 8); _dst = NativeMemory.AllocArrayZeroed<byte>(1024 * 8);
        Random r = new(2); for (int i = 0; i < 128 * 8; i++)
        {
            _src[i] = r.Next(2) == 0 ? (byte)0 : (byte)r.Next(1, 255);
        }
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_src); NativeMemory.Free(_dst); }

    #endregion

    [Benchmark(Baseline = true)]
    public void Original()
    {
        byte* source = _src; byte* dest = _dst;
        for (int d = 8; d-- != 0;)
        {
            if (source[0] != 0)
            {
                dest[0] = source[0];
            }

            if (source[1] != 0)
            {
                dest[1] = source[1];
            }

            if (source[2] != 0)
            {
                dest[2] = source[2];
            }

            if (source[3] != 0)
            {
                dest[3] = source[3];
            }

            if (source[4] != 0)
            {
                dest[4] = source[4];
            }

            if (source[5] != 0)
            {
                dest[5] = source[5];
            }

            if (source[6] != 0)
            {
                dest[6] = source[6];
            }

            if (source[7] != 0)
            {
                dest[7] = source[7];
            }

            source += 128; dest += 1024;
        }
    }

    [Benchmark]
    public void Vector()
    {
        byte* source = _src; byte* dest = _dst;
        for (int d = 8; d-- != 0;) { DrawSimd.TransRow8(dest, source, 0); source += 128; dest += 1024; }
    }
}
