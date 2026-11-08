// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

// memzone_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MemZone
{
    public int Size;            // total bytes malloced, including header
    public MemBlock BlockList;  // start / end cap for linked list
    public MemBlock* Rover;
}
