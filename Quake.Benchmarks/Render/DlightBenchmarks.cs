// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class DlightBenchmarks
{
    #region Setup / Teardown

    [Params(4, 10, 18)] public int Smax;
    private uint* _a, _b;
    private const float Local0 = 77.3f, Local1 = 41.9f, Rad = 300f, Min = 240f;

    [GlobalSetup]
    public void Setup()
    {
        _a = NativeMemory.AllocArrayZeroed<uint>(324); _b = NativeMemory.AllocArrayZeroed<uint>(324);
        Orig(); Simd();
        for (int i = 0; i < 324; i++)
        {
            if (_a[i] != _b[i])
            {
                throw new Exception("mismatch " + i);
            }
        }

        bool any = false; for (int i = 0; i < 324; i++)
        {
            any |= _a[i] != 0;
        }

        if (!any)
        {
            throw new Exception("no lights");
        }
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_a); NativeMemory.Free(_b); }

    #endregion

    void Orig()
    {
        int smax = Smax, tmax = Smax;
        for (int t = 0; t < tmax; t++)
        {
            int td = float.ConvertToIntegerNative<int>(Local1 - (t * 16)); if (td < 0)
            {
                td = -td;
            }

            for (int s = 0; s < smax; s++)
            {
                int sd = float.ConvertToIntegerNative<int>(Local0 - (s * 16)); if (sd < 0)
                {
                    sd = -sd;
                }

                float dist; if (sd > td)
                {
                    dist = sd + (td >> 1);
                }
                else
                {
                    dist = td + (sd >> 1);
                }

                if (dist < Min)
                {
                    _a[(t * smax) + s] += float.ConvertToIntegerNative<uint>((Rad - dist) * 256);
                }
            }
        }
    }

    void Simd() => RSurfSimd.AddDlightLuxels(_b, Smax, Smax, Local0, Local1, Rad, Min);

    [Benchmark(Baseline = true)] public void Original() => Orig();

    [Benchmark] public void Vectorized() => Simd();
}
