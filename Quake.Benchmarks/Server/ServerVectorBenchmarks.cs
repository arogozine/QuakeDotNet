// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Quake.Engine;
using System.Numerics;

namespace Quake.Benchmarks;

// Compares the per-component scalar bodies these hot paths used before their
// Vector3 rewrites (Original_*) against the engine's current code (Current_*).
// Each Original_* body is a verbatim copy of the replaced code; unit tests pin
// the Current_* results bit-for-bit to the same scalar formulas.

// SV_MoveBounds (world.c), SV_ClipVelocity (sv_phys.c) and the
// SV_RecursiveHullCheck midpoint (world.c), each over 1024 random inputs.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class ServerVectorBenchmarks
{
    #region Setup / Teardown

    private const int Count = 1024;
    private Vector3[] _a = null!, _b = null!, _c = null!, _d = null!;
    private float[] _frac = null!;
    private Vector3 _outMins, _outMaxs;

    [GlobalSetup]
    public void Setup()
    {
        Random rng = new(99);
        Vector3 Rand() => new((float)(rng.NextDouble() * 2000 - 1000), (float)(rng.NextDouble() * 2000 - 1000), (float)(rng.NextDouble() * 2000 - 1000));
        _a = new Vector3[Count];
        _b = new Vector3[Count];
        _c = new Vector3[Count];
        _d = new Vector3[Count];
        _frac = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            _a[i] = Rand();
            _b[i] = Rand();
            _c[i] = Vector3.Normalize(Rand());
            _d[i] = Rand() * 0.016f;
            _frac[i] = (float)rng.NextDouble();
        }
    }

    #endregion

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MoveBounds")]
    public float Original_MoveBounds()
    {
        float sink = 0;
        for (int n = 0; n < Count; n++)
        {
            Vector3 start = _a[n], end = _b[n], mins = _d[n], maxs = -_d[n];
            for (int i = 0; i < 3; i++)
            {
                if (end[i] > start[i])
                {
                    _outMins[i] = start[i] + mins[i] - 1;
                    _outMaxs[i] = end[i] + maxs[i] + 1;
                }
                else
                {
                    _outMins[i] = end[i] + mins[i] - 1;
                    _outMaxs[i] = start[i] + maxs[i] + 1;
                }
            }
            sink += _outMins.X + _outMaxs.Z;
        }
        return sink;
    }

    [Benchmark]
    [BenchmarkCategory("MoveBounds")]
    public float Current_MoveBounds()
    {
        float sink = 0;
        for (int n = 0; n < Count; n++)
        {
            World.MoveBounds(_a[n], _d[n], -_d[n], _b[n], ref _outMins, ref _outMaxs);
            sink += _outMins.X + _outMaxs.Z;
        }
        return sink;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ClipVelocity")]
    public float Original_ClipVelocity()
    {
        float sink = 0;
        Vector3 result = default;
        for (int n = 0; n < Count; n++)
        {
            sink += OriginalClipVelocity(_a[n], _c[n], ref result, 1.5f) + result.Y;
        }
        return sink;
    }

    [Benchmark]
    [BenchmarkCategory("ClipVelocity")]
    public float Current_ClipVelocity()
    {
        float sink = 0;
        Vector3 result = default;
        for (int n = 0; n < Count; n++)
        {
            sink += SvPhys.ClipVelocity(_a[n], _c[n], ref result, 1.5f) + result.Y;
        }
        return sink;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("HullMidpoint")]
    public float Original_HullMidpoint()
    {
        float sink = 0;
        for (int n = 0; n < Count; n++)
        {
            Vector3 p1 = _a[n], p2 = _b[n];
            float frac = _frac[n];
            Vector3 mid = default;
            for (int i = 0; i < 3; i++)
                mid[i] = p1[i] + frac * (p2[i] - p1[i]);
            sink += mid.X + mid.Y + mid.Z;
        }
        return sink;
    }

    [Benchmark]
    [BenchmarkCategory("HullMidpoint")]
    public float Current_HullMidpoint()
    {
        float sink = 0;
        for (int n = 0; n < Count; n++)
        {
            Vector3 p1 = _a[n], p2 = _b[n];
            float frac = _frac[n];
            Vector3 mid = p1 + frac * (p2 - p1);
            sink += mid.X + mid.Y + mid.Z;
        }
        return sink;
    }

    private static int OriginalClipVelocity(in Vector3 @in, in Vector3 normal, ref Vector3 @out, float overbounce)
    {
        const float STOP_EPSILON = 0.1f;
        int blocked = 0;
        if (normal[2] > 0)
            blocked |= 1;
        if (normal[2] == 0)
            blocked |= 2;

        float backoff = Vector3.Dot(@in, normal) * overbounce;

        for (int i = 0; i < 3; i++)
        {
            float change = normal[i] * backoff;
            @out[i] = @in[i] - change;
            if (@out[i] > -STOP_EPSILON && @out[i] < STOP_EPSILON)
                @out[i] = 0;
        }

        return blocked;
    }
}
