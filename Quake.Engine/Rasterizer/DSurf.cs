// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// d_surf.c: rasterization driver surface heap manager.
/// This is a bump/ring allocator over a fixed buffer handed to D_InitCaches
/// -- a different allocator from Memory/Cache.cs's LRU cache (that one backs
/// the Hunk; this one backs the surface texture cache specifically), ported
/// faithfully rather than reusing Memory/Cache.cs.
///
/// D_CacheSurface's actual pixel generation is R_DrawSurface (r_surf.c),
/// which builds/tiles/lights the surface texture from BSP data -- that's
/// r_*.c scene-layer scope (RSurf.cs), called directly from D_CacheSurface.
/// </summary>
public static unsafe class DSurf
{
    private const int GUARDSIZE = 4;

    public static float surfscale;
    public static bool r_cache_thrash; // set if surface cache is thrashing

    public static int sc_size;
    public static SurfCache_t* sc_base;

    // r_drawsurf (drawsurf_t, d_iface.h)
    public static DrawSurf_t r_drawsurf;

    // D_SurfaceCacheForRes
    public static int SurfaceCacheForRes(int width, int height)
    {
        int parm = Com.CheckParm("-surfcachesize"u8);
        if (parm != 0)
            return Q.Atoi(Com.com_argv[parm + 1]) * 1024;

        int size = DLocal.SURFCACHE_SIZE_AT_320X200;

        int pix = width * height;
        if (pix > 64000)
            size += (pix - 64000) * 3;

        return size;
    }


    // D_CheckCacheGuard
    public static void CheckCacheGuard()
    {
        byte* s = (byte*)sc_base + sc_size;
        for (int i = 0; i < GUARDSIZE; i++)
            if (s[i] != (byte)i)
                Sys.Error("D_CheckCacheGuard: failed"u8);
    }

    // D_ClearCacheGuard
    public static void ClearCacheGuard()
    {
        byte* s = (byte*)sc_base + sc_size;
        for (int i = 0; i < GUARDSIZE; i++)
            s[i] = (byte)i;
    }

    // D_InitCaches
    public static void InitCaches(void* buffer, int size)
    {
        Con.Printf($"{size / 1024}k surface cache\n");

        sc_size = size - GUARDSIZE;
        sc_base = (SurfCache_t*)buffer;
        DLocal.sc_rover = sc_base;

        sc_base->Next = null;
        sc_base->Owner = null;
        sc_base->Size = sc_size;

        ClearCacheGuard();
    }

    // D_FlushCaches
    public static void FlushCaches()
    {
        if (sc_base == null)
            return;

        for (SurfCache_t* c = sc_base; c != null; c = c->Next)
            if (c->Owner != null)
                *c->Owner = null;

        DLocal.sc_rover = sc_base;
        sc_base->Next = null;
        sc_base->Owner = null;
        sc_base->Size = sc_size;
    }

