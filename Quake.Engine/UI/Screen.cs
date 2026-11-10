// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// screen.c -- master for refresh, status bar, console, chat, notify, etc.
///
/// It ties together every ported r_*.c file, plus sbar.c (Sbar.cs), menu.c
/// (Menu.cs), view.c (View.cs -- V_RenderView is a view.c function distinct
/// from RMain.RenderView), snd_dma.c (SndDma.cs), and console.c's drawing
/// half (Console.cs), all called directly. VID_Update is the one
/// function-pointer hook (see its field below), since its implementation
/// lives in the display backend. VID_*/D_EnableBackBufferAccess/D_UpdateRects
/// (DirectDraw display-layer concerns) are permanently dropped with a
/// comment instead, per the precedent already established throughout
/// RMain.cs/REdge.cs/Draw.cs/RMisc.cs.
/// </summary>
public static unsafe class Screen
{
    private static void NoOp()
    {
    }

    // VID_Update -- the display backend's blit-to-screen step (vid_win.c).
    // The original calls this synchronously at the end of every
    // SCR_UpdateScreen, so every one of this file's many UpdateScreen()
    // call sites (ModalMessage, BringDownConsole, Console's SendKeyEvents
    // loop, Key_ConsoleCmd's forced update, not just Host_Frame's normal
    // per-frame call) immediately shows what it just drew. Wired by
    // VidWindow.Init to VidWindow.Present; left as a no-op for callers with
    // no display backend (HostTests.cs, ScreenTests.cs), matching the
    // original's behavior of having nothing to blit when there's no window.
    public static delegate* managed<void> VID_Update = &NoOp;

    // only the refresh window will be updated unless these variables are flagged
    public static int scr_copytop;
    public static int scr_copyeverything;

    public static float scr_con_current;
    public static float scr_conlines; // lines of console to display

    private static float oldscreensize, oldfov;

    public static readonly Cvar_t* scr_viewsize = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_fov = NativeMemory.AllocZeroed<Cvar_t>(); // 10 - 170
    public static readonly Cvar_t* scr_conspeed = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_centertime = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_showram = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_showturtle = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_showpause = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_printspeed = NativeMemory.AllocZeroed<Cvar_t>();

    // UI scaling (not in the original) -- Mark V cvar_list_sheet.h; see Canvas.cs
    public static readonly Cvar_t* scr_scaleauto = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_conscale = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_menuscale = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_sbarscale = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scr_crosshairscale = NativeMemory.AllocZeroed<Cvar_t>();

    public static bool scr_initialized; // ready to draw

    public static QPic_t* scr_ram;
    public static QPic_t* scr_net;
    public static QPic_t* scr_turtle;

    public static int scr_fullupdate;

    private static int clearconsole;

    // internal (not private): Console.cs's ported Con_DrawNotify clears this
    // directly, same as the original's console.c reaching into screen.c's
    // global.
    internal static int clearnotify;

    // pconupdate -- screen.c's own local vrect_t*, not RShared's; left null
    // (VID_Update is dropped, see below, so nothing ever needs to chase
    // this pointer).
    public static VRect_t* pconupdate;
    public static readonly VRect_t* scr_vrect = NativeMemory.AllocZeroed<VRect_t>();

    public static bool scr_disabled_for_loading;
    public static bool scr_drawloading;
    private static double scr_disabled_time;
    public static bool scr_skipupdate;

    public static bool block_drawing;

    /*
    ===============================================================================

    CENTER PRINTING

    ===============================================================================
    */

    // scr_centerstring[1024] -- Hunk/Zone-independent NativeMemory buffer
    // (not Hunk-allocated: unlike con_text, nothing ever frees/reinits the
    // arena this needs to survive), matching the "stable native buffer for
    // a plain fixed-size C array" idiom used throughout this port.
    internal static readonly byte* scr_centerstring = NativeMemory.AllocArrayZeroed<byte>(1024);
    private static double scr_centertime_start; // for slow victory printing

    // internal (not private): ScreenTests.cs drives SCR_CheckDrawCenterString/
    // SCR_CenterPrint's gating logic directly, same InternalsVisibleTo
    // pattern as RSprite.cs's clip_verts.
    internal static float scr_centertime_off;
    internal static int scr_center_lines;
    private static int scr_erase_lines;
    private static int scr_erase_center;

