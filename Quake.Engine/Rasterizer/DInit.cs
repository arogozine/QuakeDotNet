// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// d_init.c: rasterization driver initialization. D_InitCaches itself lives
/// in DSurf.cs (DSurf.InitCaches, called by Quake.Desktop's VidWindow.cs)
/// since it belongs with the surface-cache allocator it initializes.
/// D_CopyRects/D_EnableBackBufferAccess/D_DisableBackBufferAccess/D_TurnZOn/
/// D_UpdateRects are DirectDraw buffer-lock/blit concerns in the original
/// (VID_LockBuffer/VID_UnlockBuffer, or no-ops for the software driver) with
/// no equivalent under OpenTK's whole-frame texture upload -- permanently
/// not ported here.
/// </summary>
public static unsafe class DInit
{
    private const int NUM_MIPS = 4;

    // d_subdiv16, d_mipcap, d_mipscale
    public static readonly Cvar_t* d_subdiv16 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* d_mipcap = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* d_mipscale = NativeMemory.AllocZeroed<Cvar_t>();

    private static readonly float[] basemip = [1.0f, 0.5f * 0.8f, 0.25f * 0.8f];

    // D_Init
    public static void Init()
    {
        d_subdiv16->Name = "d_subdiv16"u8;
        d_subdiv16->String = "1"u8;
        Cvar.RegisterVariable(d_subdiv16);
        d_mipcap->Name = "d_mipcap"u8;
        d_mipcap->String = "0"u8;
        Cvar.RegisterVariable(d_mipcap);
        d_mipscale->Name = "d_mipscale"u8;
        d_mipscale->String = "1"u8;
        Cvar.RegisterVariable(d_mipscale);

        DIface.r_drawpolys = false;
        DIface.r_worldpolysbacktofront = false;
        DIface.r_recursiveaffinetriangles = true;
        DIface.r_pixbytes = 1;
        DIface.r_aliasuvscale = 1.0f;
    }

    // D_SetupFrame
    public static void SetupFrame()
    {
        DLocal.d_viewbuffer = DIface.r_dowarp ? DIface.r_warpbuffer : Vid.vid.Buffer;

        RShared.screenwidth = DIface.r_dowarp ? DIface.WARP_WIDTH : (int)Vid.vid.RowBytes;

        DLocal.d_roverwrapped = false;
        DLocal.d_initial_rover = DLocal.sc_rover;

        DLocal.d_minmip = (int)DInit.d_mipcap->Value;
        if (DLocal.d_minmip > 3)
            DLocal.d_minmip = 3;
        else if (DLocal.d_minmip < 0)
            DLocal.d_minmip = 0;

        for (int i = 0; i < NUM_MIPS - 1; i++)
            DLocal.d_scalemip[i] = basemip[i] * DInit.d_mipscale->Value;

        DLocal.d_drawspans = &DScan.DrawSpans8;

        DPolyse.d_aflatcolor = 0;
    }
}
