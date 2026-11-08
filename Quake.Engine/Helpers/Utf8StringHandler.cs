// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace Quake.Engine;

/// <summary>
/// Based on <see cref="Utf8.TryWriteInterpolatedStringHandler"/>
/// Modified to (1) use 4096 length native memory span, (2) support ASCII string pointers (3) decode as ASCII.
/// </summary>
[SkipLocalsInit, InterpolatedStringHandler]
public unsafe ref struct Utf8StringHandler
{
    internal const int MAX_SIZE = 4096;

    private const int ELLIPSIS_LENGTH = 3;

    // Number of buffers in the ring; a power of two so the slot is a mask.
    internal const int RING_SIZE = 8;

    private const int SLOT_STRIDE = MAX_SIZE + 1;

    // RING_SIZE contiguous buffers of MAX_SIZE + 1 bytes (room for the NUL).
    internal static readonly byte* _buffer = NativeMemory.AllocArrayZeroed<byte>(RING_SIZE * SLOT_STRIDE);

    // Number of handlers created so far; the next one uses slot s_created % RING_SIZE.
    private static int s_created;

    // This handler's buffer: a slot of the ring, written from offset 0.
    private readonly Span<byte> _destination;
    private readonly byte* _buf;
    private int _pos;
    private bool _truncated;

    /// <summary>True if text was cut off and the result ends in "...".</summary>
    public readonly bool IsTruncated => _truncated;

    private readonly int Remaining
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _truncated ? 0 : MAX_SIZE - _pos;
    }

    // The compiler-required (literalLength, formattedCount) constructor; both
    // arguments are unused because the storage is a fixed ring slot.
    public Utf8StringHandler(int literalLength, int formattedCount)
    {
        _buf = _buffer + (s_created++ & (RING_SIZE - 1)) * SLOT_STRIDE;
        _destination = new(_buf, MAX_SIZE);
    }

    /// <summary>Writes the specified string to the handler.</summary>
    /// <param name="value">The string to write.</param>
    /// <returns>true if the value could be formatted to the span; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string? value)
    {
        if (value is { })
        {
            AppendLiteral((ReadOnlySpan<char>)value);
        }
    }

    public void AppendLiteral(scoped ReadOnlySpan<char> s)
    {
        if (_truncated)
        {
            return;
        }

        Debug.Assert(s.IndexOf('\0') < 0);

        int n = Math.Min(s.Length, Remaining);
        if (n > 0)
        {
            _pos += Encoding.ASCII.GetBytes(s[..n], _destination.Slice(_pos, n));
        }

        if (n < s.Length)
        {
            MarkTruncated();
        }
    }

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value)
    {
        if (_truncated)
        {
            return;
        }

        _AppendFormatted(value);
    }

    private bool _AppendFormatted<T>(T value)
    {
        // This method could delegate to AppendFormatted with a null format, but explicitly passing
        // default as the format to TryFormat helps to improve code quality in some cases when TryFormat is inlined,
        // e.g. for Int32 it enables the JIT to eliminate code in the inlined method based on a length check on the format.

        if (value is null)
        {
            return true;
        }

        // Special-case enums to avoid boxing them.
        if (typeof(T).IsEnum)
        {
            // TODO https://github.com/dotnet/runtime/issues/81500:
            // Once Enum.TryFormat provides direct UTF8 support, use that here instead.
            return AppendEnum(value, format: null);
        }

        // If the value can format itself directly into our buffer, do so.
        if (value is IUtf8SpanFormattable utf8SpanFormattable)
        {
            if (value is float f)
            {
                if (float.IsNegativeInfinity(f))
                {
                    return _AppendFormatted("-inf"u8);
                }

                if (float.IsInfinity(f))
                {
                    return _AppendFormatted("inf"u8);
                }
            }

            if (value is double d)
            {
                if (double.IsNegativeInfinity(d))
                {
                    return _AppendFormatted("-inf"u8);
                }

                if (double.IsInfinity(d))
                {
                    return _AppendFormatted("inf"u8);
                }
            }

            if (utf8SpanFormattable.TryFormat(_destination[_pos..], out int bytesWritten, format: default, provider: CultureInfo.InvariantCulture))
            {
                _pos += bytesWritten;
                return true;
            }

            return MarkTruncated();
        }

        string? s;
        if (value is IFormattable formattable)
        {
            // If the value can format itself directly into a UTF16 buffer, do so, then transcode.
            if (value is ISpanFormattable)
            {
                return AppendSpanFormattable(value, format: null);
            }

            // If the value can ToString with the format / provider, get the resulting string, then append that.
            s = formattable.ToString(null, formatProvider: CultureInfo.InvariantCulture);
        }
        else
        {
            // Fall back to a normal ToString and append that.
            s = value.ToString();
        }

        return _AppendFormatted(s.AsSpan());
    }

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="format">The format string.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value, string? format)
    {
        if (_truncated)
        {
            return;
        }

        _AppendFormatted(value, format);
    }

    private bool _AppendFormatted<T>(T value, string? format)
    {
        if (value is null)
        {
            return true;
        }

        // Special-case enums to avoid boxing them.
        if (typeof(T).IsEnum)
        {
            // TODO https://github.com/dotnet/runtime/issues/81500:
            // Once Enum.TryFormat provides direct UTF8 support, use that here instead.
            return AppendEnum(value, format);
        }

        // If the value can format itself directly into our buffer, do so.
        if (value is IUtf8SpanFormattable utf8)
        {
            if (value is float f)
            {
                if (float.IsNegativeInfinity(f))
                {
                    return _AppendFormatted("-inf"u8);
                }

                if (float.IsInfinity(f))
                {
                    return _AppendFormatted("inf"u8);
                }
            }

            if (value is double d)
            {
                if (double.IsNegativeInfinity(d))
                {
                    return _AppendFormatted("-inf"u8);
                }

                if (double.IsInfinity(d))
                {
                    return _AppendFormatted("inf"u8);
                }
            }

            if (utf8.TryFormat(_destination[_pos..], out int bytesWritten, format, provider: CultureInfo.InvariantCulture))
            {
                _pos += bytesWritten;
                return true;
            }

            return MarkTruncated();
        }

        string? s;
        if (value is IFormattable formattable)
        {
            // If the value can format itself directly into a UTF16 buffer, do so, then transcode.
            if (value is ISpanFormattable)
            {
                return AppendSpanFormattable(value, format);
            }

            // If the value can ToString with the format / provider, get the resulting string, then append that.
            s = formattable.ToString(format, formatProvider: CultureInfo.InvariantCulture);
        }
        else
        {
            // Fall back to a normal ToString and append that.
            s = value.ToString();
        }

        return _AppendFormatted(s.AsSpan());
    }

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value, int alignment)
    {
        if (_truncated)
        {
            return;
        }

        _AppendFormatted(value, alignment);
    }

    private void _AppendFormatted<T>(T value, int alignment)
    {
        int startingPos = _pos;
        if (_AppendFormatted(value))
        {
            if (alignment != 0)
            {
                _ = TryAppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }

            return;
        }

        MarkTruncated();
    }

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="format">The format string.</param>
    /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        if (_truncated)
        {
            return;
        }

        int startingPos = _pos;
        if (_AppendFormatted(value, format))
        {
            if (alignment != 0)
            {
                _ = TryAppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }

            return;
        }

        MarkTruncated();
    }

    /// <summary>Writes the specified character span to the handler.</summary>
    /// <param name="value">The span to write.</param>
    public void AppendFormatted(scoped ReadOnlySpan<char> value)
    {
        if (_truncated)
        {
            return;
        }

        _AppendFormatted(value);
    }

    private bool _AppendFormatted(scoped ReadOnlySpan<char> value)
    {
        if (Encoding.ASCII.TryGetBytes(value, _destination[_pos..], out int bytesWritten))
        {
            _pos += bytesWritten;
            return true;
        }

        return MarkTruncated();
    }

    /// <summary>Writes the specified string of chars to the handler.</summary>
    /// <param name="value">The span to write.</param>
    /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null)
    {
        if (_truncated)
        {
            return;
        }

        int startingPos = _pos;
        if (_AppendFormatted(value))
        {
            if (alignment != 0)
            {
                _ = TryAppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }

            return;
        }

        MarkTruncated();
    }

    public void AppendFormatted(byte* s, int alignment = 0, scoped ReadOnlySpan<char> format = default)
    {
        if (_truncated)
        {
            return;
        }

        if (s == null)
        {
            return;
        }

        // Vectorized strlen.
        AppendFormatted(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(s), alignment);
    }

    // Utf8String only converts implicitly to byte*, which overload resolution
    // ignores in favor of AppendFormatted<T> (printing the type name), so
    // forward it explicitly. The one-argument form has no optional parameters
    // so it beats AppendFormatted<T>(T) (which wins over defaulted overloads).
    public void AppendFormatted(Utf8String value)
    {
        AppendFormatted((byte*)value);
    }

    public void AppendFormatted(Utf8String value, int alignment)
    {
        AppendFormatted((byte*)value, alignment);
    }

    public void AppendFormatted(Utf8String value, int alignment, scoped ReadOnlySpan<char> format)
    {
        AppendFormatted((byte*)value, alignment, format);
    }

    /// <summary>Writes the specified span of UTF-8 bytes to the handler.</summary>
    /// <param name="utf8Value">The span to write.</param>
    public void AppendFormatted(scoped ReadOnlySpan<byte> utf8Value)
    {
        if (_truncated)
        {
            return;
        }

        _AppendFormatted(utf8Value);
    }

    private bool _AppendFormatted(scoped ReadOnlySpan<byte> utf8Value)
    {
        if (utf8Value.TryCopyTo(_destination[_pos..]))
        {
            _pos += utf8Value.Length;
            return true;
        }

        return MarkTruncated();
    }

    /// <summary>Writes the specified span of UTF-8 bytes to the handler.</summary>
    /// <param name="utf8Value">The span to write.</param>
    /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(scoped ReadOnlySpan<byte> utf8Value, int alignment = 0, string? format = null)
    {
        if (_truncated)
        {
            return;
        }

        int startingPos = _pos;
        if (_AppendFormatted(utf8Value))
        {
            if (alignment != 0)
            {
                _ = TryAppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
            return;
        }

        MarkTruncated();
    }

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    public void AppendFormatted(string? value)
    {
        if (_truncated)
        {
            return;
        }

        _AppendFormatted(value.AsSpan());
    }

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(string? value, int alignment = 0, string? format = null) =>
        // Format is meaningless for strings and doesn't make sense for someone to specify.  We have the overload
        // simply to disambiguate between ROS and object, just in case someone does specify a format, as
        // string is implicitly convertible to both. Just delegate to the T-based implementation.
        AppendFormatted<string?>(value, alignment, format);

    /// <summary>Writes the specified value to the handler.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(object? value, int alignment = 0, string? format = null) =>
        // This overload is expected to be used rarely, only if either a) something strongly typed as object is
        // formatted with both an alignment and a format, or b) the compiler is unable to target type to T. It
        // exists purely to help make cases from (b) compile. Just delegate to the T-based implementation.
        AppendFormatted<object?>(value, alignment, format);

    /// <summary>Writes the specified ISpanFormattable to the handler.</summary>
    /// <param name="value">The value to write. It must be an ISpanFormattable but isn't constrained because the caller doesn't have a constraint.</param>
    /// <param name="format">The format string.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AppendSpanFormattable<T>(T value, string? format)
    {
        Debug.Assert(value is ISpanFormattable);

        Span<char> utf16 = stackalloc char[256];
        return ((ISpanFormattable)value).TryFormat(utf16, out int charsWritten, format, provider: CultureInfo.InvariantCulture) ?
            _AppendFormatted(utf16[..charsWritten]) :
            GrowAndAppendFormatted(ref this, value, utf16.Length, out charsWritten, format);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool GrowAndAppendFormatted(scoped ref Utf8StringHandler thisRef, T value, int length, out int charsWritten, string? format)
        {
            Debug.Assert(value is ISpanFormattable);

            while (true)
            {
                int newLength = length * 2;
                if ((uint)newLength > Array.MaxLength)
                {
                    newLength = length == Array.MaxLength ?
                        Array.MaxLength + 1 : // force OOM
                        Array.MaxLength;
                }
                length = newLength;

                char[] array = ArrayPool<char>.Shared.Rent(length);
                try
                {
                    if (((ISpanFormattable)value).TryFormat(array, out charsWritten, format, CultureInfo.InvariantCulture))
                    {
                        return thisRef._AppendFormatted(array.AsSpan(0, charsWritten));
                    }
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(array);
                }
            }
        }
    }

    // TODO https://github.com/dotnet/runtime/issues/81500:
    // Remove once Enum.TryFormat(Span<byte>, ...) is available.
    /// <summary>Writes the specified enum to the handler.</summary>
    /// <param name="value">The value to write. It must be an enum but isn't constrained because the caller doesn't have a constraint.</param>
    /// <param name="format">The format string.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AppendEnum<T>(T value, string? format)
    {
        Debug.Assert(typeof(T).IsEnum);

        Span<char> utf16 = stackalloc char[256];
        return RuntimeAccessors.TryFormatUnconstrained(value, utf16, out int charsWritten, format) ?
            _AppendFormatted(utf16[..charsWritten]) :
            GrowAndAppendFormatted(ref this, value, utf16.Length, out charsWritten, format);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool GrowAndAppendFormatted(scoped ref Utf8StringHandler thisRef, T value, int length, out int charsWritten, string? format)
        {
            Debug.Assert(value is ISpanFormattable);

            while (true)
            {
                int newLength = length * 2;
                if ((uint)newLength > Array.MaxLength)
                {
                    newLength = length == Array.MaxLength ?
                        Array.MaxLength + 1 : // force OOM
                        Array.MaxLength;
                }
                length = newLength;

                char[] array = ArrayPool<char>.Shared.Rent(length);
                try
                {
                    if (RuntimeAccessors.TryFormatUnconstrained(value, array, out charsWritten, format))
                    {
                        return thisRef._AppendFormatted(array.AsSpan(0, charsWritten));
                    }
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(array);
                }
            }
        }
    }

    /// <summary>Handles adding any padding required for aligning a formatted value in an interpolation expression.</summary>
    /// <param name="startingPos">The position at which the written value started.</param>
    /// <param name="alignment">Non-zero minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
    private bool TryAppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
    {
        Debug.Assert(startingPos >= 0 && startingPos <= _pos);
        Debug.Assert(alignment != 0);

        int bytesWritten = _pos - startingPos;

        bool leftAlign = false;
        if (alignment < 0)
        {
            leftAlign = true;
            alignment = -alignment;
        }

        int paddingNeeded = alignment - bytesWritten;
        if (paddingNeeded <= 0)
        {
            return true;
        }

        if (paddingNeeded <= _destination.Length - _pos)
        {
            if (leftAlign)
            {
                _destination.Slice(_pos, paddingNeeded).Fill((byte)' ');
            }
            else
            {
                _destination.Slice(startingPos, bytesWritten).CopyTo(_destination.Slice(startingPos + paddingNeeded));
                _destination.Slice(startingPos, paddingNeeded).Fill((byte)' ');
            }

            _pos += paddingNeeded;
            return true;
        }

        return MarkTruncated();
    }

    private bool MarkTruncated()
    {
        if (_truncated)
        {
            return false;
        }

        int start = Math.Min(_pos, MAX_SIZE - ELLIPSIS_LENGTH);
        _destination.Slice(start, ELLIPSIS_LENGTH).Fill((byte)'.');
        _pos = start + ELLIPSIS_LENGTH;
        _truncated = true;

        return false;
    }

    // ------------------------------------------------------------------
    // Result
    // ------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Utf8String(in Utf8StringHandler handler)
    {
        return handler.GetWrittenBuffer();
    }

    public readonly ReadOnlySpan<byte> GetWrittenBuffer()
    {
        _buf[_pos] = 0;
        return _destination[.._pos];
    }

    public byte* GetWrittenBufferPtr()
    {
        _buf[_pos] = 0;
        return _buf;
    }
}
