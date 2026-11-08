// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

// memblock_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MemBlock
{
    public int Size;      // including the header and possibly tiny fragments
    public int Tag;       // a tag of 0 is a free block
    public int Id;        // should be ZONEID
    public MemBlock* Next;
    public MemBlock* Prev;
    public int Pad;       // pad to 64 bit boundary
}
