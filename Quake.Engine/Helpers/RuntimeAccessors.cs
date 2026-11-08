// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// UnsafeAccessor wrappers for internal .NET runtime members.
/// </summary>
internal static unsafe class RuntimeAccessors
{
    /// <summary>
    /// System.Text.UTF8Encoding.UTF8EncodingSealed.ReadUtf8.
    /// Returns the number of bytes written, or a negative value on failure.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadUtf8(ref char input, int inputLength, ref byte output, int outputLength)
        => ReadUtf8(null, ref input, inputLength, ref output, outputLength);

    /// <summary>
    /// System.Enum.TryFormatUnconstrained&lt;TEnum&gt;.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryFormatUnconstrained<TEnum>(TEnum value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default)
        => TryFormatUnconstrained(null, value, destination, out charsWritten, format);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ReadUtf8")]
    private static extern int ReadUtf8(
        [UnsafeAccessorType("System.Text.UTF8Encoding+UTF8EncodingSealed, System.Private.CoreLib")] object? target,
        ref char input, int inputLength, ref byte output, int outputLength);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryFormatUnconstrained")]
    private static extern bool TryFormatUnconstrained<TEnum>(
        Enum? target, TEnum value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format);
}
