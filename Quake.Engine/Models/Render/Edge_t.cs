// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// edge_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Edge_t
{
    // fixed16_t in the original, but actually 12.20 fixed point (see
    // RDraw.EmitEdge's "* 0x100000"). Widened from int to long (Rule 8):
    // a 32-bit 12.20 value only reaches x = 2047, and RShared.MAXWIDTH is
    // 3840 in this port -- at 2560 wide, edge_tail's U wrapped negative,
    // broke the sorted edge list, and R_StepActiveU walked off edge_head.
    // Same 20 fraction bits, so results are identical below 2048 wide.
    public long U;
    public long UStep;
    public Edge_t* Prev, Next;
    public fixed ushort Surfs[2];
    public Edge_t* NextRemove;
    public float NearZi;
    public MEdge_t* Owner;
}
