// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using OpenTK.Windowing.Desktop;
using Quake.Engine;

namespace Quake.Desktop;

/// <summary>
/// Not in the original -- vid_win.c's fullscreen mode list comes from
/// Win32's EnumDisplaySettings/ChangeDisplaySettings (Rule 5: Windows-only,
/// out of scope, see VidMenu.cs's own comment). This is the OS-agnostic
/// replacement: OpenTK's Monitors API is GLFW-backed and works the same way
/// on Windows/Linux/macOS. Only the primary monitor's supported video modes
/// are queried -- the list only drives windowed sizes (fullscreen is always
/// borderless at the desktop resolution, see VidWindow.SetMode), so a
/// multi-monitor-aware list isn't needed. Deliberately not unit-tested (no
/// display/monitor is available in CI); DisplayModeList.BuildList carries
/// the actual list-building logic and is tested directly.
/// </summary>
public static class DisplayModeSource
{
    // modelist -- built lazily on first use and cached (monitors don't
    // change mid-session in the common case), shared by VidMenu.cs and
    // VidWindow.cs's vid_describe*/vid_nummodes commands so both index the
    // same list.
    private static List<DisplayMode>? modelist;

    public static List<DisplayMode> GetModeList() => modelist ??= GetAvailableModes();

    public static List<DisplayMode> GetAvailableModes()
    {
        MonitorInfo primary = Monitors.GetPrimaryMonitor();

        IEnumerable<DisplayMode> candidates = primary.SupportedVideoModes
            .Select(static (vm) => new DisplayMode(vm.Width, vm.Height));

        return DisplayModeList.BuildList(candidates, primary.HorizontalResolution, primary.VerticalResolution);
    }
}
