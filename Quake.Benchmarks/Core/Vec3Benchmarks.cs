// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Vec3_t
{
    public fixed float V[3];

    public float this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        get => V[i];
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        set => V[i] = value;
    }

    public static implicit operator float*(in Vec3_t v)
    {
        fixed (float* p = v.V)
            return p;
    }
}

// Compares a handful of MathLib's Vec3_t vector operations (mathlib.c) against
// equivalent operations on the built-in System.Numerics.Vector3, which is
// typically hardware (SIMD) accelerated.
[MemoryDiagnoser]
public unsafe class Vec3BenchmarksVsVector3
{
    #region Setup / Teardown

    private Vec3_t _a;
    private Vec3_t _b;
    private Vector3 _va;
    private Vector3 _vb;

    [GlobalSetup]
    public void Setup()
    {
        _a = default;
        _a[0] = 3.5f;
        _a[1] = -12.25f;
        _a[2] = 7.0f;

        _b = default;
        _b[0] = -1.5f;
        _b[1] = 4.0f;
        _b[2] = 9.75f;

        _va = new Vector3(_a[0], _a[1], _a[2]);
        _vb = new Vector3(_b[0], _b[1], _b[2]);
    }

    #endregion

    /*
    [Benchmark(Baseline = true)]
    public float MathLib_DotProduct() => MathLib.DotProduct(_a, _b);

    [Benchmark]
    public float Vector3_Dot() => Vector3.Dot(_va, _vb);

    [Benchmark]
    public float MathLib_CrossProduct()
    {
        Vec3_t result = default;
        MathLib.CrossProduct(_a, _b, &result);
        return result[0] + result[1] + result[2];
    }

    [Benchmark]
    public float Vector3_Cross()
    {
        Vector3 result = Vector3.Cross(_va, _vb);
        return result.X + result.Y + result.Z;
    }

    [Benchmark]
    public float MathLib_Length() => MathLib.Length(_a);

    [Benchmark]
    public float Vector3_Length() => _va.Length();

    [Benchmark]
    public float MathLib_VectorNormalize()
    {
        Vec3_t v = _a;
        MathLib.VectorNormalize(&v);
        return v[0] + v[1] + v[2];
    }

    [Benchmark]
    public float Vector3_Normalize()
    {
        Vector3 v = Vector3.Normalize(_va);
        return v.X + v.Y + v.Z;
    }

    [Benchmark]
    public float MathLib_VectorAdd()
    {
        Vec3_t result = default;
        MathLib.VectorAdd(_a, _b, &result);
        return result[0] + result[1] + result[2];
    }

    [Benchmark]
    public float Vector3_Add()
    {
        Vector3 result = _va + _vb;
        return result.X + result.Y + result.Z;
    }
    */

    [Benchmark]
    public float MathLib_VectorSubtract()
    {
        Vec3_t result = default;
        VectorSubtract(_a, _b, &result);
        return result[0] + result[1] + result[2];
    }

    [Benchmark]
    public float Vector3_Subtract()
    {
        Vector3 result = _va - _vb;
        return result.X + result.Y + result.Z;
    }

    public static void VectorSubtract(in Vec3_t a, in Vec3_t b, Vec3_t* c)
    {
        (*c)[0] = a[0] - b[0];
        (*c)[1] = a[1] - b[1];
        (*c)[2] = a[2] - b[2];
    }
}
