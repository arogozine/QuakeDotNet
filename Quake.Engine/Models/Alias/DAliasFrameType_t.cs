// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// daliasframetype_t
[StructLayout(LayoutKind.Sequential)]
public struct DAliasFrameType_t
{
    public AliasFrameType_t Type;
}
