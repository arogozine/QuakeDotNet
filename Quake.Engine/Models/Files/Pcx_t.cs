// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// pcx_t (screen.c's SCR_ScreenShot_f screenshot format)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Pcx_t
{
    public byte Manufacturer;
    public byte Version;
    public byte Encoding;
    public byte BitsPerPixel;
    public ushort Xmin, Ymin, Xmax, Ymax;
    public ushort Hres, Vres;
    public fixed byte Palette[48];
    public byte Reserved;
    public byte ColorPlanes;
    public ushort BytesPerLine;
    public ushort PaletteType;
    public fixed byte Filler[58];
    public byte Data; // unbounded -- &pcx->Data is the start of the packed image
}
