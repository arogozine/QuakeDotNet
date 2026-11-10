// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// r_main.c -- top-level refresh orchestration.
///
/// A few call targets are dropped outright (never silently changed --
/// commented at the call site): VID_LockBuffer/
/// VID_UnlockBuffer and D_TurnZOn (DirectDraw buffer-lock concerns, per the
/// existing Vid.cs/DInit.cs scoping -- unneeded by OpenTK's whole-frame
/// texture upload; D_WarpScreen itself is ported -- see DScan.WarpScreen),
/// S_ExtraUpdate (the
/// mid-render audio pump; SndDma.cs's sound mixer is ported and running,
/// but this call is unneeded since OpenAL streams on its own),
/// Sys_LowFPPrecision/Sys_HighFPPrecision (x86 FPU control-word tricks,
/// meaningless for managed doubles), and the
/// r_stack_start/Hunk-alignment stack-overflow guards in R_RenderView (no
/// raw stack pointers in managed code).
/// </summary>
public static unsafe class RMain
{
    // soldier uniform colors (render.h)
    public const int TOP_RANGE = 16;    // TOP_RANGE (render.h)
    public const int BOTTOM_RANGE = 96; // BOTTOM_RANGE (render.h)

    /*
    ===========================================================================
    Module globals (r_main.c). r_drawpolys/r_drawculledpolys/
    r_worldpolysbacktofront/r_recursiveaffinetriangles/r_pixbytes/
    r_aliasuvscale/r_dowarp/r_warpbuffer/r_framecount are DIface.cs's;
    surfaces/surface_p/surf_max/r_edges/edge_p/edge_max/auxedges/
    r_numallocatededges/NUMSTACKEDGES/MINEDGES/NUMSTACKSURFACES/MINSURFACES/
    vup/vpn/vright(+base_*)/r_origin/r_refdef/xcenter../xscaleshrink/
    d_lightstylevalue/r_drawnpolycount are step 2a's RShared.cs -- none of
    those are redeclared here.
    ===========================================================================
    */

    // colormap -- only ever assigned inside r_main.c's #if id386 block
    // (R_ViewChanged), which this port skips entirely (no x86 asm path);
    // dropped, nothing reachable reads it.

    // viewlightvec, r_viewlighting -- alight_t r_viewlighting = {128, 192,
    // viewlightvec} in the original; PLightVec's initial value is
    // immediately overwritten before every real use (R_DrawViewModel), so
    // faithfully mirroring the static initializer is cosmetic -- done in
    // Init() below.
    public static Vec3_t viewlightvec;
    public static readonly ALight_t* r_viewlighting = NativeMemory.AllocZeroed<ALight_t>();

    public static float r_time1;
    public static float dp_time1, dp_time2, db_time1, db_time2, rw_time1, rw_time2;
    public static float se_time1, se_time2, de_time1, de_time2, dv_time1, dv_time2;

    public static int numbtofpolys;
    public static BToFPoly_t* pbtofpolys;
    public static MVertex_t* r_pcurrentvertbase;

    public static int c_surf; // c_surf (r_main.c) -- extern in d_iface.h
    public static int r_maxsurfsseen, r_maxedgesseen, r_cnumsurfs;
    public static bool r_surfsonstack;
    public static int r_clipflags;

    public static bool r_fov_greater_than_90;

    public static int r_outofsurfaces;
    public static int r_outofedges;
    public static int c_faceclip;
    public static int r_amodels_drawn;

    // aliasxscale/aliasyscale/aliasxcenter/aliasycenter, screenAspect/
    // verticalFieldOfView, xOrigin/yOrigin
    public static float aliasxscale, aliasyscale, aliasxcenter, aliasycenter;
    public static float screenAspect, verticalFieldOfView, xOrigin, yOrigin;

    // screenedge[4] -- real storage lives here (earliest consumer: R_Init/
    // R_ViewChanged/R_TransformFrustum/R_BmodelCheckBBox all touch it).
    public static readonly MPlane_t* screenedge = NativeMemory.AllocArrayZeroed<MPlane_t>(4);

    // view_clipplanes[4] -- real C storage is r_draw.c (step 2l), but
    // r_main.c's R_Init/R_TransformFrustum/R_BmodelCheckBBox are the
    // earliest consumers, so declared here (same precedent as screenedge).
    public static readonly ClipPlane_t* view_clipplanes = NativeMemory.AllocArrayZeroed<ClipPlane_t>(4);

    public static int r_visframecount;
    public static int d_spanpixcount;
    public static int r_polycount, r_wholepolycount;

    // pfrustum_indexes[4]/r_frustum_indexes[4*6] -- pointers into a native
    // (not GC-moved) buffer so pfrustum_indexes' stored pointers stay valid.
    public static readonly int* r_frustum_indexes = NativeMemory.AllocArrayZeroed<int>(4 * 6);
    public static readonly int** pfrustum_indexes = NativeMemory.AllocPointerArray<int>(4);

    // reinit_surfcache -- render.h global; not read anywhere in the
    // original WinQuake tree either (dead even upstream). Kept for
    // fidelity with its C default.
    public static int reinit_surfcache = 1;

