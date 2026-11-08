// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using BenchmarkDotNet.Attributes;
using Quake.Engine;

namespace Quake.Benchmarks;

// Compares DEdge.CalcGradients (d_edge.c's D_CalcGradients) as it stands today
// -- reading the s/t texture axes via Unsafe.ReadUnaligned<Vec3_t> at the
// correct 4-float stride -- against the pre-optimization body it replaced,
// which built each axis via "Vec3_t x = default;" plus three indexer writes.
// Everything else in the function is identical between the two paths, so this
// isolates the cost of that one change.
[MemoryDiagnoser]
public unsafe class CalcGradientsBenchmarks
{
    #region Setup / Teardown

    private MSurface_t _surface;
    private MTexInfo_t _texInfo;

    [GlobalSetup]
    public void Setup()
    {
        _texInfo = default;
        _texInfo.Vecs[0] = 0; _texInfo.Vecs[1] = 1; _texInfo.Vecs[2] = 2; _texInfo.Vecs[3] = 3;
        _texInfo.Vecs[4] = 4; _texInfo.Vecs[5] = 5; _texInfo.Vecs[6] = 6; _texInfo.Vecs[7] = 7;

        _surface = default;
        _surface.TextureMins[0] = 0;
        _surface.TextureMins[1] = 0;
        _surface.Extents[0] = 100;
        _surface.Extents[1] = 200;

        // Identity view basis so RMisc.TransformVector is a pure pass-through,
        // matching the unit tests that pin CalcGradients' axis-read behavior.
        RShared.vright = new Vector3(1, 0, 0);
        RShared.vup = new Vector3(0, 1, 0);
        RShared.vpn = new Vector3(0, 0, 1);
        RShared.xscaleinv = 1;
        RShared.yscaleinv = 1;
        RShared.xcenter = 0;
        RShared.ycenter = 0;
        DEdge.transformed_modelorg = default;
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Original_CalcGradients()
    {
        fixed (MTexInfo_t* texInfo = &_texInfo)
        fixed (MSurface_t* surface = &_surface)
        {
            surface->TexInfo = texInfo;
            Original(surface);
        }
    }

    [Benchmark]
    public void Current_CalcGradients()
    {
        fixed (MTexInfo_t* texInfo = &_texInfo)
        fixed (MSurface_t* surface = &_surface)
        {
            surface->TexInfo = texInfo;
            DEdge.CalcGradients(surface);
        }
    }

    // Pre-optimization body of DEdge.CalcGradients (d_edge.c's
    // D_CalcGradients), reproduced here only to benchmark the axis-read
    // change against the current implementation. Assumes miplevel == 0
    // (DEdge.MipLevelForScale's default before it's ever called), matching
    // the state DEdge.CalcGradients itself runs under in this benchmark.
    private static void Original(MSurface_t* pface)
    {
        const int miplevel = 0;
        float mipscale = 1.0f / (1 << miplevel);

        Vector3 sAxisIn = default, tAxisIn = default;
        sAxisIn[0] = pface->TexInfo->Vecs[0];
        sAxisIn[1] = pface->TexInfo->Vecs[1];
        sAxisIn[2] = pface->TexInfo->Vecs[2];

        tAxisIn[0] = pface->TexInfo->Vecs[4];
        tAxisIn[1] = pface->TexInfo->Vecs[5];
        tAxisIn[2] = pface->TexInfo->Vecs[6];

        Vector3 p_saxis = default, p_taxis = default;
        RMisc.TransformVector(sAxisIn, out p_saxis);
        RMisc.TransformVector(tAxisIn, out p_taxis);

        float t = RShared.xscaleinv * mipscale;
        DLocal.d_sdivzstepu = p_saxis[0] * t;
        DLocal.d_tdivzstepu = p_taxis[0] * t;

        t = RShared.yscaleinv * mipscale;
        DLocal.d_sdivzstepv = -p_saxis[1] * t;
        DLocal.d_tdivzstepv = -p_taxis[1] * t;

        DLocal.d_sdivzorigin = p_saxis[2] * mipscale - RShared.xcenter * DLocal.d_sdivzstepu -
                                RShared.ycenter * DLocal.d_sdivzstepv;
        DLocal.d_tdivzorigin = p_taxis[2] * mipscale - RShared.xcenter * DLocal.d_tdivzstepu -
                                RShared.ycenter * DLocal.d_tdivzstepv;

        Vector3 p_temp1 = DEdge.transformed_modelorg * mipscale;

        t = 0x10000 * mipscale;
        DLocal.sadjust = (int)(Vector3.Dot(p_temp1, p_saxis) * 0x10000 + 0.5f)
                         - ((pface->TextureMins[0] << 16) >> miplevel)
                         + (int)(pface->TexInfo->Vecs[3] * t);
        DLocal.tadjust = (int)(Vector3.Dot(p_temp1, p_taxis) * 0x10000 + 0.5f)
                         - ((pface->TextureMins[1] << 16) >> miplevel)
                         + (int)(pface->TexInfo->Vecs[7] * t);

        DLocal.bbextents = ((pface->Extents[0] << 16) >> miplevel) - 1;
        DLocal.bbextentt = ((pface->Extents[1] << 16) >> miplevel) - 1;
    }
}
