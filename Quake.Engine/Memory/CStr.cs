// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine.Memory;

/// <summary>
/// Byte-buffer string helpers matching the exact truncation semantics of the
/// original C string calls used against fixed <c>char name[N]</c> buffers,
/// plus the managed/native conversions the port needs. Not part of the
/// original source.
///
/// Quake text is 8-bit: bytes 128-255 are the red/gold console characters.
/// All conversions therefore use Latin-1, the only built-in codec that maps
/// bytes 0-255 to chars U+0000-U+00FF (and back) losslessly on every OS --
/// unlike Marshal.*Ansi (system code page on Windows, UTF-8 elsewhere) or
/// Encoding.ASCII (turns every byte above 127 into '?').
/// </summary>
internal static unsafe class CStr
{
    // Null-terminated byte string -> managed string (Latin-1, lossless).
    public static string ToString(byte* s)
    {
        if (s == null)
            return string.Empty;
        return Encoding.Latin1.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(s));
    }

    // Managed string -> null-terminated Latin-1 bytes (for the rare cases
    // that need a managed array, e.g. file writes).
    public static byte[] ToBytes(string s) => Encoding.Latin1.GetBytes(s + "\0");

    // sprintf(dest, "...") for an interpolated message: writes the text plus a
    // trailing null. dest must hold the text plus 1 byte (the handler caps the
    // text at 255 bytes).
    public static void Write(byte* dest, [InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler text)
    {
        scoped ReadOnlySpan<byte> src = text.GetWrittenBuffer();
        src.CopyTo(new Span<byte>(dest, src.Length));
        dest[src.Length] = 0;
    }

    // sprintf(dest, "%s", s) -- writes s as Latin-1 plus a trailing null.
    // Like sprintf, dest must be large enough (s.Length + 1 bytes).
    public static void Write(byte* dest, string s)
    {
        fixed (char* c = s)
            Encoding.Latin1.GetBytes(c, s.Length, dest, s.Length);
        dest[s.Length] = 0;
    }

    // Q_strncpy (common.c): copies while src has characters and count remains;
    // writes a single trailing null only if room remains. Does NOT zero-pad.
    public static void QStrncpy(byte* dest, string src, int count)
    {
        int i = 0;
        while (i < src.Length && count > 0)
        {
            dest[i] = (byte)src[i];
            i++;
            count--;
        }
        if (count > 0)
            dest[i] = 0;
    }

    // Q_strncpy for a "..."u8 literal.
    public static void QStrncpy(byte* dest, ReadOnlySpan<byte> src, int count)
    {
        int i = 0;
        while (i < src.Length && count > 0)
        {
            dest[i] = src[i];
            i++;
            count--;
        }
        if (count > 0)
            dest[i] = 0;
    }

    // libc strncpy for a "..."u8 literal.
    public static void Strncpy(byte* dest, ReadOnlySpan<byte> src, int count)
    {
        int i = 0;
        while (i < count && i < src.Length)
        {
            dest[i] = src[i];
            i++;
        }
        while (i < count)
        {
            dest[i] = 0;
            i++;
        }
    }

    // libc strncpy: copies up to count bytes, zero-pads the remainder of the buffer.
    public static void Strncpy(byte* dest, string src, int count)
    {
        int i = 0;
        while (i < count && i < src.Length)
        {
            dest[i] = (byte)src[i];
            i++;
        }
        while (i < count)
        {
            dest[i] = 0;
            i++;
        }
    }
}
