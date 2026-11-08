// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RDrawTests
{
    private Edge_t* _edgeBuf;
    private Surf_t* _surfBuf;

    [TestInitialize]
    public void Setup()
    {
        // Axis-aligned view basis -- makes TransformVector (RMisc.cs, wired
        // to the real dot-product-against-vright/vup/vpn implementation)
        // behave as identity for this basis.
        RShared.vright = default; RShared.vright[0] = 1;
        RShared.vup = default; RShared.vup[1] = 1;
        RShared.vpn = default; RShared.vpn[2] = 1;

        RShared.modelorg = default;
        RShared.xscale = RShared.yscale = 1;
        RShared.xcenter = RShared.ycenter = 0;
        RShared.xscaleinv = RShared.yscaleinv = 1;

        RShared.r_refdef->FVrectxAdj = -1000;
        RShared.r_refdef->FVrectyAdj = -1000;
        RShared.r_refdef->FVrectRightAdj = 1000;
        RShared.r_refdef->FVrectBottomAdj = 1000;
        RShared.r_refdef->VrectXAdjShift20 = int.MinValue / 2;
        RShared.r_refdef->VrectRightAdjShift20 = int.MaxValue / 2;

        for (int i = 0; i < 4; i++)
        {
            RShared.newedges[i] = null;
            RShared.removeedges[i] = null;
        }

        _edgeBuf = (Edge_t*)NativeMemory.AllocZeroed((nuint)(16 * sizeof(Edge_t)));
        RShared.r_edges = _edgeBuf;
        RShared.edge_p = _edgeBuf;
        RShared.edge_max = _edgeBuf + 16;

        _surfBuf = (Surf_t*)NativeMemory.AllocZeroed((nuint)(4 * sizeof(Surf_t)));
        RShared.surfaces = _surfBuf;
        RShared.surface_p = _surfBuf + 2; // arbitrary non-zero index
        RShared.surf_max = _surfBuf + 4;

        RDraw.r_lastvertvalid = false;
        RDraw.r_nearzionly = false;
        RDraw.r_emitted = 0;
        RDraw.r_nearzi = 0;
        RDraw.cacheoffset = 0;
        RDraw.r_leftclipped = RDraw.r_rightclipped = false;
        DIface.r_framecount = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_edgeBuf);
        NativeMemory.Free(_surfBuf);
    }

    [TestMethod]
    public void EmitEdge_NonHorizontalEdge_CreatesTrailingEdgeAndInsertsIntoLists()
    {
        Vec3_t pv0 = default, pv1 = default;
        pv0[2] = 1; // z=1, avoids NEAR_CLIP clamp
        pv1[1] = -10; // y=-10 -> v = -transformed[1] = 10
        pv1[2] = 1;

        RDraw.EmitEdge(&pv0, &pv1);

        Assert.AreEqual(1, RDraw.r_emitted);

        // v=ceilv0=0 (pv0: v=0), v2=r_ceilv1-1=9 (pv1: v=ceil(10)=10)
        Assert.IsTrue(RShared.newedges[0] != null);
        Assert.IsTrue(RShared.removeedges[9] != null);

        Edge_t* edge = RShared.newedges[0];
        // trailing edge (side=0): surfs[0] = surface_p-surfaces, surfs[1]=0
        Assert.AreEqual((ushort)(RShared.surface_p - RShared.surfaces), edge->Surfs[0]);
        Assert.AreEqual(0, edge->Surfs[1]);

        // u0=0, r_u1=0 (both x=0) -> u_step=0, u=0 -> edge->U = 0xFFFFF
        Assert.AreEqual(0xFFFFF, edge->U);
        Assert.AreEqual(0, edge->UStep);
    }

    [TestMethod]
    public void EmitEdge_LeadingEdge_WhenCeilv0GreaterThanCeilv1()
    {
        Vec3_t pv0 = default, pv1 = default;
        pv0[1] = -10; // v=10
        pv0[2] = 1;
        pv1[2] = 1; // v=0

        RDraw.EmitEdge(&pv0, &pv1);

        // side=1 (leading): surfs[0]=0, surfs[1]=surface_p-surfaces
        Edge_t* edge = RShared.newedges[0]; // v=r_ceilv1=0
        Assert.AreEqual(0, edge->Surfs[0]);
        Assert.AreEqual((ushort)(RShared.surface_p - RShared.surfaces), edge->Surfs[1]);
    }

    [TestMethod]
    public void EmitEdge_HorizontalEdge_CachesFullyClippedAndEmitsNothing()
    {
        Vec3_t pv0 = default, pv1 = default;
        pv0[2] = 1;
        pv1[2] = 1; // same y (0) -> ceilv0 == r_ceilv1 -> horizontal

        DIface.r_framecount = 5;
        RDraw.cacheoffset = 123; // not 0x7FFFFFFF -- eligible for caching

        RDraw.EmitEdge(&pv0, &pv1);

        // r_emitted is set unconditionally before the horizontal check in
        // the original (matches -- this is not "nothing happened", just
        // "no edge object was created").
        Assert.AreEqual(1, RDraw.r_emitted);
        Assert.AreEqual(0x80000000u | 5u, RDraw.cacheoffset); // FULLY_CLIPPED_CACHED | framecount
        Assert.IsTrue(RShared.newedges[0] == null); // no edge created
    }

    [TestMethod]
    public void ClipEdge_NoClipPlane_DelegatesStraightToEmitEdge()
    {
        Vec3_t pv0 = default, pv1 = default;
        pv0[2] = 1;
        pv1[1] = -10;
        pv1[2] = 1;

        RDraw.ClipEdge(&pv0, &pv1, null);

        Assert.AreEqual(1, RDraw.r_emitted);
    }

    [TestMethod]
    public void ClipEdge_BothPointsClipped_CachesFullyClippedAndEmitsNothing()
    {
        ClipPlane_t plane = default;
        plane.Normal[2] = 1;
        plane.Dist = 100; // requires z>=100 to be unclipped

        Vec3_t pv0 = default, pv1 = default;
        pv0[2] = 1; // dist = 1-100 = -99 (clipped)
        pv1[2] = 2; // dist = 2-100 = -98 (clipped)

        DIface.r_framecount = 7;
        RDraw.cacheoffset = 999;
        RDraw.r_leftclipped = false;

        RDraw.ClipEdge(&pv0, &pv1, &plane);

        Assert.AreEqual(0, RDraw.r_emitted); // never reached EmitEdge
        Assert.AreEqual(0x80000000u | 7u, RDraw.cacheoffset);
    }

    [TestMethod]
    public void ClipEdge_OnePointClipped_ProducesClippedVertexAndEmits()
    {
        ClipPlane_t plane = default;
        plane.Normal[2] = 1;
        plane.Dist = 0; // requires z>=0

        Vec3_t pv0 = default, pv1 = default;
        pv0[2] = -1; // clipped (dist=-1)
        pv1[2] = 1;  // unclipped (dist=1)
        pv1[1] = -10;

        RDraw.ClipEdge(&pv0, &pv1, &plane);

        // the clipped edge still reaches EmitEdge with an interpolated vertex
        Assert.AreEqual(1, RDraw.r_emitted);
    }

    // Builds a wall quad in the y=0 plane: (-1,2) (1,2) (1,10) (-1,10) in (x,z),
    // runs R_RenderPoly on it with the given clip planes, and returns the
    // resulting polydesc. NumVerts is preset to -1 so an early return
    // (fully clipped) is detectable.
    private static PolyDesc_t RunRenderPoly(int clipflags, params (int plane, Vec3_t normal, float dist)[] planes)
    {
        Vec3_t* verts = stackalloc Vec3_t[4];
        verts[0] = new Vec3_t(-1, 0, 2);
        verts[1] = new Vec3_t(1, 0, 2);
        verts[2] = new Vec3_t(1, 0, 10);
        verts[3] = new Vec3_t(-1, 0, 10);

        MEdge_t* edges = stackalloc MEdge_t[5]; // edge 0 unused (sign of surfedge)
        int* surfedges = stackalloc int[4] { 1, 2, 3, 4 };
        for (int i = 0; i < 4; i++)
        {
            edges[i + 1].V[0] = (ushort)i;
            edges[i + 1].V[1] = (ushort)((i + 1) % 4);
        }

        Model_t model = default;
        model.Edges = edges;
        model.Surfedges = surfedges;

        Entity_t entity = default;
        entity.Model = &model;

        MPlane_t mplane = default;
        mplane.Type = (byte)PlaneType.Y;

        MSurface_t face = default;
        face.NumEdges = 4;
        face.FirstEdge = 0;
        face.Plane = &mplane;

        foreach (var (plane, normal, dist) in planes)
        {
            RMain.view_clipplanes[plane].Normal = normal;
            RMain.view_clipplanes[plane].Dist = dist;
        }

        var savedEntity = RShared.currententity;
        var savedBase = RMain.r_pcurrentvertbase;
        RShared.currententity = &entity;
        RMain.r_pcurrentvertbase = verts;
        DIface.r_polydesc = default;
        DIface.r_polydesc.NumVerts = -1;

        try
        {
            RDraw.RenderPoly(&face, clipflags);
            return DIface.r_polydesc;
        }
        finally
        {
            RShared.currententity = savedEntity;
            RMain.r_pcurrentvertbase = savedBase;
        }
    }

    [TestMethod]
    public void RenderPoly_NoClipFlags_PassesPolygonThrough()
    {
        var desc = RunRenderPoly(0);

        Assert.AreEqual(4, desc.NumVerts);
        Assert.AreEqual(1.0f / 2.0f, desc.NearZi, 1e-6f); // nearest vertex z = 2
        Assert.AreEqual(desc.NearZi, RDraw.r_nearzi);
    }

    [TestMethod]
    public void RenderPoly_ClipPlane_InterpolatesEdgeCrossings()
    {
        // keep z >= 6: the two z=2 vertices are replaced by crossings at z=6
        var desc = RunRenderPoly(0x01, (0, new Vec3_t(0, 0, 1), 6f));

        Assert.AreEqual(4, desc.NumVerts);
        Assert.AreEqual(1.0f / 6.0f, desc.NearZi, 1e-6f);
    }

    [TestMethod]
    public void RenderPoly_ClipPlane_CutsToTriangle()
    {
        // x + z >= 10 keeps only (1,10) (sum 11); the other three are below
        // (3, 3, 9). Crossings: (1,9) on the x=1 edge and (0,10) on the top
        // edge -> a triangle whose nearest z is 9.
        var desc = RunRenderPoly(0x01, (0, new Vec3_t(1, 0, 1), 10f));

        Assert.AreEqual(3, desc.NumVerts);
        Assert.AreEqual(1.0f / 9.0f, desc.NearZi, 1e-6f);
    }

    [TestMethod]
    public void RenderPoly_TwoClipPlanes_ChainInOrder()
    {
        // flags 0x03: plane 0 (z >= 6) then plane 1 (x >= 0)
        var desc = RunRenderPoly(0x03,
            (0, new Vec3_t(0, 0, 1), 6f),
            (1, new Vec3_t(1, 0, 0), 0f));

        Assert.AreEqual(4, desc.NumVerts);
        Assert.AreEqual(1.0f / 6.0f, desc.NearZi, 1e-6f);
    }

    [TestMethod]
    public void RenderPoly_FullyClipped_ReturnsWithoutDrawing()
    {
        var desc = RunRenderPoly(0x01, (0, new Vec3_t(0, 0, 1), 20f));

        Assert.AreEqual(-1, desc.NumVerts); // DrawPoly descriptor never built
    }
}
