// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

// Compares the per-component scalar bodies these hot paths used before their
// Vector3 rewrites (Original_*) against the engine's current code (Current_*).
// Each Original_* body is a verbatim copy of the replaced code; unit tests pin
// the Current_* results bit-for-bit to the same scalar formulas.

// R_AliasTransformAndProjectFinalVerts (r_alias.c): per-vertex transform of a
// 256-vertex alias frame through aliastransform.
[MemoryDiagnoser]
public unsafe class AliasTransformBenchmarks
{
    #region Setup / Teardown

    private const int NumVerts = 256;
    private TriVertX_t* _verts;
    private StVert_t* _stverts;
    private FinalVert_t* _fv;

    [GlobalSetup]
    public void Setup()
    {
        Random rng = new(42);
        _verts = NativeMemory.AllocArray<TriVertX_t>(NumVerts);
        _stverts = NativeMemory.AllocArray<StVert_t>(NumVerts);
        _fv = NativeMemory.AllocArray<FinalVert_t>(NumVerts);
        for (int i = 0; i < NumVerts; i++)
        {
            _verts[i].V[0] = (byte)rng.Next(256);
            _verts[i].V[1] = (byte)rng.Next(256);
            _verts[i].V[2] = (byte)rng.Next(256);
            _verts[i].LightNormalIndex = (byte)rng.Next(162);
            _stverts[i] = new StVert_t { S = rng.Next(1 << 20), T = rng.Next(1 << 20) };
        }

        const float Scale = 1.0f / ((float)0x8000 * 0x10000);
        for (int i = 0; i < 12; i++)
        {
            RAlias.aliastransform[i] = (float)((rng.NextDouble() * 2) - 1) * Scale;
        }

        RAlias.aliastransform[11] = 500 * Scale;
        RAlias.r_plightvec = Vector3.Normalize(new Vector3(-1, 0.5f, -0.25f));
        RAlias.r_ambientlight = 64;
        RAlias.r_shadelight = 128;
        RMain.aliasxcenter = 160;
        RMain.aliasycenter = 100;
        RAlias.r_apverts = _verts;
        RAlias.r_anumverts = NumVerts;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_verts);
        NativeMemory.Free(_stverts);
        NativeMemory.Free(_fv);
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Original_TransformAndProjectFinalVerts() => Original(_fv, _stverts);

    [Benchmark]
    public void Current_TransformAndProjectFinalVerts() => RAlias.TransformAndProjectFinalVerts(_fv, _stverts);

    private static void Original(FinalVert_t* fv, StVert_t* pstverts)
    {
        float* aliastransform = RAlias.aliastransform;
        TriVertX_t* pverts = RAlias.r_apverts;

        for (int i = 0; i < RAlias.r_anumverts; i++, fv++, pverts++, pstverts++)
        {
            float v0 = pverts->V[0], v1 = pverts->V[1], v2 = pverts->V[2];
            float zi = 1.0f / ((v0 * aliastransform[8]) + (v1 * aliastransform[9]) + (v2 * aliastransform[10]) + aliastransform[11]);

            fv->V[5] = (int)zi;

            fv->V[0] = (int)((((v0 * aliastransform[0]) + (v1 * aliastransform[1]) + (v2 * aliastransform[2]) + aliastransform[3]) * zi) + RMain.aliasxcenter);
            fv->V[1] = (int)((((v0 * aliastransform[4]) + (v1 * aliastransform[5]) + (v2 * aliastransform[6]) + aliastransform[7]) * zi) + RMain.aliasycenter);

            fv->V[2] = pstverts->S;
            fv->V[3] = pstverts->T;
            fv->Flags = pstverts->OnSeam;

            float lightcos = Vector3.Dot(RAlias.r_avertexnormals[pverts->LightNormalIndex], RAlias.r_plightvec);
            int temp = RAlias.r_ambientlight;

            if (lightcos < 0)
            {
                temp += (int)(RAlias.r_shadelight * lightcos);

                if (temp < 0)
                {
                    temp = 0;
                }
            }

            fv->V[4] = temp;
        }
    }
}