    public static MLeaf_t* r_viewleaf, r_oldviewleaf;

    // r_notexture_mip (render.h) -- built by R_InitTextures below; the single copy,
    // read by Model.LoadTextures (Mod_LoadTextures). Caches a Hunk pointer, so tests reset it
    // along with the hunk.
    public static Texture_t* r_notexture_mip;

    public static float r_aliastransition, r_resfudge;

    public static bool r_dowarpold, r_viewchanged;

    public static bool insubmodel;
    public static int r_dlightframecount;
    public static Vec3_t r_emins, r_emaxs;
    public static MNode_t* r_pefragtopnode;

    public static Vec3_t r_entorigin;
    public static Vec3_t r_worldmodelorg;

    /*
    ===========================================================================
    Cvars (r_main.c registers these)
    ===========================================================================
    */

    public static readonly Cvar_t* r_draworder = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_speeds = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_timegraph = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_graphheight = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_clearcolor = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_waterwarp = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_fullbright = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_drawentities = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_drawviewmodel = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_aliasstats = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_dspeeds = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_drawflat = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_ambient = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_reportsurfout = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_maxsurfs = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_numsurfs = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_reportedgeout = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_maxedges = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_numedges = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_aliastransbase = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_aliastransadj = NativeMemory.AllocZeroed<Cvar_t>();

