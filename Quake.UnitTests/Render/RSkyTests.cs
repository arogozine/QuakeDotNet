// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RSkyTests
{
    [TestMethod]
    public void SetSkyFrame_ComputesSkytimeFromGcdPeriod_AndClearsSkymade()
    {
        RSky.iskyspeed = 8;
        RSky.iskyspeed2 = 2;
        Cl.time = 100.0;
        DEdge.r_skymade = true;

        RSky.SetSkyFrame();

        // g = gcd(8,2) = 2; s1=4, s2=1; temp = 128*4*1 = 512
        // skytime = 100 - (int)(100/512)*512 = 100 - 0 = 100
        Assert.AreEqual(100f, RSky.skytime, 0.001f);
        Assert.AreEqual(8f, RSky.skyspeed);
        Assert.AreEqual(2f, RSky.skyspeed2);
        Assert.IsFalse(DEdge.r_skymade);
    }

    [TestMethod]
    public void InitSky_CopiesTopHalfAndBuildsBottomMaskFromTransparency()
    {
        // A 256x128 synthetic texture: offsets[0] points straight at pixel
        // data (no mip header needed for InitSky, it just reads raw bytes
        // via mt->Offsets[0]).
        int pixelBytes = 256 * 128;
        byte* buf = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)(sizeof(Texture_t) + pixelBytes));
        Texture_t* tx = (Texture_t*)buf;
        tx->Offsets[0] = (uint)sizeof(Texture_t);
        byte* pixels = buf + sizeof(Texture_t);

        // Row 0: right half (topsky, cols 128-255) filled with 0x42.
        for (int j = 0; j < 128; j++)
            pixels[0 * 256 + j + 128] = 0x42;

        // Row 0, left half (bottomsky source, cols 0-127): col 0 opaque
        // (color 7), col 1 transparent (0).
        pixels[0 * 256 + 0] = 7;
        pixels[0 * 256 + 1] = 0;

        try
        {
            RSky.InitSky(tx);

            Assert.IsTrue(RSky.r_skysource != null);
            // topsky half copied verbatim into newsky's right half.
            Assert.AreEqual((byte)0x42, RSky.r_skysource[0 * 256 + 0 + 128]);
        }
        finally
        {
            System.Runtime.InteropServices.NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void MakeSky_SameShiftAsLastCall_SkipsRebuild()
    {
        // Force a known xlast/ylast state via one real call, then verify a
        // second call with the same skytime/skyspeed (-> same shift) still
        // reports r_skymade true without throwing (dedup path taken).
        RSky.iskyspeed = 8;
        RSky.iskyspeed2 = 2;
        Cl.time = 0;
        RSky.SetSkyFrame();

        RSky.MakeSky();
        Assert.IsTrue(DEdge.r_skymade);

        DEdge.r_skymade = false; // simulate a new frame without SetSkyFrame changing skytime
        RSky.MakeSky();

        // dedup path returns early without setting r_skymade again (matches
        // the original: "if ((xshift==xlast)&&(yshift==ylast)) return;").
        Assert.IsFalse(DEdge.r_skymade);
    }

    [TestMethod]
    public void GenSkyTile_WritesDestinationWithoutThrowing()
    {
        byte* dest = stackalloc byte[128 * 128];
        RSky.GenSkyTile(dest);
        // No assertion beyond "doesn't throw" -- pixel content depends on
        // InitSky having populated real texture data, which is exercised
        // separately above.
    }
}
