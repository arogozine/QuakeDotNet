// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

// hunk_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HunkHeader
{
    public int Sentinel;
    public int Size;        // including sizeof(hunk_t), -1 = not allocated
    public fixed byte Name[8];
}
