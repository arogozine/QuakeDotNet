// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

// Compares the original scalar R_ConcatRotations / R_ConcatTransforms
// (mathlib.c), transcribed line for line, against the row-wise
// Vector3/Vector4 versions in MathLib.
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class ConcatMatrixBenchmarks
{
    #region Setup / Teardown

    private float* _rot1, _rot2, _rotOut;
    private float* _xf1, _xf2, _xfOut;

    [GlobalSetup]
    public void Setup()
    {
        _rot1 = (float*)NativeMemory.AlignedAlloc(9 * sizeof(float), 16);
        _rot2 = (float*)NativeMemory.AlignedAlloc(9 * sizeof(float), 16);
        _rotOut = (float*)NativeMemory.AlignedAlloc(9 * sizeof(float), 16);
        _xf1 = (float*)NativeMemory.AlignedAlloc(12 * sizeof(float), 16);
        _xf2 = (float*)NativeMemory.AlignedAlloc(12 * sizeof(float), 16);
        _xfOut = (float*)NativeMemory.AlignedAlloc(12 * sizeof(float), 16);

        Random rng = new(1);
        for (int i = 0; i < 9; i++)
        {
            _rot1[i] = (float)rng.NextDouble();
            _rot2[i] = (float)rng.NextDouble();
        }
        for (int i = 0; i < 12; i++)
        {
            _xf1[i] = (float)rng.NextDouble();
            _xf2[i] = (float)rng.NextDouble();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.AlignedFree(_rot1);
        NativeMemory.AlignedFree(_rot2);
        NativeMemory.AlignedFree(_rotOut);
        NativeMemory.AlignedFree(_xf1);
        NativeMemory.AlignedFree(_xf2);
        NativeMemory.AlignedFree(_xfOut);
    }

    #endregion

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Rotations")]
    public float ConcatRotations_Original()
    {
        OriginalConcatRotations(_rot1, _rot2, _rotOut);
        return _rotOut[8];
    }

    [Benchmark]
    [BenchmarkCategory("Rotations")]
    public float ConcatRotations_Simd()
    {
        MathLib.R_ConcatRotations(_rot1, _rot2, _rotOut);
        return _rotOut[8];
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Transforms")]
    public float ConcatTransforms_Original()
    {
        OriginalConcatTransforms(_xf1, _xf2, _xfOut);
        return _xfOut[11];
    }

    [Benchmark]
    [BenchmarkCategory("Transforms")]
    public float ConcatTransforms_Simd()
    {
        MathLib.R_ConcatTransforms(_xf1, _xf2, _xfOut);
        return _xfOut[11];
    }

    // R_ConcatRotations (mathlib.c) -- original scalar form
    private static void OriginalConcatRotations(float* in1, float* in2, float* @out)
    {
        @out[(0 * 3) + 0] = (in1[(0 * 3) + 0] * in2[(0 * 3) + 0]) + (in1[(0 * 3) + 1] * in2[(1 * 3) + 0]) + (in1[(0 * 3) + 2] * in2[(2 * 3) + 0]);
        @out[(0 * 3) + 1] = (in1[(0 * 3) + 0] * in2[(0 * 3) + 1]) + (in1[(0 * 3) + 1] * in2[(1 * 3) + 1]) + (in1[(0 * 3) + 2] * in2[(2 * 3) + 1]);
        @out[(0 * 3) + 2] = (in1[(0 * 3) + 0] * in2[(0 * 3) + 2]) + (in1[(0 * 3) + 1] * in2[(1 * 3) + 2]) + (in1[(0 * 3) + 2] * in2[(2 * 3) + 2]);

        @out[(1 * 3) + 0] = (in1[(1 * 3) + 0] * in2[(0 * 3) + 0]) + (in1[(1 * 3) + 1] * in2[(1 * 3) + 0]) + (in1[(1 * 3) + 2] * in2[(2 * 3) + 0]);
        @out[(1 * 3) + 1] = (in1[(1 * 3) + 0] * in2[(0 * 3) + 1]) + (in1[(1 * 3) + 1] * in2[(1 * 3) + 1]) + (in1[(1 * 3) + 2] * in2[(2 * 3) + 1]);
        @out[(1 * 3) + 2] = (in1[(1 * 3) + 0] * in2[(0 * 3) + 2]) + (in1[(1 * 3) + 1] * in2[(1 * 3) + 2]) + (in1[(1 * 3) + 2] * in2[(2 * 3) + 2]);

        @out[(2 * 3) + 0] = (in1[(2 * 3) + 0] * in2[(0 * 3) + 0]) + (in1[(2 * 3) + 1] * in2[(1 * 3) + 0]) + (in1[(2 * 3) + 2] * in2[(2 * 3) + 0]);
        @out[(2 * 3) + 1] = (in1[(2 * 3) + 0] * in2[(0 * 3) + 1]) + (in1[(2 * 3) + 1] * in2[(1 * 3) + 1]) + (in1[(2 * 3) + 2] * in2[(2 * 3) + 1]);
        @out[(2 * 3) + 2] = (in1[(2 * 3) + 0] * in2[(0 * 3) + 2]) + (in1[(2 * 3) + 1] * in2[(1 * 3) + 2]) + (in1[(2 * 3) + 2] * in2[(2 * 3) + 2]);
    }

    // R_ConcatTransforms (mathlib.c) -- original scalar form
    private static void OriginalConcatTransforms(float* in1, float* in2, float* @out)
    {
        @out[(0 * 4) + 0] = (in1[(0 * 4) + 0] * in2[(0 * 4) + 0]) + (in1[(0 * 4) + 1] * in2[(1 * 4) + 0]) + (in1[(0 * 4) + 2] * in2[(2 * 4) + 0]);
        @out[(0 * 4) + 1] = (in1[(0 * 4) + 0] * in2[(0 * 4) + 1]) + (in1[(0 * 4) + 1] * in2[(1 * 4) + 1]) + (in1[(0 * 4) + 2] * in2[(2 * 4) + 1]);
        @out[(0 * 4) + 2] = (in1[(0 * 4) + 0] * in2[(0 * 4) + 2]) + (in1[(0 * 4) + 1] * in2[(1 * 4) + 2]) + (in1[(0 * 4) + 2] * in2[(2 * 4) + 2]);
        @out[(0 * 4) + 3] = (in1[(0 * 4) + 0] * in2[(0 * 4) + 3]) + (in1[(0 * 4) + 1] * in2[(1 * 4) + 3]) + (in1[(0 * 4) + 2] * in2[(2 * 4) + 3]) + in1[(0 * 4) + 3];
        @out[(1 * 4) + 0] = (in1[(1 * 4) + 0] * in2[(0 * 4) + 0]) + (in1[(1 * 4) + 1] * in2[(1 * 4) + 0]) + (in1[(1 * 4) + 2] * in2[(2 * 4) + 0]);
        @out[(1 * 4) + 1] = (in1[(1 * 4) + 0] * in2[(0 * 4) + 1]) + (in1[(1 * 4) + 1] * in2[(1 * 4) + 1]) + (in1[(1 * 4) + 2] * in2[(2 * 4) + 1]);
        @out[(1 * 4) + 2] = (in1[(1 * 4) + 0] * in2[(0 * 4) + 2]) + (in1[(1 * 4) + 1] * in2[(1 * 4) + 2]) + (in1[(1 * 4) + 2] * in2[(2 * 4) + 2]);
        @out[(1 * 4) + 3] = (in1[(1 * 4) + 0] * in2[(0 * 4) + 3]) + (in1[(1 * 4) + 1] * in2[(1 * 4) + 3]) + (in1[(1 * 4) + 2] * in2[(2 * 4) + 3]) + in1[(1 * 4) + 3];
        @out[(2 * 4) + 0] = (in1[(2 * 4) + 0] * in2[(0 * 4) + 0]) + (in1[(2 * 4) + 1] * in2[(1 * 4) + 0]) + (in1[(2 * 4) + 2] * in2[(2 * 4) + 0]);
        @out[(2 * 4) + 1] = (in1[(2 * 4) + 0] * in2[(0 * 4) + 1]) + (in1[(2 * 4) + 1] * in2[(1 * 4) + 1]) + (in1[(2 * 4) + 2] * in2[(2 * 4) + 1]);
        @out[(2 * 4) + 2] = (in1[(2 * 4) + 0] * in2[(0 * 4) + 2]) + (in1[(2 * 4) + 1] * in2[(1 * 4) + 2]) + (in1[(2 * 4) + 2] * in2[(2 * 4) + 2]);
        @out[(2 * 4) + 3] = (in1[(2 * 4) + 0] * in2[(0 * 4) + 3]) + (in1[(2 * 4) + 1] * in2[(1 * 4) + 3]) + (in1[(2 * 4) + 2] * in2[(2 * 4) + 3]) + in1[(2 * 4) + 3];
    }
}
