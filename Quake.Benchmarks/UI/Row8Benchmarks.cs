// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class Row8Benchmarks
{
    #region Setup / Teardown

    private byte* _src, _d0, _d1, _d2, _d3;

    [GlobalSetup]
    public void Setup()
    {
        _src = NativeMemory.AllocArrayZeroed<byte>(128 * 8); _d0 = NativeMemory.AllocArrayZeroed<byte>(1024 * 8);
        _d1 = NativeMemory.AllocArrayZeroed<byte>(1024 * 8); _d2 = NativeMemory.AllocArrayZeroed<byte>(1024 * 8); _d3 = NativeMemory.AllocArrayZeroed<byte>(1024 * 8);
        Random r = new(2); for (int i = 0; i < 128 * 8; i++)
        {
            _src[i] = r.Next(2) == 0 ? (byte)0 : (byte)r.Next(1, 256);
        }

        Run(_d0, 0); Run(_d1, 1); Run(_d2, 2); Run(_d3, 3);
        for (int i = 1; i <= 3; i++)
        {
            if (!new ReadOnlySpan<byte>(_d0, 8192).SequenceEqual(new ReadOnlySpan<byte>(i == 1 ? _d1 : i == 2 ? _d2 : _d3, 8192)))
            {
                throw new Exception("mismatch " + i);
            }
        }
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_src); NativeMemory.Free(_d0); NativeMemory.Free(_d1); NativeMemory.Free(_d2); NativeMemory.Free(_d3); }

    #endregion

    void Run(byte* dest, int mode)
    {
        byte* source = _src;
        for (int d = 8; d-- != 0; source += 128, dest += 1024)
        {
            if (mode == 0)
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
            }
            else if (mode == 1)
            {
                DrawSimd.TransRow8Swar(dest, source, 0);
            }
            else if (mode == 2)
            {
                DrawSimd.TransRow8V128(dest, source, 0);
            }
            else
            {
                DrawSimd.TransRow(dest, source, 8, 0);
            }
        }
    }

    [Benchmark(Baseline = true)] public void Original() => Run(_d0, 0);

    [Benchmark] public void UlongSwar() => Run(_d1, 1);

    [Benchmark] public void Vector128On8() => Run(_d2, 2);

    [Benchmark] public void CurrentVector64() => Run(_d3, 3);
}
