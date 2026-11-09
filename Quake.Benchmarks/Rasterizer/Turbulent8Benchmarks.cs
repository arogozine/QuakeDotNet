// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

// Turbulent8 (d_scan.c's Turbulent8 / D_DrawTurbulent8Span): the classic C port is the
// baseline; Engine is DScan.Turbulent8; further candidates are in Turbulent8Variants.cs.
// Output of every variant is asserted identical to the baseline in Setup.
[MemoryDiagnoser]
public unsafe partial class Turbulent8Benchmarks
{
    #region Setup / Teardown

    private const int ViewWidth = 320;
    private const int ViewHeight = 200;
    private const int SpanCount = 400;
    private byte* _tex;
    private byte* _view;
    private ESpan_t* _spans;
    private const int SPEED = 20;

    [GlobalSetup]
    public void Setup()
    {
        _tex = NativeMemory.AllocArrayZeroed<byte>(64 * 64 + 4); // +4: dword texel gather may overread 3 bytes
        _view = NativeMemory.AllocArrayZeroed<byte>(ViewWidth * ViewHeight);
        _spans = NativeMemory.AllocArray<ESpan_t>(SpanCount);

        Random rng = new(1234);
        for (int i = 0; i < 64 * 64; i++)
            _tex[i] = (byte)rng.Next(256);

        for (int i = 0; i < SpanCount; i++)
        {
            int count = rng.Next(4, 96);
            _spans[i] = new ESpan_t
            {
                U = rng.Next(0, ViewWidth - count),
                V = i % ViewHeight,
                Count = count,
                PNext = i + 1 < SpanCount ? &_spans[i + 1] : null,
            };
        }

        // r_main.c's sintable: AMP + sin(i * 2pi / CYCLE) * AMP, AMP = 8 * 0x10000.
        const float AMP = 8 * 0x10000;
        for (int i = 0; i < DIface.CYCLE * 2; i++)
            RShared.sintable[i] = (int)(AMP + MathF.Sin(i * MathF.PI * 2 / DIface.CYCLE) * AMP);

        RShared.cacheblock = _tex;
        RShared.cl_time = 1.5f;
        DLocal.d_viewbuffer = _view;
        RShared.screenwidth = ViewWidth;

        // Perspective-ish gradients so s/z, t/z, 1/z vary across each span.
        DLocal.d_sdivzorigin = 3.0f;
        DLocal.d_sdivzstepu = 0.02f;
        DLocal.d_sdivzstepv = 0.013f;
        DLocal.d_tdivzorigin = 2.0f;
        DLocal.d_tdivzstepu = 0.011f;
        DLocal.d_tdivzstepv = 0.017f;
        DLocal.d_ziorigin = 0.01f;
        DLocal.d_zistepu = 0.0001f;
        DLocal.d_zistepv = 0.0002f;
        DLocal.sadjust = 0;
        DLocal.tadjust = 0;
        DLocal.bbextents = (64 << 16) - 1;
        DLocal.bbextentt = (64 << 16) - 1;

        // Sanity: all variants must produce identical pixels.
        new Span<byte>(_view, ViewWidth * ViewHeight).Clear();
        Classic();
        byte[] expected = new Span<byte>(_view, ViewWidth * ViewHeight).ToArray();

        foreach (Action variant in new Action[] { Engine, TexGather, VecTail, TexGatherVecTail })
        {
            new Span<byte>(_view, ViewWidth * ViewHeight).Clear();
            variant();
            if (!expected.AsSpan().SequenceEqual(new Span<byte>(_view, ViewWidth * ViewHeight)))
                throw new InvalidOperationException($"{variant.Method.Name} output differs from the classic C port");
        }

        new Span<byte>(_view, ViewWidth * ViewHeight).Clear();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_tex);
        NativeMemory.Free(_view);
        NativeMemory.Free(_spans);
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Classic() => Turbulent8_Classic(&_spans[0]);

    [Benchmark]
    public void Engine() => DScan.Turbulent8(&_spans[0]);
}
