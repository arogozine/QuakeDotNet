// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RBspTests
{
    [TestInitialize]
    public void Setup()
    {
        RMain.r_visframecount = 1;
        REdge.r_currentkey = 100;
    }

    [TestMethod]
    public void RotateBmodel_IdentityAngles_LeavesViewBasisUnchanged()
    {
        Entity_t entity = default; // Angles all zero
        RShared.currententity = &entity;

        RShared.modelorg = default;
        RShared.modelorg[0] = 5;
        RShared.modelorg[1] = 6;
        RShared.modelorg[2] = 7;

        RShared.vpn = default;
        RShared.vpn[2] = 1;
        RShared.vright = default;
        RShared.vright[0] = 1;
        RShared.vup = default;
        RShared.vup[1] = 1;

        RBsp.RotateBmodel();

        // yaw/pitch/roll all 0 -> entity_rotation is the identity matrix,
        // so R_EntityRotate leaves every vector unchanged.
        Assert.AreEqual(5f, RShared.modelorg[0], 0.0001f);
        Assert.AreEqual(6f, RShared.modelorg[1], 0.0001f);
        Assert.AreEqual(7f, RShared.modelorg[2], 0.0001f);
        Assert.AreEqual(0f, RShared.vpn[0], 0.0001f);
        Assert.AreEqual(0f, RShared.vpn[1], 0.0001f);
        Assert.AreEqual(1f, RShared.vpn[2], 0.0001f);
    }

    [TestMethod]
    public void RecursiveWorldNode_SolidNode_ReturnsImmediately()
    {
        MNode_t node = default;
        node.Contents = (int)Contents.Solid;
        node.VisFrame = 999; // deliberately not matching -- proves the
                              // Contents check short-circuits before it

        RBsp.RecursiveWorldNode(&node, 0);

        Assert.AreEqual(100, REdge.r_currentkey); // untouched
    }

    [TestMethod]
    public void RecursiveWorldNode_NotInCurrentVisframe_ReturnsImmediately()
    {
        MNode_t node = default;
        node.Contents = 0; // branch node
        node.VisFrame = RMain.r_visframecount - 1; // stale

        RBsp.RecursiveWorldNode(&node, 0);

        Assert.AreEqual(100, REdge.r_currentkey); // untouched
    }

    [TestMethod]
    public void RecursiveWorldNode_VisitsFrontLeafBeforeBackLeaf()
    {
        // A single (int)PlaneType.X split at x=0, with modelorg on the positive
        // side (dot > BACKFACE_EPSILON -> side 0 is "front").
        MPlane_t plane = default;
        plane.Type = (int)PlaneType.X;
        plane.Dist = 0;

        MLeaf_t frontLeaf = default;
        frontLeaf.Contents = -1; // non-solid leaf
        frontLeaf.VisFrame = RMain.r_visframecount;

        MLeaf_t backLeaf = default;
        backLeaf.Contents = -1;
        backLeaf.VisFrame = RMain.r_visframecount;

        MNode_t root = default;
        root.Contents = 0;
        root.VisFrame = RMain.r_visframecount;
        root.Plane = &plane;
        root.Children[0] = (MNode_t*)&frontLeaf; // front (side 0)
        root.Children[1] = (MNode_t*)&backLeaf;  // back (side 1)
        root.NumSurfaces = 0;

        RShared.modelorg = default;
        RShared.modelorg[0] = 5; // dot = 5 - 0 = 5 > BACKFACE_EPSILON -> side 0

        RBsp.RecursiveWorldNode(&root, 0);

        // Front leaf visited (and keyed) before the back leaf, per the
        // "front side first" recursion order.
        Assert.AreEqual(100, frontLeaf.Key);
        Assert.AreEqual(101, backLeaf.Key);
        Assert.AreEqual(102, REdge.r_currentkey);
    }

    [TestMethod]
    public void RecursiveWorldNode_ClipFlagsRejectNodeOutsideFrustum()
    {
        for (int i = 0; i < 4; i++)
            RMain.view_clipplanes[i] = default;

        // Plane 0: require x >= 100 to not be rejected.
        RMain.view_clipplanes[0].Normal[0] = 1;
        RMain.view_clipplanes[0].Dist = 100;
        for (int i = 1; i < 4; i++)
        {
            RMain.view_clipplanes[i].Normal[2] = 1;
            RMain.view_clipplanes[i].Dist = -1000; // never rejects
        }

        RMisc.SetUpFrustumIndexes();

        MNode_t node = default;
        node.Contents = 0;
        node.VisFrame = RMain.r_visframecount;
        for (int i = 0; i < 6; i++)
            node.MinMaxs[i] = 0; // a box at the origin, entirely < x=100

        RBsp.RecursiveWorldNode(&node, 0b1); // only clip against plane 0

        // Rejected by plane 0 before any leaf/branch processing -> key
        // sequence never advances.
        Assert.AreEqual(100, REdge.r_currentkey);
    }

    // R_RecursiveClipBPoly: an edge straddling the node plane gets a
    // generated vertex at the plane crossing, split into one edge per side,
    // plus the two edges along the clip plane (4 + 2 = 6 edges total here).
    [TestMethod]
    public void RecursiveClipBPoly_EdgeCrossingPlane_GeneratesInterpolatedVertex()
    {
        var savedVerts = RBsp.pbverts; var savedEdges = RBsp.pbedges;
        var savedNumV = RBsp.numbverts; var savedNumE = RBsp.numbedges;
        var savedOrigin = RMain.r_entorigin;
        float* rot = RBsp.entity_rotation;
        float[] savedRot = [rot[0], rot[1], rot[2], rot[3], rot[4], rot[5], rot[6], rot[7], rot[8]];
        try
        {
            for (int i = 0; i < 9; i++) rot[i] = (i % 4 == 0) ? 1f : 0f; // identity
            RMain.r_entorigin = default;

            MPlane_t plane = default;
            plane.Normal = new Vec3_t(1, 0, 0);
            plane.Dist = 0;

            // both children: solid leaves in the current visframe -> nothing is drawn
            MNode_t front = default, back = default;
            front.Contents = back.Contents = (int)Contents.Solid;
            front.VisFrame = back.VisFrame = RMain.r_visframecount;
            MNode_t node = default;
            node.Plane = &plane;
            node.Children[0] = &front;
            node.Children[1] = &back;

            RBsp.pbverts = RBsp.bverts;
            RBsp.pbedges = RBsp.bedges;
            RBsp.numbverts = RBsp.numbedges = 0;

            Vec3_t v0 = new(-1, 2, 4), v1 = new(3, 6, 8);
            BEdge_t edge = default;
            edge.V0 = &v0;
            edge.V1 = &v1;

            RBsp.RecursiveClipBPoly(&edge, &node, null);

            // dist(v0) = -1, dist(v1) = 3 -> frac = 0.25
            Assert.AreEqual(1, RBsp.numbverts);
            Assert.AreEqual(new Vec3_t(0, 3, 5), RBsp.bverts[0]);
            Assert.AreEqual(4, RBsp.numbedges);
            Assert.AreEqual((nint)(&v0), (nint)RBsp.bedges[0].V0); // back-side half: v0 -> clipped
            Assert.AreEqual((nint)RBsp.bverts, (nint)RBsp.bedges[0].V1);
            Assert.AreEqual((nint)RBsp.bverts, (nint)RBsp.bedges[1].V0); // front-side half: clipped -> v1
            Assert.AreEqual((nint)(&v1), (nint)RBsp.bedges[1].V1);
        }
        finally
        {
            RBsp.pbverts = savedVerts; RBsp.pbedges = savedEdges;
            RBsp.numbverts = savedNumV; RBsp.numbedges = savedNumE;
            RMain.r_entorigin = savedOrigin;
            for (int i = 0; i < 9; i++) rot[i] = savedRot[i];
        }
    }
}
