// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// efrag_t (refresh.h)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFrag_t
{
    public MLeaf_t* Leaf;
    public EFrag_t* LeafNext;
    public Entity_t* Entity;
    public EFrag_t* EntNext;
}
