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

// R_DrawParticles (r_part.c): the per-particle origin/velocity integration
// (the part of the loop that changed), over 2048 explode-type particles. The
// engine's DrawParticles also rasterizes and needs full renderer state, so
// both bodies are copies of just the integration step.
[MemoryDiagnoser]
public unsafe class ParticleIntegrationBenchmarks
{
    #region Setup / Teardown

    private const int Count = 2048;
    private Particle_t* _particles;
    private const float Frametime = 1.0f / 72;
    private const float Dvel = 4 * Frametime;
    private const float Grav = Frametime * 800 * 0.05f;

    [GlobalSetup]
    public void Setup()
    {
        Random rng = new(7);
        _particles = NativeMemory.AllocArrayZeroed<Particle_t>(Count);
        for (int i = 0; i < Count; i++)
        {
            _particles[i].Org = new Vector3(rng.Next(-1000, 1000), rng.Next(-1000, 1000), rng.Next(-1000, 1000));
            _particles[i].Vel = new Vector3(rng.Next(-256, 256), rng.Next(-256, 256), rng.Next(-256, 256));
        }
    }

    [GlobalCleanup]
    public void Cleanup() => NativeMemory.Free(_particles);

    #endregion

    [Benchmark(Baseline = true)]
    public void Original_Integrate()
    {
        for (Particle_t* p = _particles, end = _particles + Count; p < end; p++)
        {
            p->Org[0] += p->Vel[0] * Frametime;
            p->Org[1] += p->Vel[1] * Frametime;
            p->Org[2] += p->Vel[2] * Frametime;

            for (int i = 0; i < 3; i++)
                p->Vel[i] += p->Vel[i] * Dvel;
            p->Vel[2] -= Grav;
        }
    }

    [Benchmark]
    public void Current_Integrate()
    {
        for (Particle_t* p = _particles, end = _particles + Count; p < end; p++)
        {
            p->Org += p->Vel * Frametime;

            p->Vel += p->Vel * Dvel;
            p->Vel[2] -= Grav;
        }
    }
}
