// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// spanpackage_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SpanPackage_t
{
    public void* PDest;
    public short* Pz;
    public int Count;
    public byte* PTex;
    public int Sfrac, Tfrac, Light, Zi;
}
