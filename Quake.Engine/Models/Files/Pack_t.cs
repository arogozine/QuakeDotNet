// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine.Memory;

namespace Quake.Engine;

// pack_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Pack_t
{
    public fixed byte FileName[QuakeDef.MAX_OSPATH];
    public int Handle;
    public int NumFiles;
    public PackFile_t* Files;
}
