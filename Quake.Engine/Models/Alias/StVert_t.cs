// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// stvert_t
// TODO (original comment): could be shorts
[StructLayout(LayoutKind.Sequential)]
public struct StVert_t
{
    public int OnSeam;
    public int S;
    public int T;
}
