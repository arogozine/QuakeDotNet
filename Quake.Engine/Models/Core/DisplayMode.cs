// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Globalization;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// Not in the original -- vid_win.c's modelist[]/vmode_t enumerates
// Win32-only fullscreen DIB modes via EnumDisplaySettings (Rule 5: Windows-
// only, out of scope). DisplayMode is this port's OS-agnostic stand-in for
// one candidate resolution; DisplayModeSource.cs (Quake.Desktop) is the
// platform-facing half that queries OpenTK's cross-platform Monitors API
// and feeds candidates through BuildList below.
[StructLayout(LayoutKind.Sequential)]
public readonly record struct DisplayMode(int Width, int Height) : IUtf8SpanFormattable
{
    // The "WxH" part of VID_GetModeDescription (vid_win.c). Formats straight
    // into the destination so it can sit inside a Utf8StringHandler
    // interpolation, e.g. $"{mode} windowed" (VID_GetModeDescription2's
    // "tacks on windowed/fullscreen"). Writes nothing and returns false if
    // it does not fit.
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        bytesWritten = 0;

        if (!Width.TryFormat(utf8Destination, out int w, default, CultureInfo.InvariantCulture))
            return false;
        if (w >= utf8Destination.Length)
            return false;
        utf8Destination[w++] = (byte)'x';
        if (!Height.TryFormat(utf8Destination[w..], out int h, default, CultureInfo.InvariantCulture))
            return false;

        bytesWritten = w + h;
        return true;
    }
}
