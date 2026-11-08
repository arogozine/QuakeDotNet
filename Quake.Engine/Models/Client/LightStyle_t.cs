// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// lightstyle_t (client.h) -- animated light-style strings ('a'..'z' per
// tenth-second frame, e.g. flickering torches); cl_lightstyle is populated
// by cl_parse.c's CL_ParseLightStyle (ClParse.ParseLightStyle). Starts as
// plain zero-initialized data (length 0 -> R_AnimateLight treats every style
// as full bright, matching the original's own de-facto behavior before any
// server message arrives).
[StructLayout(LayoutKind.Sequential)]
public unsafe struct LightStyle_t
{
    public int Length;
    public fixed byte Map[QuakeDef.MAX_STYLESTRING];
}
