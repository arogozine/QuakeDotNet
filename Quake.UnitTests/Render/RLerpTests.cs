// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

// RLerp (r_common.c R_SetupEntityTransform / R_SetupAliasFrame, Mark_V). Touches Cl.time
// and the r_lerp* cvars, so it stays out of the parallel pool like the other Cl* classes.
[TestClass]
public sealed unsafe class RLerpTests
{
    [TestInitialize]
    public void Setup()
    {
        RMain.r_lerpmove->Value = 1;
        RMain.r_lerpmodels->Value = 1;
        Cl.time = 0;
    }

    private static Entity_t StepEntity()
    {
        Entity_t e = default;
        e.LerpFlags = LerpFlags.MOVESTEP;
        return e;
    }

    [TestMethod]
    public void SetupEntityTransform_StepEntity_BlendsOverTenthOfASecond()
    {
        Entity_t e = StepEntity();
        e.LerpFlags |= LerpFlags.RESETMOVE;
        e.Origin = new Vec3_t(0, 0, 0);
        RLerp.SetupEntityTransform(&e); // reset: snaps

        Cl.time = 1.0;
        e.Origin = new Vec3_t(10, 0, 0); // new server position
        RLerp.SetupEntityTransform(&e);
        Assert.AreEqual(0f, e.Origin.X, 0.0001f); // blend 0

        Cl.time = 1.05;
        e.Origin = new Vec3_t(10, 0, 0);
        RLerp.SetupEntityTransform(&e);
        Assert.AreEqual(5f, e.Origin.X, 0.001f);

        Cl.time = 2.0;
        e.Origin = new Vec3_t(10, 0, 0);
        RLerp.SetupEntityTransform(&e);
        Assert.AreEqual(10f, e.Origin.X, 0.0001f); // clamped
    }

    [TestMethod]
    public void SetupEntityTransform_AnglesWrapTheShortWay()
    {
        Entity_t e = StepEntity();
        e.LerpFlags |= LerpFlags.RESETMOVE;
        e.Angles = new Vec3_t(0, 350, 0);
        RLerp.SetupEntityTransform(&e);

        Cl.time = 1.0;
        e.Angles = new Vec3_t(0, 10, 0);
        RLerp.SetupEntityTransform(&e);
        Cl.time = 1.05;
        e.Angles = new Vec3_t(0, 10, 0);
        RLerp.SetupEntityTransform(&e);

        Assert.AreEqual(360f, e.Angles.Y, 0.001f); // 350 -> 370 halfway
    }

    [TestMethod]
    public void SetupEntityTransform_ResetMove_SnapsAndClearsFlag()
    {
        Entity_t e = StepEntity();
        e.LerpFlags |= LerpFlags.RESETMOVE;
        e.Origin = new Vec3_t(500, 0, 0);
        Cl.time = 3.0;
        RLerp.SetupEntityTransform(&e);

        Assert.AreEqual(500f, e.Origin.X, 0.0001f);
        Assert.AreEqual(0, e.LerpFlags & LerpFlags.RESETMOVE);
        Assert.AreEqual(500f, e.PreviousOrigin.X, 0.0001f);
    }

    [TestMethod]
    public void SetupEntityTransform_NonStepEntity_IsLeftAlone()
    {
        Entity_t e = default; // no MOVESTEP
        e.Origin = new Vec3_t(0, 0, 0);
        RLerp.SetupEntityTransform(&e);
        Cl.time = 1.0;
        e.Origin = new Vec3_t(10, 0, 0);
        RLerp.SetupEntityTransform(&e);

        Assert.AreEqual(10f, e.Origin.X, 0.0001f);
    }

    [TestMethod]
    public void SetupEntityTransform_ViewModel_IsNeverMoveLerped()
    {
        Entity_t* v = Cl.viewent;
        v->LerpFlags = LerpFlags.MOVESTEP | LerpFlags.RESETMOVE;
        v->Origin = default;
        RLerp.SetupEntityTransform(v);
        Cl.time = 1.0;
        v->Origin = new Vec3_t(10, 0, 0);
        RLerp.SetupEntityTransform(v);
        Assert.AreEqual(10f, v->Origin.X, 0.0001f);
    }

    [TestMethod]
    public void SetupEntityTransform_LerpMoveOff_DoesNotBlend()
    {
        RMain.r_lerpmove->Value = 0;
        Entity_t e = StepEntity();
        e.LerpFlags |= LerpFlags.RESETMOVE;
        RLerp.SetupEntityTransform(&e);
        Cl.time = 1.0;
        e.Origin = new Vec3_t(10, 0, 0);
        RLerp.SetupEntityTransform(&e);

        Assert.AreEqual(10f, e.Origin.X, 0.0001f);
    }

