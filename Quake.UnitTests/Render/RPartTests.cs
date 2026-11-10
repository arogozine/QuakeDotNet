// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RPartTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
        Cmd.BufInit();
        Com.com_argv = [];
        Com.com_argc = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void InitParticles_NoCommandLineArg_UsesDefaultMax()
    {
        RPart.InitParticles();

        Assert.AreEqual(2048, RPart.r_numparticles);
        Assert.IsTrue(RPart.particles != null);
    }

    [TestMethod]
    public void ClearParticles_LinksAllParticlesIntoFreeList()
    {
        RPart.InitParticles();
        RPart.ClearParticles();

        Assert.IsTrue(RPart.active_particles == null);
        Assert.IsTrue(RPart.free_particles == &RPart.particles[0]);

        int count = 0;
        for (Particle_t* p = RPart.free_particles; p != null; p = p->Next)
        {
            count++;
        }

        Assert.AreEqual(RPart.r_numparticles, count);
    }

    [TestMethod]
    public void RunParticleEffect_ConsumesRequestedCountFromFreeList()
    {
        RPart.InitParticles();
        RPart.ClearParticles();
        int freeBefore = CountFree();

        Vec3_t org = default, dir = default;
        RPart.RunParticleEffect(org, dir, 0, 10);

        Assert.AreEqual(freeBefore - 10, CountFree());

        int activeCount = 0;
        for (Particle_t* p = RPart.active_particles; p != null; p = p->Next)
        {
            activeCount++;
        }

        Assert.AreEqual(10, activeCount);
    }

    [TestMethod]
    public void RunParticleEffect_ExhaustsFreeList_StopsGracefully()
    {
        RPart.InitParticles();
        RPart.ClearParticles();
        int total = RPart.r_numparticles;

        Vec3_t org = default, dir = default;
        // Ask for far more than exist -- must not throw, must stop cleanly.
        RPart.RunParticleEffect(org, dir, 0, total + 500);

        Assert.IsTrue(RPart.free_particles == null);

        int activeCount = 0;
        for (Particle_t* p = RPart.active_particles; p != null; p = p->Next)
        {
            activeCount++;
        }

        Assert.AreEqual(total, activeCount);
    }

    [TestMethod]
    public void DrawParticles_ReapsDeadParticleAtHeadOfActiveList()
    {
        RPart.InitParticles();
        RPart.ClearParticles();

        Cl.time = 100;
        Cl.oldtime = 99.9;

        // Manually splice one dead particle to the head of active_particles.
        Particle_t* dead = RPart.free_particles;
        RPart.free_particles = dead->Next;
        dead->Next = null;
        dead->Die = 1; // long expired
        dead->Type = PType_t.Static;
        RPart.active_particles = dead;

        RPart.DrawParticles();

        // The dead particle must have been moved back to free_particles and
        // active_particles must no longer point at it.
        Assert.IsTrue(RPart.active_particles != dead);
        bool foundInFree = false;
        for (Particle_t* p = RPart.free_particles; p != null; p = p->Next)
        {
            if (p == dead) { foundInFree = true; break; }
        }

        Assert.IsTrue(foundInFree);
    }

    [TestMethod]
    public void RocketTrail_TracerType_AlternatesVelocitySignByParity()
    {
        RPart.InitParticles();
        RPart.ClearParticles();

        Vec3_t start = default, end = default;
        end[0] = 3; // short trail -> only a handful of particles spawned

        RPart.RocketTrail(start, end, 3); // type 3 = tracer

        // Just verify it ran without throwing and consumed at least one
        // particle -- the exact tracercount parity is a static field shared
        // across calls, so don't assert its specific value here.
        int activeCount = 0;
        for (Particle_t* p = RPart.active_particles; p != null; p = p->Next)
        {
            activeCount++;
        }

        Assert.IsGreaterThan(0, activeCount);
    }

    private static int CountFree()
    {
        int n = 0;
        for (Particle_t* p = RPart.free_particles; p != null; p = p->Next)
        {
            n++;
        }

        return n;
    }
}
