// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// r_misc.c -- the per-frame view-setup core (R_CheckVariables,
/// R_TransformFrustum, TransformVector, R_TransformPlane,
/// R_SetUpFrustumIndexes, R_SetupFrame) is ported here, and so are its
/// debug/print leaf utilities: R_TimeRefresh_f/R_PrintTimes/R_PrintDSpeeds/
/// R_PrintAliasStats/R_TimeGraph are called from RMain.cs and
/// R_TimeRefresh_f is registered as the "timerefresh" console command; R_LineGraph is TimeGraph's own helper; WarpPalette has no
/// caller anywhere in the WinQuake tree (dead code even upstream) but is
/// ported faithfully per CLAUDE.md rule 2 regardless. Show's body is
/// VID_Update-only and stays a permanent no-op, matching this port's other
/// DirectDraw buffer-lock drops (RMain.cs's VID_LockBuffer/VID_UnlockBuffer
/// precedent) -- Quake.Desktop's OpenTK layer redraws every frame on its
/// own schedule regardless of Show being called.
/// </summary>
public static unsafe class RMisc
{
    // R_CheckVariables
    private static float oldbright;

    public static void CheckVariables()
    {
        if (RMain.r_fullbright->Value != oldbright)
        {
            oldbright = RMain.r_fullbright->Value;
            DSurf.FlushCaches(); // so all lighting changes
        }
    }

    // R_TransformFrustum
    public static void TransformFrustum()
    {
        for (int i = 0; i < 4; i++)
        {
            Vec3_t v2;

            Vec3_t n = RMain.screenedge[i].Normal;
            Vec3_t v = new(n.Z, -n.X, n.Y);

            v2 = v.Y * RShared.vright + v.Z * RShared.vup + v.X * RShared.vpn;

            RMain.view_clipplanes[i].Normal = v2;

            RMain.view_clipplanes[i].Dist = Vector3.Dot(RShared.modelorg, v2);
        }
    }

    // TransformVector
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransformVector(in Vec3_t @in, out Vec3_t @out)
    {
        @out = new Vec3_t(
            Vector3.Dot(@in, RShared.vright),
            Vector3.Dot(@in, RShared.vup),
            Vector3.Dot(@in, RShared.vpn));
    }

    // R_TransformPlane
    public static void TransformPlane(MPlane_t* p, float* normal, float* dist)
    {
        float d = Vector3.Dot(RShared.r_origin, p->Normal);

        *dist = p->Dist - d;

        // TODO (original comment): when we have rotating entities, this will need to use the view matrix
        Vec3_t inVec = p->Normal;
        TransformVector(inVec, out Vec3_t outVec);

        *((Vec3_t*)normal) = outVec;
    }

    // R_SetUpFrustumIndexes
    public static void SetUpFrustumIndexes()
    {
        int* pindex = RMain.r_frustum_indexes;

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (RMain.view_clipplanes[i].Normal[j] < 0)
                {
                    pindex[j] = j;
                    pindex[j + 3] = j + 3;
                }
                else
                {
                    pindex[j] = j + 3;
                    pindex[j + 3] = j;
                }
            }

