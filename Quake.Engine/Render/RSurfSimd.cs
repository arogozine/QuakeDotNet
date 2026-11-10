// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.Intrinsics;

namespace Quake.Engine;

/// <summary>
/// Vector kernels for r_surf.c's lightmap loops (R_AddDynamicLights,
/// R_BuildLightMap). Each produces exactly what the original scalar loop does.
/// </summary>
internal static unsafe class RSurfSimd
{
    // for (i < size) blocklights[i] += lightmap[i] * scale;
    public static void AddScaled(uint* blocklights, byte* lightmap, int size, uint scale)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<uint> sv = Vector256.Create(scale);
            for (; i + 8 <= size; i += 8)
            {
                Vector128<ushort> w = Vector128.WidenLower(Vector128.CreateScalarUnsafe(*(ulong*)(lightmap + i)).AsByte());
                Vector256<uint> d = Vector256.Create(Vector128.WidenLower(w), Vector128.WidenUpper(w));
                (Vector256.Load(blocklights + i) + (d * sv)).Store(blocklights + i);
            }
        }

        for (; i < size; i++)
        {
            blocklights[i] += lightmap[i] * scale;
        }
    }

    // for (i < size) { t = (int)(255*256 - blocklights[i]) >> shift; if (t < 64) t = 64; blocklights[i] = t; }
    public static void BoundInvertShift(uint* blocklights, int size, int shift)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<int> top = Vector256.Create(255 * 256), floor = Vector256.Create(1 << 6);
            for (; i + 8 <= size; i += 8)
            {
                Vector256<int> t = Vector256.ShiftRightArithmetic(top - Vector256.Load(blocklights + i).AsInt32(), shift);
                Vector256.MaxNative(t, floor).AsUInt32().Store(blocklights + i);
            }
        }

        for (; i < size; i++)
        {
            int t = (int)((255 * 256) - blocklights[i]) >> shift;
            if (t < (1 << 6))
            {
                t = 1 << 6;
            }

            blocklights[i] = (uint)t;
        }
    }

    // The t/s luxel loops of R_AddDynamicLights for one light.
    public static void AddDlightLuxels(uint* blocklights, int smax, int tmax, float local0, float local1, float rad, float minlight)
    {
        Vector256<float> seq = Vector256.Create(0f, 16f, 32f, 48f, 64f, 80f, 96f, 112f);
        Vector256<float> radV = Vector256.Create(rad), minV = Vector256.Create(minlight), l0 = Vector256.Create(local0);

        for (int t = 0; t < tmax; t++)
        {
            int td = float.ConvertToIntegerNative<int>(local1 - (t * 16));
            if (td < 0)
            {
                td = -td;
            }

            int s = 0;
            if (Vector256.IsHardwareAccelerated)
            {
                Vector256<int> tdV = Vector256.Create(td);
                for (; s + 8 <= smax; s += 8)
                {
                    Vector256<float> sf = Vector256.Create((float)(s * 16)) + seq;
                    Vector256<int> sd = Vector256.Abs(Vector256.ConvertToInt32Native(l0 - sf));
                    Vector256<int> gt = Vector256.GreaterThan(sd, tdV);
                    Vector256<int> big = Vector256.ConditionalSelect(gt, sd, tdV);
                    Vector256<int> small = Vector256.ConditionalSelect(gt, tdV, sd);
                    Vector256<float> dist = Vector256.ConvertToSingle(big + Vector256.ShiftRightArithmetic(small, 1));
                    Vector256<int> add = Vector256.ConvertToInt32Native((radV - dist) * Vector256.Create(256f));
                    Vector256<int> mask = Vector256.LessThan(dist, minV).AsInt32();
                    uint* dst = blocklights + (t * smax) + s;
                    (Vector256.Load(dst) + (add & mask).AsUInt32()).Store(dst);
                }
            }

            for (; s < smax; s++)
            {
                int sd = float.ConvertToIntegerNative<int>(local0 - (s * 16));
                if (sd < 0)
                {
                    sd = -sd;
                }

                float dist;
                if (sd > td)
                {
                    dist = sd + (td >> 1);
                }
                else
                {
                    dist = td + (sd >> 1);
                }

                if (dist < minlight)
                {
                    blocklights[(t * smax) + s] += float.ConvertToIntegerNative<uint>((rad - dist) * 256);
                }
            }
        }
    }
}
