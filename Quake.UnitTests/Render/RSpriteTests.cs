// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RSpriteTests
{
    [TestMethod]
    public void RotateSprite_ZeroBeamlength_IsNoOp()
    {
        DIface.r_spritedesc.Vpn = default;
        DIface.r_spritedesc.Vpn[2] = 1;

        RMain.r_entorigin = default;
        RMain.r_entorigin[0] = 5;
        RShared.modelorg = default;
        RShared.modelorg[0] = 7;

        RSprite.RotateSprite(0f);

        Assert.AreEqual(5f, RMain.r_entorigin[0]);
        Assert.AreEqual(7f, RShared.modelorg[0]);
    }

    [TestMethod]
    public void RotateSprite_NonzeroBeamlength_ShiftsOriginAndModelorg()
    {
        DIface.r_spritedesc.Vpn = default;
        DIface.r_spritedesc.Vpn[2] = 1; // vpn = (0,0,1)

        RMain.r_entorigin = default;
        RShared.modelorg = default;

        RSprite.RotateSprite(10f);

        // vec = vpn * -beamlength = (0,0,-10)
        // r_entorigin += vec -> (0,0,-10); modelorg -= vec -> (0,0,10)
        Assert.AreEqual(-10f, RMain.r_entorigin[2], 0.0001f);
        Assert.AreEqual(10f, RShared.modelorg[2], 0.0001f);
    }

    [TestMethod]
    public void ClipSpriteFace_AllVertsInsidePlane_ReturnsSameCountUnchanged()
    {
        RSprite.clip_current = 0;
        float* row0 = RSprite.Row(0);

        // A quad, all vertices at z=10, clipped against a plane requiring z>=0.
        for (int i = 0; i < 4; i++)
        {
            row0[(i * 5) + 0] = i; // x
            row0[(i * 5) + 1] = 0; // y
            row0[(i * 5) + 2] = 10; // z (in front)
            row0[(i * 5) + 3] = 0; // s
            row0[(i * 5) + 4] = 0; // t
        }

        ClipPlane_t plane = default;
        plane.Normal[2] = 1;
        plane.Dist = 0; // accepts z >= 0

        int outcount = RSprite.ClipSpriteFace(4, &plane);

        Assert.AreEqual(4, outcount);
        Assert.AreEqual(1, RSprite.clip_current); // flipped from 0 to 1

        float* row1 = RSprite.Row(1);
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual((float)i, row1[(i * 5) + 0], 0.0001f);
        }
    }

    [TestMethod]
    public void ClipSpriteFace_HalfBehindPlane_ProducesTwoInsertedVertices()
    {
        RSprite.clip_current = 0;
        float* row0 = RSprite.Row(0);

        // Quad with vertices 0,1 in front (z=10) and 2,3 behind (z=-10) the
        // z>=0 plane -- two edges cross the plane, so the clipped winding
        // keeps the 2 front vertices plus 2 newly-interpolated ones.
        float[] zs = { 10, 10, -10, -10 };
        for (int i = 0; i < 4; i++)
        {
            row0[(i * 5) + 0] = i;
            row0[(i * 5) + 1] = 0;
            row0[(i * 5) + 2] = zs[i];
            row0[(i * 5) + 3] = 0;
            row0[(i * 5) + 4] = 0;
        }

        ClipPlane_t plane = default;
        plane.Normal[2] = 1;
        plane.Dist = 0;

        int outcount = RSprite.ClipSpriteFace(4, &plane);

        Assert.AreEqual(4, outcount); // 2 kept + 2 inserted at the crossings
    }

    [TestMethod]
    public void GetSpriteframe_OutOfRangeFrame_FallsBackToFrameZero()
    {
        Entity_t entity = default;
        entity.Frame = 99; // out of range
        RShared.currententity = &entity;

        MSpriteFrame_t frame0 = default;
        frame0.Width = 32;

        MSprite_t sprite = default;
        sprite.NumFrames = 1;
        sprite.Frames.Type = SpriteFrameType_t.Single;
        sprite.Frames.FramePtr = &frame0;

        MSpriteFrame_t* result = RSprite.GetSpriteframe(&sprite);

        Assert.IsTrue(result == &frame0);
    }

    [TestMethod]
    public void DrawSprite_UnknownType_Throws()
    {
        Entity_t entity = default;
        RShared.currententity = &entity;

        MSpriteFrame_t frame0 = default;
        MSprite_t sprite = default;
        sprite.Type = (SpriteType)99; // not any SPR_* constant
        sprite.NumFrames = 1;
        sprite.Frames.Type = SpriteFrameType_t.Single;
        sprite.Frames.FramePtr = &frame0;

        Model_t model = default;
        model.Cache.Data = &sprite;
        entity.Model = &model;

        Assert.ThrowsExactly<QuakeSysErrorException>(() => RSprite.DrawSprite());
    }
}
