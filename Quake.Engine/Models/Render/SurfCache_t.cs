// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// surfcache_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SurfCache_t
{
    public SurfCache_t* Next;
    public SurfCache_t** Owner; // null is an empty chunk of memory
    public fixed int LightAdj[BspFile.MAXLIGHTMAPS]; // checked for strobe flush
    public int DLight;
    public int Size;            // including header
    public uint Width;
    public uint Height;         // DEBUG only needed for debug
    public float MipScale;
    public Texture_t* Texture;  // checked for animating textures
    public fixed byte Data[4];  // width*height elements
}
