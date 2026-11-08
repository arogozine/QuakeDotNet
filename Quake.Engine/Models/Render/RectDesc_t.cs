// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine.Memory;

namespace Quake.Engine;

// rectdesc_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RectDesc_t
{
    public VRect_t Rect;
    public int Width;
    public int Height;
    public byte* PTexBytes;
    public int RowBytes;
}