    // r_lerpmodels / r_lerpmove -- cvar_list_sheet.h (Mark_V); see RLerp
    public static readonly Cvar_t* r_lerpmodels = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* r_lerpmove = NativeMemory.AllocZeroed<Cvar_t>();

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        Cvar.RegisterVariable(cvar);
    }

    /*
    ===========================================================================
    R_InitTextures
    ===========================================================================
    */
    public static void InitTextures()
    {
        r_notexture_mip = (Texture_t*)Hunk.AllocName(sizeof(Texture_t) + (16 * 16) + (8 * 8) + (4 * 4) + (2 * 2), "notexture"u8);

        r_notexture_mip->Width = r_notexture_mip->Height = 16;
        r_notexture_mip->Offsets[0] = (uint)sizeof(Texture_t);
        r_notexture_mip->Offsets[1] = r_notexture_mip->Offsets[0] + (16 * 16);
        r_notexture_mip->Offsets[2] = r_notexture_mip->Offsets[1] + (8 * 8);
        r_notexture_mip->Offsets[3] = r_notexture_mip->Offsets[2] + (4 * 4);

        for (int m = 0; m < 4; m++)
        {
            byte* dest = (byte*)r_notexture_mip + r_notexture_mip->Offsets[m];
            for (int y = 0; y < (16 >> m); y++)
            {
                for (int x = 0; x < (16 >> m); x++)
                {
                    *dest++ = (y < (8 >> m)) ^ (x < (8 >> m)) ? (byte)0 : (byte)0xff;
                }
            }
        }
    }

    /*
    ===========================================================================
    R_Init
    ===========================================================================
    */
    public static void Init()
    {
        InitTurb();

        Cmd.AddCommand("timerefresh"u8, &RMisc.TimeRefresh);
        Cmd.AddCommand("pointfile"u8, &RPart.ReadPointFile_f);

        RegisterCvar(r_draworder, "r_draworder"u8, "0"u8);
        RegisterCvar(r_speeds, "r_speeds"u8, "0"u8);
        RegisterCvar(r_timegraph, "r_timegraph"u8, "0"u8);
        RegisterCvar(r_graphheight, "r_graphheight"u8, "10"u8);
        RegisterCvar(r_drawflat, "r_drawflat"u8, "0"u8);
        RegisterCvar(r_ambient, "r_ambient"u8, "0"u8);
        RegisterCvar(r_clearcolor, "r_clearcolor"u8, "2"u8);
        RegisterCvar(r_waterwarp, "r_waterwarp"u8, "1"u8);
        RegisterCvar(r_fullbright, "r_fullbright"u8, "0"u8);
        RegisterCvar(r_drawentities, "r_drawentities"u8, "1"u8);
        RegisterCvar(r_drawviewmodel, "r_drawviewmodel"u8, "1"u8);
        RegisterCvar(r_aliasstats, "r_polymodelstats"u8, "0"u8);
        RegisterCvar(r_dspeeds, "r_dspeeds"u8, "0"u8);
        RegisterCvar(r_reportsurfout, "r_reportsurfout"u8, "0"u8);
        RegisterCvar(r_maxsurfs, "r_maxsurfs"u8, "0"u8);
        RegisterCvar(r_numsurfs, "r_numsurfs"u8, "0"u8);
        RegisterCvar(r_reportedgeout, "r_reportedgeout"u8, "0"u8);
        RegisterCvar(r_maxedges, "r_maxedges"u8, "0"u8);
        RegisterCvar(r_numedges, "r_numedges"u8, "0"u8);
        RegisterCvar(r_aliastransbase, "r_aliastransbase"u8, "200"u8);
        RegisterCvar(r_aliastransadj, "r_aliastransadj"u8, "100"u8);
        RegisterCvar(r_lerpmodels, "r_lerpmodels"u8, "1"u8, archive: true);
        RegisterCvar(r_lerpmove, "r_lerpmove"u8, "1"u8, archive: true);

        Cvar.SetValue("r_maxedges"u8, RShared.NUMSTACKEDGES);
        Cvar.SetValue("r_maxsurfs"u8, RShared.NUMSTACKSURFACES);

        view_clipplanes[0].LeftEdge = 1;
        view_clipplanes[1].RightEdge = 1;
        view_clipplanes[1].LeftEdge = view_clipplanes[2].LeftEdge = view_clipplanes[3].LeftEdge = 0;
        view_clipplanes[0].RightEdge = view_clipplanes[2].RightEdge = view_clipplanes[3].RightEdge = 0;

        RShared.r_refdef->XOrigin = RLocal.XCENTERING;
        RShared.r_refdef->YOrigin = RLocal.YCENTERING;

        // alight_t r_viewlighting = {128, 192, viewlightvec} -- see the
        // field comment above.
        r_viewlighting->AmbientLight = 128;
        r_viewlighting->ShadeLight = 192;
        r_viewlighting->PLightVec = viewlightvec;

        RPart.InitParticles();

        DInit.Init();
    }

    /*
    ===========================================================================
    R_NewMap
    ===========================================================================
    */
    public static void NewMap()
    {
        for (int i = 0; i < Cl.worldmodel->NumLeafs; i++)
        {
            Cl.worldmodel->Leafs[i].EFrags = null;
        }

        r_viewleaf = null;
        RPart.ClearParticles();

        r_cnumsurfs = (int)r_maxsurfs->Value;

        if (r_cnumsurfs <= RShared.MINSURFACES)
        {
            r_cnumsurfs = RShared.MINSURFACES;
        }

        if (r_cnumsurfs > RShared.NUMSTACKSURFACES)
        {
            RShared.surfaces = (Surf_t*)Hunk.AllocName(r_cnumsurfs * sizeof(Surf_t), "surfaces"u8);
            RShared.surface_p = RShared.surfaces;
            RShared.surf_max = &RShared.surfaces[r_cnumsurfs];
            r_surfsonstack = false;
            RShared.surfaces--;
            // R_SurfacePatch () -- empty on non-x86 builds (nonintel.c); the x86
            // version (r_edgea.s) only patches asm offsets, so there is nothing to call.
        }
        else
        {
            r_surfsonstack = true;
        }

        r_maxedgesseen = 0;
        r_maxsurfsseen = 0;

        RShared.r_numallocatededges = (int)r_maxedges->Value;

        if (RShared.r_numallocatededges < RShared.MINEDGES)
        {
            RShared.r_numallocatededges = RShared.MINEDGES;
        }

        RShared.auxedges = RShared.r_numallocatededges <= RShared.NUMSTACKEDGES
            ? null
            : (Edge_t*)Hunk.AllocName(RShared.r_numallocatededges * sizeof(Edge_t), "edges"u8);

        r_dowarpold = false;
        r_viewchanged = false;
    }

    /*
    ===========================================================================
    R_SetVrect
    ===========================================================================
    */
    public static void SetVrect(VRect_t* pvrectin, VRect_t* pvrect, int lineadj)
    {
        float size = Screen.scr_viewsize->Value > 100 ? 100 : Screen.scr_viewsize->Value;
        if (Cl.intermission != 0)
        {
            size = 100;
            lineadj = 0;
        }
        size /= 100;

        int h = pvrectin->Height - lineadj;
        pvrect->Width = (int)(pvrectin->Width * size);
        if (pvrect->Width < 96)
        {
            size = 96.0f / pvrectin->Width;
            pvrect->Width = 96; // min for icons
        }
        pvrect->Width &= ~7;
        pvrect->Height = (int)(pvrectin->Height * size);
        if (pvrect->Height > pvrectin->Height - lineadj)
        {
            pvrect->Height = pvrectin->Height - lineadj;
        }

        pvrect->Height &= ~1;

        pvrect->X = (pvrectin->Width - pvrect->Width) / 2;
        pvrect->Y = (h - pvrect->Height) / 2;

        if (View.lcd_x->Value != 0)
        {
            pvrect->Y >>= 1;
            pvrect->Height >>= 1;
        }
    }

    /*
    ===========================================================================
    R_ViewChanged

    Called every time the vid structure or r_refdef changes.
    Guaranteed to be called before the first refresh
    ===========================================================================
    */
    public static void ViewChanged(VRect_t* pvrect, int lineadj, float aspect)
    {
        r_viewchanged = true;

        SetVrect(pvrect, &RShared.r_refdef->Vrect, lineadj);

        RShared.r_refdef->HorizontalFieldOfView = 2.0f * MathF.Tan(RShared.r_refdef->FovX / 360 * MathLib.M_PI);
        RShared.r_refdef->FVrectx = RShared.r_refdef->Vrect.X;
        RShared.r_refdef->FVrectxAdj = RShared.r_refdef->Vrect.X - 0.5f;
        RShared.r_refdef->VrectXAdjShift20 = ((long)RShared.r_refdef->Vrect.X << 20) + (1 << 19) - 1;
        RShared.r_refdef->FVrecty = RShared.r_refdef->Vrect.Y;
        RShared.r_refdef->FVrectyAdj = RShared.r_refdef->Vrect.Y - 0.5f;
        RShared.r_refdef->VrectRight = RShared.r_refdef->Vrect.X + RShared.r_refdef->Vrect.Width;
        RShared.r_refdef->VrectRightAdjShift20 = ((long)RShared.r_refdef->VrectRight << 20) + (1 << 19) - 1;
        RShared.r_refdef->FVrectRight = RShared.r_refdef->VrectRight;
        RShared.r_refdef->FVrectRightAdj = RShared.r_refdef->VrectRight - 0.5f;
        RShared.r_refdef->VrectRightEdge = RShared.r_refdef->VrectRight - 0.99f;
        RShared.r_refdef->VrectBottom = RShared.r_refdef->Vrect.Y + RShared.r_refdef->Vrect.Height;
        RShared.r_refdef->FVrectBottom = RShared.r_refdef->VrectBottom;
        RShared.r_refdef->FVrectBottomAdj = RShared.r_refdef->VrectBottom - 0.5f;

        RShared.r_refdef->AliasVrect.X = (int)(RShared.r_refdef->Vrect.X * DIface.r_aliasuvscale);
        RShared.r_refdef->AliasVrect.Y = (int)(RShared.r_refdef->Vrect.Y * DIface.r_aliasuvscale);
        RShared.r_refdef->AliasVrect.Width = (int)(RShared.r_refdef->Vrect.Width * DIface.r_aliasuvscale);
        RShared.r_refdef->AliasVrect.Height = (int)(RShared.r_refdef->Vrect.Height * DIface.r_aliasuvscale);
        RShared.r_refdef->AliasVrectRight = RShared.r_refdef->AliasVrect.X + RShared.r_refdef->AliasVrect.Width;
        RShared.r_refdef->AliasVrectBottom = RShared.r_refdef->AliasVrect.Y + RShared.r_refdef->AliasVrect.Height;

        RShared.pixelAspect = aspect;
        xOrigin = RShared.r_refdef->XOrigin;
        yOrigin = RShared.r_refdef->YOrigin;

        screenAspect = RShared.r_refdef->Vrect.Width * RShared.pixelAspect / RShared.r_refdef->Vrect.Height;
        // 320*200 1.0 pixelAspect = 1.6 screenAspect
        // 320*240 1.0 pixelAspect = 1.3333 screenAspect
        // proper 320*200 pixelAspect = 0.8333333

        verticalFieldOfView = RShared.r_refdef->HorizontalFieldOfView / screenAspect;

        // values for perspective projection
        // if math were exact, the values would range from 0.5 to to range+0.5
        // hopefully they wll be in the 0.000001 to range+.999999 and truncate
        // the polygon rasterization will never render in the first row or
        // column but will definately render in the [range] row and column,
        // so adjust the buffer origin to get an exact edge to edge fill
        RShared.xcenter = (RShared.r_refdef->Vrect.Width * RLocal.XCENTERING) + RShared.r_refdef->Vrect.X - 0.5f;
        aliasxcenter = RShared.xcenter * DIface.r_aliasuvscale;
        RShared.ycenter = (RShared.r_refdef->Vrect.Height * RLocal.YCENTERING) + RShared.r_refdef->Vrect.Y - 0.5f;
        aliasycenter = RShared.ycenter * DIface.r_aliasuvscale;

        RShared.xscale = RShared.r_refdef->Vrect.Width / RShared.r_refdef->HorizontalFieldOfView;
        aliasxscale = RShared.xscale * DIface.r_aliasuvscale;
        RShared.xscaleinv = 1.0f / RShared.xscale;
        RShared.yscale = RShared.xscale * RShared.pixelAspect;
        aliasyscale = RShared.yscale * DIface.r_aliasuvscale;
        RShared.yscaleinv = 1.0f / RShared.yscale;
        RShared.xscaleshrink = (RShared.r_refdef->Vrect.Width - 6) / RShared.r_refdef->HorizontalFieldOfView;
        RShared.yscaleshrink = RShared.xscaleshrink * RShared.pixelAspect;

        // left side clip
        screenedge[0].Normal[0] = -1.0f / (xOrigin * RShared.r_refdef->HorizontalFieldOfView);
        screenedge[0].Normal[1] = 0;
        screenedge[0].Normal[2] = 1;
        screenedge[0].Type = (int)PlaneType.AnyZ;

        // right side clip
        screenedge[1].Normal[0] = 1.0f / ((1.0f - xOrigin) * RShared.r_refdef->HorizontalFieldOfView);
        screenedge[1].Normal[1] = 0;
        screenedge[1].Normal[2] = 1;
        screenedge[1].Type = (int)PlaneType.AnyZ;

        // top side clip
        screenedge[2].Normal[0] = 0;
        screenedge[2].Normal[1] = -1.0f / (yOrigin * verticalFieldOfView);
        screenedge[2].Normal[2] = 1;
        screenedge[2].Type = (int)PlaneType.AnyZ;

        // bottom side clip
        screenedge[3].Normal[0] = 0;
        screenedge[3].Normal[1] = 1.0f / ((1.0f - yOrigin) * verticalFieldOfView);
        screenedge[3].Normal[2] = 1;
        screenedge[3].Type = (int)PlaneType.AnyZ;

        for (int i = 0; i < 4; i++)
        {
            Vec3_t n = screenedge[i].Normal;
            MathLib.VectorNormalize(&n);
            screenedge[i].Normal = n;
        }

        float res_scale = MathF.Sqrt(RShared.r_refdef->Vrect.Width * RShared.r_refdef->Vrect.Height / (320.0f * 152.0f)) *
                           (2.0f / RShared.r_refdef->HorizontalFieldOfView);
        r_aliastransition = r_aliastransbase->Value * res_scale;
        r_resfudge = r_aliastransadj->Value * res_scale;

        r_fov_greater_than_90 = Screen.scr_fov->Value > 90.0f;

        DModech.ViewChanged();
    }

    /*
    ===========================================================================
    R_MarkLeaves
    ===========================================================================
    */
    public static void MarkLeaves()
    {
        if (r_oldviewleaf == r_viewleaf)
        {
            return;
        }

        r_visframecount++;
        r_oldviewleaf = r_viewleaf;

        byte* vis = Model.LeafPVS(r_viewleaf, Cl.worldmodel);

        MarkVisibleLeaves(vis, Cl.worldmodel->NumLeafs, Cl.worldmodel->Leafs, r_visframecount);
    }

    // The leaf loop of R_MarkLeaves: for every set bit i < numLeafs of the
    // PVS row, stamp leaf i+1 and its parents with visframecount. Scans 64
    // leaves per step and visits only the set bits (the original tested
    // every bit); only bits below numLeafs are looked at, as in the original.
    internal static void MarkVisibleLeaves(byte* vis, int numLeafs, MLeaf_t* leafs, int visframecount)
    {
        int fullWords = numLeafs >> 6;
        for (int w = 0; w < fullWords; w++)
        {
            ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(vis + (w << 3), 8));
            while (bits != 0)
            {
                MarkLeaf(leafs, (w << 6) + BitOperations.TrailingZeroCount(bits), visframecount);
                bits &= bits - 1;
            }
        }

        int rem = numLeafs & 63;
        if (rem != 0)
        {
            // assemble only the bytes that exist in the row (no over-read)
            ulong bits = 0;
            int nbytes = (rem + 7) >> 3;
            for (int b = 0; b < nbytes; b++)
            {
                bits |= (ulong)vis[(fullWords << 3) + b] << (b << 3);
            }

            bits &= (1UL << rem) - 1;
            while (bits != 0)
            {
                MarkLeaf(leafs, (fullWords << 6) + BitOperations.TrailingZeroCount(bits), visframecount);
                bits &= bits - 1;
            }
        }
    }

    private static void MarkLeaf(MLeaf_t* leafs, int i, int visframecount)
    {
        MNode_t* node = (MNode_t*)&leafs[i + 1];
        do
        {
            if (node->VisFrame == visframecount)
            {
                break;
            }

            node->VisFrame = visframecount;
            node = node->Parent;
        } while (node != null);
    }

    /*
    ===========================================================================
    R_DrawEntitiesOnList
    ===========================================================================
    */
    public static void DrawEntitiesOnList()
    {
        if (r_drawentities->Value == 0)
        {
            return;
        }

        for (int i = 0; i < ClEntities.cl_numvisedicts; i++)
        {
            RShared.currententity = ClEntities.cl_visedicts[i];

            if (RShared.currententity == ClEntities.cl_entities + Cl.viewentity)
            {
                continue; // don't draw the player
            }

            switch (RShared.currententity->Model->Type)
            {
                case ModType_t.Sprite:
                    r_entorigin = RShared.currententity->Origin;
                    RShared.modelorg = RShared.r_origin - r_entorigin;
                    RSprite.DrawSprite();
                    break;

                case ModType_t.Alias:
                    // r_main.c (Mark_V): move lerp before the origin is read
                    if (r_lerpmove->Value != 0)
                    {
                        RLerp.SetupEntityTransform(RShared.currententity);
                    }

                    r_entorigin = RShared.currententity->Origin;
                    RShared.modelorg = RShared.r_origin - r_entorigin;

                    // see if the bounding box lets us trivially reject, also
                    // sets trivial accept status
                    if (RAlias.AliasCheckBBox())
                    {
                        int j = RLight.LightPoint(&RShared.currententity->Origin);

                        ALight_t lighting = default;
                        lighting.AmbientLight = j;
                        lighting.ShadeLight = j;

                        Vec3_t lightvec = default;
                        lightvec[0] = -1;
                        lightvec[1] = 0;
                        lightvec[2] = 0;
                        lighting.PLightVec = lightvec;

                        for (int lnum = 0; lnum < ClEntities.MAX_DLIGHTS; lnum++)
                        {
                            if (ClEntities.cl_dlights[lnum].Die >= Cl.time)
                            {
                                Vec3_t dist = RShared.currententity->Origin - ClEntities.cl_dlights[lnum].Origin;
                                float add = ClEntities.cl_dlights[lnum].Radius - dist.Length();

                                if (add > 0)
                                {
                                    lighting.AmbientLight = (int)(lighting.AmbientLight + add);
                                }
                            }
                        }

                        // clamp lighting so it doesn't overbright as much
                        if (lighting.AmbientLight > 128)
                        {
                            lighting.AmbientLight = 128;
                        }

                        if (lighting.AmbientLight + lighting.ShadeLight > 192)
                        {
                            lighting.ShadeLight = 192 - lighting.AmbientLight;
                        }

                        RAlias.DrawModel(&lighting);
                    }

                    break;
            }
        }
    }

    /*
    ===========================================================================
    R_DrawViewModel
    ===========================================================================
    */
    public static void DrawViewModel()
    {
        Vec3_t lightvec = default;
        lightvec[0] = -1;
        lightvec[1] = 0;
        lightvec[2] = 0;

        if (r_drawviewmodel->Value == 0 || r_fov_greater_than_90)
        {
            return;
        }

        if ((Cl.items & IT_INVISIBILITY) != 0)
        {
            return;
        }

        if (Cl.stats[(int)Stat.Health] <= 0)
        {
            return;
        }

        RShared.currententity = Cl.viewent;
        if (RShared.currententity->Model == null)
        {
            return;
        }

        r_entorigin = RShared.currententity->Origin;
        RShared.modelorg = RShared.r_origin - r_entorigin;

        viewlightvec = RShared.vup;
        viewlightvec = -viewlightvec;

        int j = RLight.LightPoint(&RShared.currententity->Origin);

        if (j < 24)
        {
            j = 24; // allways give some light on gun
        }

        r_viewlighting->AmbientLight = j;
        r_viewlighting->ShadeLight = j;

        // add dynamic lights
        for (int lnum = 0; lnum < ClEntities.MAX_DLIGHTS; lnum++)
        {
            ref DLight_t dl = ref ClEntities.cl_dlights[lnum];
            if (dl.Radius == 0)
            {
                continue;
            }

            if (dl.Die < Cl.time)
            {
                continue;
            }

            Vec3_t dist = RShared.currententity->Origin - dl.Origin;
            float add = dl.Radius - dist.Length();
            if (add > 0)
            {
                r_viewlighting->AmbientLight = (int)(r_viewlighting->AmbientLight + add);
            }
        }

        // clamp lighting so it doesn't overbright as much
        if (r_viewlighting->AmbientLight > 128)
        {
            r_viewlighting->AmbientLight = 128;
        }

        if (r_viewlighting->AmbientLight + r_viewlighting->ShadeLight > 192)
        {
            r_viewlighting->ShadeLight = 192 - r_viewlighting->AmbientLight;
        }

        r_viewlighting->PLightVec = lightvec;

        RAlias.DrawModel(r_viewlighting);
    }

    /*
    ===========================================================================
    R_BmodelCheckBBox
    ===========================================================================
    */
    public static int BmodelCheckBBox(Model_t* clmodel, float* minmaxs)
    {
        int clipflags = 0;

        if (RShared.currententity->Angles[0] != 0 || RShared.currententity->Angles[1] != 0 || RShared.currententity->Angles[2] != 0)
        {
            for (int i = 0; i < 4; i++)
            {
                double d = Vector3.Dot(RShared.currententity->Origin, view_clipplanes[i].Normal);
                d -= view_clipplanes[i].Dist;

                if (d <= -clmodel->Radius)
                {
                    return RLocal.BMODEL_FULLY_CLIPPED;
                }

                if (d <= clmodel->Radius)
                {
                    clipflags |= 1 << i;
                }
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                // generate accept and reject points
                // FIXME: do with fast look-ups or integer tests based on the
                // sign bit of the floating point values
                int* pindex = pfrustum_indexes[i];

                Vec3_t rejectpt = default;
                rejectpt[0] = minmaxs[pindex[0]];
                rejectpt[1] = minmaxs[pindex[1]];
                rejectpt[2] = minmaxs[pindex[2]];

                double d = Vector3.Dot(rejectpt, view_clipplanes[i].Normal);
                d -= view_clipplanes[i].Dist;

                if (d <= 0)
                {
                    return RLocal.BMODEL_FULLY_CLIPPED;
                }

                Vec3_t acceptpt = default;
                acceptpt[0] = minmaxs[pindex[3 + 0]];
                acceptpt[1] = minmaxs[pindex[3 + 1]];
                acceptpt[2] = minmaxs[pindex[3 + 2]];

                d = Vector3.Dot(acceptpt, view_clipplanes[i].Normal);
                d -= view_clipplanes[i].Dist;

                if (d <= 0)
                {
                    clipflags |= 1 << i;
                }
            }
        }

        return clipflags;
    }

    /*
    ===========================================================================
    R_DrawBEntitiesOnList
    ===========================================================================
    */
    public static void DrawBEntitiesOnList()
    {
        if (r_drawentities->Value == 0)
        {
            return;
        }

        Vec3_t oldorigin = RShared.modelorg;
        insubmodel = true;
        r_dlightframecount = DIface.r_framecount;

        // minmaxs -- per-entity scratch, reused across iterations rather than
        // stackalloc'd inside the loop (unlike the original's per-call C
        // stack array, a stackalloc inside a loop grows the frame each pass).
        float* minmaxs = stackalloc float[6];

        for (int i = 0; i < ClEntities.cl_numvisedicts; i++)
        {
            RShared.currententity = ClEntities.cl_visedicts[i];

            if (RShared.currententity->Model->Type != ModType_t.Brush)
            {
                continue;
            }

            Model_t* clmodel = RShared.currententity->Model;

            // see if the bounding box lets us trivially reject, also sets
            // trivial accept status
            // minmaxs[0..2] = mins, minmaxs[3..5] = maxs
            *(Vec3_t*)minmaxs = RShared.currententity->Origin + clmodel->Mins;
            *(Vec3_t*)(minmaxs + 3) = RShared.currententity->Origin + clmodel->Maxs;

            int clipflags = BmodelCheckBBox(clmodel, minmaxs);

            if (clipflags == RLocal.BMODEL_FULLY_CLIPPED)
            {
                continue;
            }

            r_entorigin = RShared.currententity->Origin;
            RShared.modelorg = RShared.r_origin - r_entorigin;
            // FIXME: is this needed?
            r_worldmodelorg = RShared.modelorg;

            r_pcurrentvertbase = clmodel->Vertexes;

            // FIXME: stop transforming twice
            RBsp.RotateBmodel();

            // calculate dynamic lighting for bmodel if it's not an
            // instanced model
            if (clmodel->FirstModelSurface != 0)
            {
                for (int k = 0; k < ClEntities.MAX_DLIGHTS; k++)
                {
                    if (ClEntities.cl_dlights[k].Die < Cl.time || ClEntities.cl_dlights[k].Radius == 0)
                    {
                        continue;
                    }

                    RLight.MarkLights(&ClEntities.cl_dlights[k], 1 << k, clmodel->Nodes + clmodel->Hulls[0].FirstClipNode);
                }
            }

            // if the driver wants polygons, deliver those. Z-buffering is on
            // at this point, so no clipping to the world tree is needed,
            // just frustum clipping
            if (DIface.r_drawpolys || DIface.r_drawculledpolys)
            {
                RDraw.ZDrawSubmodelPolys(clmodel);
            }
            else
            {
                r_pefragtopnode = null;

                r_emins = *(Vec3_t*)minmaxs;
                r_emaxs = *(Vec3_t*)(minmaxs + 3);

                REfrag.SplitEntityOnNode2(Cl.worldmodel->Nodes);

                if (r_pefragtopnode != null)
                {
                    RShared.currententity->TopNode = r_pefragtopnode;

                    if (r_pefragtopnode->Contents >= 0)
                    {
                        // not a leaf; has to be clipped to the world BSP
                        r_clipflags = clipflags;
                        RBsp.DrawSolidClippedSubmodelPolygons(clmodel);
                    }
                    else
                    {
                        // falls entirely in one leaf, so we just put all the
                        // edges in the edge list and let 1/z sorting handle
                        // drawing order
                        RBsp.DrawSubmodelPolygons(clmodel, clipflags);
                    }

                    RShared.currententity->TopNode = null;
                }
            }

            // put back world rotation and frustum clipping
            // FIXME: R_RotateBmodel should just work off base_vxx
            RShared.vpn = RShared.base_vpn;
            RShared.vup = RShared.base_vup;
            RShared.vright = RShared.base_vright;
            RShared.modelorg = RShared.base_modelorg;
            RShared.modelorg = oldorigin;
            RMisc.TransformFrustum();
        }

        insubmodel = false;
    }

    /*
    ===========================================================================
    R_EdgeDrawing
    ===========================================================================
    */
    // ledges/lsurfs -- the original declares these as per-call C stack
    // arrays (cache-line aligned via pointer masking) purely as scratch
    // space fully overwritten each frame by the edge/surface algorithms.
    // Kept as lazily-allocated, 64-byte-aligned native buffers here instead
    // of reallocating every frame (managed code has no stack-array-plus-
    // manual-alignment idiom); behavior is unchanged.
    private static Edge_t* ledges;
    private static Surf_t* lsurfs;

    public static void EdgeDrawing()
    {
        if (RShared.auxedges != null)
        {
            RShared.r_edges = RShared.auxedges;
        }
        else
        {
            if (ledges == null)
            {
                ledges = (Edge_t*)NativeMemory.AlignedAlloc((nuint)(RShared.NUMSTACKEDGES * sizeof(Edge_t)), 64);
            }

            RShared.r_edges = ledges;
        }

        if (r_surfsonstack)
        {
            if (lsurfs == null)
            {
                lsurfs = (Surf_t*)NativeMemory.AlignedAlloc((nuint)(RShared.NUMSTACKSURFACES * sizeof(Surf_t)), 64);
            }

            RShared.surfaces = lsurfs;
            RShared.surf_max = &RShared.surfaces[r_cnumsurfs];
            // surface 0 doesn't really exist; it's just a dummy because
            // index 0 is used to indicate no edge attached to surface
            RShared.surfaces--;
            // R_SurfacePatch () -- empty on non-x86 builds (nonintel.c); the x86
            // version (r_edgea.s) only patches asm offsets, so there is nothing to call.
        }

        REdge.BeginEdgeFrame();

        if (r_dspeeds->Value != 0)
        {
            rw_time1 = (float)Sys.FloatTime();
        }

        RBsp.RenderWorld();

        if (DIface.r_drawculledpolys)
        {
            REdge.ScanEdges();
        }

        // D_TurnZOn -- "not needed for software version" even in the
        // original (d_init.c); permanently a no-op, not ported.

        if (r_dspeeds->Value != 0)
        {
            rw_time2 = (float)Sys.FloatTime();
            db_time1 = rw_time2;
        }

        DrawBEntitiesOnList();

        if (r_dspeeds->Value != 0)
        {
            db_time2 = (float)Sys.FloatTime();
            se_time1 = db_time2;
        }

        // VID_UnlockBuffer/S_ExtraUpdate/VID_LockBuffer -- dropped (both
        // display layer and sound mixer are ported now, but this mid-render
        // buffer-lock/audio-pump triad is unneeded by OpenTK/OpenAL's model)

        if (!(DIface.r_drawpolys || DIface.r_drawculledpolys))
        {
            REdge.ScanEdges();
        }
    }

    /*
    ===========================================================================
    R_RenderView

    r_refdef must be set before the first call
    ===========================================================================
    */
    private static byte* warpBuffer;

    public static void RenderView_()
    {
        // r_warpbuffer -- WARP_WIDTH*WARP_HEIGHT scratch buffer, a per-call C
        // stack array in the original; kept as a lazily-allocated native
        // buffer instead.
        if (warpBuffer == null)
        {
            warpBuffer = NativeMemory.AllocArrayZeroed<byte>(DIface.WARP_WIDTH * DIface.WARP_HEIGHT);
        }

        DIface.r_warpbuffer = warpBuffer;

        if (r_timegraph->Value != 0 || r_speeds->Value != 0 || r_dspeeds->Value != 0)
        {
            r_time1 = (float)Sys.FloatTime();
        }

        RMisc.SetupFrame();

        MarkLeaves(); // done here so we know if we're in water

        // Sys_LowFPPrecision -- x86 FPU control-word trick, dropped
        // (managed doubles have no such mode)

        if (ClEntities.cl_entities[0].Model == null || Cl.worldmodel == null)
        {
            Sys.Error("R_RenderView: NULL worldmodel"u8);
        }

        // VID_UnlockBuffer/S_ExtraUpdate/VID_LockBuffer -- dropped

        EdgeDrawing();

        // VID_UnlockBuffer/S_ExtraUpdate/VID_LockBuffer -- dropped

        if (r_dspeeds->Value != 0)
        {
            se_time2 = (float)Sys.FloatTime();
            de_time1 = se_time2;
        }

        DrawEntitiesOnList();

        if (r_dspeeds->Value != 0)
        {
            de_time2 = (float)Sys.FloatTime();
            dv_time1 = de_time2;
        }

        DrawViewModel();

        if (r_dspeeds->Value != 0)
        {
            dv_time2 = (float)Sys.FloatTime();
            dp_time1 = (float)Sys.FloatTime();
        }

        RPart.DrawParticles();

        if (r_dspeeds->Value != 0)
        {
            dp_time2 = (float)Sys.FloatTime();
        }

        if (DIface.r_dowarp)
        {
            DScan.WarpScreen();
        }

        // V_SetContentsColor(r_viewleaf->contents)
        View.SetContentsColor(r_viewleaf->Contents);

        if (r_timegraph->Value != 0)
        {
            RMisc.TimeGraph();
        }

        if (r_aliasstats->Value != 0)
        {
            RMisc.PrintAliasStats();
        }

        if (r_speeds->Value != 0)
        {
            RMisc.PrintTimes();
        }

        if (r_dspeeds->Value != 0)
        {
            RMisc.PrintDSpeeds();
        }

        if (r_reportsurfout->Value != 0 && r_outofsurfaces != 0)
        {
            Con.Printf($"Short {r_outofsurfaces} surfaces\n");
        }

        if (r_reportedgeout->Value != 0 && r_outofedges != 0)
        {
            Con.Printf($"Short roughly {r_outofedges * 2 / 3} edges\n");
        }

        // Sys_HighFPPrecision -- dropped (see Sys_LowFPPrecision above)
    }

    public static void RenderView()
    {
        // r_stack_start delta guard / Hunk_LowMark alignment guard / raw
        // pointer alignment guards -- dropped: no raw stack pointer or
        // manual byte-alignment concerns for managed allocations.
        RenderView_();
    }

    /*
    ===========================================================================
    R_InitTurb
    ===========================================================================
    */
    public static void InitTurb()
    {
        for (int i = 0; i < RShared.SIN_BUFFER_SIZE; i++)
        {
            RShared.sintable[i] = (int)(RLocal.AMP + (MathF.Sin(i * 3.14159f * 2 / DIface.CYCLE) * RLocal.AMP));
            RShared.intsintable[i] = (int)(RLocal.AMP2 + (MathF.Sin(i * 3.14159f * 2 / DIface.CYCLE) * RLocal.AMP2)); // AMP2, not 20
        }
    }
}
