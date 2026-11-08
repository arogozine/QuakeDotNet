// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using BenchmarkDotNet.Attributes;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

// Vector3 indexer (v[i]) vs. .X/.Y/.Z, on the shape of RAlias.ProjectFinalVert
// (R_AliasProjectFinalVert): float reads from a Vector3* plus a projection.
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 1)]
public unsafe class Vector3AccessBenchmarks
{
    private const int Count = 4096;
    private Vector3* _av = null!;
    private int* _out = null!;
    private float _xscale = 160f, _yscale = -160f, _xcenter = 160f, _ycenter = 100f;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        _av = (Vector3*)NativeMemory.AlignedAlloc((nuint)(Count * sizeof(Vector3)), 16);
        _out = (int*)NativeMemory.AlignedAlloc((nuint)(Count * 2 * sizeof(int)), 16);
        for (int i = 0; i < Count; i++)
            _av[i] = new Vector3(rng.NextSingle() * 200 - 100, rng.NextSingle() * 200 - 100, rng.NextSingle() * 200 + 5);

        Indexer();
        int[] a = new Span<int>(_out, Count * 2).ToArray();
        Fields();
        if (!a.AsSpan().SequenceEqual(new Span<int>(_out, Count * 2)))
            throw new InvalidOperationException("Indexer and field variants disagree");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.AlignedFree(_av);
        NativeMemory.AlignedFree(_out);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProjectIndexer(Vector3* av, int* o, float xs, float ys, float xc, float yc)
    {
        float zi = 1.0f / (*av)[2];
        o[0] = float.ConvertToIntegerNative<int>((*av)[0] * xs * zi + xc);
        o[1] = float.ConvertToIntegerNative<int>((*av)[1] * ys * zi + yc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProjectFields(Vector3* av, int* o, float xs, float ys, float xc, float yc)
    {
        float zi = 1.0f / av->Z;
        o[0] = float.ConvertToIntegerNative<int>(av->X * xs * zi + xc);
        o[1] = float.ConvertToIntegerNative<int>(av->Y * ys * zi + yc);
    }

    [Benchmark(Baseline = true)]
    public void Indexer()
    {
        for (int i = 0; i < Count; i++)
            ProjectIndexer(&_av[i], &_out[i * 2], _xscale, _yscale, _xcenter, _ycenter);
    }

    [Benchmark]
    public void Fields()
    {
        for (int i = 0; i < Count; i++)
            ProjectFields(&_av[i], &_out[i * 2], _xscale, _yscale, _xcenter, _ycenter);
    }
}
