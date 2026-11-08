// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// qpic_t (variable-length: Data is width*height bytes in practice, not a fixed 4)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct QPic_t
{
    public int Width;
    public int Height;
    public fixed byte Data[4];
}
