// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Numerics;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class RAliasTests
{
    [TestMethod]
    public void TransformVector_IdentityTransform_LeavesVectorUnchanged()
    {
        // aliastransform = [I | 0] (row-major 3x4)
        RAlias.aliastransform[0] = 1; RAlias.aliastransform[1] = 0; RAlias.aliastransform[2] = 0; RAlias.aliastransform[3] = 0;
        RAlias.aliastransform[4] = 0; RAlias.aliastransform[5] = 1; RAlias.aliastransform[6] = 0; RAlias.aliastransform[7] = 0;
        RAlias.aliastransform[8] = 0; RAlias.aliastransform[9] = 0; RAlias.aliastransform[10] = 1; RAlias.aliastransform[11] = 0;

        float* in_ = stackalloc float[3] { 3, 4, 5 };
        Vector3 out_ = default;

        RAlias.TransformVector(in_, &out_);

        Assert.AreEqual(3f, out_.X, 0.0001f);
        Assert.AreEqual(4f, out_.Y, 0.0001f);
        Assert.AreEqual(5f, out_.Z, 0.0001f);
    }

    [TestMethod]
    public void TransformVector_AppliesTranslationColumn()
    {
        RAlias.aliastransform[0] = 1; RAlias.aliastransform[1] = 0; RAlias.aliastransform[2] = 0; RAlias.aliastransform[3] = 10;
        RAlias.aliastransform[4] = 0; RAlias.aliastransform[5] = 1; RAlias.aliastransform[6] = 0; RAlias.aliastransform[7] = 20;
        RAlias.aliastransform[8] = 0; RAlias.aliastransform[9] = 0; RAlias.aliastransform[10] = 1; RAlias.aliastransform[11] = 30;

        float* in_ = stackalloc float[3] { 0, 0, 0 };
        Vector3 out_ = default;

        RAlias.TransformVector(in_, &out_);

        Assert.AreEqual(10f, out_.X, 0.0001f);
        Assert.AreEqual(20f, out_.Y, 0.0001f);
        Assert.AreEqual(30f, out_.Z, 0.0001f);
    }

    [TestMethod]
    public void SetUpTransform_IdentityAnglesAndAxisAlignedBasis_ProducesHandTracedMatrix()
    {
        Entity_t entity = default; // angles all zero
        RShared.currententity = &entity;

        Mdl_t mdl = default;
        mdl.Scale[0] = mdl.Scale[1] = mdl.Scale[2] = 1;
        // ScaleOrigin, modelorg all zero (default)
        RAlias.pmdl = &mdl;

        RShared.modelorg = default;
        RShared.vright = default; RShared.vright[0] = 1;
        RShared.vup = default; RShared.vup[1] = 1;
        RShared.vpn = default; RShared.vpn[2] = 1;

        RAlias.SetUpTransform(0);

        // Hand-traced (see step 2g summary): t2matrix/tmatrix both reduce to
        // identity, rotationmatrix = identity, so aliastransform = viewmatrix
        // = [vright | 0; -vup | 0; vpn | 0].
        float[] expected = { 1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0 };
        for (int i = 0; i < 12; i++)
            Assert.AreEqual(expected[i], RAlias.aliastransform[i], 0.0001f, $"index {i}");
    }

    [TestMethod]
    public void SetupLighting_ClampsAmbientAndShadeToLightMin()
    {
        Entity_t entity = default;
        RShared.currententity = &entity;

        // alias_forward/right/up need a prior SetUpTransform call to be set;
        // use the identity basis from the previous test's configuration.
        Mdl_t mdl = default;
        mdl.Scale[0] = mdl.Scale[1] = mdl.Scale[2] = 1;
        RAlias.pmdl = &mdl;
        RShared.modelorg = default;
        RShared.vright = default; RShared.vright[0] = 1;
        RShared.vup = default; RShared.vup[1] = 1;
        RShared.vpn = default; RShared.vpn[2] = 1;
        RAlias.SetUpTransform(0);

        ALight_t lighting = default;
        lighting.AmbientLight = 0; // below LIGHT_MIN
        lighting.ShadeLight = -5;  // negative -> clamped to 0
        var lightvec = new Vec3_t(1, 0, 0);
        lighting.PLightVec = lightvec;

        RAlias.SetupLighting(&lighting);

        // ambientlight: max(0, LIGHT_MIN=5) -> (255-5)<<VID_CBITS(6), still >= LIGHT_MIN
        int expectedAmbient = (255 - 5) << Vid.VID_CBITS;
        Assert.AreEqual(expectedAmbient, RAlias.r_ambientlight);
        Assert.AreEqual(0f, RAlias.r_shadelight, 0.0001f);
    }

    [TestMethod]
    public void ProjectFinalVert_AppliesScaleAndCenterOffset()
    {
        Vector3 av = default;
        av.X = 2;
        av.Y = 4;
        av.Z = 2; // zi = 0.5

        RMain.aliasxscale = 10;
        RMain.aliasyscale = 20;
        RMain.aliasxcenter = 100;
        RMain.aliasycenter = 200;

        FinalVert_t fv = default;
        RAlias.ProjectFinalVert(&fv, &av);

        // v[0] = (2*10*0.5) + 100 = 110; v[1] = (4*20*0.5) + 200 = 240
        Assert.AreEqual(110, fv.V[0]);
        Assert.AreEqual(240, fv.V[1]);
    }

    // R_AliasTransformAndProjectFinalVerts (r_alias.c) reference: the
    // original's per-row scalar expressions
    private static void TransformAndProjectReference(FinalVert_t* fv, TriVertX_t* pverts, StVert_t* pstverts, int numverts)
    {
        float* a = RAlias.aliastransform;
        for (int i = 0; i < numverts; i++, fv++, pverts++, pstverts++)
        {
            float zi = 1.0f / (pverts->V[0] * a[8] + pverts->V[1] * a[9] + pverts->V[2] * a[10] + a[11]);
            fv->V[5] = (int)zi;
            fv->V[0] = (int)((pverts->V[0] * a[0] + pverts->V[1] * a[1] + pverts->V[2] * a[2] + a[3]) * zi + RMain.aliasxcenter);
            fv->V[1] = (int)((pverts->V[0] * a[4] + pverts->V[1] * a[5] + pverts->V[2] * a[6] + a[7]) * zi + RMain.aliasycenter);
            fv->V[2] = pstverts->S;
            fv->V[3] = pstverts->T;
            fv->Flags = pstverts->OnSeam;

            float lightcos = Vector3.Dot(RAlias.r_avertexnormals[pverts->LightNormalIndex], RAlias.r_plightvec);
            int temp = RAlias.r_ambientlight;
            if (lightcos < 0)
            {
                temp += (int)(RAlias.r_shadelight * lightcos);
                if (temp < 0)
                    temp = 0;
            }
            fv->V[4] = temp;
        }
    }

    [TestMethod]
    public void TransformAndProjectFinalVerts_RandomTransforms_MatchOriginalBitwise()
    {
        const int NumVerts = 256;
        TriVertX_t* verts = stackalloc TriVertX_t[NumVerts];
        StVert_t* stverts = stackalloc StVert_t[NumVerts];
        FinalVert_t* expected = stackalloc FinalVert_t[NumVerts];
        FinalVert_t* actual = stackalloc FinalVert_t[NumVerts];
        Random rng = new(1357);

        RAlias.r_ambientlight = 64;
        RAlias.r_shadelight = 128;
        RAlias.r_plightvec = Vector3.Normalize(new Vector3(-1, 0.5f, -0.25f));
        RMain.aliasxcenter = 160;
        RMain.aliasycenter = 100;

        for (int iter = 0; iter < 50; iter++)
        {
            // a view transform with the trivial-accept 1/2**31 prescale, so
            // zi and the projected x/y land in the original's ranges, with z
            // kept positive (in front of the viewer)
            const float Scale = 1.0f / ((float)0x8000 * 0x10000);
            for (int i = 0; i < 12; i++)
                RAlias.aliastransform[i] = (float)(rng.NextDouble() * 2 - 1) * (i % 4 == 3 ? 200 : 1) * Scale;
            RAlias.aliastransform[11] = (float)(300 + rng.NextDouble() * 500) * Scale;

            for (int i = 0; i < NumVerts; i++)
            {
                verts[i].V[0] = (byte)rng.Next(256);
                verts[i].V[1] = (byte)rng.Next(256);
                verts[i].V[2] = (byte)rng.Next(256);
                verts[i].LightNormalIndex = (byte)rng.Next(162);
                stverts[i] = new StVert_t { OnSeam = rng.Next(2) * 0x20, S = rng.Next(1 << 20), T = rng.Next(1 << 20) };
            }

            RAlias.r_apverts = verts;
            RAlias.r_anumverts = NumVerts;
            TransformAndProjectReference(expected, verts, stverts, NumVerts);
            RAlias.TransformAndProjectFinalVerts(actual, stverts);

            for (int i = 0; i < NumVerts; i++)
            {
                for (int k = 0; k < 6; k++)
                    Assert.AreEqual(expected[i].V[k], actual[i].V[k], $"iteration {iter}, vert {i}, V[{k}]");
                Assert.AreEqual(expected[i].Flags, actual[i].Flags, $"iteration {iter}, vert {i}, Flags");
            }
        }
    }

    // R_AliasTransformVector (r_alias.c) reference: the original's scalar expressions
    [TestMethod]
    public void TransformVector_RandomTransforms_MatchOriginalBitwise()
    {
        float* a = RAlias.aliastransform;
        float* in_ = stackalloc float[3];
        Random rng = new(2468);

        for (int iter = 0; iter < 1000; iter++)
        {
            for (int i = 0; i < 12; i++)
                a[i] = (float)(rng.NextDouble() * 200 - 100);
            for (int i = 0; i < 3; i++)
                in_[i] = (float)(rng.NextDouble() * 2000 - 1000);

            float e0 = in_[0] * a[0] + in_[1] * a[1] + in_[2] * a[2] + a[3];
            float e1 = in_[0] * a[4] + in_[1] * a[5] + in_[2] * a[6] + a[7];
            float e2 = in_[0] * a[8] + in_[1] * a[9] + in_[2] * a[10] + a[11];

            Vector3 actual = default;
            RAlias.TransformVector(in_, &actual);

            Assert.AreEqual(BitConverter.SingleToInt32Bits(e0), BitConverter.SingleToInt32Bits(actual.X), $"iteration {iter}, X");
            Assert.AreEqual(BitConverter.SingleToInt32Bits(e1), BitConverter.SingleToInt32Bits(actual.Y), $"iteration {iter}, Y");
            Assert.AreEqual(BitConverter.SingleToInt32Bits(e2), BitConverter.SingleToInt32Bits(actual.Z), $"iteration {iter}, Z");
        }
    }

    // R_AliasTransformFinalVert (r_alias.c) reference for the viewspace result
    [TestMethod]
    public void TransformFinalVert_RandomTransforms_MatchOriginalViewspaceBitwise()
    {
        float* a = RAlias.aliastransform;
        TriVertX_t vert = default;
        StVert_t st = default;
        Random rng = new(8642);

        for (int iter = 0; iter < 1000; iter++)
        {
            for (int i = 0; i < 12; i++)
                a[i] = (float)(rng.NextDouble() * 2 - 1);
            vert.V[0] = (byte)rng.Next(256);
            vert.V[1] = (byte)rng.Next(256);
            vert.V[2] = (byte)rng.Next(256);
            vert.LightNormalIndex = (byte)rng.Next(162);

            float e0 = vert.V[0] * a[0] + vert.V[1] * a[1] + vert.V[2] * a[2] + a[3];
            float e1 = vert.V[0] * a[4] + vert.V[1] * a[5] + vert.V[2] * a[6] + a[7];
            float e2 = vert.V[0] * a[8] + vert.V[1] * a[9] + vert.V[2] * a[10] + a[11];

            FinalVert_t fv = default;
            Vector3 av = default;
            RAlias.TransformFinalVert(&fv, &av, &vert, &st);

            Assert.AreEqual(BitConverter.SingleToInt32Bits(e0), BitConverter.SingleToInt32Bits(av.X), $"iteration {iter}, X");
            Assert.AreEqual(BitConverter.SingleToInt32Bits(e1), BitConverter.SingleToInt32Bits(av.Y), $"iteration {iter}, Y");
            Assert.AreEqual(BitConverter.SingleToInt32Bits(e2), BitConverter.SingleToInt32Bits(av.Z), $"iteration {iter}, Z");
        }
    }
}
