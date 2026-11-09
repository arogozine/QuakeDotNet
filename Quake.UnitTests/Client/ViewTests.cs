// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class ViewTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void Init_RegistersCvarsAndCommands()
    {
        View.Init();

        Assert.IsTrue(Cmd.Exists(new Utf8String("v_cshift"u8)));
        Assert.IsTrue(Cmd.Exists(new Utf8String("bf"u8)));
        Assert.IsTrue(Cmd.Exists(new Utf8String("centerview"u8)));

        Assert.IsTrue(Cvar.FindVar(new Utf8String("gamma"u8)) != null);
        Assert.AreEqual(1f, View.v_gamma->Value);
        Assert.AreEqual(200f, View.cl_rollspeed->Value);
    }

    [TestMethod]
    public void SetContentsColor_Lava_SetsCshiftContentsSlotToLavaColor()
    {
        View.SetContentsColor((int)Contents.Lava);

        // (int)CShiftType.Contents is slot 0 (client.h)
        Assert.AreEqual(View.cshift_lava.Destcolor[0], Cl.cshifts[0].Destcolor[0]);
        Assert.AreEqual(View.cshift_lava.Destcolor[1], Cl.cshifts[0].Destcolor[1]);
        Assert.AreEqual(View.cshift_lava.Destcolor[2], Cl.cshifts[0].Destcolor[2]);
        Assert.AreEqual(View.cshift_lava.Percent, Cl.cshifts[0].Percent);
    }

    [TestMethod]
    public void SetContentsColor_Empty_SetsCshiftContentsSlotToEmptyColor()
    {
        View.SetContentsColor((int)Contents.Empty);

        Assert.AreEqual(View.cshift_empty.Percent, Cl.cshifts[0].Percent);
    }

    [TestMethod]
    public void SetContentsColor_DefaultsToWaterForAnyOtherContents()
    {
        View.SetContentsColor((int)Contents.Sky);

        Assert.AreEqual(View.cshift_water.Percent, Cl.cshifts[0].Percent);
    }

    [TestMethod]
    public void CalcRoll_ZeroVelocity_ReturnsZero()
    {
        Cvar.cvar_vars = null;
        View.cl_rollangle->Value = 2.0f;
        View.cl_rollspeed->Value = 200f;

        Vec3_t angles = default;
        Vec3_t velocity = default;

        float roll = View.CalcRoll(angles, velocity);

        Assert.AreEqual(0f, roll);
    }

    [TestMethod]
    public void CalcRoll_SidewaysVelocity_ScalesWithSpeed()
    {
        View.cl_rollangle->Value = 2.0f;
        View.cl_rollspeed->Value = 200f;

        Vec3_t angles = default; // facing along +X, right is -Y (Quake convention)
        Vec3_t velocity = default;
        velocity[1] = 100; // half of cl_rollspeed

        float roll = View.CalcRoll(angles, velocity);

        Assert.AreEqual(-1.0f, roll, 0.001f);
    }

    [TestMethod]
    public void AngleDelta_WrapsAboveOneEighty()
    {
        float result = View.AngleDelta(270);
        Assert.AreEqual(-90f, result, 0.001f);
    }

    [TestMethod]
    public void AngleDelta_NoWrapNeeded()
    {
        float result = View.AngleDelta(45);
        Assert.AreEqual(45f, result, 0.001f);
    }

    [TestMethod]
    public void CalcBob_ClampsToUpperBound()
    {
        View.cl_bobcycle->Value = 0.6f;
        View.cl_bobup->Value = 0.5f;
        View.cl_bob->Value = 1.0f; // exaggerate to force clamping

        Cl.time = 0.15; // within first half of cycle
        Cl.velocity[0] = 1000;
        Cl.velocity[1] = 0;

        float bob = View.CalcBob();

        Assert.IsLessThanOrEqualTo(4f, bob);
        Assert.IsGreaterThanOrEqualTo(-7f, bob);
    }

    [TestMethod]
    public void BoundOffsets_ClampsEachAxisToEntityRelativeBox()
    {
        Entity_t* ent = &ClEntities.cl_entities[Cl.viewentity];
        ent->Origin = new Vec3_t(100, 200, 300);

        // below min on X, above max on Y, below min on Z
        RShared.r_refdef->Vieworg = new Vec3_t(0, 999, 0);
        View.BoundOffsets();
        Assert.AreEqual(new Vec3_t(86, 214, 278), RShared.r_refdef->Vieworg);

        // above max on X, below min on Y, above max on Z
        RShared.r_refdef->Vieworg = new Vec3_t(999, 0, 999);
        View.BoundOffsets();
        Assert.AreEqual(new Vec3_t(114, 186, 330), RShared.r_refdef->Vieworg);

        // already inside: unchanged
        RShared.r_refdef->Vieworg = new Vec3_t(105, 195, 310);
        View.BoundOffsets();
        Assert.AreEqual(new Vec3_t(105, 195, 310), RShared.r_refdef->Vieworg);
    }

    [TestMethod]
    public void CalcBob_UsesHorizontalSpeedOnly()
    {
        View.cl_bobcycle->Value = 0.6f;
        View.cl_bobup->Value = 0.5f;
        View.cl_bob->Value = 0.02f;
        Cl.time = 0.15;

        Cl.velocity = new Vec3_t(500, 0, 0);
        float straight = View.CalcBob();

        Cl.velocity = new Vec3_t(300, 400, 999); // horizontal speed is also 500
        float diagonal = View.CalcBob();

        Assert.AreEqual(straight, diagonal, 1e-5f);
    }
}
