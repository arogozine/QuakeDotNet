// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// maliasskingroup_t (skindescs[1] is likewise a flexible-array member)
[StructLayout(LayoutKind.Sequential)]
public struct MAliasSkinGroup_t
{
    public int NumSkins;
    public int Intervals; // byte offset relative to the aliashdr_t
    public MAliasSkinDesc_t SkinDescs;
}
