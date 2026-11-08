// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

[StructLayout(LayoutKind.Sequential)]
public unsafe readonly struct Utf8String
{
    public static readonly Utf8String Empty = ""u8;

    public readonly byte* _str;

    public int Length {
        get
        {
            byte* index = _str;
            int i = 0;

            while (*index++ != 0) i++;

            return i;
        }
    }

    // The characters without the terminating NUL, for APIs that take a span.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> AsSpan() => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_str);

    // Stand-in for a C string literal (a char* into the executable's static
    // data). A "..."u8 literal's bytes live in the assembly's RVA static data,
    // which is mapped with the image and never moved by the GC, so the pointer
    // stays valid after construction and may be stored (cvar names, command
    // names). Escaping `fixed` here is deliberate (rule 8): only pass "..."u8
    // literals -- never a span over an array, stackalloc or other
    // movable/short-lived memory. Literals are only ASCII (u8 encodes chars
    // >= 0x80 as UTF-8); the compiler appends the terminating NUL.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Utf8String(ReadOnlySpan<byte> u8Literal)
    {
        this._str = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(u8Literal));
        Debug.Assert(_str[u8Literal.Length] == 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Utf8String(byte* str)
    {
        this._str = str;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Utf8String(void* str)
    {
        this._str = (byte*)str;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Utf8String(byte* str)
    {
        return new(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Utf8String (ReadOnlySpan<byte> u8Literal)
    {
        return new(u8Literal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator byte* (Utf8String u8Literal) => u8Literal._str;

    // Q_strcmp(a, b) == 0; two nulls are equal, null never equals a string.
    private static bool ContentEquals(byte* a, byte* b)
    {
        if (a == b)
            return true;
        if (a == null || b == null)
            return false;
        while (*a == *b)
        {
            if (*a == 0)
                return true;
            a++;
            b++;
        }
        return false;
    }

    public override bool Equals(object? obj) => obj is Utf8String other && this == other;

    public override int GetHashCode()
    {
        if (_str == null)
            return 0;
        HashCode h = default;
        for (byte* p = _str; *p != 0; p++)
            h.Add(*p);
        return h.ToHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Utf8String left, Utf8String right) => ContentEquals(left._str, right._str);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Utf8String left, Utf8String right) => !ContentEquals(left._str, right._str);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Utf8String left, void* right) => left._str == (byte*)right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Utf8String left, void* right) => left._str != (byte*)right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(void* left, Utf8String right) => (byte*)left == right._str;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(void* left, Utf8String right) => (byte*)left != right._str;
}