// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// Fixed 3-byte result of sprintf(num, "%3i", v) as read by sbar.c (only num[0..2] are used):
/// right-aligned, space padded, truncated to the first 3 characters when wider.
/// </summary>
[SkipLocalsInit]
public ref struct Fixed3AsciiInt
{
    private const int Alignment = 3;

    [InlineArray(Alignment)]
    public struct Byte3
    {
        private byte _element0;
    }

    public Byte3 N;

    public readonly byte this[[ConstantExpected] int i] => N[i];

    // sprintf (num, "%3i", v) -- sbar.c
    public void Format(int v)
    {
        Span<byte> t = stackalloc byte[12]; // "-2147483648"
        int pos = t.Length;
        uint u = v < 0 ? (uint)(-(long)v) : (uint)v;
        do
        {
            t[--pos] = (byte)('0' + (u % 10));
            u /= 10;
        } while (u != 0);
        if (v < 0)
        {
            t[--pos] = (byte)'-';
        }

        ReadOnlySpan<byte> s = t[pos..]; // full "%i" text
        int len = Math.Min(s.Length, Alignment);
        int pad = Alignment - len;
        N[..pad].Fill((byte)' ');
        s[..len].CopyTo(N[pad..]);
    }
}
