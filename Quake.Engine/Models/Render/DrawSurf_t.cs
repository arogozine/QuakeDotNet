// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// drawsurf_t (d_iface.h) -- callback-to-Quake struct filled in by
// D_CacheSurface and consumed by the R_DrawSurface hook.
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public unsafe struct DrawSurf_t
{
    public byte* SurfDat;   // destination for generated surface
    public int RowBytes;    // destination logical width in bytes
    public MSurface_t* Surf; // description for surface to generate
    public fixed int LightAdj[BspFile.MAXLIGHTMAPS]; // adjust for lightmap levels for dynamic lighting
    public Texture_t* Texture; // corrected for animating textures
    public int SurfMip;     // mipmapped ratio of surface texels / world pixels
    public int SurfWidth;   // in mipmapped texels
    public int SurfHeight;  // in mipmapped texels
}
