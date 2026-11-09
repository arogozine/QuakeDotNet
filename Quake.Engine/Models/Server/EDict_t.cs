// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// edict_t (progs.h)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EDict_t
{
    // MAX_ENT_LEAFS (progs.h) -- Mark V MAX_MARK_V_ENT_LEAFS (was 16)
    public const int MAX_ENT_LEAFS = 32;

    public int Free;    // qboolean
    public Link_t Area; // linked to a division node or leaf

    public int NumLeafs;
    public fixed short LeafNums[MAX_ENT_LEAFS];

    public EntityState_t Baseline;

    public byte Alpha;         // johnfitz -- alpha (ENTALPHA_*)
    public bool SendInterval;  // johnfitz -- send time until nextthink to client for better lerp timing

    public float FreeTime; // sv.time when the object was freed
    public EntVars_t V;    // C exported fields from progs
    // other fields from progs come immediately after: progs->entityfields*4
    // bytes in total (195 fields for the stock id1 progs.dat, versus 105 in
    // EntVars_t), so the real edict size is Progs.pr_edict_size, never
    // sizeof(EDict_t). Always address edicts through Progs.EdictNum and clear
    // them with progs->entityfields*4, not sizeof(EntVars_t).
}