            // FIXME (original comment): do just once at start
            RMain.pfrustum_indexes[i] = pindex;
            pindex += 6;
        }
    }

    // R_SetupFrame
    public static void SetupFrame()
    {
        // cl.time -- keep RShared.cl_time (used by Turbulent8's warp animation
        // phase) in sync with the real client clock every frame.
        RShared.cl_time = (float)Cl.time;

        // don't allow cheats in multiplayer
        if (Cl.maxclients > 1)
        {
            Cvar.Set("r_draworder"u8, "0"u8);
            Cvar.Set("r_fullbright"u8, "0"u8);
            Cvar.Set("r_ambient"u8, "0"u8);
            Cvar.Set("r_drawflat"u8, "0"u8);
        }

        if (RMain.r_numsurfs->Value != 0)
        {
            int used = (int)(RShared.surface_p - RShared.surfaces);
            if (used > RMain.r_maxsurfsseen)
                RMain.r_maxsurfsseen = used;

            Con.Printf($"Used {used} of {(int)(RShared.surf_max - RShared.surfaces)} surfs; {RMain.r_maxsurfsseen} max\n");
        }

        if (RMain.r_numedges->Value != 0)
        {
            int edgecount = (int)(RShared.edge_p - RShared.r_edges);
            if (edgecount > RMain.r_maxedgesseen)
                RMain.r_maxedgesseen = edgecount;

            Con.Printf($"Used {edgecount} of {RShared.r_numallocatededges} edges; {RMain.r_maxedgesseen} max\n");
        }

        RShared.r_refdef->AmbientLight = (int)RMain.r_ambient->Value;

        if (RShared.r_refdef->AmbientLight < 0)
            RShared.r_refdef->AmbientLight = 0;

        if (!Sv.active)
            RMain.r_draworder->Value = 0; // don't let cheaters look behind walls

        CheckVariables();

        RLight.AnimateLight();

        DIface.r_framecount++;

        RMain.numbtofpolys = 0;

        // build the transformation matrix for the given view angles
        RShared.modelorg = RShared.r_refdef->Vieworg;
        RShared.r_origin = RShared.r_refdef->Vieworg;

        MathLib.AngleVectors(RShared.r_refdef->ViewAngles, out RShared.vpn, out RShared.vright, out RShared.vup);

        // current viewleaf
        RMain.r_oldviewleaf = RMain.r_viewleaf;
        RMain.r_viewleaf = Model.PointInLeaf(RShared.r_origin, Cl.worldmodel);

        RMain.r_dowarpold = DIface.r_dowarp;
        DIface.r_dowarp = RMain.r_waterwarp->Value != 0 && RMain.r_viewleaf->Contents <= (int)Contents.Water;

        if (DIface.r_dowarp != RMain.r_dowarpold || RMain.r_viewchanged || View.lcd_x->Value != 0)
        {
            VRect_t vrect = default;

            if (DIface.r_dowarp)
            {
                if (Vid.vid.Width <= Vid.vid.MaxWarpWidth && Vid.vid.Height <= Vid.vid.MaxWarpHeight)
                {
                    vrect.X = 0;
                    vrect.Y = 0;
                    vrect.Width = (int)Vid.vid.Width;
                    vrect.Height = (int)Vid.vid.Height;

                    RMain.ViewChanged(&vrect, Sbar.sb_physlines, Vid.vid.Aspect);
                }
                else
                {
                    float w = Vid.vid.Width;
                    float h = Vid.vid.Height;

                    if (w > Vid.vid.MaxWarpWidth)
                    {
                        h *= Vid.vid.MaxWarpWidth / w;
                        w = Vid.vid.MaxWarpWidth;
                    }

                    if (h > Vid.vid.MaxWarpHeight)
                    {
                        h = Vid.vid.MaxWarpHeight;
                        w *= Vid.vid.MaxWarpHeight / h;
                    }

                    vrect.X = 0;
                    vrect.Y = 0;
                    vrect.Width = (int)w;
                    vrect.Height = (int)h;

                    RMain.ViewChanged(&vrect,
                        (int)(Sbar.sb_physlines * (h / Vid.vid.Height)),
                        Vid.vid.Aspect * (h / w) * (Vid.vid.Width / (float)Vid.vid.Height));
                }
            }
            else
            {
                vrect.X = 0;
                vrect.Y = 0;
                vrect.Width = (int)Vid.vid.Width;
                vrect.Height = (int)Vid.vid.Height;

                RMain.ViewChanged(&vrect, Sbar.sb_physlines, Vid.vid.Aspect);
            }

            RMain.r_viewchanged = false;
        }

        // start off with just the four screen edge clip planes
        TransformFrustum();

        // save base values
        /*
        RShared.base_vpn = RShared.vpn;
        RShared.base_vright = RShared.vright;
        RShared.base_vup = RShared.vup;
        RShared.base_modelorg = RShared.modelorg;
        */
        RShared.base_vpn = RShared.vpn;
        RShared.base_vright = RShared.vright;
        RShared.base_vup = RShared.vup;
        RShared.base_modelorg = RShared.modelorg;

        RSky.SetSkyFrame();

        SetUpFrustumIndexes();

        DSurf.r_cache_thrash = false;

        // clear frame counts
        RMain.c_faceclip = 0;
        RMain.d_spanpixcount = 0;
        RMain.r_polycount = 0;
        RShared.r_drawnpolycount = 0;
        RMain.r_wholepolycount = 0;
        RMain.r_amodels_drawn = 0;
        RMain.r_outofsurfaces = 0;
        RMain.r_outofedges = 0;

        DInit.SetupFrame();
    }

    // Show
    // Debugging use
    public static void Show()
    {
        // VID_Update(&vr) dropped -- DirectDraw display-layer concern, no
        // equivalent under OpenTK (same precedent as VID_LockBuffer/
        // VID_UnlockBuffer elsewhere in RMain.cs); this function has no
        // other body.
    }

    // R_TimeRefresh_f
    // For program optimization
    public static void TimeRefresh()
    {
        float startangle = RShared.r_refdef->ViewAngles[1];

        float start = (float)Sys.FloatTime();
        for (int i = 0; i < 128; i++)
        {
            RShared.r_refdef->ViewAngles[1] = i / 128.0f * 360.0f;

            // VID_LockBuffer/VID_UnlockBuffer/VID_Update dropped -- DirectDraw
            // display-layer concern, no equivalent under OpenTK (RMain.cs's
            // own explicit-drops precedent).
            RMain.RenderView();
        }
        float stop = (float)Sys.FloatTime();
        float time = stop - start;
        Con.Printf($"{time:F6} seconds ({128 / time:F6} fps)\n");

        RShared.r_refdef->ViewAngles[1] = startangle;
    }

    // R_LineGraph
    // Only called by R_DisplayTime
    public static void LineGraph(int x, int y, int h)
    {
        // FIXME (original comment): should be disabled on no-buffer
        // adapters, or should be in the driver
        x += RShared.r_refdef->Vrect.X;
        y += RShared.r_refdef->Vrect.Y;

        byte* dest = Vid.vid.Buffer + Vid.vid.RowBytes * y + x;

        int s = (int)RMain.r_graphheight->Value;

        if (h > s)
            h = s;

        int i;
        for (i = 0; i < h; i++, dest -= Vid.vid.RowBytes * 2)
        {
            dest[0] = 0xff;
            *(dest - Vid.vid.RowBytes) = 0x30;
        }
        for (; i < s; i++, dest -= Vid.vid.RowBytes * 2)
        {
            dest[0] = 0x30;
            *(dest - Vid.vid.RowBytes) = 0x30;
        }
    }

    // R_TimeGraph
    // Performance monitoring tool
    internal const int MAX_TIMINGS = 100;
    private static int timex;
    internal static readonly byte* r_timings = NativeMemory.AllocArrayZeroed<byte>(MAX_TIMINGS);

    public static void TimeGraph()
    {
        float r_time2 = (float)Sys.FloatTime();

        int a = (int)((r_time2 - RMain.r_time1) / 0.01f);
        r_timings[timex] = (byte)a;
        a = timex;

        int x = RShared.r_refdef->Vrect.Width <= MAX_TIMINGS
            ? RShared.r_refdef->Vrect.Width - 1
            : RShared.r_refdef->Vrect.Width - (RShared.r_refdef->Vrect.Width - MAX_TIMINGS) / 2;

        do
        {
            LineGraph(x, RShared.r_refdef->Vrect.Height - 2, r_timings[a]);
            if (x == 0)
                break; // screen too small to hold entire thing
            x--;
            a--;
            if (a == -1)
                a = MAX_TIMINGS - 1;
        } while (a != timex);

        timex = (timex + 1) % MAX_TIMINGS;
    }

    // R_PrintTimes
    public static void PrintTimes()
    {
        float r_time2 = (float)Sys.FloatTime();

        float ms = 1000 * (r_time2 - RMain.r_time1);

        Con.Printf($"{ms,5:F1} ms {RMain.c_faceclip,3}/{RMain.r_polycount,3}/{RShared.r_drawnpolycount,3} poly {RMain.c_surf,3} surf\n");
        RMain.c_surf = 0;
    }

    // R_PrintDSpeeds
    public static void PrintDSpeeds()
    {
        float r_time2 = (float)Sys.FloatTime();

        float dp_time = (RMain.dp_time2 - RMain.dp_time1) * 1000;
        float rw_time = (RMain.rw_time2 - RMain.rw_time1) * 1000;
        float db_time = (RMain.db_time2 - RMain.db_time1) * 1000;
        float se_time = (RMain.se_time2 - RMain.se_time1) * 1000;
        float de_time = (RMain.de_time2 - RMain.de_time1) * 1000;
        float dv_time = (RMain.dv_time2 - RMain.dv_time1) * 1000;
        float ms = (r_time2 - RMain.r_time1) * 1000;

        Con.Printf($"{(int)ms,3} {dp_time,4:F1}p {(int)rw_time,3}w {db_time,4:F1}b {(int)se_time,3}s {de_time,4:F1}e {dv_time,4:F1}v\n");
    }

    // R_PrintAliasStats
    public static void PrintAliasStats()
    {
        Con.Printf($"{RMain.r_amodels_drawn,3} polygon model drawn\n");
    }

    // host_basepal -- host.c's loaded gfx/palette.lmp; populated by
    // Host.cs's Host_Init (Host.cs:184) from the "gfx/palette.lmp" hunk
    // file. WarpPalette has no caller anywhere in the WinQuake tree (dead
    // code even upstream) -- ported faithfully per CLAUDE.md rule 2 regardless.
    public static byte* host_basepal;

    // WarpPalette
    public static void WarpPalette()
    {
        byte* newpalette = stackalloc byte[768];
        Span<int> basecolor = [130, 80, 50];

        // pull the colors halfway to bright brown
        for (int i = 0; i < 256; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                newpalette[i * 3 + j] = host_basepal != null
                    ? (byte)((host_basepal[i * 3 + j] + basecolor[j]) / 2)
                    : (byte)0;
            }
        }

        Vid.ShiftPalette(newpalette);
    }

}
