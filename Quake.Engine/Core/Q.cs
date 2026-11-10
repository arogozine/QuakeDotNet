// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// Port of the Q_* helper macros/functions from q_shared.c/common.c
/// (memset/memcpy-style byte helpers and other small shared utilities).
/// </summary>
public static unsafe class Q
{
    // Q_memset
    //
    // NOTE: the aligned branch builds its word pattern from the *full* fill
    // int (fill | fill<<8 | fill<<16 | fill<<24), not (byte)fill -- so for a
    // fill value with more than the low 8 bits set, this branch and the
    // unaligned branch below genuinely produce different byte patterns. That
    // quirk is preserved here (Quake itself never passes such a fill value,
    // but rule 2 requires exact behavior regardless). Span<T>.Fill is
    // vectorized (SIMD), so it's a faster, byte-for-byte equivalent drop-in
    // for both of the original's manual loops.
    public static void MemSet(void* dest, int fill, int count)
    {
        if ((((long)dest | count) & 3) == 0)
        {
            int pattern = fill | (fill << 8) | (fill << 16) | (fill << 24);
            new Span<int>(dest, count >> 2).Fill(pattern);
        }
        else
        {
            new Span<byte>(dest, count).Fill((byte)fill);
        }
    }

    // Q_memcpy
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MemCpy(void* dest, void* src, int count)
    {
        Buffer.MemoryCopy(src, dest, count, count);
    }

    // Q_memcmp
    //
    // The original only reports equal (0) vs. not-equal (-1), never a
    // lexicographic ordering, so a vectorized equality check is a valid
    // (and much faster) stand-in for the scalar byte-by-byte loop.
    public static int MemCmp(void* m1, void* m2, int count)
    {
        return new ReadOnlySpan<byte>(m1, count).SequenceEqual(new ReadOnlySpan<byte>(m2, count)) ? 0 : -1;
    }

    // Q_strcpy
    public static void StrCpy(byte* dest, Utf8String src_)
    {
        // Vectorized copy of the string plus its NUL. Differs from the original
        // byte loop only for overlapping buffers (memmove vs. forward smear),
        // which Quake never relies on.
        byte* src = src_;
        MemCpy(dest, src, StrLen(src) + 1);
    }

    // Q_strncpy
    public static void StrNCpy(byte* dest, byte* src, int count)
    {
        while (*src != 0 && count-- != 0)
        {
            *dest++ = *src++;
        }
        if (count != 0)
        {
            *dest++ = 0;
        }
    }

    // Q_strlen
    //
    // CreateReadOnlySpanFromNullTerminated uses the BCL's vectorized, page-safe
    // NUL search (it never reads a vector across a page boundary past the
    // terminator, unlike IndexOf over an int.MaxValue-long span).
    public static int StrLen(byte* str)
    {
        return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(str).Length;
    }

    // Q_strrchr
    //
    // Searches only the characters before the terminator (like the original,
    // which never matches c == 0), using a vectorized backwards search.
    public static byte* StrRChr(byte* s, byte c)
    {
        int index = new ReadOnlySpan<byte>(s, StrLen(s)).LastIndexOf(c);
        return index < 0 ? null : s + index;
    }

    // Q_strcat
    public static void StrCat(byte* dest, Utf8String src)
    {
        dest += StrLen(dest);
        StrCpy(dest, src);
    }

    // Q_strcmp
    public static int StrCmp(byte* s1, Utf8String s2_)
    {
        byte* s2 = s2_;
        while (true)
        {
            if (*s1 != *s2)
            {
                return -1;
            }

            if (*s1 == 0)
            {
                return 0;
            }

            s1++;
            s2++;
        }
    }

    // Q_strncmp
    public static int StrNCmp(byte* s1, Utf8String s2_, int count)
    {
        byte* s2 = s2_;
        while (true)
        {
            if (count-- == 0)
            {
                return 0;
            }

            if (*s1 != *s2)
            {
                return -1;
            }

            if (*s1 == 0)
            {
                return 0;
            }

            s1++;
            s2++;
        }
    }

    // Q_strncasecmp
    public static int StrNCaseCmp(byte* s1, byte* s2, int n)
    {
        while (true)
        {
            int c1 = *s1++;
            int c2 = *s2++;

            if (n-- == 0)
            {
                return 0;
            }

            if (c1 != c2)
            {
                if (c1 is >= (byte)'a' and <= (byte)'z')
                {
                    c1 -= 'a' - 'A';
                }

                if (c2 is >= (byte)'a' and <= (byte)'z')
                {
                    c2 -= 'a' - 'A';
                }

                if (c1 != c2)
                {
                    return -1;
                }
            }
            if (c1 == 0)
            {
                return 0;
            }
        }
    }

    // Q_strcasecmp
    public static int StrCaseCmp(byte* s1, Utf8String s2)
    {
        return StrNCaseCmp(s1, s2, 99999);
    }

    // Q_atoi
    public static int Atoi(byte* str)
    {
        int sign;

        if (*str == (byte)'-')
        {
            sign = -1;
            str++;
        }
        else
        {
            sign = 1;
        }

        int val = 0;

        // check for hex
        if (str[0] == (byte)'0' && (str[1] == (byte)'x' || str[1] == (byte)'X'))
        {
            str += 2;
            while (true)
            {
                int c = *str++;
                if (c >= '0' && c <= '9')
                {
                    val = (val << 4) + c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    val = (val << 4) + c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    val = (val << 4) + c - 'A' + 10;
                }
                else
                {
                    return val * sign;
                }
            }
        }

        // check for character
        if (str[0] == (byte)'\'')
        {
            return sign * str[1];
        }

        // assume decimal
        while (true)
        {
            int c = *str++;
            if (c < '0' || c > '9')
            {
                return val * sign;
            }

            val = (val * 10) + c - '0';
        }
    }

    // Q_atof
    public static float Atof(byte* str)
    {
        int sign;

        if (*str == (byte)'-')
        {
            sign = -1;
            str++;
        }
        else
        {
            sign = 1;
        }

        double val = 0;

        // check for hex
        if (str[0] == (byte)'0' && (str[1] == (byte)'x' || str[1] == (byte)'X'))
        {
            str += 2;
            while (true)
            {
                int c = *str++;
                if (c >= '0' && c <= '9')
                {
                    val = (val * 16) + c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    val = (val * 16) + c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    val = (val * 16) + c - 'A' + 10;
                }
                else
                {
                    return (float)(val * sign);
                }
            }
        }

        // check for character
        if (str[0] == (byte)'\'')
        {
            return sign * str[1];
        }

        // assume decimal
        int decimalPoint = -1;
        int total = 0;
        while (true)
        {
            int c = *str++;
            if (c == '.')
            {
                decimalPoint = total;
                continue;
            }
            if (c < '0' || c > '9')
            {
                break;
            }

            val = (val * 10) + c - '0';
            total++;
        }

        if (decimalPoint == -1)
        {
            return (float)(val * sign);
        }

        while (total > decimalPoint)
        {
            val /= 10;
            total--;
        }

        return (float)(val * sign);
    }

    // rand() (libc) -- the one process-wide RNG; PF_random (pr_cmds.c) shares it.
    public static int Rand() => Random.Shared.Next();
}
