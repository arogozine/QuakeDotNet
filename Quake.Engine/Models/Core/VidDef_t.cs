// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// viddef_t
// Minimal port: only the fields the rasterization layer (D_FillRect,
// D_ViewChanged) and r_main.c's R_SetupFrame (RMisc.cs) read directly. The
// full video subsystem (VID_Init/VID_SetMode/etc, window/surface creation)
// is Quake.Desktop/VidWindow.cs's OpenTK display layer, which populates
// this struct's fields (Buffer/RowBytes/Colormap/ConBuffer/Direct/NumPages/...)
// at startup rather than redefining them elsewhere, so callers keep a
// single VidDef_t layout.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VidDef_t
{
    public byte* Buffer;    // invisible buffer
    public uint RowBytes;   // may be > width if displayed in a window
    public uint Width;
    public uint Height;
    public float Aspect;    // width / height -- < 0 is taller than wide
    public int MaxWarpWidth;
    public int MaxWarpHeight;

    // colormap, colormap16 -- 256 * VID_GRADES light-scaled palette tables.
    // Colormap is set by Quake.Desktop's VidWindow.cs (Vid.vid.Colormap =
    // Host.host_colormap) before any pixel that reads through it
    // (R_DrawSurfaceBlock8_mip*) is drawn. Colormap16 stays unset -- the
    // 16-bit path (R_DrawSurfaceBlock16) is dead code today since
    // DInit.Init always sets DIface.r_pixbytes = 1 (8-bit only), matching
    // the original's own behavior outside a 16bpp video mode.
    public byte* Colormap;
    public ushort* Colormap16;

    // conbuffer, conrowbytes, conwidth, conheight -- separate console-
    // resolution framebuffer view used by Draw_Character/Draw_ConsoleBackground
    // (the console can render at a different resolution than the 3D view).
    // direct -- raw framebuffer pointer for Draw_DebugChar; null means "no
    // direct FB access", matching the original's own null-check gate at
    // that call site. Both populated by Quake.Desktop's VidWindow.cs.
    public byte* ConBuffer;
    public uint ConRowBytes;
    public uint ConWidth;
    public uint ConHeight;
    public byte* Direct;

    // numpages -- number of screen buffers the display driver double/triple
    // buffers across; screen.c's clear/notify-line countdown logic compares
    // against it. recalc_refdef -- set whenever a vid-affecting cvar
    // changes, telling SCR_UpdateScreen to recompute r_refdef via
    // SCR_CalcRefdef. Both populated by Quake.Desktop's VidWindow.cs; Vid.vid
    // below default-initializes NumPages to 1 regardless (the minimum sane
    // value -- a zeroed default would make every "< vid.numpages" countdown
    // in screen.c permanently false).
    public int NumPages;
    public int RecalcRefdef;
}
