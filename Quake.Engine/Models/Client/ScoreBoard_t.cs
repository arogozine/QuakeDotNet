// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

// scoreboard_t (client.h)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ScoreBoard_t
{

    public fixed byte Name[MAX_SCOREBOARDNAME];
    public float Entertime;
    public int Frags;
    public int Colors;                        // two 4 bit fields
    public fixed byte Translations[Vid.VID_GRADES * 256];
}
