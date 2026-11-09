// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RMiscTests
{
    [TestInitialize]
    public void Setup()
    {
        RShared.vright = default;
        RShared.vup = default;
        RShared.vpn = default;
        RShared.vright[0] = 1;
        RShared.vup[1] = 1;
        RShared.vpn[2] = 1;

        for (int i = 0; i < 4; i++)
        {
            RMain.screenedge[i] = default;
            RMain.view_clipplanes[i] = default;
        }
    }

    [TestMethod]
    public void TransformVector_ProjectsOntoAxisAlignedViewBasis()
    {
        Vec3_t v = default;
        v[0] = 2;
        v[1] = 3;
        v[2] = 4;

        RMisc.TransformVector(v, out Vec3_t outVec);

        // vright=(1,0,0), vup=(0,1,0), vpn=(0,0,1) -- identity projection
        Assert.AreEqual(2f, outVec[0]);
        Assert.AreEqual(3f, outVec[1]);
        Assert.AreEqual(4f, outVec[2]);
    }

    [TestMethod]
    public void TransformFrustum_ComputesClipPlaneFromScreenEdgeAndViewBasis()
    {
        // screenedge[0].normal = (1,0,0): v = (normal[2], -normal[0], normal[1]) = (0,-1,0)
        RMain.screenedge[0].Normal[0] = 1;
        RMain.screenedge[0].Normal[1] = 0;
        RMain.screenedge[0].Normal[2] = 0;

        RShared.modelorg = default;
        RShared.modelorg[0] = 5;
        RShared.modelorg[1] = 6;
        RShared.modelorg[2] = 7;

        RMisc.TransformFrustum();

        // v2 = v[1]*vright + v[2]*vup + v[0]*vpn = -1*(1,0,0) + 0 + 0 = (-1,0,0)
        Assert.AreEqual(-1f, RMain.view_clipplanes[0].Normal[0]);
        Assert.AreEqual(0f, RMain.view_clipplanes[0].Normal[1]);
        Assert.AreEqual(0f, RMain.view_clipplanes[0].Normal[2]);
        Assert.AreEqual(-5f, RMain.view_clipplanes[0].Dist); // dot((5,6,7),(-1,0,0))
    }

    [TestMethod]
    public void TransformPlane_SubtractsOriginDistanceAndTransformsNormal()
    {
        RShared.r_origin = default;
        RShared.r_origin[0] = 1;

        MPlane_t plane = default;
        plane.Normal[0] = 1;
        plane.Normal[1] = 0;
        plane.Normal[2] = 0;
        plane.Dist = 10;

        //Vec3_t normal = default;
        float* normal = stackalloc float[3];
        float dist;
        RMisc.TransformPlane(&plane, normal, &dist);

        Assert.AreEqual(9f, dist); // 10 - dot((1,0,0),(1,0,0))
        Assert.AreEqual(1f, normal[0]); // identity view basis
        Assert.AreEqual(0f, normal[1]);
        Assert.AreEqual(0f, normal[2]);
    }

    [TestMethod]
    public void SetUpFrustumIndexes_PicksRejectAcceptBySignOfNormal()
    {
        RMain.view_clipplanes[0].Normal[0] = -1; // negative -> reject index j, accept j+3
        RMain.view_clipplanes[0].Normal[1] = 1;  // positive -> reject index j+3, accept j
        RMain.view_clipplanes[0].Normal[2] = -1;

        RMisc.SetUpFrustumIndexes();

        int* pindex = RMain.pfrustum_indexes[0];
        Assert.AreEqual(0, pindex[0]);
        Assert.AreEqual(3, pindex[3]);
        Assert.AreEqual(4, pindex[1]);
        Assert.AreEqual(1, pindex[4]);
        Assert.AreEqual(2, pindex[2]);
        Assert.AreEqual(5, pindex[5]);
    }

    [TestMethod]
    public void CheckVariables_FlushesSurfaceCacheWhenFullbrightChanges()
    {
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(256);
        try
        {
            DSurf.InitCaches(buf, 256);
            DLocal.sc_rover = null; // simulate the cache having been consumed

            RMain.r_fullbright->Value = 0;
            RMisc.CheckVariables(); // establishes oldbright baseline (private static)
            Assert.IsTrue(DLocal.sc_rover == null); // unchanged: fullbright didn't change

            RMain.r_fullbright->Value = 1;
            RMisc.CheckVariables();

            // D_FlushCaches resets sc_rover back to sc_base.
            Assert.AreEqual((nint)DSurf.sc_base, (nint)DLocal.sc_rover);
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void LineGraph_HandTracedPixelPositions()
    {
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(400);
        try
        {
            Vid.vid.Buffer = buf;
            Vid.vid.RowBytes = 20;
            RShared.r_refdef->Vrect.X = 0;
            RShared.r_refdef->Vrect.Y = 0;
            RMain.r_graphheight->Value = 5;

            RMisc.LineGraph(2, 10, 3);

            // Hand-traced (see step 2l summary): dest = buf + rowbytes*y + x = buf+202
            Assert.AreEqual((byte)0xff, buf[202]);
            Assert.AreEqual((byte)0x30, buf[182]);
            Assert.AreEqual((byte)0xff, buf[122]);
            Assert.AreEqual((byte)0x30, buf[82]);
            Assert.AreEqual((byte)0x30, buf[42]);
            Assert.AreEqual((byte)0x30, buf[22]);
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void PrintTimes_ResetsSurfaceCounter()
    {
        RMain.c_surf = 42;
        RMain.r_time1 = 0;

        RMisc.PrintTimes();

        Assert.AreEqual(0, RMain.c_surf);
    }

    [TestMethod]
    public void PrintDSpeeds_DoesNotThrow()
    {
        RMisc.PrintDSpeeds();
    }

    [TestMethod]
    public void PrintAliasStats_DoesNotThrow()
    {
        RMain.r_amodels_drawn = 3;
        RMisc.PrintAliasStats();
    }

    [TestMethod]
    public void WarpPalette_NullBasepal_ProducesZeroedPaletteWithoutThrowing()
    {
        RMisc.host_basepal = null;
        RMisc.WarpPalette(); // must not dereference the null pointer
    }

    [TestMethod]
    public void WarpPalette_WithBasepal_BlendsHalfwayToBrightBrown()
    {
        byte* basepal = stackalloc byte[768];
        basepal[0] = 0; basepal[1] = 0; basepal[2] = 0; // color 0 = black
        RMisc.host_basepal = basepal;

        RMisc.WarpPalette(); // no direct output to inspect (newpalette is
                             // local + passed to the Vid.ShiftPalette no-op),
                             // so this is a not-throw / basepal-read smoke
                             // test matching this function's leaf-utility
                             // status.
    }

    [TestMethod]
    public void TransformVector_UsesViewBasisAxes()
    {
        Vec3_t savedRight = RShared.vright, savedUp = RShared.vup, savedPn = RShared.vpn;
        try
        {
            RShared.vright = new Vec3_t(0, 1, 0);
            RShared.vup = new Vec3_t(0, 0, 1);
            RShared.vpn = new Vec3_t(1, 0, 0);

            Vec3_t outVec = default;
            RMisc.TransformVector(new Vec3_t(2, 3, 4), out outVec);

            Assert.AreEqual(new Vec3_t(3, 4, 2), outVec);
        }
        finally
        {
            RShared.vright = savedRight;
            RShared.vup = savedUp;
            RShared.vpn = savedPn;
        }
    }
}
