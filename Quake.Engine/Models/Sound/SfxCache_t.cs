// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// sfxcache_t -- variable-sized: sample data follows immediately after the
// header (like C's "byte data[1]"). Access it via (byte*)(sc + 1).
[StructLayout(LayoutKind.Sequential)]
public struct SfxCache_t
{
    public int Length;
    public int LoopStart;
    public int Speed;
    public int Width;
    public int Stereo;
}
