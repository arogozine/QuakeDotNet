// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

// cache_system_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CacheSystem
{
    public int Size;            // including this header
    public CacheUser* User;
    public fixed byte Name[16];
    public CacheSystem* Prev;
    public CacheSystem* Next;
    public CacheSystem* LruPrev;
    public CacheSystem* LruNext;   // for LRU flushing
}
