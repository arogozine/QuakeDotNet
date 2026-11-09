// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Numerics;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class MathLibTests
{
    private static Vec3_t Vec(float x, float y, float z)
    {
        Vec3_t v = default;
        v[0] = x;
        v[1] = y;
        v[2] = z;
        return v;
    }

    [TestMethod]
    public void VectorNormalize_ProducesUnitLength_AndReturnsOriginalLength()
    {
        Vec3_t v = Vec(3, 4, 0);
        float length = MathLib.VectorNormalize(&v);

        Assert.AreEqual(5f, length);
        Assert.AreEqual(1f, v.Length(), 1e-6f);
    }

    [TestMethod]
    public void VectorNormalize_OfZeroVector_ReturnsZero_AndLeavesVectorUnchanged()
    {
        Vec3_t v = Vec(0, 0, 0);
        float length = MathLib.VectorNormalize(&v);

        Assert.AreEqual(0f, length);
        Assert.AreEqual(0f, v[0]);
    }

    [TestMethod]
    public void Q_log2_ReturnsFloorOfLog2()
    {
        Assert.AreEqual(0, MathLib.Q_log2(1));
        Assert.AreEqual(1, MathLib.Q_log2(2));
        Assert.AreEqual(3, MathLib.Q_log2(8));
        Assert.AreEqual(3, MathLib.Q_log2(15));
    }

    [TestMethod]
    public void GreatestCommonDivisor_ComputesGcd()
    {
        Assert.AreEqual(6, MathLib.GreatestCommonDivisor(54, 24));
        Assert.AreEqual(6, MathLib.GreatestCommonDivisor(24, 54));
        Assert.AreEqual(5, MathLib.GreatestCommonDivisor(5, 0));
    }

    [TestMethod]
    public void FloorDivMod_PositiveNumerator_MatchesTruncatingDivision()
    {
        int q, r;
        MathLib.FloorDivMod(7, 2, &q, &r);
        Assert.AreEqual(3, q);
        Assert.AreEqual(1, r);
    }

    [TestMethod]
    public void FloorDivMod_NegativeNumerator_FloorsTowardNegativeInfinity()
    {
        int q, r;
        MathLib.FloorDivMod(-7, 2, &q, &r);

        // floor(-7/2) == -4, remainder makes q*denom + r == numer
        Assert.AreEqual(-4, q);
        Assert.AreEqual(1, r);
    }

    [TestMethod]
    public void anglemod_WrapsToZeroThreeSixtyRange()
    {
        Assert.AreEqual(0f, MathLib.anglemod(360f), 0.01f);
        Assert.AreEqual(10f, MathLib.anglemod(370f), 0.01f);
    }

    [TestMethod]
    public void AngleVectors_ForZeroAngles_ReturnsAxisAlignedBasis()
    {
        Vec3_t angles = Vec(0, 0, 0);
        MathLib.AngleVectors(angles, out Vec3_t forward, out Vec3_t right, out Vec3_t up);

        Assert.AreEqual(1f, forward[0], 1e-6f);
        Assert.AreEqual(0f, forward[1], 1e-6f);
        Assert.AreEqual(0f, forward[2], 1e-6f);

        Assert.AreEqual(0f, right[0], 1e-6f);
        Assert.AreEqual(-1f, right[1], 1e-6f);
        Assert.AreEqual(0f, right[2], 1e-6f);

        Assert.AreEqual(0f, up[0], 1e-6f);
        Assert.AreEqual(0f, up[1], 1e-6f);
        Assert.AreEqual(1f, up[2], 1e-6f);
    }

    [TestMethod]
    public void R_ConcatRotations_WithIdentity_ReturnsOtherMatrix()
    {
        float* identity = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        float* m = stackalloc float[9] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        float* @out = stackalloc float[9];

        MathLib.R_ConcatRotations(identity, m, @out);

        for (int i = 0; i < 9; i++)
            Assert.AreEqual(m[i], @out[i]);
    }

    [TestMethod]
    public void R_ConcatTransforms_WithIdentity_ReturnsOtherMatrix()
    {
        float* identity = stackalloc float[12] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
        float* m = stackalloc float[12] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        float* @out = stackalloc float[12];

        MathLib.R_ConcatTransforms(identity, m, @out);

        for (int i = 0; i < 12; i++)
            Assert.AreEqual(m[i], @out[i]);
    }

    // R_ConcatRotations (mathlib.c) reference: the original scalar expressions
    private static void ConcatRotationsReference(float* in1, float* in2, float* @out)
    {
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                @out[i * 3 + j] = in1[i * 3 + 0] * in2[0 * 3 + j] + in1[i * 3 + 1] * in2[1 * 3 + j] + in1[i * 3 + 2] * in2[2 * 3 + j];
    }

    // R_ConcatTransforms (mathlib.c) reference: the original scalar expressions
    private static void ConcatTransformsReference(float* in1, float* in2, float* @out)
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
                @out[i * 4 + j] = in1[i * 4 + 0] * in2[0 * 4 + j] + in1[i * 4 + 1] * in2[1 * 4 + j] + in1[i * 4 + 2] * in2[2 * 4 + j];
            @out[i * 4 + 3] = in1[i * 4 + 0] * in2[0 * 4 + 3] + in1[i * 4 + 1] * in2[1 * 4 + 3] + in1[i * 4 + 2] * in2[2 * 4 + 3] + in1[i * 4 + 3];
        }
    }

    private static void FillRandom(Random rng, float* m, int count)
    {
        for (int i = 0; i < count; i++)
            m[i] = (float)(rng.NextDouble() * 2000.0 - 1000.0);
    }

    private static void AssertBitwiseEqual(float* expected, float* actual, int count, string context)
    {
        for (int i = 0; i < count; i++)
            Assert.AreEqual(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(actual[i]),
                $"{context}: element {i} expected {expected[i]} got {actual[i]}");
    }

    [TestMethod]
    public void R_ConcatRotations_RandomMatrices_MatchOriginalBitwise()
    {
        Random rng = new(1234);
        float* in1 = stackalloc float[9];
        float* in2 = stackalloc float[9];
        float* expected = stackalloc float[9];
        float* actual = stackalloc float[9];

        for (int iter = 0; iter < 1000; iter++)
        {
            FillRandom(rng, in1, 9);
            FillRandom(rng, in2, 9);

            ConcatRotationsReference(in1, in2, expected);
            MathLib.R_ConcatRotations(in1, in2, actual);

            AssertBitwiseEqual(expected, actual, 9, $"iteration {iter}");
        }
    }

    [TestMethod]
    public void R_ConcatTransforms_RandomMatrices_MatchOriginalBitwise()
    {
        Random rng = new(5678);
        float* in1 = stackalloc float[12];
        float* in2 = stackalloc float[12];
        float* expected = stackalloc float[12];
        float* actual = stackalloc float[12];

        for (int iter = 0; iter < 1000; iter++)
        {
            FillRandom(rng, in1, 12);
            FillRandom(rng, in2, 12);

            ConcatTransformsReference(in1, in2, expected);
            MathLib.R_ConcatTransforms(in1, in2, actual);

            AssertBitwiseEqual(expected, actual, 12, $"iteration {iter}");
        }
    }

    [TestMethod]
    public void R_ConcatRotations_OutputAliasingInput_MatchesNonAliased()
    {
        Random rng = new(42);
        float* in1 = stackalloc float[9];
        float* in2 = stackalloc float[9];
        float* expected = stackalloc float[9];
        FillRandom(rng, in1, 9);
        FillRandom(rng, in2, 9);
        ConcatRotationsReference(in1, in2, expected);

        float* a = stackalloc float[9];
        float* b = stackalloc float[9];

        Buffer.MemoryCopy(in1, a, 9 * sizeof(float), 9 * sizeof(float));
        MathLib.R_ConcatRotations(a, in2, a);
        AssertBitwiseEqual(expected, a, 9, "out == in1");

        Buffer.MemoryCopy(in2, b, 9 * sizeof(float), 9 * sizeof(float));
        MathLib.R_ConcatRotations(in1, b, b);
        AssertBitwiseEqual(expected, b, 9, "out == in2");
    }

    [TestMethod]
    public void R_ConcatTransforms_OutputAliasingInput_MatchesNonAliased()
    {
        Random rng = new(43);
        float* in1 = stackalloc float[12];
        float* in2 = stackalloc float[12];
        float* expected = stackalloc float[12];
        FillRandom(rng, in1, 12);
        FillRandom(rng, in2, 12);
        ConcatTransformsReference(in1, in2, expected);

        float* a = stackalloc float[12];
        float* b = stackalloc float[12];

        Buffer.MemoryCopy(in1, a, 12 * sizeof(float), 12 * sizeof(float));
        MathLib.R_ConcatTransforms(a, in2, a);
        AssertBitwiseEqual(expected, a, 12, "out == in1");

        Buffer.MemoryCopy(in2, b, 12 * sizeof(float), 12 * sizeof(float));
        MathLib.R_ConcatTransforms(in1, b, b);
        AssertBitwiseEqual(expected, b, 12, "out == in2");
    }

    [TestMethod]
    public void R_ConcatTransforms_AddsTranslationColumnOnly()
    {
        float* identity = stackalloc float[12] { 1, 0, 0, 10, 0, 1, 0, 20, 0, 0, 1, 30 };
        float* m = stackalloc float[12] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        float* @out = stackalloc float[12];

        MathLib.R_ConcatTransforms(identity, m, @out);

        float* expected = stackalloc float[12] { 1, 2, 3, 14, 5, 6, 7, 28, 9, 10, 11, 42 };
        for (int i = 0; i < 12; i++)
            Assert.AreEqual(expected[i], @out[i]);
    }

    [TestMethod]
    public void ProjectPointOnPlane_ProjectsOntoPlaneNormal()
    {
        Vec3_t normal = Vec(0, 0, 1);
        Vec3_t p = Vec(5, 5, 5);
        Vec3_t dst = default;

        MathLib.ProjectPointOnPlane(&dst, p, normal);

        Assert.AreEqual(5f, dst[0], 1e-6f);
        Assert.AreEqual(5f, dst[1], 1e-6f);
        Assert.AreEqual(0f, dst[2], 1e-6f);
    }

    [TestMethod]
    public void PerpendicularVector_IsOrthogonalToSource_AndNormalized()
    {
        Vec3_t src = Vec(0, 0, 1);
        Vec3_t dst = default;

        MathLib.PerpendicularVector(&dst, src);

        Assert.AreEqual(0f, Vector3.Dot(dst, src), 1e-6f);
        Assert.AreEqual(1f, dst.Length(), 1e-6f);
    }

    [TestMethod]
    public void BoxOnPlaneSide_BoxEntirelyInFront_ReturnsOne()
    {
        MPlane_t plane = default;
        plane.Normal[0] = 1;
        plane.Normal[1] = 0;
        plane.Normal[2] = 0;
        plane.Dist = 1f;
        plane.SignBits = 0;

        Vec3_t emins = Vec(2, -1, -1);
        Vec3_t emaxs = Vec(3, 1, 1);

        Assert.AreEqual(1, MathLib.BoxOnPlaneSide(emins, emaxs, &plane));
    }

    [TestMethod]
    public void BoxOnPlaneSide_BoxEntirelyBehind_ReturnsTwo()
    {
        MPlane_t plane = default;
        plane.Normal[0] = 1;
        plane.Normal[1] = 0;
        plane.Normal[2] = 0;
        plane.Dist = 5f;
        plane.SignBits = 0;

        Vec3_t emins = Vec(-1, -1, -1);
        Vec3_t emaxs = Vec(1, 1, 1);

        Assert.AreEqual(2, MathLib.BoxOnPlaneSide(emins, emaxs, &plane));
    }

    [TestMethod]
    public void BoxOnPlaneSideFast_AxialPlane_UsesFastPath()
    {
        MPlane_t plane = default;
        plane.Type = 0; // axial on X
        plane.Dist = 1f;

        Vec3_t emins = Vec(2, -1, -1);
        Vec3_t emaxs = Vec(3, 1, 1);

        Assert.AreEqual(1, MathLib.BoxOnPlaneSideFast(emins, emaxs, &plane));
    }

    [TestMethod]
    public void IS_NAN_DetectsNaNAndInfinity()
    {
        Assert.IsTrue(MathLib.IS_NAN(float.NaN));
        Assert.IsTrue(MathLib.IS_NAN(float.PositiveInfinity));
        Assert.IsFalse(MathLib.IS_NAN(1f));
    }

    [TestMethod]
    public void RotatePointAroundVector_QuarterTurnAboutZ_RotatesXToY()
    {
        Vec3_t dir = Vec(0, 0, 1);
        Vec3_t point = Vec(1, 0, 0);
        Vec3_t dst = default;

        MathLib.RotatePointAroundVector(&dst, dir, point, 90f);

        Assert.AreEqual(0f, MathF.Abs(dst[0]), 1e-5f);
        Assert.AreEqual(1f, MathF.Abs(dst[1]), 1e-5f);
        Assert.AreEqual(0f, dst[2], 1e-5f);
    }

    [TestMethod]
    public void RotatePointAroundVector_PreservesLength_AndAxisComponent()
    {
        Vec3_t dir = Vec(0, 0, 1);
        Vec3_t point = Vec(1, 2, 3);
        Vec3_t dst = default;

        MathLib.RotatePointAroundVector(&dst, dir, point, 37f);

        Assert.AreEqual(point.Length(), dst.Length(), 1e-4f);
        Assert.AreEqual(3f, dst[2], 1e-5f);
    }

    [TestMethod]
    public void BOPS_Error_Throws()
    {
        Assert.ThrowsExactly<QuakeSysErrorException>(() => MathLib.BOPS_Error());
    }

    [TestMethod]
    public void BoxOnPlaneSide_BoxStraddlingPlane_ReturnsThree()
    {
        MPlane_t plane = default;
        plane.Normal[0] = 1;
        plane.Dist = 0f;
        plane.SignBits = 0;

        Assert.AreEqual(3, MathLib.BoxOnPlaneSide(Vec(-1, -1, -1), Vec(1, 1, 1), &plane));
    }

    [TestMethod]
    public void BoxOnPlaneSide_AllSignBits_MatchBruteForceCornerTest()
    {
        Vec3_t emins = Vec(-1, -2, -3);
        Vec3_t emaxs = Vec(4, 5, 6);

        for (int bits = 0; bits < 8; bits++)
        {
            MPlane_t plane = default;
            plane.Normal[0] = (bits & 1) != 0 ? -1 : 1;
            plane.Normal[1] = (bits & 2) != 0 ? -1 : 1;
            plane.Normal[2] = (bits & 4) != 0 ? -1 : 1;
            plane.SignBits = (byte)bits;
            plane.Dist = 0.5f;

            float min = float.MaxValue, max = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                Vec3_t corner = Vec((c & 1) != 0 ? emaxs[0] : emins[0],
                                    (c & 2) != 0 ? emaxs[1] : emins[1],
                                    (c & 4) != 0 ? emaxs[2] : emins[2]);
                float d = Vector3.Dot(plane.Normal, corner);
                min = MathF.Min(min, d);
                max = MathF.Max(max, d);
            }

            int expected = (max >= plane.Dist ? 1 : 0) | (min < plane.Dist ? 2 : 0);
            Assert.AreEqual(expected, MathLib.BoxOnPlaneSide(emins, emaxs, &plane), $"signbits {bits}");
        }
    }

    [TestMethod]
    public void BoxOnPlaneSide_BadSignBits_Throws()
    {
        Assert.ThrowsExactly<QuakeSysErrorException>(() =>
        {
            MPlane_t plane = default;
            plane.SignBits = 8;
            Vec3_t v = Vec(0, 0, 0);
            MathLib.BoxOnPlaneSide(v, v, &plane);
        });
    }

    [TestMethod]
    public void BoxOnPlaneSideFast_AxialPlane_BehindAndStraddling()
    {
        MPlane_t plane = default;
        plane.Type = 0;

        plane.Dist = 5f;
        Assert.AreEqual(2, MathLib.BoxOnPlaneSideFast(Vec(-1, -1, -1), Vec(1, 1, 1), &plane));

        plane.Dist = 0f;
        Assert.AreEqual(3, MathLib.BoxOnPlaneSideFast(Vec(-1, -1, -1), Vec(1, 1, 1), &plane));
    }

    [TestMethod]
    public void BoxOnPlaneSideFast_NonAxialPlane_FallsBackToGeneralCase()
    {
        MPlane_t plane = default;
        plane.Type = 3;
        plane.Normal[0] = 1;
        plane.Dist = 1f;
        plane.SignBits = 0;

        Assert.AreEqual(1, MathLib.BoxOnPlaneSideFast(Vec(2, -1, -1), Vec(3, 1, 1), &plane));
    }

    [TestMethod]
    public void VectoYaw_ReturnsTruncatedDegrees()
    {
        Assert.AreEqual(0f, MathLib.VectoYaw(Vec(0, 0, 5)));
        Assert.AreEqual(0f, MathLib.VectoYaw(Vec(1, 0, 0)));
        Assert.AreEqual(90f, MathLib.VectoYaw(Vec(0, 1, 0)));
        Assert.AreEqual(270f, MathLib.VectoYaw(Vec(0, -1, 0)));
    }

    [TestMethod]
    public void VectoAngles_ComputesPitchAndYaw()
    {
        Vec3_t result = default;

        MathLib.VectoAngles(Vec(0, 0, 1), &result);
        Assert.AreEqual(Vec(90, 0, 0), result);

        MathLib.VectoAngles(Vec(0, 0, -1), &result);
        Assert.AreEqual(Vec(270, 0, 0), result);

        MathLib.VectoAngles(Vec(1, 1, 0), &result);
        Assert.AreEqual(Vec(0, 45, 0), result);

        MathLib.VectoAngles(Vec(1, 0, -1), &result);
        Assert.AreEqual(Vec(315, 0, 0), result);
    }

    [TestMethod]
    public void FloorDivMod_NonPositiveDenominator_Throws()
    {
        Assert.ThrowsExactly<QuakeSysErrorException>(() =>
        {
            int q, r;
            MathLib.FloorDivMod(1, 0, &q, &r);
        });
    }

    [TestMethod]
    public void Invert24To16_InvertsFixedPoint()
    {
        Assert.AreEqual(-1, MathLib.Invert24To16(255));
        Assert.AreEqual(0x10000, MathLib.Invert24To16(0x1000000));
        Assert.AreEqual(0x8000, MathLib.Invert24To16(0x2000000));
    }

    // AngleVectors (mathlib.c) reference: the original scalar formulation
    [TestMethod]
    public void AngleVectors_ArbitraryAngles_MatchOriginalScalarFormulas()
    {
        float[][] cases = [[30, 45, 10], [-60, 200, 75], [89, -170, -20]];
        foreach (float[] a in cases)
        {
            float d2r = MathLib.M_PI * 2 / 360;
            float sy = MathF.Sin(a[1] * d2r), cy = MathF.Cos(a[1] * d2r);
            float sp = MathF.Sin(a[0] * d2r), cp = MathF.Cos(a[0] * d2r);
            float sr = MathF.Sin(a[2] * d2r), cr = MathF.Cos(a[2] * d2r);

            MathLib.AngleVectors(Vec(a[0], a[1], a[2]), out Vec3_t forward, out Vec3_t right, out Vec3_t up);

            Assert.AreEqual(cp * cy, forward.X, 1e-6f);
            Assert.AreEqual(cp * sy, forward.Y, 1e-6f);
            Assert.AreEqual(-sp, forward.Z, 1e-6f);
            Assert.AreEqual(-1 * sr * sp * cy + -1 * cr * -sy, right.X, 1e-6f);
            Assert.AreEqual(-1 * sr * sp * sy + -1 * cr * cy, right.Y, 1e-6f);
            Assert.AreEqual(-1 * sr * cp, right.Z, 1e-6f);
            Assert.AreEqual(cr * sp * cy + -sr * -sy, up.X, 1e-6f);
            Assert.AreEqual(cr * sp * sy + -sr * cy, up.Y, 1e-6f);
            Assert.AreEqual(cr * cp, up.Z, 1e-6f);
        }
    }

    [TestMethod]
    public void VectoAngles_StraightUpAndDown_UseFixedPitch()
    {
        Vec3_t r = default;

        MathLib.VectoAngles(Vec(0, 0, 5), &r);
        Assert.AreEqual(Vec(90, 0, 0), r);

        MathLib.VectoAngles(Vec(0, 0, -5), &r);
        Assert.AreEqual(Vec(270, 0, 0), r);
    }

    [TestMethod]
    public void VectoAngles_NegativeYawAndPitch_WrapTo360()
    {
        Vec3_t r = default;

        MathLib.VectoAngles(Vec(1, -1, -1), &r);

        Assert.AreEqual(315f, r.Y);
        Assert.AreEqual(325f, r.X); // (int)(atan2(-1, sqrt(2)) * 180/pi) = -35
        Assert.AreEqual(0f, r.Z);
    }

    // Random floats spanning unit, map-scale, extreme-exponent and arbitrary
    // bit-pattern values, to exercise rounding, underflow and overflow.
    private static float RandomWideFloat(Random rng)
    {
        switch (rng.Next(4))
        {
            case 0: return (float)(rng.NextDouble() * 2 - 1);
            case 1: return (float)((rng.NextDouble() * 2 - 1) * 8192);
            case 2: return (float)((rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-30, 30)));
            default: return BitConverter.Int32BitsToSingle(rng.Next()) * (rng.Next(2) == 0 ? -1 : 1);
        }
    }

    private static Vec3_t RandomWideVec(Random rng) => Vec(RandomWideFloat(rng), RandomWideFloat(rng), RandomWideFloat(rng));

    private static void AssertBitwiseEqual(Vec3_t expected, Vec3_t actual, string context)
    {
        AssertBitwiseEqual(&expected.X, &actual.X, 3, context);
    }

    // CrossProduct (mathlib.c) reference: the original scalar expressions
    private static Vec3_t CrossProductReference(Vec3_t v1, Vec3_t v2)
    {
        return Vec(
            v1[1] * v2[2] - v1[2] * v2[1],
            v1[2] * v2[0] - v1[0] * v2[2],
            v1[0] * v2[1] - v1[1] * v2[0]);
    }

    // Length (mathlib.c) reference: sum of squares in index order, then
    // sqrt in double narrowed to float
    private static float LengthReference(Vec3_t v)
    {
        float length = 0;
        for (int i = 0; i < 3; i++)
            length += v[i] * v[i];
        return (float)Math.Sqrt(length);
    }

    // VectorNormalize (mathlib.c) reference: multiply by 1/length
    private static float VectorNormalizeReference(ref Vec3_t v)
    {
        float length = v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
        length = (float)Math.Sqrt(length);
        if (length != 0)
        {
            float ilength = 1 / length;
            v[0] *= ilength;
            v[1] *= ilength;
            v[2] *= ilength;
        }
        return length;
    }

    [TestMethod]
    public void CrossProduct_RandomVectors_MatchOriginalBitwise()
    {
        Random rng = new(4321);

        for (int iter = 0; iter < 100_000; iter++)
        {
            Vec3_t a = RandomWideVec(rng);
            Vec3_t b = RandomWideVec(rng);
            Vec3_t actual = Vector3.Cross(a, b);

            AssertBitwiseEqual(CrossProductReference(a, b), actual, $"iteration {iter}: {a} x {b}");
        }
    }

    [TestMethod]
    public void Length_RandomVectors_MatchOriginalBitwise()
    {
        Random rng = new(8765);

        for (int iter = 0; iter < 100_000; iter++)
        {
            Vec3_t v = RandomWideVec(rng);
            float expected = LengthReference(v);
            float actual = v.Length();

            Assert.AreEqual(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual),
                $"iteration {iter}: {v} expected {expected} got {actual}");
        }
    }

    [TestMethod]
    public void VectorNormalize_RandomVectors_MatchOriginalBitwise()
    {
        Random rng = new(2468);

        for (int iter = 0; iter < 100_000; iter++)
        {
            Vec3_t input = RandomWideVec(rng);
            Vec3_t expected = input;
            Vec3_t actual = input;

            float expectedLength = VectorNormalizeReference(ref expected);
            float actualLength = MathLib.VectorNormalize(&actual);

            Assert.AreEqual(BitConverter.SingleToInt32Bits(expectedLength), BitConverter.SingleToInt32Bits(actualLength),
                $"iteration {iter}: length of {input}");
            AssertBitwiseEqual(expected, actual, $"iteration {iter}: {input}");
        }
    }

    [TestMethod]
    public void VectorNormalize_ZeroVector_LeftUnchanged_ReturnsZero()
    {
        Vec3_t v = Vec(0, -0f, 0);

        float length = MathLib.VectorNormalize(&v);

        Assert.AreEqual(0f, length);
        AssertBitwiseEqual(Vec(0, -0f, 0), v, "zero vector");
    }
}
