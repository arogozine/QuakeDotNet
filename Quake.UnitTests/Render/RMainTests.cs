// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RMainTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
        Cmd.BufInit();

        for (int i = 0; i < 4; i++)
        {
            RMain.view_clipplanes[i] = default;
            RMain.screenedge[i] = default;
        }
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void InitTurb_PopulatesSinAndIntSinTables()
    {
        new Span<int>(RShared.sintable, RShared.SIN_BUFFER_SIZE).Clear();
        new Span<int>(RShared.intsintable, RShared.SIN_BUFFER_SIZE).Clear();

        RMain.InitTurb();

        // sintable[0] = AMP + sin(0)*AMP = AMP; intsintable[0] = AMP2 + sin(0)*AMP2 = AMP2
        Assert.AreEqual(RLocal.AMP, RShared.sintable[0]);
        Assert.AreEqual(RLocal.AMP2, RShared.intsintable[0]);

        // not all zero -- the sine wave actually varies across the table
        Assert.AreNotEqual(RShared.sintable[0], RShared.sintable[10]);
    }

    [TestMethod]
    public void SetVrect_ClampsWidthToMinimumAndAppliesViewsize()
    {
        Screen.scr_viewsize->Value = 50; // half size
        Cl.intermission = 0;

        VRect_t vrectin = new() { X = 0, Y = 0, Width = 320, Height = 200 };
        VRect_t vrect = default;

        RMain.SetVrect(&vrectin, &vrect, 0);

        // 320*0.5 = 160, rounded down to a multiple of 8
        Assert.AreEqual(160, vrect.Width);
        Assert.IsGreaterThanOrEqualTo(96, vrect.Width);
    }

    [TestMethod]
    public void SetVrect_IntermissionForcesFullSizeAndZeroLineAdj()
    {
        Screen.scr_viewsize->Value = 50;
        Cl.intermission = 1;

        VRect_t vrectin = new() { X = 0, Y = 0, Width = 320, Height = 200 };
        VRect_t vrect = default;

        RMain.SetVrect(&vrectin, &vrect, 24); // lineadj would normally shrink height

        Assert.AreEqual(320 & ~7, vrect.Width);
        Assert.AreEqual(200 & ~1, vrect.Height); // lineadj zeroed out by intermission
    }

    [TestMethod]
    public void ViewChanged_ComputesScaleAndCenterFromVrect()
    {
        *RShared.r_refdef = default;
        RShared.r_refdef->FovX = 90;
        Screen.scr_fov->Value = 90;
        Screen.scr_viewsize->Value = 100;
        Cl.intermission = 0;

        VRect_t vrectin = new() { X = 0, Y = 0, Width = 320, Height = 200 };

        RMain.ViewChanged(&vrectin, 0, 1.0f);

        Assert.AreEqual(320, RShared.r_refdef->Vrect.Width);
        // horizontalFieldOfView = 2*tan(45deg) = 2.0
        Assert.AreEqual(2.0f, RShared.r_refdef->HorizontalFieldOfView, 0.0001f);
        Assert.AreEqual(160.0f, RShared.xscale, 0.01f); // width / hfov = 320/2
        Assert.IsFalse(RMain.r_fov_greater_than_90);
    }

    [TestMethod]
    public void BmodelCheckBBox_FullyClippedWhenOutsideFrustum()
    {
        // Axis-aligned frustum: view_clipplanes[i].normal picks out a half
        // space; put minmaxs entirely on the negative (rejected) side of
        // plane 0.
        RMain.view_clipplanes[0].Normal[0] = 1;
        RMain.view_clipplanes[0].Normal[1] = 0;
        RMain.view_clipplanes[0].Normal[2] = 0;
        RMain.view_clipplanes[0].Dist = 100; // require x >= 100 to not be rejected

        for (int i = 1; i < 4; i++)
        {
            RMain.view_clipplanes[i].Normal[0] = 0;
            RMain.view_clipplanes[i].Normal[1] = 0;
            RMain.view_clipplanes[i].Normal[2] = 1;
            RMain.view_clipplanes[i].Dist = -1000; // never rejects
        }

        RMisc.SetUpFrustumIndexes();

        Entity_t entity = default; // angles all zero -> takes the frustum-index path
        RShared.currententity = &entity;

        float* minmaxs = stackalloc float[6];
        minmaxs[0] = -10; minmaxs[1] = -10; minmaxs[2] = -10;
        minmaxs[3] = 10; minmaxs[4] = 10; minmaxs[5] = 10;

        Model_t model = default;
        model.Radius = 5;

        int clipflags = RMain.BmodelCheckBBox(&model, minmaxs);

        Assert.AreEqual(RLocal.BMODEL_FULLY_CLIPPED, clipflags);
    }

    [TestMethod]
    public void BmodelCheckBBox_AcceptsBoxStraddlingFrustumPlane()
    {
        RMain.view_clipplanes[0].Normal[0] = 1;
        RMain.view_clipplanes[0].Normal[1] = 0;
        RMain.view_clipplanes[0].Normal[2] = 0;
        RMain.view_clipplanes[0].Dist = 0; // x >= 0 accepted

        for (int i = 1; i < 4; i++)
        {
            RMain.view_clipplanes[i].Normal[0] = 0;
            RMain.view_clipplanes[i].Normal[1] = 0;
            RMain.view_clipplanes[i].Normal[2] = 1;
            RMain.view_clipplanes[i].Dist = -1000;
        }

        RMisc.SetUpFrustumIndexes();

        Entity_t entity = default;
        RShared.currententity = &entity;

        float* minmaxs = stackalloc float[6];
        minmaxs[0] = -10; minmaxs[1] = -10; minmaxs[2] = -10;
        minmaxs[3] = 10; minmaxs[4] = 10; minmaxs[5] = 10;

        Model_t model = default;
        model.Radius = 5;

        int clipflags = RMain.BmodelCheckBBox(&model, minmaxs);

        Assert.AreNotEqual(RLocal.BMODEL_FULLY_CLIPPED, clipflags);
    }

    // R_Init/R_NewMap setup side effects only. R_RenderView isn't driven
    // here: it needs a real loaded worldmodel, surface cache and z-buffer.

    [TestMethod]
    public void Init_RegistersCvarsAndCommandsWithoutThrowing()
    {
        RMain.Init();

        Assert.IsTrue(Cmd.Exists(new Utf8String("timerefresh"u8)));
        Assert.IsTrue(Cvar.FindVar(new Utf8String("r_draworder"u8)) != null);
        Assert.AreEqual((float)RShared.NUMSTACKEDGES, RMain.r_maxedges->Value);
    }

    [TestMethod]
    public void NewMap_UsesStackSurfacesWhenUnderThreshold()
    {
        // A synthetic 1-leaf worldmodel: NewMap only needs NumLeafs/Leafs.
        Model_t model = default;
        model.NumLeafs = 1;
        model.Leafs = (MLeaf_t*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(MLeaf_t)));
        Cl.worldmodel = &model;

        RMain.r_maxsurfs->Value = 0; // below MINSURFACES -> stack path
        RMain.r_maxedges->Value = 0;
        RPart.InitParticles(); // R_Init does this before any R_NewMap -> R_ClearParticles

        RMain.NewMap();

        Assert.IsTrue(RMain.r_surfsonstack);
        Assert.AreEqual(RShared.MINSURFACES, RMain.r_cnumsurfs);

        NativeMemory.Free(model.Leafs);
    }
}
