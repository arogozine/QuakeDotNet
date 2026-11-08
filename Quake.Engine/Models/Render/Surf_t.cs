// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// surf_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Surf_t
{
    public Surf_t* Next;      // active surface stack in r_edge.c
    public Surf_t* Prev;      // used in r_edge.c for active surf stack
    public ESpan_t* Spans;    // pointer to linked list of spans to draw
    public int Key;           // sorting key (BSP order)
    public int LastU;         // set during tracing
    public int SpanState;     // 0 = not in span, 1 = in span, -1 = in inverted span
    public int Flags;         // currentface flags
    public void* Data;        // associated data like msurface_t
    public Entity_t* Entity;
    public float NearZi;      // nearest 1/z on surface, for mipmapping
    public bool InSubmodel;
    public float DZiOrigin, DZiStepU, DZiStepV;
    public fixed int Pad[2];  // to 64 bytes
}