    // SCR_CenterPrint
    public static void CenterPrint(byte* str)
    {
        Q.StrNCpy(scr_centerstring, str, 1023);
        scr_centertime_off = scr_centertime->Value;
        scr_centertime_start = Cl.time;

        // count the number of lines for centering
        scr_center_lines = 1;
        while (*str != 0)
        {
            if (*str == (byte)'\n')
            {
                scr_center_lines++;
            }

            str++;
        }
    }

    // SCR_EraseCenterString
    public static void EraseCenterString()
    {
        if (scr_erase_center++ > Vid.vid.NumPages)
        {
            scr_erase_lines = 0;
            return;
        }

        Canvas.Set(CanvasType.Console);
        int y = scr_center_lines <= 4 ? (int)(Canvas.Height * 0.35f) : 48;

        scr_copytop = 1;
        int s = Canvas.Scale;
        Draw.TileClear(0, (y * s) + Canvas.OriginY, (int)Vid.vid.Width, 8 * scr_erase_lines * s);
    }

    // SCR_DrawCenterString
    public static void DrawCenterString()
    {
        // the finale prints the characters one at a time
        int remaining = Cl.intermission != 0
            ? (int)(scr_printspeed->Value * (Cl.time - scr_centertime_start))
            : 9999;

        scr_erase_center = 0;
        byte* start = scr_centerstring;

        Canvas.Set(CanvasType.Console);
        int y = scr_center_lines <= 4 ? (int)(Canvas.Height * 0.35f) : 48;

        while (true)
        {
            // scan the width of the line
            int l;
            for (l = 0; l < 40; l++)
            {
                if (start[l] == (byte)'\n' || start[l] == 0)
                {
                    break;
                }
            }

            int x = (Canvas.Width - (l * 8)) / 2;
            for (int j = 0; j < l; j++, x += 8)
            {
                Draw.Character(x, y, start[j]);
                if (remaining-- == 0)
                {
                    return;
                }
            }

            y += 8;

            while (*start != 0 && *start != (byte)'\n')
            {
                start++;
            }

            if (*start == 0)
            {
                break;
            }

            start++; // skip the \n
        }
    }

    // SCR_CheckDrawCenterString
    public static void CheckDrawCenterString()
    {
        scr_copytop = 1;
        if (scr_center_lines > scr_erase_lines)
        {
            scr_erase_lines = scr_center_lines;
        }

        scr_centertime_off -= Host.host_frametime;

        if (scr_centertime_off <= 0 && Cl.intermission == 0)
        {
            return;
        }

        if (Keys.key_dest != KeyDest.Game)
        {
            return;
        }

        DrawCenterString();
    }

    //=============================================================================

    // CalcFov
    public static float CalcFov(float fov_x, float width, float height)
    {
        if (fov_x < 1 || fov_x > 179)
        {
            Sys.Error($"Bad fov: {fov_x:F6}");
        }

        float x = width / MathF.Tan(fov_x / 360 * MathLib.M_PI);
        float a = MathF.Atan(height / x);
        a = a * 360 / MathLib.M_PI;
        return a;
    }

    // Not in the original -- Hor+ field of view: treats fov_x as the
    // horizontal FOV of a 4:3 display (what the original was tuned for) and
    // widens/narrows it to the actual display aspect so the vertical FOV
    // stays the same at every aspect ratio. Returns fov_x unchanged at 4:3.
    // Clamped to [1, 179] so CalcFov's "Bad fov" check can never fire.
    public static float CalcHorPlusFov(float fov_x, float width, float height)
    {
        float t = MathF.Tan(fov_x / 360 * MathLib.M_PI) * (width / height) / (4.0f / 3.0f);
        float a = MathF.Atan(t) * 360 / MathLib.M_PI;
        return Math.Clamp(a, 1, 179);
    }

