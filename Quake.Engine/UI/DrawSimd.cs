// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Engine;

/// <summary>
/// Row-level helpers for the per-pixel loops in draw.c (Draw_FadeScreen,
/// Draw_TransPic, Draw_Character, Draw_Fill, Draw_ConsoleBackground). Each
/// produces exactly the bytes the original scalar loop does.
/// </summary>
internal static unsafe class DrawSimd
{
    // Draw_FadeScreen: zero every pixel of the row except those with
    // (x & 3) == t (t = (y & 1) << 1). All rows start at x = 0, so the keep
    // mask is the same 4-byte pattern for every 8/16/32-byte block.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FadeRow(byte* pbuf, int width, int t)
    {
        int x = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            uint pat = 0xFFu << (t << 3);
            Vector256<uint> mask = Vector256.Create(pat);
            for (; x + 32 <= width; x += 32)
                (Vector256.Load(pbuf + x) & mask.AsByte()).Store(pbuf + x);
        }
        else
        {
            ulong pat = 0xFFul << (t << 3);
            pat |= pat << 32;
            for (; x + 8 <= width; x += 8)
                *(ulong*)(pbuf + x) &= pat;
        }
        for (; x < width; x++)
            if ((x & 3) != t)
                pbuf[x] = 0;
    }

    // Draw_TransPic inner row: dest[u] = src[u] where src[u] != transparent.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransRow(byte* dest, byte* src, int width, byte transparent)
    {
        int u = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<byte> tv = Vector128.Create(transparent);
            for (; u + 16 <= width; u += 16)
            {
                Vector128<byte> s = Vector128.Load(src + u);
                Vector128<byte> d = Vector128.Load(dest + u);
                Vector128.ConditionalSelect(Vector128.Equals(s, tv), d, s).Store(dest + u);
            }
            if (u + 8 <= width)
            {
                TransRow8V128(dest + u, src + u, transparent);
                u += 8;
            }
        }
        else
        {
            for (; u + 8 <= width; u += 8)
                TransRow8Swar(dest + u, src + u, transparent);
        }
        for (; u < width; u++)
        {
            byte tbyte = src[u];
            if (tbyte != transparent)
                dest[u] = tbyte;
        }
    }

    // 8 pixels via SWAR on a ulong: keeps src where src != transparent.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransRow8Swar(byte* dest, byte* src, byte transparent)
    {
        ulong s = *(ulong*)src;
        ulong x = s ^ (0x0101010101010101ul * transparent);   // zero byte == transparent
        // exact per-byte "nonzero" flag in the high bit (no carries across bytes)
        ulong nz = (((x & 0x7F7F7F7F7F7F7F7Ful) + 0x7F7F7F7F7F7F7F7Ful) | x) & 0x8080808080808080ul;
        ulong m = (nz >> 7) * 0xFF;
        *(ulong*)dest = (*(ulong*)dest & ~m) | (s & m);
    }

    // 8 pixels through a Vector128 holding the 8 bytes in its low half.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransRow8V128(byte* dest, byte* src, byte transparent)
    {
        Vector128<byte> s = Vector128.CreateScalarUnsafe(*(ulong*)src).AsByte();
        Vector128<byte> d = Vector128.CreateScalarUnsafe(*(ulong*)dest).AsByte();
        *(ulong*)dest = Vector128.ConditionalSelect(Vector128.Equals(s, Vector128.Create(transparent)), d, s).AsUInt64().ToScalar();
    }

    // Exactly 8 pixels (Draw_Character's row): best available 8-byte select.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransRow8(byte* dest, byte* src, byte transparent)
    {
        if (Vector128.IsHardwareAccelerated)
            TransRow8V128(dest, src, transparent);
        else
            TransRow8Swar(dest, src, transparent);
    }

    // for (i < count) dst[i] |= src[i]  (Sv.AddToFatPVS's PVS merge)
    public static void OrBytes(byte* dst, byte* src, int count)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
            for (; i + 32 <= count; i += 32)
                (Vector256.Load(dst + i) | Vector256.Load(src + i)).Store(dst + i);
        for (; i < count; i++)
            dst[i] |= src[i];
    }
}
