// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

/// <summary>
/// protocol.h (Mark V) -- PROTOCOL_FITZQUAKE constants and the ENTALPHA_* helpers.
/// </summary>
[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void ProtocolNumbers()
    {
        Assert.AreEqual(15, Protocol.PROTOCOL_NETQUAKE);
        Assert.AreEqual(666, Protocol.PROTOCOL_FITZQUAKE);
    }

    [TestMethod]
    public void FitzEntityUpdateBits()
    {
        Assert.AreEqual(1 << 15, Protocol.U_EXTEND1);
        Assert.AreEqual(1 << 16, Protocol.U_ALPHA);
        Assert.AreEqual(1 << 17, Protocol.U_FRAME2);
        Assert.AreEqual(1 << 18, Protocol.U_MODEL2);
        Assert.AreEqual(1 << 19, Protocol.U_LERPFINISH);
        Assert.AreEqual(1 << 23, Protocol.U_EXTEND2);
        Assert.AreEqual(Protocol.U_NOLERP, Protocol.U_STEP);
    }

    [TestMethod]
    public void FitzClientdataAndSoundBits()
    {
        Assert.AreEqual(1 << 15, Protocol.SU_EXTEND1);
        Assert.AreEqual(1 << 16, Protocol.SU_WEAPON2);
        Assert.AreEqual(1 << 22, Protocol.SU_CELLS2);
        Assert.AreEqual(1 << 23, Protocol.SU_EXTEND2);
        Assert.AreEqual(1 << 24, Protocol.SU_WEAPONFRAME2);
        Assert.AreEqual(1 << 25, Protocol.SU_WEAPONALPHA);
        Assert.AreEqual(1 << 3, Protocol.SND_LARGEENTITY);
        Assert.AreEqual(1 << 4, Protocol.SND_LARGESOUND);
        Assert.AreEqual(1, Protocol.B_LARGEMODEL | 0);
        Assert.AreEqual(4, Protocol.B_ALPHA);
    }

    [TestMethod]
    public void SvcCodes()
    {
        Assert.AreEqual(37, (int)Svc.Skybox);
        Assert.AreEqual(40, (int)Svc.Bf);
        Assert.AreEqual(41, (int)Svc.Fog);
        Assert.AreEqual(42, (int)Svc.SpawnBaseline2);
        Assert.AreEqual(43, (int)Svc.SpawnStatic2);
        Assert.AreEqual(44, (int)Svc.SpawnStaticSound2);
    }

    [TestMethod]
    public void EntAlphaEncodeDecode()
    {
        Assert.AreEqual(Protocol.ENTALPHA_DEFAULT, Protocol.EntAlphaEncode(0));
        Assert.AreEqual(Protocol.ENTALPHA_ONE, Protocol.EntAlphaEncode(1));
        Assert.AreEqual(Protocol.ENTALPHA_ZERO, Protocol.EntAlphaEncode(0.0001f)); // clamps up to 1
        Assert.AreEqual(1.0f, Protocol.EntAlphaDecode(Protocol.ENTALPHA_DEFAULT));
        Assert.AreEqual(1.0f, Protocol.EntAlphaDecode(Protocol.ENTALPHA_ONE));
        Assert.AreEqual(0.0f, Protocol.EntAlphaDecode(Protocol.ENTALPHA_ZERO));
    }
}
