// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// msurface_t.cachespots (model.h): surfcache_t *cachespots[MIPLEVELS]
// Pointer types can't be InlineArray elements (not valid generic type
// arguments), so this is a plain sequential struct of MIPLEVELS pointers,
// indexed by pointer arithmetic from the first slot (no bounds check, as in C;
// asserted in debug builds).
// AddressOf lets callers take the address of a slot (&surface->cachespots[i]
// in the original), used by D_CacheSurface's "cache->owner = ...". It is only
// valid when the array lives in pinned/native memory (e.g. reached through an
// MSurface_t*), never on a copy on the stack or managed heap.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CacheSpotArray
{
    // nint, not SurfCache_t*: Unsafe.Add<T> can't take a pointer type argument
    private nint _e0, _e1, _e2, _e3;

    public SurfCache_t* this[int i]
    {
        readonly get => (SurfCache_t*)Slot(i);
        set => Slot(i) = (nint)value;
    }

    public SurfCache_t** AddressOf(int i)
    {
        Debug.Assert((uint)i < BspFile.MIPLEVELS);

        return (SurfCache_t**)Unsafe.AsPointer(ref Slot(i));
    }

    private readonly ref nint Slot(int i)
    {
        Debug.Assert((uint)i < BspFile.MIPLEVELS);

        return ref Unsafe.Add(ref Unsafe.AsRef(in _e0), i);
    }
}
