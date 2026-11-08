// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

// ddef_t
[StructLayout(LayoutKind.Sequential)]
public struct DDef_t
{
    public ushort Type; // if DEF_SAVEGLOBAL bit is set the variable needs to be saved in savegames
    public ushort Ofs;
    public int S_Name;
}
