// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// packfile_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PackFile_t
{
    public fixed byte Name[QuakeDef.MAX_QPATH];
    public int FilePos;
    public int FileLen;
}
