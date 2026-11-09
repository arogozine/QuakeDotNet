// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// dlight_t (client.h)
[StructLayout(LayoutKind.Sequential)]
public struct DLight_t
{
    public Vec3_t Origin;
    public float Radius;
    public float Die;      // stop lighting after this time
    public float Decay;    // drop this each second
    public float MinLight; // don't add when contributing less
    public int Key;
}
