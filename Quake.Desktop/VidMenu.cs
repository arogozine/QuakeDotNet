// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine;

namespace Quake.Desktop;

/// <summary>
/// vid_win.c's VID_MenuDraw/VID_MenuKey, plus the modelist[]/modedesc_t/
/// vid_line state they use -- wired into Menu.cs's vid_menudrawfn/
/// vid_menukeyfn hooks from VidWindow.Init(), the same way VID_Init assigns
/// them in the original (vid_win.c:2174-2175). The original's mode list
/// (modelist[0..2] fixed windowed entries plus modelist[3..] enumerated via
/// Win32's EnumDisplaySettings/ChangeDisplaySettings) is replaced with
/// DisplayModeSource.GetModeList()'s OS-agnostic list:
/// OpenTK's cross-platform Monitors API instead of the Windows-only
/// EnumDisplaySettings call, capped at the primary monitor's native
/// resolution. The original's separate "Fullscreen Modes" section becomes a
/// single Fullscreen on/off row above the list (fullscreen is always
/// borderless at the desktop resolution, see VidWindow.SetMode), and the
/// listed resolutions are windowed sizes. Since that list can be much
/// longer than the original's fixed 3-column row, this renders as a
/// vertical scrollable list (up/down arrow navigation, a bounded visible
/// window with scroll indicators) instead of the original's fixed row/grid
/// layout -- not in the original, but the original's own grid was itself
/// just a fixed-size layout for a fixed-size list, so this is the same idea
/// scaled to a dynamic one.
/// </summary>
public static unsafe class VidMenu
{
    // VID_VISIBLE_ROWS -- not in the original (its fixed 3-entry/grid list
    // never needed scrolling): how many resolution entries are shown at
    // once. Rows start at ROW_TOP; footer text is drawn below the last
    // visible row instead of at a fixed Y, so this can be tuned without
    // colliding with it.
    private const int VID_VISIBLE_ROWS = 10;
    private const int ROW_HEIGHT = 8;

    // Not in the original -- the Fullscreen on/off row, above the list.
    private const int FULLSCREEN_ROW = 36 + (2 * 8);

    // "Windowed Resolutions" header, then the first resolution row, each
    // one blank line below the row before.
    private const int HEADER_ROW = FULLSCREEN_ROW + (2 * ROW_HEIGHT);
    private const int ROW_TOP = HEADER_ROW + (2 * ROW_HEIGHT);

    // vid_line -- the currently highlighted item: 0 is the Fullscreen row,
    // 1..N are resolution entries (mode index vid_line - 1).
    private static int vid_line;

    // vid_scroll_top -- mode index of the first resolution entry visible.
    private static int vid_scroll_top;

