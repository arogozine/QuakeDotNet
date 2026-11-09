// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Numerics;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RAclipTests
{
    [TestInitialize]
    public void Setup()
    {
        RShared.r_refdef->AliasVrect.X = 0;
        RShared.r_refdef->AliasVrect.Y = 0;
        RShared.r_refdef->AliasVrectRight = 320;
        RShared.r_refdef->AliasVrectBottom = 240;
    }

    [TestMethod]
    public void ClipLeft_InterpolatesAtVrectXBoundary()
    {
        FinalVert_t pfv0 = default, pfv1 = default, @out = default;
        pfv0.V[0] = 0; pfv0.V[1] = 10; // pfv0.v[1] >= pfv1.v[1] branch
        pfv1.V[0] = 100; pfv1.V[1] = 0;

        // AliasVrect.X = 0 -> scale = (0-0)/(100-0) = 0 -> out = pfv0 (+0.5 truncated)
        RAclip.ClipLeft(&pfv0, &pfv1, &@out);

        Assert.AreEqual(0, @out.V[0]);
        Assert.AreEqual(10, @out.V[1]);
    }

    [TestMethod]
    public void ClipRight_InterpolatesAtVrectRightBoundary()
    {
        FinalVert_t pfv0 = default, pfv1 = default, @out = default;
        pfv0.V[0] = 300; pfv0.V[1] = 10;
        pfv1.V[0] = 340; pfv1.V[1] = 20; // pfv0.v[1] < pfv1.v[1] -> else branch

        // AliasVrectRight = 320 -> scale = (320-340)/(300-340) = 0.5
        RAclip.ClipRight(&pfv0, &pfv1, &@out);

        // out.v[i] = pfv1.v[i] + (pfv0.v[i]-pfv1.v[i])*0.5 + 0.5
        // v[0] = 340 + (300-340)*0.5 + 0.5 = 340 - 20 + 0.5 = 320.5 -> truncated 320
        // v[1] = 20 + (10-20)*0.5 + 0.5 = 20 - 5 + 0.5 = 15.5 -> truncated 15
        Assert.AreEqual(320, @out.V[0]);
        Assert.AreEqual(15, @out.V[1]);
    }

    [TestMethod]
    public void AliasClip_AllVertsInside_CopiesAllThroughUnclipped()
    {
        FinalVert_t* in_ = stackalloc FinalVert_t[3];
        FinalVert_t* out_ = stackalloc FinalVert_t[3];

        for (int i = 0; i < 3; i++)
        {
            in_[i] = default;
            in_[i].Flags = 0; // no clip flag set on any vertex
            in_[i].V[0] = i;
        }

        int k = RAclip.AliasClip(in_, out_, DIface.ALIAS_LEFT_CLIP, 3, &RAclip.ClipLeft);

        Assert.AreEqual(3, k);
        Assert.AreEqual(0, out_[0].V[0]);
        Assert.AreEqual(1, out_[1].V[0]);
        Assert.AreEqual(2, out_[2].V[0]);
    }

    [TestMethod]
    public void AliasClip_AllVertsOutside_ProducesNoOutput()
    {
        FinalVert_t* in_ = stackalloc FinalVert_t[3];
        FinalVert_t* out_ = stackalloc FinalVert_t[3];

        for (int i = 0; i < 3; i++)
        {
            in_[i] = default;
            in_[i].Flags = DIface.ALIAS_LEFT_CLIP; // every vertex clipped -> flags&oldflags always true -> continue
        }

        int k = RAclip.AliasClip(in_, out_, DIface.ALIAS_LEFT_CLIP, 3, &RAclip.ClipLeft);

        Assert.AreEqual(0, k);
    }

    [TestMethod]
    public void AliasClip_OneVertexCrossesEdge_InsertsClippedVertex()
    {
        // Triangle where vertex 0 is inside, vertices 1 and 2 are outside
        // (left-clipped): crossing edges (2->0) and (0->1) each produce one
        // clipped point, vertex 0 itself is copied through -> k=3.
        FinalVert_t* in_ = stackalloc FinalVert_t[3];
        FinalVert_t* out_ = stackalloc FinalVert_t[3];

        in_[0] = default; in_[0].Flags = 0; in_[0].V[0] = 50; in_[0].V[1] = 0;
        in_[1] = default; in_[1].Flags = DIface.ALIAS_LEFT_CLIP; in_[1].V[0] = -50; in_[1].V[1] = 10;
        in_[2] = default; in_[2].Flags = DIface.ALIAS_LEFT_CLIP; in_[2].V[0] = -50; in_[2].V[1] = -10;

        int k = RAclip.AliasClip(in_, out_, DIface.ALIAS_LEFT_CLIP, 3, &RAclip.ClipLeft);

        Assert.AreEqual(3, k);
        // out[0]: clipped edge (2->0), out[1]: vertex 0 copied through,
        // out[2]: clipped edge (0->1).
        Assert.AreEqual(50, out_[1].V[0]); // vertex 0 copied unchanged
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ClipZ_InterpolatesViewspaceXYToClipPlane(bool pfv0HasLargerY)
    {
        RMain.aliasxscale = 1; RMain.aliasyscale = 1;
        RMain.aliasxcenter = 0; RMain.aliasycenter = 0;

        // unclipped vertex (z = 10) and clipped vertex (z = 0); ALIAS_Z_CLIP_PLANE = 5 -> scale = 0.5
        // interpolated viewspace = (150, 300, 5) -> projected with zi = 0.2 -> (30, 60)
        FinalVert_t* pfv0 = &RAclip.fv[0];
        FinalVert_t* pfv1 = &RAclip.fv[1];
        FinalVert_t @out = default;

        pfv0->V[1] = pfv0HasLargerY ? 10 : 0;
        pfv1->V[1] = pfv0HasLargerY ? 0 : 10;
        RAclip.av[0] = pfv0HasLargerY ? new Vector3(100, 200, 10) : new Vector3(200, 400, 0);
        RAclip.av[1] = pfv0HasLargerY ? new Vector3(200, 400, 0) : new Vector3(100, 200, 10);

        RAclip.ClipZ(pfv0, pfv1, &@out);

        Assert.AreEqual(30, @out.V[0]);
        Assert.AreEqual(60, @out.V[1]);
    }

    // R_Alias_clip_z (r_aclip.c) reference: the original's scalar expressions
    [TestMethod]
    public void ClipZ_RandomVerts_MatchOriginalBitwise()
    {
        Random rng = new(97531);
        RMain.aliasxscale = 160; RMain.aliasyscale = -160;
        RMain.aliasxcenter = 160; RMain.aliasycenter = 100;

        for (int iter = 0; iter < 1000; iter++)
        {
            FinalVert_t* pfv0 = &RAclip.fv[0];
            FinalVert_t* pfv1 = &RAclip.fv[1];
            for (int k = 1; k < 5; k++)
            {
                pfv0->V[k] = rng.Next(-1000, 1000);
                pfv1->V[k] = rng.Next(-1000, 1000);
            }
            // pfv0 is in front of the clip plane, pfv1 behind it
            float[] a0 = [(float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 200 - 100), (float)(RLocal.ALIAS_Z_CLIP_PLANE + 1 + rng.NextDouble() * 50)];
            float[] a1 = [(float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 200 - 100), (float)(RLocal.ALIAS_Z_CLIP_PLANE - 1 - rng.NextDouble() * 50)];
            RAclip.av[0] = new Vector3(a0[0], a0[1], a0[2]);
            RAclip.av[1] = new Vector3(a1[0], a1[1], a1[2]);

            float scale, x, y;
            int e2, e3, e4;
            if (pfv0->V[1] >= pfv1->V[1])
            {
                scale = (RLocal.ALIAS_Z_CLIP_PLANE - a0[2]) / (a1[2] - a0[2]);
                x = a0[0] + (a1[0] - a0[0]) * scale;
                y = a0[1] + (a1[1] - a0[1]) * scale;
                e2 = (int)(pfv0->V[2] + (pfv1->V[2] - pfv0->V[2]) * scale);
                e3 = (int)(pfv0->V[3] + (pfv1->V[3] - pfv0->V[3]) * scale);
                e4 = (int)(pfv0->V[4] + (pfv1->V[4] - pfv0->V[4]) * scale);
            }
            else
            {
                scale = (RLocal.ALIAS_Z_CLIP_PLANE - a1[2]) / (a0[2] - a1[2]);
                x = a1[0] + (a0[0] - a1[0]) * scale;
                y = a1[1] + (a0[1] - a1[1]) * scale;
                e2 = (int)(pfv1->V[2] + (pfv0->V[2] - pfv1->V[2]) * scale);
                e3 = (int)(pfv1->V[3] + (pfv0->V[3] - pfv1->V[3]) * scale);
                e4 = (int)(pfv1->V[4] + (pfv0->V[4] - pfv1->V[4]) * scale);
            }
            float zi = 1.0f / RLocal.ALIAS_Z_CLIP_PLANE;
            int e0 = (int)(x * RMain.aliasxscale * zi + RMain.aliasxcenter);
            int e1 = (int)(y * RMain.aliasyscale * zi + RMain.aliasycenter);

            FinalVert_t @out = default;
            RAclip.ClipZ(pfv0, pfv1, &@out);

            Assert.AreEqual(e0, @out.V[0], $"iteration {iter}, V[0]");
            Assert.AreEqual(e1, @out.V[1], $"iteration {iter}, V[1]");
            Assert.AreEqual(e2, @out.V[2], $"iteration {iter}, V[2]");
            Assert.AreEqual(e3, @out.V[3], $"iteration {iter}, V[3]");
            Assert.AreEqual(e4, @out.V[4], $"iteration {iter}, V[4]");
        }
    }
}
