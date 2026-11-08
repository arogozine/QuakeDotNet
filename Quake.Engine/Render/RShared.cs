// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// r_shared.h: general refresh-related stuff shared between the refresh
/// engine and the rasterization driver -- the pieces the d_*.c rasterization
/// bodies read directly, plus the active-edge-table scan-conversion state
/// and view-basis globals that the r_*.c scene layer (RMain.cs/RBsp.cs/
/// REdge.cs) owns and populates each frame.
/// </summary>
public static unsafe class RShared
{
    // r_shared.h originally capped these at 1024x1280 (d_ifacea.h redefined
    // MAXHEIGHT to the same 1024). Raised to a 4K ceiling (3840x2160) so the
    // scan-conversion tables/stackallocs sized off these (d_scantable,
    // zspantable, newedges/removeedges, DPolyse.cs's DPS_MAXSPANS,
    // DSprite.cs/DScan.cs's stackallocs) can address modern resolutions --
    // see VidMenu.cs/DisplayModeSource.cs for the resolution list this feeds.
    public const int MAXHEIGHT = 2160;
    public const int MAXWIDTH = 3840;
    public const int MAXDIMENSION = MAXHEIGHT > MAXWIDTH ? MAXHEIGHT : MAXWIDTH;

    // MAXVERTS -- max points in a surface polygon
    public const int MAXVERTS = 16;
    // MAXWORKINGVERTS -- max points in an intermediate polygon (while processing)
    public const int MAXWORKINGVERTS = MAXVERTS + 4;

    // INFINITE_DISTANCE -- distance that's always guaranteed to be farther
    // away than anything in the scene.
    public const int INFINITE_DISTANCE = 0x10000;

    // cachewidth
    public static int cachewidth;

    // cacheblock
    public static byte* cacheblock;

    // screenwidth
    public static int screenwidth;

    // pixelAspect
    public static float pixelAspect;

    // xcenter, ycenter / xscale, yscale / xscaleinv, yscaleinv
    // Owned by r_main.c's view-setup code (RMisc.SetupFrame), populated
    // each frame; declared here so the d_*.c gradient/particle math that
    // reads them directly compiles and lives alongside its other shared state.
    public static float xcenter, ycenter;
    public static float xscale, yscale;
    public static float xscaleinv, yscaleinv;

    // modelorg, base_modelorg -- owned by r_main.c/r_bsp.c (RMisc.cs/RBsp.cs),
    // declared minimally for the same reason as xcenter/ycenter above.
    public static Vec3_t modelorg, base_modelorg;

    // r_origin -- r_local.h; needed directly by D_DrawParticle.
    public static Vec3_t r_origin;

    // vup/base_vup, vpn/base_vpn, vright/base_vright -- the current view
    // basis (and its un-rolled base). r_main.c's R_SetupFrame (RMisc.cs)
    // computes these each frame; TransformVector's real implementation
    // (also RMisc.cs) rotates by dot products against these.
    public static Vec3_t vup, base_vup;
    public static Vec3_t vpn, base_vpn;
    public static Vec3_t vright, base_vright;

    // currententity -- entity_t* of the model currently being drawn.
    public static Entity_t* currententity;

    // xscaleshrink, yscaleshrink -- r_local.h
    public static float xscaleshrink, yscaleshrink;

    // r_drawnpolycount
    public static int r_drawnpolycount;

    //===================================================================
    // Active-edge-table scan-conversion state (r_edge.c-owned; declared
    // here per r_shared.h so RShared stays the single source for the
    // renderer/driver-shared globals).

    public const int NUMSTACKEDGES = 2400;
    public const int MINEDGES = NUMSTACKEDGES;
    public const int NUMSTACKSURFACES = 800;
    public const int MINSURFACES = NUMSTACKSURFACES;

    // r_shared.h originally fixed this at 3000, independent of MAXWIDTH
    // (1280) but always bigger than it. REdge.ScanEdges relies on that:
    // max_span_p = &basespans[MAXSPANS - Vrect.Width], which underflows
    // (wraps to a pointer before the buffer, corrupting the edge list once
    // spans overrun it) once Vrect.Width >= MAXSPANS. Raising MAXWIDTH to
    // 3840 for 4K without also raising this blew past the original 3000
    // ceiling well before reaching the new resolution ceiling -- scaled by
    // the same ~2.34 (3000/1280) headroom ratio the original had so the
    // buffer keeps the same safety margin relative to MAXWIDTH.
    public const int MAXSPANS = MAXWIDTH * 3000 / 1280;

    // surfaces, surface_p, surf_max -- surfaces are generated in back to
    // front order by the bsp, so if a surf pointer is greater than another
    // one, it should be drawn in front. surfaces[1] is the background, and
    // is used as the active surface stack. surfaces[0] is a dummy, because
    // index 0 is used to indicate no surface attached to an edge_t.
    public static Surf_t* surfaces, surface_p, surf_max;

    // r_edges, edge_p, edge_max
    public static Edge_t* r_edges, edge_p, edge_max;

    public static readonly Edge_t** newedges = NativeMemory.AllocPointerArray<Edge_t>(MAXHEIGHT);
    public static readonly Edge_t** removeedges = NativeMemory.AllocPointerArray<Edge_t>(MAXHEIGHT);

    // FIXME: make stack vars when debugging done
    // The original's edge_t globals are natively allocated here so their
    // addresses are stable (the edge list links to them by pointer).
    public static readonly Edge_t* edge_head = NativeMemory.AllocZeroed<Edge_t>();
    public static readonly Edge_t* edge_tail = NativeMemory.AllocZeroed<Edge_t>();
    public static readonly Edge_t* edge_aftertail = NativeMemory.AllocZeroed<Edge_t>();

    public static Edge_t* auxedges;
    public static int r_numallocatededges;

    // sintable, intsintable -- turbulent-warp lookup tables, needed by
    // Turbulent8. Populated with sin()-based values by r_main.c's
    // R_InitTurb (RMain.InitTurb), called from RMain.Init.
    public const int SIN_BUFFER_SIZE = MAXDIMENSION + DIface.CYCLE;
    public static readonly int* sintable = NativeMemory.AllocArrayZeroed<int>(SIN_BUFFER_SIZE);
    public static readonly int* intsintable = NativeMemory.AllocArrayZeroed<int>(SIN_BUFFER_SIZE);

    // cl.time -- needed by Turbulent8 for the warp animation phase. Kept as
    // its own field rather than reading Cl.time directly; assigned from
    // Cl.time every frame in RMisc.SetupFrame.
    public static float cl_time;

    // d_lightstylevalue -- 8.8 frac of base light value; needed by D_CacheSurface.
    public static readonly int* d_lightstylevalue = NativeMemory.AllocArrayZeroed<int>(256);

    // r_refdef (refdef_t, render.h) -- populated every frame by r_main.c
    // (RMisc.SetupFrame). Only the subset of fields the ported d_*.c bodies
    // read directly is declared here (D_ViewChanged, D_PolysetDrawFinalVerts,
    // D_SpriteScanRightEdge, D_DrawParticle indirectly via d_vrect* set from
    // this).
    public static readonly RRefDef_t* r_refdef = (RRefDef_t*)NativeMemory.AllocZeroed((nuint)sizeof(RRefDef_t));
}

