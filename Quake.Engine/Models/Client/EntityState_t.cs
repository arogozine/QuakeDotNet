// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

// entity_state_t (quakedef.h)
[StructLayout(LayoutKind.Sequential)]
public struct EntityState_t
{
    public Vec3_t Origin;
    public Vec3_t Angles;
    public int ModelIndex;
    public int Frame;
    public int ColorMap;
    public int Skin;
    public int Effects;
    public byte Alpha;      // johnfitz -- PROTOCOL_FITZQUAKE; ModelIndex/Frame stay int (wire width is per-protocol)
}
