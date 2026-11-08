// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

namespace Quake.UnitTests;

using Quake.Engine;

/// <summary>
/// Engine limits raised to Mark V values (Mark_V/quakedef.h engine_limits,
/// bspfile.h, q_sound.h, model.c, pr_exec.c, r_bsp.c).
/// </summary>
[TestClass]
public sealed unsafe class EngineLimitsTests
{
    [TestMethod]
    public void ClientLimitsMatchMarkV()
    {
        Assert.AreEqual(2048, QuakeDef.MAX_MODELS);              // MAX_FITZQUAKE_MODELS
        Assert.AreEqual(2048, QuakeDef.MAX_SOUNDS);              // MAX_FITZQUAKE_SOUNDS
        Assert.AreEqual(128, ClEntities.MAX_DLIGHTS);      // MAX_FITZQUAKE_DLIGHTS
        Assert.AreEqual(4096, ClEntities.MAX_VISEDICTS);   // MAX_MARK_V_VISEDICTS
        Assert.AreEqual(8192, ClEntities.MAX_EFRAGS);      // MAX_MARK_V_EFRAGS
        Assert.AreEqual(512, ClEntities.MAX_STATIC_ENTITIES); // MAX_FITZQUAKE_STATIC_ENTITIES
        Assert.AreEqual(256, ClTEnt.MAX_TEMP_ENTITIES);    // MAX_FITZQUAKE_TEMP_ENTITIES
        Assert.AreEqual(32, ClTEnt.MAX_BEAMS);             // MAX_FITZQUAKE_BEAMS
    }

    [TestMethod]
    public void ServerLimitsMatchMarkV()
    {
        Assert.AreEqual(32000, QuakeDef.MAX_EDICTS);       // MAX_FITZQUAKE_MAX_EDICTS
        Assert.AreEqual(32, EDict_t.MAX_ENT_LEAFS);        // MAX_MARK_V_ENT_LEAFS
        Assert.AreEqual(QuakeDef.MAX_SOUNDS, QuakeDef.MAX_SOUNDS);
        Assert.AreEqual(65536, QuakeDef.MAX_MSGLEN);       // MAX_MARK_V_MSGLEN
        Assert.AreEqual(65536, Net.NET_MAXMESSAGE);        // NET_MARK_V_MAXMESSAGE
        Assert.AreEqual(1024, QuakeDef.MAX_DATAGRAM);            // protocol 15 packet bound (MAX_WINQUAKE_DATAGRAM), unchanged
        Assert.AreEqual(65527, QuakeDef.MAX_MARK_V_DATAGRAM);    // NETFLAG_DATA - 1 - NET_HEADERSIZE
        Assert.AreEqual(1400, QuakeDef.MAX_FITZQUAKE_DATAGRAM_MTU);
        Assert.AreEqual(7998, QuakeDef.MAX_WINQUAKE_SIGNON);
        Assert.AreEqual(65534, QuakeDef.MAX_MARK_V_SIGNON);
        Assert.AreEqual(64, Progs.MAX_STACK_DEPTH);
        Assert.AreEqual(2048, Model.MAX_MOD_KNOWN);
    }

    [TestMethod]
    public void BspLeafLimitMatchesMarkV()
    {
        Assert.AreEqual((32768 * 4) - 1, BspFile.MAX_MAP_LEAFS);
        Assert.AreEqual(BspFile.MAX_MAP_LEAFS / 8, Model.MOD_NOVIS_SIZE);
    }

    [TestMethod]
    public void SoundLimitsMatchMarkV()
    {
        Assert.AreEqual(1024, SndDma.MAX_CHANNELS);
        Assert.AreEqual(128, SndDma.MAX_DYNAMIC_CHANNELS);
    }

    [TestMethod]
    public void Protocol15WireLimitsStayByteSized()
    {
        Assert.AreEqual(256, Protocol.MAX_P15_MODELS);
        Assert.AreEqual(256, Protocol.MAX_P15_SOUNDS);
        Assert.AreEqual(8192, Protocol.MAX_P15_SOUND_ENTITIES);
        Assert.IsTrue(Protocol.MAX_P15_MODELS <= QuakeDef.MAX_MODELS);
        Assert.IsTrue(Protocol.MAX_P15_SOUNDS <= QuakeDef.MAX_SOUNDS);
    }

    [TestMethod]
    public void PushMoveScratchBuffersCoverAllEdicts()
    {
        // last element of each SV_PushMove scratch table must be addressable
        SvPhys.pushMovedEdict[QuakeDef.MAX_EDICTS - 1] = null;
        SvPhys.pushMovedFrom[QuakeDef.MAX_EDICTS - 1] = default;
    }
}
