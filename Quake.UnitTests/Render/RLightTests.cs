// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RLightTests
{
    [TestInitialize]
    public void Setup()
    {
        for (int i = 0; i < QuakeDef.MAX_LIGHTSTYLES; i++)
            ClLightStyle.cl_lightstyle[i] = default;
        new Span<int>(RShared.d_lightstylevalue, 256).Clear();
        RMain.r_dlightframecount = 0;
    }

    [TestMethod]
    public void AnimateLight_ZeroLengthStyle_IsFullBright()
    {
        RLight.AnimateLight();

        Assert.AreEqual(256, RShared.d_lightstylevalue[0]);
    }

    [TestMethod]
    public void AnimateLight_CyclesThroughMapCharacters()
    {
        Cl.time = 0.3; // i = 3

        {
            byte* map = ClLightStyle.cl_lightstyle[5].Map;
            map[0] = (byte)'a';
            map[1] = (byte)'a';
            map[2] = (byte)'a';
            map[3] = (byte)'m'; // 'm' - 'a' = 12 -> *22 = 264
        }
        ClLightStyle.cl_lightstyle[5].Length = 4;

        RLight.AnimateLight();

        Assert.AreEqual(264, RShared.d_lightstylevalue[5]);
    }

    [TestMethod]
    public void MarkLights_LeafNode_ReturnsImmediately()
    {
        MNode_t leaf = default;
        leaf.Contents = -1; // leaf

        DLight_t light = default;
        light.Radius = 100;

        // Should not throw despite leaf.Plane being null -- the contents<0
        // check must short-circuit before any plane dereference.
        RLight.MarkLights(&light, 1, &leaf);
    }

    [TestMethod]
    public void MarkLights_DistBeyondRadius_RecursesFrontOnly()
    {
        MPlane_t plane = default;
        plane.Normal[0] = 1;
        plane.Dist = 0;

        MNode_t frontLeaf = default;
        frontLeaf.Contents = -1;
        MNode_t backLeaf = default;
        backLeaf.Contents = -1;

        MNode_t root = default;
        root.Contents = 0;
        root.Plane = &plane;
        root.Children[0] = &frontLeaf;
        root.Children[1] = &backLeaf;

        DLight_t light = default;
        light.Origin[0] = 1000; // dist = 1000, far beyond radius
        light.Radius = 10;

        // Recurses into children[0] (leaf, contents<0 -> returns immediately);
        // must not touch node.firstsurface/numsurfaces path (would need a
        // valid worldmodel). No throw = correct branch taken.
        RLight.MarkLights(&light, 1, &root);
    }

    [TestMethod]
    public void MarkLights_WithinRadius_MarksSurfacesAndSetsDlightBits()
    {
        RMain.r_dlightframecount = 5;
        DIface.r_framecount = 4;

        MPlane_t plane = default;
        plane.Normal[0] = 1;
        plane.Dist = 0;

        Model_t model = default;
        MSurface_t surf = default;
        surf.DLightFrame = 0; // stale -- not current frame
        model.Surfaces = &surf;
        Cl.worldmodel = &model;

        MNode_t frontLeaf = default;
        frontLeaf.Contents = -1;
        MNode_t backLeaf = default;
        backLeaf.Contents = -1;

        MNode_t root = default;
        root.Contents = 0;
        root.Plane = &plane;
        root.Children[0] = &frontLeaf;
        root.Children[1] = &backLeaf;
        root.FirstSurface = 0;
        root.NumSurfaces = 1;

        DLight_t light = default; // origin at (0,0,0) -> dist = 0, within [-radius,radius]
        light.Radius = 10;

        RLight.MarkLights(&light, 1 << 3, &root);

        Assert.AreEqual(5, surf.DLightFrame);
        Assert.AreEqual(1 << 3, surf.DLightBits);
    }

    [TestMethod]
    public void LightPoint_NoLightdata_ReturnsFullBright255()
    {
        Model_t model = default;
        model.LightData = null;
        Cl.worldmodel = &model;

        Vec3_t p = default;
        int r = RLight.LightPoint(&p);

        Assert.AreEqual(255, r);
    }

    [TestMethod]
    public void LightPoint_ClampsToAmbientLightFloor()
    {
        Model_t model = default;
        model.LightData = (byte*)1; // non-null sentinel

        MLeaf_t leaf = default;
        leaf.Contents = -1; // -> RecursiveLightPoint returns -1 (no hit) -> r becomes 0
        model.Nodes = (MNode_t*)&leaf;
        Cl.worldmodel = &model;

        RShared.r_refdef->AmbientLight = 42;

        Vec3_t p = default;
        p[2] = 100;
        int r = RLight.LightPoint(&p);

        Assert.AreEqual(42, r);
    }
}
