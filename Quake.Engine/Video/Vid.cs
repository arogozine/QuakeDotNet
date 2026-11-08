// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Quake.Engine;

/// <summary>
/// vid.h: video driver defs. Only vid.buffer/rowbytes/width/height are ported
/// here (needed directly by D_FillRect and D_ViewChanged), plus the
/// OS-agnostic D_BeginDirectRect/D_EndDirectRect used by Draw_BeginDisc/
/// Draw_EndDisc; VID_Init and the
/// rest of the palette/window setup live in Quake.Desktop/VidWindow.cs.
/// </summary>
public static unsafe class Vid
{
    // vid
    public static VidDef_t vid = new() { NumPages = 1 };

    public const int VID_CBITS = 6;
    public const int VID_GRADES = 1 << VID_CBITS;

    // d_8to16table -- 8-bit palette index to 16-bit truecolor lookup.
    public static readonly ushort* d_8to16table = NativeMemory.AllocArrayZeroed<ushort>(256);

    // d_8to24table -- this port's RGBA32 stand-in for the hardware palette
    // VID_SetPalette originally programmed directly into the GDI/MGL
    // device (there is no such original global by this name in vid_win.c;
    // it borrows gl_vidnt.c's naming for the same idea -- a 256-entry
    // index -> truecolor table). Quake.Desktop's window layer uploads this
    // every frame as a 256x1 RGBA palette texture, and its fragment shader
    // looks up Vid.vid.Buffer's indexed bytes in it on the GPU. Kept here
    // (not in Quake.Desktop) so SetPalette below stays unit-testable from
    // Quake.UnitTests without a cross-project internals-sharing hack
    public static readonly uint* d_8to24table = NativeMemory.AllocSimdAlignedArray<uint>(256);

    // VID_SetPalette (vid_win.c) -- OS-agnostic subset only: rebuilds
    // d_8to24table from a 256*3 RGB palette. Skips the Win32 GDI static-
    // color/SYSPAL_NOSTATIC remapping entirely (no such palette manager
    // exists once colors are just RGBA32 texture data) and the WM_SYSCOLORCHANGE
    // broadcast (no other windows to notify).
    public static void SetPalette(byte* palette)
    {
        int i = 0;

        // 4 entries (12 RGB bytes) per step: load 16, shuffle to RGBx x4, set
        // alpha. The last load of the loop ends at byte 252*3 + 4 = 760 <= 768.
        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<byte> idx = Vector128.Create((byte)0, 1, 2, 0x80, 3, 4, 5, 0x80, 6, 7, 8, 0x80, 9, 10, 11, 0x80);
            Vector128<uint> alpha = Vector128.Create(0xFF000000u);
            for (; i < 252; i += 4)
                (Vector128.Shuffle(Vector128.Load(palette + i * 3), idx).AsUInt32() | alpha).Store(d_8to24table + i);
        }

        for (; i < 256; i++)
        {
            byte r = palette[i * 3];
            byte g = palette[i * 3 + 1];
            byte b = palette[i * 3 + 2];
            d_8to24table[i] = (uint)(r | (g << 8) | (b << 16) | (0xFFu << 24));
        }
    }

    // VID_ShiftPalette -- identical to VID_SetPalette in the original (the
    // DIB/MGL path has no separate "shift" step); RMisc.WarpPalette/screen-
    // flash effects call this and now see it take effect via d_8to24table.
    public static void ShiftPalette(byte* palette)
    {
        SetPalette(palette);
    }

    // backingbuf (vid_win.c) -- saved screen contents under the direct rect;
    // 48 rows so the doubled-row (aspect > 1.5) case of a 24x24 icon fits.
    internal static readonly byte* backingbuf = NativeMemory.AllocArrayZeroed<byte>(48 * 24);

    /*
    ================
    D_BeginDirectRect (vid_win.c)

    vid.numpages == 1 path only: this port always has a single software
    page, so the MGL multi-page branch has no equivalent. VID_LockBuffer/
    VID_UnlockBuffer are no-ops here (vid.direct is plain memory), and
    FlipScreen(&rect) becomes a full Screen.VID_Update -- the backend only
    knows how to present the whole buffer. vid_initialized is approximated
    by vid.direct being non-null, since VidWindow sets it during VID_Init
    and clears it in VID_Shutdown; this also keeps the call a no-op with no
    display backend (unit tests, files loaded before VID_Init).
    ================
    */
    public static void D_BeginDirectRect(int x, int y, byte* pbitmap, int width, int height)
    {
        if (vid.Direct == null)
            return;

        int reps, repshift;
        if (vid.Aspect > 1.5f)
        {
            reps = 2;
            repshift = 1;
        }
        else
        {
            reps = 1;
            repshift = 0;
        }

        for (int i = 0; i < (height << repshift); i += reps)
        {
            for (int j = 0; j < reps; j++)
            {
                byte* dest = vid.Direct + x + ((y << repshift) + i + j) * vid.RowBytes;
                Buffer.MemoryCopy(dest, &backingbuf[(i + j) * 24], width, width);
                Buffer.MemoryCopy(&pbitmap[(i >> repshift) * width], dest, width, width);
            }
        }

        Screen.VID_Update();
    }

    /*
    ================
    D_EndDirectRect (vid_win.c)

    See D_BeginDirectRect for what is dropped.
    ================
    */
    public static void D_EndDirectRect(int x, int y, int width, int height)
    {
        if (vid.Direct == null)
            return;

        int reps, repshift;
        if (vid.Aspect > 1.5f)
        {
            reps = 2;
            repshift = 1;
        }
        else
        {
            reps = 1;
            repshift = 0;
        }

        for (int i = 0; i < (height << repshift); i += reps)
        {
            for (int j = 0; j < reps; j++)
            {
                Buffer.MemoryCopy(&backingbuf[(i + j) * 24],
                    vid.Direct + x + ((y << repshift) + i + j) * vid.RowBytes,
                    width, width);
            }
        }

        Screen.VID_Update();
    }
}
