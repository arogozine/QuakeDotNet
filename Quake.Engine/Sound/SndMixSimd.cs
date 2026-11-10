// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Quake.Engine;

/// <summary>
/// Vector kernels for snd_mix.c's inner loops (Snd_WriteLinearBlastStereo16,
/// S_TransferPaintBuffer, SND_PaintChannelFrom8/16). Each produces exactly
/// what the original scalar loop does, including the 32-bit wraparound of
/// the multiplies.
/// </summary>
internal static unsafe class SndMixSimd
{
    // out[i] = clamp16((p[i] * vol) >> 8)
    public static void ScaleClamp16(int* p, short* dst, int count, int vol)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<int> v = Vector256.Create(vol), lo = Vector256.Create(-0x8000), hi = Vector256.Create(0x7fff);
            for (; i + 16 <= count; i += 16)
            {
                Vector256<int> a = Vector256.MinNative(Vector256.MaxNative(Vector256.ShiftRightArithmetic(Vector256.Load(p + i) * v, 8), lo), hi);
                Vector256<int> b = Vector256.MinNative(Vector256.MaxNative(Vector256.ShiftRightArithmetic(Vector256.Load(p + i + 8) * v, 8), lo), hi);
                Vector256.Narrow(a, b).Store(dst + i);
            }
        }

        for (; i < count; i++)
        {
            int val = (p[i] * vol) >> 8;
            if (val > 0x7fff)
            {
                val = 0x7fff;
            }
            else if (val < -0x8000)
            {
                val = -0x8000;
            }

            dst[i] = (short)val;
        }
    }

    // out[i] = (byte)((clamp16((p[i] * vol) >> 8) >> 8) + 128)
    public static void ScaleClamp8(int* p, byte* dst, int count, int vol)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<int> v = Vector256.Create(vol), lo = Vector256.Create(-0x8000), hi = Vector256.Create(0x7fff), bias = Vector256.Create(128);
            for (; i + 32 <= count; i += 32)
            {
                Vector256<short> s0 = Vector256.Narrow(Conv(p + i, v, lo, hi, bias), Conv(p + i + 8, v, lo, hi, bias));
                Vector256<short> s1 = Vector256.Narrow(Conv(p + i + 16, v, lo, hi, bias), Conv(p + i + 24, v, lo, hi, bias));
                Vector256.Narrow(s0.AsUInt16(), s1.AsUInt16()).Store(dst + i);
            }
        }

        for (; i < count; i++)
        {
            int val = (p[i] * vol) >> 8;
            if (val > 0x7fff)
            {
                val = 0x7fff;
            }
            else if (val < -0x8000)
            {
                val = -0x8000;
            }

            dst[i] = (byte)((val >> 8) + 128);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<int> Conv(int* q, Vector256<int> v, Vector256<int> lo, Vector256<int> hi, Vector256<int> bias)
        {
            Vector256<int> x = Vector256.MinNative(Vector256.MaxNative(Vector256.ShiftRightArithmetic(Vector256.Load(q) * v, 8), lo), hi);
            return Vector256.ShiftRightArithmetic(x, 8) + bias;
        }
    }

    // paintbuffer[i].Left += (sfx[i] * lv) >> 8; .Right += (sfx[i] * rv) >> 8
    public static void Paint16(int* pb, short* sfx, int count, int lv, int rv)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<int> m = Vector256.Create(lv, rv, lv, rv, lv, rv, lv, rv);
            Vector128<short> dupLo = Vector128.Create((short)0, 0, 1, 1, 2, 2, 3, 3);
            Vector128<short> dupHi = Vector128.Create((short)4, 4, 5, 5, 6, 6, 7, 7);
            for (; i + 8 <= count; i += 8)
            {
                Vector128<short> s = Vector128.Load(sfx + i);
                Vector128<short> a = Vector128.Shuffle(s, dupLo), b = Vector128.Shuffle(s, dupHi);
                Vector256<int> d0 = Vector256.Create(Vector128.WidenLower(a), Vector128.WidenUpper(a));
                Vector256<int> d1 = Vector256.Create(Vector128.WidenLower(b), Vector128.WidenUpper(b));
                (Vector256.Load(pb + (2 * i)) + Vector256.ShiftRightArithmetic(d0 * m, 8)).Store(pb + (2 * i));
                (Vector256.Load(pb + (2 * i) + 8) + Vector256.ShiftRightArithmetic(d1 * m, 8)).Store(pb + (2 * i) + 8);
            }
        }

        for (; i < count; i++)
        {
            int data = sfx[i];
            pb[2 * i] += (data * lv) >> 8;
            pb[(2 * i) + 1] += (data * rv) >> 8;
        }
    }

    // paintbuffer[i].Left += sfx[i] * lscale; .Right += sfx[i] * rscale
    // (the original's snd_scaletable[row][j] == (sbyte)j * row * 8)
    public static void Paint8(int* pb, sbyte* sfx, int count, int lscale, int rscale)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<int> m = Vector256.Create(lscale, rscale, lscale, rscale, lscale, rscale, lscale, rscale);
            Vector128<sbyte> dup = Vector128.Create((sbyte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
            for (; i + 8 <= count; i += 8)
            {
                Vector128<sbyte> s = Vector128.CreateScalarUnsafe(*(long*)(sfx + i)).AsSByte();
                Vector128<sbyte> dd = Vector128.Shuffle(s, dup);
                Vector128<short> lo = Vector128.WidenLower(dd), hi = Vector128.WidenUpper(dd);
                Vector256<int> d0 = Vector256.Create(Vector128.WidenLower(lo), Vector128.WidenUpper(lo));
                Vector256<int> d1 = Vector256.Create(Vector128.WidenLower(hi), Vector128.WidenUpper(hi));
                (Vector256.Load(pb + (2 * i)) + (d0 * m)).Store(pb + (2 * i));
                (Vector256.Load(pb + (2 * i) + 8) + (d1 * m)).Store(pb + (2 * i) + 8);
            }
        }

        for (; i < count; i++)
        {
            int data = sfx[i];
            pb[2 * i] += data * lscale;
            pb[(2 * i) + 1] += data * rscale;
        }
    }
}
