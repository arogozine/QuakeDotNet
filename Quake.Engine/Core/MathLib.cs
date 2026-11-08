// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// mathlib.c -- math primitives
/// Functions that took a vec3_t parameter in the original take a Vec3_t here:
/// "in Vec3_t" for a read-only (input) vector, "Vec3_t*" for one the
/// function writes into (an output, or one normalized/negated in place) --
/// matching the original's vec3_t* out-param convention (rule 1: prefer
/// pointers over "ref" for these).
/// Only the !id386 (non-assembly) code paths are ported: this port has no
/// x86 asm, so BoxOnPlaneSide and Invert24To16 always use the C fallback.
/// </summary>
public static unsafe class MathLib
{
    public const float M_PI = 3.14159265358979323846f;


    // vec3_origin
    public static Vec3_t vec3_origin;

    // nanmask
    private static int NanMask = 255 << 23;

    // IS_NAN
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_NAN(float x) => ((*(int*)&x) & NanMask) == NanMask;

    private const float DEG2RAD = MathF.PI / 180.0f;

    // ProjectPointOnPlane
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ProjectPointOnPlane(Vec3_t* dst, in Vec3_t p, in Vec3_t normal)
    {
        float invDenom = 1.0f / Vector3.Dot(normal, normal);
        float d = Vector3.Dot(normal, p) * invDenom;

        *dst = p - d * (normal * invDenom);
    }

    // PerpendicularVector
    // assumes "src" is normalized
    public static void PerpendicularVector(Vec3_t* dst, in Vec3_t src)
    {
        int pos = 0;
        float minelem = 1.0f;

        // find the smallest magnitude axially aligned vector
        for (int i = 0; i < 3; i++)
        {
            if (MathF.Abs(src[i]) < minelem)
            {
                pos = i;
                minelem = MathF.Abs(src[i]);
            }
        }

        Vec3_t tempvec = default;
        tempvec[pos] = 1.0f;

        // project the point onto the plane defined by src
        ProjectPointOnPlane(dst, tempvec, src);

        // normalize the result
        VectorNormalize(dst);
    }

    // RotatePointAroundVector
    public static void RotatePointAroundVector(Vec3_t* dst, in Vec3_t dir, in Vec3_t point, float degrees)
    {
        Vec3_t vf = dir;
        Vec3_t vr = default;
        Vec3_t vup;
        PerpendicularVector(&vr, dir);
        vup = Vector3.Cross(vr, vf);

        // m has vr, vup, vf as columns; im is its transpose
        float* m = stackalloc float[9];
        float* im = stackalloc float[9];
        for (int i = 0; i < 3; i++)
        {
            m[i * 3 + 0] = vr[i];
            m[i * 3 + 1] = vup[i];
            m[i * 3 + 2] = vf[i];
        }
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                im[i * 3 + j] = m[j * 3 + i];

        // rotation about Z
        float rad = degrees * DEG2RAD;
        float s = MathF.Sin(rad);
        float c = MathF.Cos(rad);
        float* zrot = stackalloc float[9] { c, s, 0, -s, c, 0, 0, 0, 1 };

        float* tmpmat = stackalloc float[9];
        float* rot = stackalloc float[9];
        R_ConcatRotations(m, zrot, tmpmat);
        R_ConcatRotations(tmpmat, im, rot);

        *dst = new Vec3_t(
            rot[0 * 3 + 0] * point.X + rot[0 * 3 + 1] * point.Y + rot[0 * 3 + 2] * point.Z,
            rot[1 * 3 + 0] * point.X + rot[1 * 3 + 1] * point.Y + rot[1 * 3 + 2] * point.Z,
            rot[2 * 3 + 0] * point.X + rot[2 * 3 + 1] * point.Y + rot[2 * 3 + 2] * point.Z);
    }

    // anglemod
    public static float anglemod(float a)
    {
        return (360.0f / 65536) * ((int)(a * (65536 / 360.0f)) & 65535);
    }

