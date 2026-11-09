// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// miptex_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MipTex_t
{
    public fixed byte Name[16];
    public uint Width, Height;
    public fixed uint Offsets[BspFile.MIPLEVELS]; // four mip maps stored
}
