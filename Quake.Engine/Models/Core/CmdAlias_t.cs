// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// cmdalias_s / cmdalias_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CmdAlias_t
{
    public CmdAlias_t* Next;
    public fixed byte Name[32]; // MAX_ALIAS_NAME
    public byte* Value;
}