    // BOPS_Error
    public static void BOPS_Error()
    {
        Sys.Error("BoxOnPlaneSide:  Bad signbits"u8);
    }

    // BoxOnPlaneSide
    // Returns 1, 2, or 1 + 2
    public static int BoxOnPlaneSide(in Vec3_t emins, in Vec3_t emaxs, MPlane_t* p)
    {
        if (p->SignBits > 7)
        {
            BOPS_Error();
            return 0;
        }

        // general case: signbits select, per axis, which box corner is the
        // near/far one (bit set: dist1 uses emins, dist2 uses emaxs)
        int sb = p->SignBits;
        Vec3_t corner1 = new(
            (sb & 1) != 0 ? emins.X : emaxs.X,
            (sb & 2) != 0 ? emins.Y : emaxs.Y,
            (sb & 4) != 0 ? emins.Z : emaxs.Z);
        Vec3_t corner2 = new(
            (sb & 1) != 0 ? emaxs.X : emins.X,
            (sb & 2) != 0 ? emaxs.Y : emins.Y,
            (sb & 4) != 0 ? emaxs.Z : emins.Z);

        float dist1 = Vector3.Dot(p->Normal, corner1);
        float dist2 = Vector3.Dot(p->Normal, corner2);

        int sides = 0;
        if (dist1 >= p->Dist)
            sides = 1;
        if (dist2 < p->Dist)
            sides |= 2;

        return sides;
    }

    // BOX_ON_PLANE_SIDE macro
    public static int BoxOnPlaneSideFast(in Vec3_t emins, in Vec3_t emaxs, MPlane_t* p)
    {
        if (p->Type < 3)
        {
            if (p->Dist <= emins[p->Type])
                return 1;
            if (p->Dist >= emaxs[p->Type])
                return 2;
            return 3;
        }

        return BoxOnPlaneSide(emins, emaxs, p);
    }

    // AngleVectors
    public static void AngleVectors(in Vec3_t angles, out Vec3_t forward, out Vec3_t right, out Vec3_t up)
    {
        float angle = angles.Y * (M_PI * 2 / 360);
        (float sy, float cy) = MathF.SinCos(angle);
        angle = angles.X * (M_PI * 2 / 360);
        (float sp, float cp) = MathF.SinCos(angle);
        angle = angles.Z * (M_PI * 2 / 360);
        (float sr, float cr) = MathF.SinCos(angle);

        forward = new Vec3_t(cp * cy, cp * sy, -sp);
        right = new Vec3_t(
            -1 * sr * sp * cy + -1 * cr * -sy,
            -1 * sr * sp * sy + -1 * cr * cy,
            -1 * sr * cp);
        up = new Vec3_t(
            cr * sp * cy + -sr * -sy,
            cr * sp * sy + -sr * cy,
            cr * cp);
    }

    // PF_vectoyaw's math (pr_cmds.c)
    public static float VectoYaw(in Vec3_t value)
    {
        if (value.Y == 0 && value.X == 0)
            return 0;

        float yaw = (int)(MathF.Atan2(value.Y, value.X) * 180 / M_PI);
        if (yaw < 0)
            yaw += 360;
        return yaw;
    }

    // PF_vectoangles's math (pr_cmds.c)
    public static void VectoAngles(in Vec3_t value, Vec3_t* result)
    {
        float yaw, pitch;

        if (value.Y == 0 && value.X == 0)
        {
            yaw = 0;
            pitch = value.Z > 0 ? 90 : 270;
        }
        else
        {
            yaw = (int)(MathF.Atan2(value.Y, value.X) * 180 / M_PI);
            if (yaw < 0)
                yaw += 360;

            float forward = MathF.Sqrt(value.X * value.X + value.Y * value.Y);
            pitch = (int)(MathF.Atan2(value.Z, forward) * 180 / M_PI);
            if (pitch < 0)
                pitch += 360;
        }

        *result = new Vec3_t(pitch, yaw, 0);
    }