    // SCR_CalcRefdef
    //
    // Must be called whenever vid changes
    // Internal use only
    private static void CalcRefdef()
    {
        scr_fullupdate = 0; // force a background redraw
        Vid.vid.RecalcRefdef = 0;

        // force the status bar to redraw
        Sbar.Changed();

        //========================================

        // bound viewsize
        if (scr_viewsize->Value < 30)
        {
            Cvar.Set("viewsize"u8, "30"u8);
        }

        if (scr_viewsize->Value > 120)
        {
            Cvar.Set("viewsize"u8, "120"u8);
        }

        // bound field of view
        if (scr_fov->Value < 10)
        {
            Cvar.Set("fov"u8, "10"u8);
        }

        if (scr_fov->Value > 170)
        {
            Cvar.Set("fov"u8, "170"u8);
        }

        // r_refdef->fov_x = scr_fov.value -- widened to Hor+ (CalcHorPlusFov's
        // own comment), based on the whole framebuffer's displayed aspect
        // (vid.aspect is the pixel aspect; 0 before any mode is set counts as
        // square) rather than vrect, so viewsize/status bar/water warp
        // changes don't alter the FOV.
        float pixelAspect = Vid.vid.Aspect > 0 ? Vid.vid.Aspect : 1;
        RShared.r_refdef->FovX = Vid.vid.Width != 0 && Vid.vid.Height != 0
            ? CalcHorPlusFov(scr_fov->Value, Vid.vid.Width * pixelAspect, Vid.vid.Height)
            : scr_fov->Value;
        RShared.r_refdef->FovY = CalcFov(RShared.r_refdef->FovX, RShared.r_refdef->Vrect.Width, RShared.r_refdef->Vrect.Height);

        // intermission is always full screen
        float size = Cl.intermission != 0 ? 120 : scr_viewsize->Value;

        if (size >= 120)
        {
            Sbar.sb_lines = 0; // no status bar at all
        }
        else if (size >= 110)
        {
            Sbar.sb_lines = 24; // no inventory
        }
        else
        {
            Sbar.sb_lines = 24 + 16 + 8;
        }

        // these calculations mirror those in R_Init() for r_refdef, but take
        // no account of water warping
        VRect_t vrect = default;
        vrect.X = 0;
        vrect.Y = 0;
        vrect.Width = (int)Vid.vid.Width;
        vrect.Height = (int)Vid.vid.Height;

        // Sbar.sb_lines is in status bar canvas units (Canvas.SbarScale
        // physical lines each); the refresh works in physical lines
        int sbPhysLines = Sbar.sb_physlines;

        RMain.SetVrect(&vrect, scr_vrect, sbPhysLines);

        // guard against going from one mode to another that's less than half
        // the vertical resolution
        if (scr_con_current > Vid.vid.ConHeight)
        {
            scr_con_current = Vid.vid.ConHeight;
        }

        // notify the refresh of the change
        RMain.ViewChanged(&vrect, sbPhysLines, Vid.vid.Aspect);
    }

    // SCR_SizeUp_f
    //
    // Keybinding command
    public static void SizeUp_f()
    {
        Cvar.SetValue("viewsize"u8, scr_viewsize->Value + 10);
        Vid.vid.RecalcRefdef = 1;
    }

    // SCR_SizeDown_f
    //
    // Keybinding command
    public static void SizeDown_f()
    {
        Cvar.SetValue("viewsize"u8, scr_viewsize->Value - 10);
        Vid.vid.RecalcRefdef = 1;
    }

