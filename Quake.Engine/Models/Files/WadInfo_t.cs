// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// wadinfo_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WadInfo_t
{
    public fixed byte Identification[4]; // should be WAD2 or 2DAW
    public int NumLumps;
    public int InfoTableOfs;
}
