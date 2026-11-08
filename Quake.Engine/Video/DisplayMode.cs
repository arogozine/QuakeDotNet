// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// Pure, platform-agnostic resolution-list building logic used by the Video
/// Options menu (VidMenu.cs in Quake.Desktop). Kept here, rather than in
/// Quake.Desktop alongside the OpenTK-facing DisplayModeSource, so it has no
/// OpenTK dependency and Quake.UnitTests can exercise it directly (Rule 4).
/// </summary>
public static class DisplayModeList
{
    // Not in the original (vid_win.c's own minimum was whatever
    // EnumDisplaySettings happened to report, down to 320x200): the floor
    // this port supports and starts at -- see VidWindow.cs's vid_width/
    // vid_height defaults. Always present in BuildList's result (below),
    // even on a monitor whose native resolution is smaller.
    public const int MinWidth = 1024;
    public const int MinHeight = 768;

    // A fixed baseline set of common resolutions, always considered
    // alongside whatever the platform layer reports -- insurance against a
    // GLFW backend/compositor that only reports the current desktop mode
    // (observed on some Linux/macOS setups), so common targets like 1080p
    // are still offered even when the OS-reported list is sparse.
    // 1280x720 is deliberately not in this set: its 720 height is below
    // MinHeight, so it would never survive the MinWidth/MinHeight filter
    // below anyway -- dead weight.
    private static readonly DisplayMode[] BaselineModes =
    [
        new(MinWidth, MinHeight),
        new(1280, 1024),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
    ];

    // Builds the deduped, native-capped, ascending-sorted resolution list
    // the menu displays: merges candidates (platform-reported modes) with
    // BaselineModes, drops anything smaller than MinWidth/MinHeight or
    // larger than the monitor's native resolution, dedupes by (Width,
    // Height) ignoring refresh rate/bit depth, and sorts ascending by width
    // then height. MinWidth/MinHeight is always included in the result,
    // even if it exceeds nativeWidth/nativeHeight, so the menu never ends
    // up with nothing to offer on an unusually small display.
    public static List<DisplayMode> BuildList(IEnumerable<DisplayMode> candidates, int nativeWidth, int nativeHeight)
    {
        HashSet<DisplayMode> seen = [];
        List<DisplayMode> result = [];

        foreach (DisplayMode mode in candidates.Concat(BaselineModes))
        {
            if (mode.Width < MinWidth || mode.Height < MinHeight)
                continue;
            if (mode.Width > nativeWidth || mode.Height > nativeHeight)
                continue;
            if (seen.Add(mode))
                result.Add(mode);
        }

        if (seen.Add(new DisplayMode(MinWidth, MinHeight)))
            result.Add(new DisplayMode(MinWidth, MinHeight));

        result.Sort(static (a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
        return result;
    }
}
