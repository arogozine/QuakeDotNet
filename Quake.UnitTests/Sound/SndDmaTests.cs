// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class SndDmaTests
{
    // Static field of an unmanaged struct type: a stable, non-moving address
    // (the runtime never relocates static value-type storage), so SndDma.shm
    // can safely point at it across the whole test's lifetime -- same
    // reasoning as Cache.cs's cache_head sentinel.
    private static Dma_t _shm;

    [TestInitialize]
    public void Setup()
    {
        NativeMemory.Clear(SndDma.channels, (nuint)(SndDma.MAX_CHANNELS * sizeof(Channel_t)));
        SndDma.paintedtime = 0;
        Cl.viewentity = -1; // no channel matches this by default

        _shm = default;
        _shm.Channels = 2;
        SndDma.shm = (Dma_t*)Unsafe.AsPointer(ref _shm);
    }

    [TestMethod]
    public void PickChannel_SameEntityAndChannel_OverridesExistingChannel()
    {
        const int dynBase = BspFile.NUM_AMBIENTS; // first dynamic channel index
        SndDma.channels[dynBase + 2].EntNum = 42;
        SndDma.channels[dynBase + 2].EntChannel = 3;

        Channel_t* picked = SndDma.PickChannel(42, 3);

        Assert.IsTrue(picked == &SndDma.channels[dynBase + 2]);
    }

    [TestMethod]
    public void PickChannel_ProtectsViewentityChannelFromNonViewentitySounds()
    {
        const int dynBase = BspFile.NUM_AMBIENTS;
        Cl.viewentity = 1;

        Sfx_t dummy = default;
        SndDma.channels[dynBase].EntNum = 1; // owned by the view entity
        SndDma.channels[dynBase].Sfx = &dummy;

        // entnum=2 (not the view entity), entchannel=0 (never an override match)
        Channel_t* picked = SndDma.PickChannel(2, 0);

        // channel[dynBase] must be skipped; channel[dynBase+1] is the first
        // eligible fallback (life_left ties go to the first one found).
        Assert.IsTrue(picked == &SndDma.channels[dynBase + 1]);
    }

    [TestMethod]
    public void PickChannel_NoOverrideMatch_PicksChannelWithLeastLifeLeft()
    {
        const int dynBase = BspFile.NUM_AMBIENTS;
        SndDma.paintedtime = 1000;

        // every dynamic channel is a candidate, however many there are, so give
        // them all a later end than the ones under test
        for (int i = 0; i < SndDma.MAX_DYNAMIC_CHANNELS; i++)
            SndDma.channels[dynBase + i].End = 5000;

        int[] ends = [1500, 1300, 1700, 1100, 1600, 1900, 1200, 1400];
        for (int i = 0; i < ends.Length; i++)
            SndDma.channels[dynBase + i].End = ends[i];

        Channel_t* picked = SndDma.PickChannel(99, 0);

        // smallest (End - paintedtime) is index 3 -> End 1100
        Assert.IsTrue(picked == &SndDma.channels[dynBase + 3]);
    }

    [TestMethod]
    public void Spatialize_ViewentityChannel_IsAlwaysFullVolume()
    {
        Cl.viewentity = 5;
        Channel_t ch = default;
        ch.EntNum = 5;
        ch.MasterVol = 200;
        ch.Origin = new Vec3_t { [0] = 999, [1] = 999, [2] = 999 }; // irrelevant

        SndDma.Spatialize(&ch);

        Assert.AreEqual(200, ch.LeftVol);
        Assert.AreEqual(200, ch.RightVol);
    }

    [TestMethod]
    public void Spatialize_StereoChannel_AppliesPanAndDistanceAttenuation()
    {
        Cl.viewentity = -1;
        _shm.Channels = 2;

        SndDma.listener_origin = default;
        SndDma.listener_right = new Vec3_t { [0] = 1, [1] = 0, [2] = 0 };

        Channel_t ch = default;
        ch.EntNum = 7;
        ch.MasterVol = 200;
        ch.Origin = new Vec3_t { [0] = 10, [1] = 0, [2] = 0 }; // straight ahead on the right axis, distance 10
        ch.DistMult = 0.05f; // dist = 10 * 0.05 = 0.5

        SndDma.Spatialize(&ch);

        // dot(listener_right, normalized direction) = 1 -> rscale=2, lscale=0
        // scale = (1 - 0.5) = 0.5 -> right = 0.5*2*200 = 200, left = 0.5*0*200 = 0
        Assert.AreEqual(200, ch.RightVol);
        Assert.AreEqual(0, ch.LeftVol);
    }

    [TestMethod]
    public void Spatialize_MonoChannel_IgnoresPan()
    {
        Cl.viewentity = -1;
        _shm.Channels = 1;

        SndDma.listener_origin = default;
        SndDma.listener_right = new Vec3_t { [0] = 1, [1] = 0, [2] = 0 };

        Channel_t ch = default;
        ch.EntNum = 7;
        ch.MasterVol = 200;
        ch.Origin = new Vec3_t { [0] = 10, [1] = 0, [2] = 0 };
        ch.DistMult = 0.05f;

        SndDma.Spatialize(&ch);

        Assert.AreEqual(100, ch.LeftVol);
        Assert.AreEqual(100, ch.RightVol);
    }
}
