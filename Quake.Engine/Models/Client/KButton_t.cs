// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;

namespace Quake.Engine;

// kbutton_t (client.h)
public unsafe struct KButton_t
{
    public fixed int Down[2]; // key nums holding it down
    public int State;         // low bit is down state, bits 1/2 are edges
}
