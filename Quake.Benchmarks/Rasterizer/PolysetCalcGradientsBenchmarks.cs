// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace Quake.Benchmarks;

// Compares the original scalar D_PolysetCalcGradients (d_polyse.c),
// transcribed line for line, against DPolyse.PolysetCalcGradients, which
// computes the s/t/light/zi gradients as four Vector128 lanes. Both read the
// same r_p0/r_p1/r_p2/d_xdenom state.
public unsafe class PolysetCalcGradientsBenchmarks
{
    #region Setup / Teardown

    private const int SkinWidth = 296;
    private static int r_lstepx, r_lstepy, r_sstepx, r_sstepy, r_tstepx, r_tstepy, r_zistepx, r_zistepy;
    private static int a_sstepxfrac, a_tstepxfrac, a_ststepxwhole;
    private static readonly Vector128<float> LightLaneMask = Vector128.Create(0, 0, -1, 0).AsSingle();

    [GlobalSetup]
    public void Setup()
    {
        SetVert(DPolyse.r_p0, 120, 40, 37 << 16, 12 << 16, 0x3A00, 0x2400_0000);
        SetVert(DPolyse.r_p1, 160, 70, 80 << 16, 30 << 16, 0x2200, 0x2800_0000);
        SetVert(DPolyse.r_p2, 95, 88, 52 << 16, 61 << 16, 0x5100, 0x2100_0000);

        int* p0 = DPolyse.r_p0, p1 = DPolyse.r_p1, p2 = DPolyse.r_p2;
        // front-facing (negative), as D_DrawNonSubdiv requires: -2670
        DPolyse.d_xdenom = ((p0[1] - p1[1]) * (p0[0] - p2[0])) - ((p0[0] - p1[0]) * (p0[1] - p2[1]));
    }

    #endregion

    private static void SetVert(int* p, int u, int v, int s, int t, int light, int zi)
    {
        p[0] = u; p[1] = v; p[2] = s; p[3] = t; p[4] = light; p[5] = zi;
    }

    [Benchmark(Baseline = true)]
    public int Original()
    {
        OriginalCalcGradients(SkinWidth);
        return a_ststepxwhole;
    }

    [Benchmark]
    public int Simd()
    {
        DPolyse.PolysetCalcGradients(SkinWidth);
        return DPolyse.a_ststepxwhole;
    }

    [Benchmark]
    public int Simd2()
    {
        PolysetCalcGradients2(SkinWidth);
        return DPolyse.a_ststepxwhole;
    }

    internal static void PolysetCalcGradients2(int skinwidthParam)
    {
        int* r_p0 = DPolyse.r_p0, r_p1 = DPolyse.r_p1, r_p2 = DPolyse.r_p2;

        Vector2 p00_01_minus_p20_21 = *(Vector2*)(r_p0) - *(Vector2*)(r_p2);
        Vector2 p10_11_minus_p20_21 = *(Vector2*)(r_p1) - *(Vector2*)(r_p2);

        float p00_minus_p20 = r_p0[0] - r_p2[0];
        float p01_minus_p21 = r_p0[1] - r_p2[1];
        float p10_minus_p20 = r_p1[0] - r_p2[0];
        float p11_minus_p21 = r_p1[1] - r_p2[1];

        float xstepdenominv = 1.0f / DPolyse.d_xdenom;
        float ystepdenominv = -xstepdenominv;

        Vector128<int> p2 = Vector128.Load(r_p2 + 2);
        Vector128<float> t0 = Vector128.ConvertToSingle(Vector128.Load(r_p0 + 2) - p2);
        Vector128<float> t1 = Vector128.ConvertToSingle(Vector128.Load(r_p1 + 2) - p2);

        Vector128<float> stepx = ((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv;
        Vector128<float> stepy = ((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv;

        // ceil () for light so positive steps are exaggerated, negative steps
        // diminished,  pushing us away from underflow toward overflow. Underflow is
        // very visible, overflow is very unlikely, because of ambient lighting
        stepx = Vector128.ConditionalSelect(LightLaneMask, Vector128.Ceiling(stepx), stepx);
        stepy = Vector128.ConditionalSelect(LightLaneMask, Vector128.Ceiling(stepy), stepy);

        Vector128<int> istepx = Vector128.ConvertToInt32Native(stepx);
        Vector128<int> istepy = Vector128.ConvertToInt32Native(stepy);

        r_sstepx = istepx.GetElement(0);
        r_tstepx = istepx.GetElement(1);
        r_lstepx = istepx.GetElement(2);
        r_zistepx = istepx.GetElement(3);

        r_sstepy = istepy.GetElement(0);
        r_tstepy = istepy.GetElement(1);
        r_lstepy = istepy.GetElement(2);
        r_zistepy = istepy.GetElement(3);

        a_sstepxfrac = r_sstepx & 0xFFFF;
        a_tstepxfrac = r_tstepx & 0xFFFF;

        a_ststepxwhole = (skinwidthParam * (r_tstepx >> 16)) + (r_sstepx >> 16);
    }

    // D_PolysetCalcGradients (d_polyse.c) -- original scalar form
    private static void OriginalCalcGradients(int skinwidth)
    {
        int* r_p0 = DPolyse.r_p0, r_p1 = DPolyse.r_p1, r_p2 = DPolyse.r_p2;

        float p00_minus_p20 = r_p0[0] - r_p2[0];
        float p01_minus_p21 = r_p0[1] - r_p2[1];
        float p10_minus_p20 = r_p1[0] - r_p2[0];
        float p11_minus_p21 = r_p1[1] - r_p2[1];

        float xstepdenominv = 1.0f / DPolyse.d_xdenom;
        float ystepdenominv = -xstepdenominv;

        float t0 = r_p0[4] - r_p2[4];
        float t1 = r_p1[4] - r_p2[4];
        r_lstepx = float.ConvertToIntegerNative<int>(MathF.Ceiling(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv));
        r_lstepy = float.ConvertToIntegerNative<int>(MathF.Ceiling(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv));

        t0 = r_p0[2] - r_p2[2];
        t1 = r_p1[2] - r_p2[2];
        r_sstepx = float.ConvertToIntegerNative<int>(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv);
        r_sstepy = float.ConvertToIntegerNative<int>(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv);

        t0 = r_p0[3] - r_p2[3];
        t1 = r_p1[3] - r_p2[3];
        r_tstepx = float.ConvertToIntegerNative<int>(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv);
        r_tstepy = float.ConvertToIntegerNative<int>(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv);

        t0 = r_p0[5] - r_p2[5];
        t1 = r_p1[5] - r_p2[5];
        r_zistepx = float.ConvertToIntegerNative<int>(((t1 * p01_minus_p21) - (t0 * p11_minus_p21)) * xstepdenominv);
        r_zistepy = float.ConvertToIntegerNative<int>(((t1 * p00_minus_p20) - (t0 * p10_minus_p20)) * ystepdenominv);

        a_sstepxfrac = r_sstepx & 0xFFFF;
        a_tstepxfrac = r_tstepx & 0xFFFF;

        a_ststepxwhole = (skinwidth * (r_tstepx >> 16)) + (r_sstepx >> 16);
    }
}
