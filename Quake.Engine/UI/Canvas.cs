// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Draw_SetCanvas canvas types -- Mark V gl_draw.c / draw.h (CANVAS_*).
public enum CanvasType
{
    Default,   // CANVAS_DEFAULT -- physical pixels, scale 1
    Console,   // CANVAS_CONSOLE / CANVAS_DEFAULT_CONSCALE -- console text, centerprint, notify
    Menu,      // CANVAS_MENU -- 320x200 virtual area centred in x and y
    Sbar,      // CANVAS_SBAR -- bottom-anchored status bar
    Crosshair, // CANVAS_CROSSHAIR
}

/// <summary>
/// Not in WinQuake. Port of Mark V's Draw_SetCanvas (gl_draw.c) and the
/// scr_scaleauto block of SCR_UpdateScreen (screen.c) to the software
/// renderer: the 8-bit buffer stays 1:1 with the window and the draw
/// primitives (Draw.cs) expand every virtual pixel into a Scale x Scale block.
/// Callers lay out in virtual coordinates (Width / Height below).
/// </summary>
public static class Canvas
{
    // Current canvas, applied by Draw.cs. Scale 1 / origin 0 is exactly the
    // original unscaled behaviour.
    public static int Scale = 1;
    public static int OriginX;
    public static int OriginY;

    // Per-canvas integer scales, computed by Update (Mark V vid.menu_scale,
    // vid.sbar_scale, conscale).
    public static int ConScale = 1;
    public static int MenuScale = 1;
    public static int SbarScale = 1;
    public static int CrosshairScale = 1;

    public const int MENU_WIDTH = 320;
    public const int MENU_HEIGHT = 200;

    // Virtual size of the current canvas.
    public static int Width => Scale > 0 ? (int)(Vid.vid.Width / (uint)Scale) : (int)Vid.vid.Width;
    public static int Height => Scale > 0 ? (int)(Vid.vid.Height / (uint)Scale) : (int)Vid.vid.Height;

    // Draw_SetCanvas
    public static void Set(CanvasType type)
    {
        int w = (int)Vid.vid.Width;
        int h = (int)Vid.vid.Height;

        switch (type)
        {
            case CanvasType.Console:
                Scale = ConScale;
                OriginX = 0;
                OriginY = 0;
                break;

            case CanvasType.Menu:
                // centred in both axes (Mark V: cly + (clheight - 200*s) / 2)
                Scale = MenuScale;
                OriginX = Math.Max(0, (w - (MENU_WIDTH * Scale)) / 2);
                OriginY = Math.Max(0, (h - (MENU_HEIGHT * Scale)) / 2);
                break;

            case CanvasType.Sbar:
                // anchored to the bottom edge so the bar meets the 3D view
                // even when Height is not a multiple of Scale
                Scale = SbarScale;
                OriginX = 0;
                OriginY = h - ((h / Scale) * Scale);
                break;

            case CanvasType.Crosshair:
                Scale = CrosshairScale;
                OriginX = 0;
                OriginY = 0;
                break;

            default:
                Scale = 1;
                OriginX = 0;
                OriginY = 0;
                break;
        }
    }

    // Largest scale that still leaves the canvas at least 320 virtual pixels
    // wide (Mark V: vid.conwidth = CLAMP(320, ..., clwidth)).
    private static int LimitToWidth(int scale, int width) => Math.Clamp(scale, 1, Math.Max(1, width / MENU_WIDTH));

    // Pure scale math, split out for unit tests. Returns false on a
    // zero-sized window (Mark V falls back and retries next frame).
    //
    // scaleauto: -1 = off (1x), 0 = manual cvars, 1..3 = automatic (Mark V
    // clamps to this range).
    public static bool Compute(int width, int height, float scaleauto, float conscale, float menuscale, float sbarscale,
        float crosshairscale, out int con, out int menu, out int sbar, out int crosshair)
    {
        con = menu = sbar = crosshair = 1;
        crosshair = LimitToWidth((int)crosshairscale, width);

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        int size = Math.Clamp((int)scaleauto, -1, 3);

        if (size == 0)
        {
            con = LimitToWidth((int)conscale, width);
            menu = Math.Clamp((int)menuscale, 1, Math.Max(1, Math.Min(width / MENU_WIDTH, height / MENU_HEIGHT)));
            sbar = LimitToWidth((int)sbarscale, width);
            return true;
        }

        if (size < 0)
        {
            return true; // everything stays 1x
        }

        // screen.c: SCR_UpdateScreen (GLQUAKE_SCALED_DRAWING)
        int textWidth = size > 1 ? 320 : 400;
        int textHeight = size > 1 ? 200 : 320;
        int fit = Math.Max(1, Math.Min(width / textWidth, height / textHeight));
        con = LimitToWidth(fit, width);
        menu = Math.Clamp(fit, 1, Math.Max(1, Math.Min(width / MENU_WIDTH, height / MENU_HEIGHT)));

        int unitHeight = size == 3 ? 200 : size == 2 ? 280 : 320;
        sbar = LimitToWidth(height / unitHeight, width);
        return true;
    }

    // Recomputes the scales from the window size and cvars. Returns true if
    // anything changed (caller raises vid.recalc_refdef).
    public static unsafe bool Update()
    {
        int width = (int)Vid.vid.Width;
        int height = (int)Vid.vid.Height;

        if (!Compute(width, height,
                Screen.scr_scaleauto->Value, Screen.scr_conscale->Value, Screen.scr_menuscale->Value,
                Screen.scr_sbarscale->Value, Screen.scr_crosshairscale->Value,
                out int con, out int menu, out int sbar, out int crosshair))
        {
            return false;
        }

        uint conWidth = (uint)(width / con);
        uint conHeight = (uint)(height / con);

        bool changed = con != ConScale || menu != MenuScale || sbar != SbarScale || crosshair != CrosshairScale
            || conWidth != Vid.vid.ConWidth || conHeight != Vid.vid.ConHeight;

        ConScale = con;
        MenuScale = menu;
        SbarScale = sbar;
        CrosshairScale = crosshair;

        // the console resolution is the virtual console canvas (Mark V
        // vid.conwidth / vid.conheight)
        Vid.vid.ConWidth = conWidth;
        Vid.vid.ConHeight = conHeight;

        return changed;
    }
}