    // Two single frames whose vertex data sits at offsets 100 and 200 from the header.
    private sealed class Fixture : IDisposable
    {
        public AliasHdr_t* Hdr;
        public Model_t* Model;

        public Fixture()
        {
            Hdr = (AliasHdr_t*)NativeMemory.AllocZeroed((nuint)(sizeof(AliasHdr_t) + 2 * sizeof(MAliasFrameDesc_t)));
            MAliasFrameDesc_t* f = &Hdr->Frames;
            f[0].Type = AliasFrameType_t.Single;
            f[0].Frame = 100;
            f[1].Type = AliasFrameType_t.Single;
            f[1].Frame = 200;
            Model = (Model_t*)NativeMemory.AllocZeroed((nuint)sizeof(Model_t));
        }

        public void Dispose()
        {
            NativeMemory.Free(Hdr);
            NativeMemory.Free(Model);
        }
    }

    [TestMethod]
    public void SetupAliasFrame_PoseChange_StartsBlendFromPreviousPose()
    {
        using Fixture fx = new();
        Entity_t e = default;
        e.Model = fx.Model;
        e.Frame = 0;

        RLerp.SetupAliasFrame(fx.Hdr, 0, &e, out LerpData_t ld); // first sight: reset, no blend
        Assert.AreEqual(100, ld.Pose1);
        Assert.AreEqual(100, ld.Pose2);

        Cl.time = 1.0;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out ld); // frame changed
        Assert.AreEqual(100, ld.Pose1);
        Assert.AreEqual(200, ld.Pose2);
        Assert.AreEqual(0f, ld.Blend, 0.0001f);

        Cl.time = 1.05;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out ld);
        Assert.AreEqual(0.5f, ld.Blend, 0.001f);

        Cl.time = 1.5;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out ld);
        Assert.AreEqual(1f, ld.Blend, 0.0001f);
    }

    [TestMethod]
    public void SetupAliasFrame_ResetAnim2_DefersLerpForTwoFrameChanges()
    {
        using Fixture fx = new();
        Entity_t e = default;
        e.Model = fx.Model;
        RLerp.SetupAliasFrame(fx.Hdr, 0, &e, out _);

        e.LerpFlags |= LerpFlags.RESETANIM | LerpFlags.RESETANIM2;
        Cl.time = 1.0;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out LerpData_t ld); // RESETANIM eats this one
        Assert.AreEqual(200, ld.Pose1);

        Cl.time = 1.1;
        RLerp.SetupAliasFrame(fx.Hdr, 0, &e, out ld); // RESETANIM2 eats this one
        Assert.AreEqual(100, ld.Pose1);
        Assert.AreEqual(100, ld.Pose2);

        Cl.time = 1.2;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out ld); // lerping again
        Assert.AreEqual(100, ld.Pose1);
        Assert.AreEqual(200, ld.Pose2);
    }

    [TestMethod]
    public void SetupAliasFrame_ModelSwap_DoesNotBlendAcrossModels()
    {
        using Fixture fx = new();
        using Fixture other = new();
        Entity_t e = default;
        e.Model = fx.Model;
        RLerp.SetupAliasFrame(fx.Hdr, 0, &e, out _);

        e.Model = other.Model;
        Cl.time = 1.0;
        RLerp.SetupAliasFrame(other.Hdr, 1, &e, out LerpData_t ld);
        Assert.AreEqual(200, ld.Pose1);
        Assert.AreEqual(200, ld.Pose2);
    }

    [TestMethod]
    public void SetupAliasFrame_LerpModelsOff_ReturnsCurrentPoseOnly()
    {
        RMain.r_lerpmodels->Value = 0;
        using Fixture fx = new();
        Entity_t e = default;
        e.Model = fx.Model;
        RLerp.SetupAliasFrame(fx.Hdr, 0, &e, out _);
        Cl.time = 1.0;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out LerpData_t ld);

        Assert.AreEqual(200, ld.Pose1);
        Assert.AreEqual(200, ld.Pose2);
        Assert.AreEqual(1f, ld.Blend, 0.0001f);
    }

    [TestMethod]
    public void SetupAliasFrame_NoLerpModel_OnlyBlendsWhenLerpModelsIsTwo()
    {
        using Fixture fx = new();
        fx.Model->Flags = ModelFlags.MOD_NOLERP;
        Entity_t e = default;
        e.Model = fx.Model;
        RLerp.SetupAliasFrame(fx.Hdr, 0, &e, out _);
        Cl.time = 1.0;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out LerpData_t ld);
        Assert.AreEqual(200, ld.Pose1); // no blend

        RMain.r_lerpmodels->Value = 2;
        Cl.time = 1.0;
        RLerp.SetupAliasFrame(fx.Hdr, 1, &e, out ld);
        Assert.AreEqual(100, ld.Pose1); // blends from the pose before
    }
}
