// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// daliasframe_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DAliasFrame_t
{
    public TriVertX_t BboxMin; // lightnormal isn't used
    public TriVertX_t BboxMax; // lightnormal isn't used
    public fixed byte Name[16]; // frame name from grabbing
}
