// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dstatement_t
[StructLayout(LayoutKind.Sequential)]
public struct DStatement_t
{
    public ushort Op;
    public short A, B, C;
}