    //============================================================================

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        Cvar.RegisterVariable(cvar);
    }

    // SCR_Init
    public static void Init()
    {
        RegisterCvar(scr_fov, "fov"u8, "90"u8);
        RegisterCvar(scr_viewsize, "viewsize"u8, "100"u8, true);
        RegisterCvar(scr_conspeed, "scr_conspeed"u8, "300"u8);
        RegisterCvar(scr_showram, "showram"u8, "1"u8);
        RegisterCvar(scr_showturtle, "showturtle"u8, "0"u8);
        RegisterCvar(scr_showpause, "showpause"u8, "1"u8);
        RegisterCvar(scr_centertime, "scr_centertime"u8, "2"u8);
        RegisterCvar(scr_printspeed, "scr_printspeed"u8, "8"u8);

        // Mark V: scr_scaleauto -1 = off, 0 = use the manual scales, 1..3 = automatic
        RegisterCvar(scr_scaleauto, "scr_scaleauto"u8, "1"u8, true);
        RegisterCvar(scr_conscale, "scr_conscale"u8, "1"u8, true);
        RegisterCvar(scr_menuscale, "scr_menuscale"u8, "1"u8, true);
        RegisterCvar(scr_sbarscale, "scr_sbarscale"u8, "1"u8, true);
        RegisterCvar(scr_crosshairscale, "scr_crosshairscale"u8, "1"u8, true);

        // register our commands
        Cmd.AddCommand("screenshot"u8, &ScreenShot_f);
        Cmd.AddCommand("sizeup"u8, &SizeUp_f);
        Cmd.AddCommand("sizedown"u8, &SizeDown_f);

        scr_ram = Draw.PicFromWad("ram"u8);
        scr_net = Draw.PicFromWad("net"u8);
        scr_turtle = Draw.PicFromWad("turtle"u8);

        scr_initialized = true;
    }

    // SCR_DrawRam
    public static void DrawRam()
    {
        if (scr_showram->Value == 0)
        {
            return;
        }

        if (!DSurf.r_cache_thrash)
        {
            return;
        }

        Canvas.Set(CanvasType.Default);
        Draw.Pic(scr_vrect->X + 32, scr_vrect->Y, scr_ram);
    }

    // SCR_DrawTurtle
    private static int turtleCount;

    public static void DrawTurtle()
    {
        if (scr_showturtle->Value == 0)
        {
            return;
        }

        if (Host.host_frametime < 0.1f)
        {
            turtleCount = 0;
            return;
        }

        turtleCount++;
        if (turtleCount < 3)
        {
            return;
        }

        Canvas.Set(CanvasType.Default);
        Draw.Pic(scr_vrect->X, scr_vrect->Y, scr_turtle);
    }

    // SCR_DrawNet
    public static void DrawNet()
    {
        if (Host.realtime - Cls.last_received_message < 0.3)
        {
            return;
        }

        if (ClDemo.demoplayback)
        {
            return;
        }

        Canvas.Set(CanvasType.Default);
        Draw.Pic(scr_vrect->X + 64, scr_vrect->Y, scr_net);
    }

    // SCR_DrawPause (originally "DrawPause" in the header comment, the
    // function itself is SCR_DrawPause)
    public static void DrawPause()
    {
        if (scr_showpause->Value == 0) // turn off for screenshots
        {
            return;
        }

        if (!Cl.paused)
        {
            return;
        }

        QPic_t* pic = Draw.CachePic("gfx/pause.lmp"u8);
        Canvas.Set(CanvasType.Menu);
        Draw.Pic((Canvas.MENU_WIDTH - pic->Width) / 2, (Canvas.MENU_HEIGHT - 48 - pic->Height) / 2, pic);
    }

    // SCR_DrawLoading
    public static void DrawLoading()
    {
        if (!scr_drawloading)
        {
            return;
        }

        QPic_t* pic = Draw.CachePic("gfx/loading.lmp"u8);
        Canvas.Set(CanvasType.Menu);
        Draw.Pic((Canvas.MENU_WIDTH - pic->Width) / 2, (Canvas.MENU_HEIGHT - 48 - pic->Height) / 2, pic);
    }

    //=============================================================================

    // SCR_SetUpToDrawConsole
    public static void SetUpToDrawConsole()
    {
        Con.CheckResize(((int)Vid.vid.ConWidth >> 3) - 2);

        if (scr_drawloading)
        {
            return; // never a console with loading plaque
        }

        // decide on the height of the console
        Con.con_forcedup = Cl.worldmodel == null || Cls.signon != Cls.SIGNONS;

        if (Con.con_forcedup)
        {
            scr_conlines = Vid.vid.ConHeight; // full screen
            scr_con_current = scr_conlines;
        }
        else if (Keys.key_dest == KeyDest.Console)
        {
            scr_conlines = Vid.vid.ConHeight / 2; // half screen
        }
        else
        {
            scr_conlines = 0; // none visible
        }

        if (scr_conlines < scr_con_current)
        {
            scr_con_current -= scr_conspeed->Value * Host.host_frametime;
            if (scr_conlines > scr_con_current)
            {
                scr_con_current = scr_conlines;
            }
        }
        else if (scr_conlines > scr_con_current)
        {
            scr_con_current += scr_conspeed->Value * Host.host_frametime;
            if (scr_conlines < scr_con_current)
            {
                scr_con_current = scr_conlines;
            }
        }

        if (clearconsole++ < Vid.vid.NumPages)
        {
            scr_copytop = 1;
            int conPhys = (int)scr_con_current * Canvas.ConScale;
            Draw.TileClear(0, conPhys, (int)Vid.vid.Width, (int)Vid.vid.Height - conPhys);
            Sbar.Changed();
        }
        else if (clearnotify++ < Vid.vid.NumPages)
        {
            scr_copytop = 1;
            Draw.TileClear(0, 0, (int)Vid.vid.Width, Con.con_notifylines * Canvas.ConScale);
        }
        else
        {
            Con.con_notifylines = 0;
        }
    }

    // SCR_DrawConsole
    public static void DrawConsole()
    {
        Canvas.Set(CanvasType.Console);

        if (scr_con_current != 0)
        {
            scr_copyeverything = 1;
            Con.DrawConsole(scr_con_current, true);
            clearconsole = 0;
        }
        else
        {
            if (Keys.key_dest == KeyDest.Game || Keys.key_dest == KeyDest.Message)
            {
                Con.DrawNotify(); // only draw notify in game
            }
        }
    }

    /*
    ==============================================================================

                            SCREEN SHOTS

    ==============================================================================
    */

    // WritePCXfile
    public static void WritePCXfile(byte* filename, byte* data, int width, int height, int rowbytes, byte* palette)
    {
        Pcx_t* pcx = (Pcx_t*)Hunk.TempAlloc((width * height * 2) + 1000);
        if (pcx == null)
        {
            Con.Printf("SCR_ScreenShot_f: not enough memory\n"u8);
            return;
        }

        pcx->Manufacturer = 0x0a; // PCX id
        pcx->Version = 5;         // 256 color
        pcx->Encoding = 1;        // uncompressed
        pcx->BitsPerPixel = 8;    // 256 color
        pcx->Xmin = 0;
        pcx->Ymin = 0;
        pcx->Xmax = (ushort)Com.LittleShort((short)(width - 1));
        pcx->Ymax = (ushort)Com.LittleShort((short)(height - 1));
        pcx->Hres = (ushort)Com.LittleShort((short)width);
        pcx->Vres = (ushort)Com.LittleShort((short)height);
        Q.MemSet(pcx->Palette, 0, 48);
        pcx->ColorPlanes = 1; // chunky image
        pcx->BytesPerLine = (ushort)Com.LittleShort((short)width);
        pcx->PaletteType = (ushort)Com.LittleShort(2); // not a grey scale
        Q.MemSet(pcx->Filler, 0, 58);

        // pack the image
        byte* pack = &pcx->Data;

        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                if ((*data & 0xc0) != 0xc0)
                {
                    *pack++ = *data++;
                }
                else
                {
                    *pack++ = 0xc1;
                    *pack++ = *data++;
                }
            }

            data += rowbytes - width;
        }

        // write the palette
        *pack++ = 0x0c; // palette ID byte
        for (int i = 0; i < 768; i++)
        {
            *pack++ = *palette++;
        }

        // write output file
        int length = (int)(pack - (byte*)pcx);
        Com.WriteFile(filename, pcx, length);
    }

    // SCR_ScreenShot_f
    public static void ScreenShot_f()
    {
        byte* pcxname = stackalloc byte[80];
        byte* checkname = stackalloc byte[QuakeDef.MAX_OSPATH];

        // find a file name to save it to
        Q.StrCpy(pcxname, "quake00.pcx"u8);

        int i;
        for (i = 0; i <= 99; i++)
        {
            pcxname[5] = (byte)((i / 10) + '0');
            pcxname[6] = (byte)((i % 10) + '0');
            int written = FormatCheckname(checkname, QuakeDef.MAX_OSPATH, Com.com_gamedir, pcxname);
            checkname[written] = 0;
            if (Sys.FileTime(checkname) == -1)
            {
                break; // file doesn't exist
            }
        }
        if (i == 100)
        {
            Con.Printf("SCR_ScreenShot_f: Couldn't create a PCX file\n"u8);
            return;
        }

        // save the pcx file
        // D_EnableBackBufferAccess/D_DisableBackBufferAccess -- DirectDraw
        // display-layer concern, permanently dropped per the existing
        // DInit.cs/RMain.cs precedent (meaningless without a real backbuffer).

        WritePCXfile(pcxname, Vid.vid.Buffer, (int)Vid.vid.Width, (int)Vid.vid.Height, (int)Vid.vid.RowBytes, RMisc.host_basepal);

        Con.Printf($"Wrote {pcxname}\n");
    }

    // sprintf (checkname, "%s/%s", com_gamedir, pcxname) -- not part of the
    // original source, a small helper since Q.cs has no sprintf equivalent.
    private static int FormatCheckname(byte* dest, int destSize, byte* gamedir, byte* name)
    {
        int n = 0;
        for (int k = 0; gamedir[k] != 0 && n < destSize - 1; k++)
        {
            dest[n++] = gamedir[k];
        }

        if (n < destSize - 1)
        {
            dest[n++] = (byte)'/';
        }

        for (int k = 0; name[k] != 0 && n < destSize - 1; k++)
        {
            dest[n++] = name[k];
        }

        return n;
    }

    //=============================================================================

    // SCR_BeginLoadingPlaque
    public static void BeginLoadingPlaque()
    {
        SndDma.StopAllSounds(true);

        if (Cls.state != CActive.Connected)
        {
            return;
        }

        if (Cls.signon != Cls.SIGNONS)
        {
            return;
        }

        // redraw with no console and the loading plaque
        Con.ClearNotify();
        scr_centertime_off = 0;
        scr_con_current = 0;

        scr_drawloading = true;
        scr_fullupdate = 0;
        Sbar.Changed();
        UpdateScreen();
        scr_drawloading = false;

        scr_disabled_for_loading = true;
        scr_disabled_time = Host.realtime;
        scr_fullupdate = 0;
    }

    // SCR_EndLoadingPlaque
    public static void EndLoadingPlaque()
    {
        scr_disabled_for_loading = false;
        scr_fullupdate = 0;
        Con.ClearNotify();
    }

    //=============================================================================

    private static byte* scr_notifystring;
    public static bool scr_drawdialog;

    // SCR_DrawNotifyString
    public static void DrawNotifyString()
    {
        byte* start = scr_notifystring;

        Canvas.Set(CanvasType.Console);
        int y = (int)(Canvas.Height * 0.35f);

        while (true)
        {
            int l;
            for (l = 0; l < 40; l++)
            {
                if (start[l] == (byte)'\n' || start[l] == 0)
                {
                    break;
                }
            }

            int x = (Canvas.Width - (l * 8)) / 2;
            for (int j = 0; j < l; j++, x += 8)
            {
                Draw.Character(x, y, start[j]);
            }

            y += 8;

            while (*start != 0 && *start != (byte)'\n')
            {
                start++;
            }

            if (*start == 0)
            {
                break;
            }

            start++; // skip the \n
        }
    }

    // SCR_ModalMessage
    //
    // Displays a text string in the center of the screen and waits for a Y
    // or N keypress. Sys.SendKeyEvents pumps window events through the
    // active display backend's Sys.SendKeyEventsHook (see Sys.cs); a caller
    // with no display backend wired up (HostTests.cs) will throw when it
    // reaches that call, honestly reflecting that the input subsystem this
    // function fundamentally depends on doesn't exist there.
    public static bool ModalMessage(Utf8String text)
    {
        if (Cls.state == CActive.Dedicated)
        {
            return true;
        }

        scr_notifystring = text;

        // draw a fresh screen
        scr_fullupdate = 0;
        scr_drawdialog = true;
        UpdateScreen();
        scr_drawdialog = false;

        SndDma.ClearBuffer(); // so dma doesn't loop current sound

        do
        {
            Keys.key_count = -1; // wait for a key down and up
            Sys.SendKeyEvents();
        } while (Keys.key_lastpress != 'y' && Keys.key_lastpress != 'n' && Keys.key_lastpress != Keys.K_ESCAPE);

        scr_fullupdate = 0;
        UpdateScreen();

        return Keys.key_lastpress == 'y';
    }

    //=============================================================================

    // SCR_BringDownConsole
    //
    // Brings the console down and fades the palettes back to normal
    public static void BringDownConsole()
    {
        scr_centertime_off = 0;

        for (int i = 0; i < 20 && scr_conlines != scr_con_current; i++)
        {
            UpdateScreen();
        }

        Cl.cshifts[0].Percent = 0; // no area contents palette on next frame

        // VID_SetPalette(host_basepal) -- DirectDraw display-layer concern,
        // permanently dropped per the existing precedent.
    }

    // SCR_UpdateScreen
    //
    // This is called every frame, and can also be called explicitly to
    // flush text to the screen.
    //
    // WARNING: be very careful calling this from elsewhere, because the
    // refresh needs almost the entire 256k of stack space!
    private static float oldscr_viewsize;
    private static float oldlcd_x;

    public static void UpdateScreen()
    {
        if (scr_skipupdate || block_drawing)
        {
            return;
        }

        scr_copytop = 0;
        scr_copyeverything = 0;

        if (scr_disabled_for_loading)
        {
            if (Host.realtime - scr_disabled_time > 60)
            {
                scr_disabled_for_loading = false;
                Con.Printf("load failed.\n"u8);
            }
            else
            {
                return;
            }
        }

        if (Cls.state == CActive.Dedicated)
        {
            return; // stdout only
        }

        if (!scr_initialized || !Con.con_initialized)
        {
            return; // not initialized yet
        }

        Canvas.Set(CanvasType.Default);

        // UI scale (window size or scr_*scale cvars changed)
        if (Canvas.Update())
        {
            Vid.vid.RecalcRefdef = 1;
        }

        if (scr_viewsize->Value != oldscr_viewsize)
        {
            oldscr_viewsize = scr_viewsize->Value;
            Vid.vid.RecalcRefdef = 1;
        }

        // check for vid changes
        if (oldfov != scr_fov->Value)
        {
            oldfov = scr_fov->Value;
            Vid.vid.RecalcRefdef = 1;
        }

        if (oldlcd_x != View.lcd_x->Value)
        {
            oldlcd_x = View.lcd_x->Value;
            Vid.vid.RecalcRefdef = 1;
        }

        if (oldscreensize != scr_viewsize->Value)
        {
            oldscreensize = scr_viewsize->Value;
            Vid.vid.RecalcRefdef = 1;
        }

        if (Vid.vid.RecalcRefdef != 0)
        {
            // something changed, so reorder the screen
            CalcRefdef();
        }

        // do 3D refresh drawing, and then update the screen
        // D_EnableBackBufferAccess -- DirectDraw display-layer concern,
        // permanently dropped (see type summary).

        if (scr_fullupdate++ < Vid.vid.NumPages)
        { // clear the entire screen
            scr_copyeverything = 1;
            Draw.TileClear(0, 0, (int)Vid.vid.Width, (int)Vid.vid.Height);
            Sbar.Changed();
        }

        pconupdate = null;

        SetUpToDrawConsole();
        EraseCenterString();

        // D_DisableBackBufferAccess / VID_LockBuffer -- permanently dropped.

        View.RenderView();

        // VID_UnlockBuffer / D_EnableBackBufferAccess -- permanently dropped.

        if (scr_drawdialog)
        {
            Sbar.Draw_();
            Draw.FadeScreen();
            DrawNotifyString();
            scr_copyeverything = 1;
        }
        else if (scr_drawloading)
        {
            DrawLoading();
            Sbar.Draw_();
        }
        else if (Cl.intermission == 1 && Keys.key_dest == KeyDest.Game)
        {
            Sbar.IntermissionOverlay();
        }
        else if (Cl.intermission == 2 && Keys.key_dest == KeyDest.Game)
        {
            Sbar.FinaleOverlay();
            CheckDrawCenterString();
        }
        else if (Cl.intermission == 3 && Keys.key_dest == KeyDest.Game)
        {
            CheckDrawCenterString();
        }
        else
        {
            DrawRam();
            DrawNet();
            DrawTurtle();
            DrawPause();
            CheckDrawCenterString();
            Sbar.Draw_();
            DrawConsole();
            Menu.Draw_();
        }

        // D_DisableBackBufferAccess -- permanently dropped.
        // D_UpdateRects(pconupdate) -- permanently dropped (pconupdate is
        // always null here anyway, see above).

        View.UpdatePalette();

        // update one of three areas
        // VID_Update(&vrect) -- the vrect partial-update selection itself is
        // a DirectDraw display-layer concern with no observable effect here
        // (this port always redraws everything), but the underlying blit it
        // triggered is very much still needed -- see VID_Update's own
        // comment above.
        VID_Update();
    }

    // SCR_UpdateWholeScreen
}
