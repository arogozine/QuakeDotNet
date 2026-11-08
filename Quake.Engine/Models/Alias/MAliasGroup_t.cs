// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// maliasgroup_t
// frames[1] in the original is a flexible-array member: only Frames (index 0)
// is declared here, and Hunk_AllocName reserves numframes-1 additional
// contiguous slots that are reached via pointer arithmetic past it.
[StructLayout(LayoutKind.Sequential)]
public struct MAliasGroup_t
{
    public int NumFrames;
    public int Intervals; // byte offset relative to the aliashdr_t
    public MAliasGroupFrameDesc_t Frames;
}