    // VectorNormalize
    public static float VectorNormalize(Vec3_t* v)
    {
        float length = v->Length();

        if (length != 0)
            *v *= 1 / length;

        return length;
    }

    // Q_log2
    public static int Q_log2(int val)
    {
        int answer = 0;
        while ((val >>= 1) != 0)
            answer++;
        return answer;
    }

    // R_ConcatRotations
    // in1/in2/out are row-major 3x3 matrices (float[3][3] in the original),
    // flattened to float* with stride 3.
    // Each output row is in1's row-weighted sum of in2's rows, evaluated with
    // Vector3 ops in the same left-to-right order as the original scalar
    // expressions (no FMA contraction), so results are bit-identical. in2 is
    // read in full, and each in1 row is read, before that output row is
    // written, so @out may alias either input.
    public static void R_ConcatRotations(float* in1, float* in2, float* @out)
    {
        Vector3 b0 = *(Vector3*)(in2 + 0 * 3);
        Vector3 b1 = *(Vector3*)(in2 + 1 * 3);
        Vector3 b2 = *(Vector3*)(in2 + 2 * 3);

        for (int i = 0; i < 3; i++)
        {
            float* a = in1 + i * 3;
            *(Vector3*)(@out + i * 3) = a[0] * b0 + a[1] * b1 + a[2] * b2;
        }
    }

    // R_ConcatTransforms
    // in1/in2/out are row-major 3x4 matrices (float[3][4] in the original),
    // flattened to float* with stride 4.
    // Same row-wise Vector4 formulation as R_ConcatRotations; in1's
    // translation column is added to W only, so the other columns never pick
    // up a "+ 0" (which would turn -0 into +0).
    public static void R_ConcatTransforms(float* in1, float* in2, float* @out)
    {
        Vector4 b0 = *(Vector4*)(in2 + 0 * 4);
        Vector4 b1 = *(Vector4*)(in2 + 1 * 4);
        Vector4 b2 = *(Vector4*)(in2 + 2 * 4);

        for (int i = 0; i < 3; i++)
        {
            float* a = in1 + i * 4;
            Vector4 r = a[0] * b0 + a[1] * b1 + a[2] * b2;
            r.W += a[3];
            *(Vector4*)(@out + i * 4) = r;
        }
    }

    // FloorDivMod
    // Returns mathematically correct (floor-based) quotient and remainder for
    // numer and denom, both of which should contain no fractional part. The
    // quotient must fit in 32 bits.
    public static void FloorDivMod(double numer, double denom, int* quotient, int* rem)
    {
        int q, r;

        if (denom <= 0.0)
            Sys.Error($"FloorDivMod: bad denominator {denom}\n");

        if (numer >= 0.0)
        {
            double x = Math.Floor(numer / denom);
            q = double.ConvertToIntegerNative<int>(x);
            r = double.ConvertToIntegerNative<int>(Math.Floor(numer - (x * denom)));
        }
        else
        {
            // perform operations with positive values, and fix mod to make floor-based
            double x = Math.Floor(-numer / denom);
            q = -double.ConvertToIntegerNative<int>(x);
            r = double.ConvertToIntegerNative<int>(Math.Floor(-numer - (x * denom)));
            if (r != 0)
            {
                q--;
                r = double.ConvertToIntegerNative<int>(denom) - r;
            }
        }

        *quotient = q;
        *rem = r;
    }

    // GreatestCommonDivisor
    public static int GreatestCommonDivisor(int i1, int i2)
    {
        if (i1 > i2)
        {
            if (i2 == 0)
                return i1;
            return GreatestCommonDivisor(i2, i1 % i2);
        }
        else
        {
            if (i1 == 0)
                return i2;
            return GreatestCommonDivisor(i1, i2 % i1);
        }
    }

    // Invert24To16
    // Inverts an 8.24 value (fixed16_t) to a 16.16 value.
    public static int Invert24To16(int val)
    {
        if (val < 256)
            return unchecked((int)0xFFFFFFFF);

        return (int)((double)0x10000 * 0x1000000 / val + 0.5);
    }
}