    // D_SCAlloc
    public static SurfCache_t* SCAlloc(int width, int size)
    {
        if (width < 0 || width > 256)
            Sys.Error($"D_SCAlloc: bad cache width {width}\n");

        if (size <= 0 || size > 0x10000)
            Sys.Error($"D_SCAlloc: bad cache size {size}\n");

        size = sizeof(SurfCache_t) - 4 + size; // offsetof(surfcache_t, data) + size
        size = (size + 3) & ~3;
        if (size > sc_size)
            Sys.Error($"D_SCAlloc: {size} > cache size");

        bool wrappedThisTime = false;

        if (DLocal.sc_rover == null || (byte*)DLocal.sc_rover - (byte*)sc_base > sc_size - size)
        {
            if (DLocal.sc_rover != null)
                wrappedThisTime = true;
            DLocal.sc_rover = sc_base;
        }

        SurfCache_t* @new = DLocal.sc_rover;
        if (DLocal.sc_rover->Owner != null)
            *DLocal.sc_rover->Owner = null;

        while (@new->Size < size)
        {
            DLocal.sc_rover = DLocal.sc_rover->Next;
            if (DLocal.sc_rover == null)
                Sys.Error("D_SCAlloc: hit the end of memory"u8);
            if (DLocal.sc_rover->Owner != null)
                *DLocal.sc_rover->Owner = null;

            @new->Size += DLocal.sc_rover->Size;
            @new->Next = DLocal.sc_rover->Next;
        }

        if (@new->Size - size > 256)
        {
            DLocal.sc_rover = (SurfCache_t*)((byte*)@new + size);
            DLocal.sc_rover->Size = @new->Size - size;
            DLocal.sc_rover->Next = @new->Next;
            DLocal.sc_rover->Width = 0;
            DLocal.sc_rover->Owner = null;
            @new->Next = DLocal.sc_rover;
            @new->Size = size;
        }
        else
        {
            DLocal.sc_rover = @new->Next;
        }

        @new->Width = (uint)width;
        if (width > 0)
            @new->Height = (uint)((size - sizeof(SurfCache_t) + 4) / width);

        @new->Owner = null; // should be set properly after return

        if (DLocal.d_roverwrapped)
        {
            if (wrappedThisTime || (byte*)DLocal.sc_rover >= (byte*)DLocal.d_initial_rover)
                r_cache_thrash = true;
        }
        else if (wrappedThisTime)
        {
            DLocal.d_roverwrapped = true;
        }

        CheckCacheGuard(); // DEBUG
        return @new;
    }

    // D_CacheSurface
    public static SurfCache_t* CacheSurface(MSurface_t* surface, int miplevel)
    {
        // if the surface is animating or flashing, flush the cache
        r_drawsurf.Texture = RSurf.TextureAnimation(surface->TexInfo->Texture);
        r_drawsurf.LightAdj[0] = RShared.d_lightstylevalue[surface->Styles[0]];
        r_drawsurf.LightAdj[1] = RShared.d_lightstylevalue[surface->Styles[1]];
        r_drawsurf.LightAdj[2] = RShared.d_lightstylevalue[surface->Styles[2]];
        r_drawsurf.LightAdj[3] = RShared.d_lightstylevalue[surface->Styles[3]];

        // see if the cache holds appropriate data
        SurfCache_t* cache = surface->CacheSpots[miplevel];

        if (cache != null && cache->DLight == 0 && surface->DLightFrame != DIface.r_framecount
            && cache->Texture == r_drawsurf.Texture
            && cache->LightAdj[0] == r_drawsurf.LightAdj[0]
            && cache->LightAdj[1] == r_drawsurf.LightAdj[1]
            && cache->LightAdj[2] == r_drawsurf.LightAdj[2]
            && cache->LightAdj[3] == r_drawsurf.LightAdj[3])
            return cache;

        // determine shape of surface
        surfscale = 1.0f / (1 << miplevel);
        r_drawsurf.SurfMip = miplevel;
        r_drawsurf.SurfWidth = surface->Extents[0] >> miplevel;
        r_drawsurf.RowBytes = r_drawsurf.SurfWidth;
        r_drawsurf.SurfHeight = surface->Extents[1] >> miplevel;

        // allocate memory if needed
        if (cache == null) // if a texture just animated, don't reallocate it
        {
            cache = SCAlloc(r_drawsurf.SurfWidth, r_drawsurf.SurfWidth * r_drawsurf.SurfHeight);
            surface->CacheSpots[miplevel] = cache;
            cache->Owner = surface->CacheSpots.AddressOf(miplevel);
            cache->MipScale = surfscale;
        }

        cache->DLight = surface->DLightFrame == DIface.r_framecount ? 1 : 0;

        r_drawsurf.SurfDat = cache->Data;

        cache->Texture = r_drawsurf.Texture;
        cache->LightAdj[0] = r_drawsurf.LightAdj[0];
        cache->LightAdj[1] = r_drawsurf.LightAdj[1];
        cache->LightAdj[2] = r_drawsurf.LightAdj[2];
        cache->LightAdj[3] = r_drawsurf.LightAdj[3];

        // draw and light the surface texture
        r_drawsurf.Surf = surface;

        RMain.c_surf++;
        RSurf.DrawSurface();

        return surface->CacheSpots[miplevel];
    }
}

