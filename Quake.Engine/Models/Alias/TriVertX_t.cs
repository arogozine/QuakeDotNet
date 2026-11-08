// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// trivertx_t
// This mirrors trivert_t in trilib.h, is present so Quake knows how to load
// this data.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TriVertX_t
{
    public fixed byte V[3];
    public byte LightNormalIndex;
}