    /*
    ================
    VID_MenuDraw
    ================
    */
    public static void MenuDraw()
    {
        List<DisplayMode> modes = DisplayModeSource.GetModeList();

        QPic_t* p = Draw.CachePic("gfx/vidmodes.lmp"u8);
        Menu.DrawPic((320 - p->Width) / 2, 4, p);

        // Fullscreen on/off -- drawn like Menu.cs's M_DrawCheckbox.
        Menu.Print(4 * 8, FULLSCREEN_ROW, "Fullscreen"u8);
        Menu.Print(16 * 8, FULLSCREEN_ROW, VidWindow.IsFullscreen ? "on"u8 : "off"u8);

        Menu.Print(6 * 8, HEADER_ROW, "Windowed Resolutions"u8);

        int visibleRows = Math.Min(VID_VISIBLE_ROWS, modes.Count);
        int row = ROW_TOP;

        for (int i = 0; i < visibleRows; i++)
        {
            int index = vid_scroll_top + i;

            // M_PrintWhite for the current mode (vid_win.c's iscur), which
            // only exists while windowed.
            bool iscur = !VidWindow.IsFullscreen &&
                         modes[index].Width == (int)Vid.vid.Width && modes[index].Height == (int)Vid.vid.Height;

            if (iscur)
            {
                Menu.PrintWhite(16 * 8, row, $"{modes[index].Width}x{modes[index].Height}");
            }
            else
            {
                Utf8StringHandler handler = (Utf8StringHandler)$"{modes[index].Width}x{modes[index].Height}";
                Menu.Print(16 * 8, row, handler.GetWrittenBufferPtr());
            }

            row += ROW_HEIGHT;
        }

        // scroll indicators -- not in the original (its list always fit on
        // screen); shown only when there's more content in that direction.
        if (vid_scroll_top > 0)
        {
            Menu.Print(4 * 8, ROW_TOP, "^"u8);
        }

        if (vid_scroll_top + visibleRows < modes.Count)
        {
            Menu.Print(4 * 8, row - ROW_HEIGHT, "v"u8);
        }

        int footerRow = ROW_TOP + (VID_VISIBLE_ROWS * ROW_HEIGHT) + ROW_HEIGHT;
        Menu.Print(9 * 8, footerRow, "Press Enter to set mode"u8);
        Menu.Print(15 * 8, footerRow + (2 * ROW_HEIGHT), "Esc to exit"u8);

        // line cursor
        int cursorRow = vid_line == 0
            ? FULLSCREEN_ROW
            : ROW_TOP + ((vid_line - 1 - vid_scroll_top) * ROW_HEIGHT);
        Menu.DrawCharacter(14 * 8, cursorRow, 12 + ((int)(Host.realtime * 4) & 1));
    }

    /*
    ================
    VID_MenuKey
    ================
    */
    public static void MenuKey(int key)
    {
        if (key == Keys.K_ESCAPE)
        {
            SndDma.LocalSound("misc/menu1.wav"u8);
            Menu.Menu_Options_f();
            return;
        }

        List<DisplayMode> modes = DisplayModeSource.GetModeList();
        int items = modes.Count + 1;

        switch (key)
        {
            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                vid_line = (vid_line + items - 1) % items;
                ScrollToSelection(modes.Count);
                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                vid_line = (vid_line + 1) % items;
                ScrollToSelection(modes.Count);
                break;

            case Keys.K_LEFTARROW:
            case Keys.K_RIGHTARROW:
                if (vid_line == 0)
                {
                    ToggleFullscreen();
                }

                break;

            case Keys.K_ENTER:
                if (vid_line == 0)
                {
                    ToggleFullscreen();
                }
                else
                {
                    // Picking a resolution means windowed at that size.
                    SndDma.LocalSound("misc/menu1.wav"u8);
                    DisplayMode mode = modes[vid_line - 1];
                    VidWindow.SetMode(false, mode.Width, mode.Height);
                }
                break;
        }
    }

    // Not in the original -- the Fullscreen row's action (the menu
    // equivalent of the vid_fullscreen/vid_windowed commands).
    private static void ToggleFullscreen()
    {
        SndDma.LocalSound("misc/menu1.wav"u8);
        VidWindow.SetMode(!VidWindow.IsFullscreen, (int)VidWindow.vid_width->Value, (int)VidWindow.vid_height->Value);
    }

    // Not in the original -- keeps vid_scroll_top's visible window
    // containing the selected resolution after a wraparound move (e.g.
    // wrapping from the Fullscreen row up to the last entry snaps the
    // window straight to the bottom). The Fullscreen row scrolls the list
    // back to the top.
    private static void ScrollToSelection(int count)
    {
        int visibleRows = Math.Min(VID_VISIBLE_ROWS, count);
        int modeIndex = vid_line - 1;

        if (modeIndex < 0)
        {
            vid_scroll_top = 0;
        }
        else if (modeIndex < vid_scroll_top)
        {
            vid_scroll_top = modeIndex;
        }
        else if (modeIndex >= vid_scroll_top + visibleRows)
        {
            vid_scroll_top = modeIndex - visibleRows + 1;
        }

        vid_scroll_top = Math.Clamp(vid_scroll_top, 0, Math.Max(0, count - visibleRows));
    }
}
