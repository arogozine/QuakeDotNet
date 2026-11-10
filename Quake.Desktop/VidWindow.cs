// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Desktop;

/// <summary>
/// vid_win.c's VID_Init/VID_Shutdown/VID_SetPalette/FlipScreen -- OS-agnostic
/// subset only, per open_tk.md: creates an OpenTK surface that is either a
/// fixed-size window or a borderless window covering the whole monitor at
/// the desktop's current resolution (the original's fullscreen mode-list/
/// ChangeDisplaySettings/DirectDraw-vs-DIB branching is out of scope -- no
/// display mode is ever changed, see SetMode()'s own comment), plus the
/// vid_win.c console commands that still make sense (vid_describemodes,
/// vid_fullscreen, ...), allocates Vid.vid's indexed framebuffer, and each
/// frame streams it unexpanded into an R8UI texture (storage allocated once
/// per resolution) alongside Vid.d_8to24table as a 256x1 palette texture;
/// the fragment shader does the index -> RGBA palette lookup on the GPU.
/// Also ports vid_win.c's VID_AllocBuffers (the z-buffer + surface cache
/// allocation, DSurf.InitCaches's call site) -- see AllocBuffers()'s own
/// comment -- and, windowed-only, VID_SetMode (see SetMode()'s own comment),
/// which VidMenu.cs's ported VID_MenuDraw/VID_MenuKey (the Video Options
/// menu, wired up below in Init()) calls to resize into a picked resolution.
/// Keyboard/mouse routing lives in InputHost.cs; this class owns only the
/// window/GL/framebuffer lifecycle.
/// </summary>
public static unsafe class VidWindow
{
    // vid_width/vid_height -- new cvars (not in the original, which instead
    // has a vid_mode index into a whole mode-list table): this port's
    // deliberately simplified stand-in for VID_Init's mode selection --
    // rather than an index into a modelist[] table, the resolution is
    // written here directly by VidMenu.cs's MenuKey after picking an entry
    // from DisplayModeSource.GetAvailableModes()'s OS-agnostic list (Rule 5:
    // the Windows-only EnumDisplaySettings/ChangeDisplaySettings fullscreen
    // machinery the original's modelist[] partly depended on is still out
    // of scope; only the resolution *value* changed, not the windowed-only
    // architecture).
    // 1024x768 -- DisplayModeList.MinWidth/MinHeight, the minimum resolution
    // this port supports; also the starting resolution (not the original's
    // 320x200/640x480, since this port targets modern displays).
    // Archived so the chosen windowed size survives a restart, like the
    // original's archived vid_config_x/vid_config_y. Only the windowed size:
    // fullscreen always uses the desktop resolution.
    public static readonly Cvar_t* vid_width = Cvar.Alloc("vid_width"u8, "1024"u8, archive: true);
    public static readonly Cvar_t* vid_height = Cvar.Alloc("vid_height"u8, "768"u8, archive: true);

    // _vid_fullscreen -- not in the original (whose closest equivalent is the
    // archived vid_fullscreen_mode index into modelist[]): 1 = borderless
    // fullscreen at the desktop resolution (the default), 0 = windowed at
    // vid_width x vid_height. Underscore-prefixed like _windowed_mouse/
    // _vid_default_mode_win, and can't be called vid_fullscreen since that
    // name is the console command below (Cmd.AddCommand refuses names already
    // used by a cvar).
    public static readonly Cvar_t* _vid_fullscreen = Cvar.Alloc("_vid_fullscreen"u8, "1"u8, archive: true);

    public static NativeWindow Window { get; private set; } = null!;

    // Whether the current mode is borderless fullscreen -- the modestate ==
    // MS_FULLDIB test the original makes throughout vid_win.c.
    public static bool IsFullscreen { get; private set; }

    private static int indexTexture;
    private static int paletteTexture;
    private static byte* videoBuffer;

    /*
    ========================
    VID_Init

    OS-agnostic subset: registers vid_width/vid_height, creates the window +
    GL context, allocates vid.buffer, builds the initial palette table, and
    creates the GPU index + palette textures. Skips: mode-list enumeration,
    DirectDraw/MGL setup, GDI static-color remapping (see Vid.SetPalette's
    own comment) -- none of which have a meaning once the display is just an
    OpenGL texture blit.
    ========================
    */
    public static void Init()
    {
        Cvar.RegisterVariable(vid_width);
        Cvar.RegisterVariable(vid_height);
        Cvar.RegisterVariable(_vid_fullscreen);

        // VID_Init's Cmd_AddCommand block (vid_win.c:2080-2088), minus
        // vid_testmode/vid_forcemode (Rule 8: those exist so the user can
        // back out of a fullscreen mode that leaves the display unusable --
        // this port never changes the display mode, so there's nothing to
        // back out of).
        Cmd.AddCommand("vid_nummodes"u8, &NumModes_f);
        Cmd.AddCommand("vid_describecurrentmode"u8, &DescribeCurrentMode_f);
        Cmd.AddCommand("vid_describemode"u8, &DescribeMode_f);
        Cmd.AddCommand("vid_describemodes"u8, &DescribeModes_f);
        Cmd.AddCommand("vid_windowed"u8, &Windowed_f);
        Cmd.AddCommand("vid_fullscreen"u8, &Fullscreen_f);
        Cmd.AddCommand("vid_minimize"u8, &Minimize_f);

        bool fullscreen = _vid_fullscreen->Value != 0;
        int width = Math.Clamp((int)vid_width->Value, DisplayModeList.MinWidth, RShared.MAXWIDTH);
        int height = Math.Clamp((int)vid_height->Value, DisplayModeList.MinHeight, RShared.MAXHEIGHT);

        // WindowBorder.Fixed -- vid_win.c's windowed-mode style bits (vid_win.c
        // ~1267-1269) never include WS_THICKFRAME/WS_SIZEBOX, so the user can't
        // drag-resize the window; the original's only way to change resolution
        // is picking a mode from the Video Options menu (VID_MenuKey's K_ENTER
        // case calling VID_SetMode), which SetMode below ports.
        // StartVisible = false: SetWindowGeometry below may turn this into a
        // borderless fullscreen window first, so the user never sees a
        // windowed flash (VID_Init's own hide_window trick, vid_win.c:2159).
        NativeWindowSettings settings = new()
        {
            ClientSize = new Vector2i(width, height),
            Title = "QuakeDotNet",
            Profile = ContextProfile.Core,
            APIVersion = new Version(3, 3),
            StartVisible = false,
            WindowBorder = WindowBorder.Fixed,
        };

        Window = new NativeWindow(settings);
        Window.Context.MakeCurrent();

        Vector2i bufferSize = SetWindowGeometry(fullscreen, width, height);
        IsFullscreen = fullscreen;
        Window.IsVisible = true;
        width = bufferSize.X;
        height = bufferSize.Y;

        // Sys_Quit's "signal this loop to exit" equivalent for a window-
        // close click, since there's no WM_CLOSE/Host_Quit_f console command
        // involved (see Sys.Quit's own comment).
        Window.Closing += _ => Sys.quitRequested = true;

        InputHost.Attach(Window);

        // Screen.VID_Update -- wire the original's synchronous end-of-
        // SCR_UpdateScreen blit to this backend's Present (see that field's
        // own comment, Screen.cs): every UpdateScreen() call, not just
        // Host_Frame's normal per-frame one, must show what it just drew
        // (e.g. Screen.ModalMessage's Y/N dialog, drawn while blocked
        // waiting for input deep inside a nested window-event pump that
        // never returns to Program.cs's main loop to call Present itself).
        Screen.VID_Update = &Present;

        AllocBuffers(width, height);

        Vid.vid.MaxWarpWidth = DIface.WARP_WIDTH;
        Vid.vid.MaxWarpHeight = DIface.WARP_HEIGHT;
        Vid.vid.NumPages = 1;

        // vid.colormap = host_colormap; vid.fullbright = ... -- the fullbright
        // scalar itself is dropped (Rule 8: nothing in this port's software
        // renderer reads vid.fullbright -- RSurf.cs/RMain.cs use the
        // r_fullbright cvar instead -- so there is no C behavior to match).
        Vid.vid.Colormap = Host.host_colormap;

        Vid.SetPalette(RMisc.host_basepal);

        // vid_menudrawfn/vid_menukeyfn -- VID_Init's own registration of the
        // Video Options submenu (vid_win.c:2174-2175), pointed at VidMenu's
        // VID_MenuDraw/VID_MenuKey port instead of this file's own code,
        // since the menu logic lives in Quake.Desktop (it needs SetMode
        // below) while Menu.cs (Quake.Engine) only owns the function-pointer
        // hooks, exactly like the original's own menu.c/vid_win.c split.
        Menu.vid_menudrawfn = &VidMenu.MenuDraw;
        Menu.vid_menukeyfn = &VidMenu.MenuKey;

        // Palette texture on unit 1: Vid.d_8to24table's 256 RGBA entries as a
        // 256x1 row, re-uploaded every Present (1 KB) so SetPalette/
        // ShiftPalette changes (palette flashes) show up without any
        // dirty-flag plumbing into Quake.Engine.
        GL.ActiveTexture(TextureUnit.Texture1);
        paletteTexture = CreateNearestTexture();
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            256, 1, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

        // Index texture on unit 0 (left active, so AllocTexture's bind on a
        // resize targets it). Integer textures must use nearest filtering.
        GL.ActiveTexture(TextureUnit.Texture0);
        indexTexture = CreateNearestTexture();

        // vid.buffer rows are 1 byte per pixel, so a width not divisible by
        // 4 would otherwise be read with the default 4-byte row padding.
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        InitQuad();

        // VidWindow is the only GL user in this port, so the program, VAO and
        // textures bound here stay bound for the lifetime of the context --
        // Present() only has to upload and draw, not rebind every frame.
        GL.UseProgram(shaderProgram);
        GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uIndex"), 0);
        GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uPalette"), 1);
        GL.BindVertexArray(vao);

        AllocTexture(width, height);
    }

    // Not in the original -- creates and binds (on the active unit) a
    // nearest-filtered, edge-clamped 2D texture.
    private static int CreateNearestTexture()
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return tex;
    }

    /*
    ========================
    AllocTexture

    Not in the original (no GPU texture exists in vid_win.c) -- allocates the
    GPU-side storage for the R8UI index texture once per resolution
    (TexImage2D with a null pointer) and sets the matching viewport, so
    Present() can stream each frame into it with TexSubImage2D instead of
    reallocating the texture's storage on every frame. Called from Init()
    and again from SetMode() after a resolution change.
    ========================
    */
    private static void AllocTexture(int width, int height)
    {
        GL.BindTexture(TextureTarget.Texture2D, indexTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8ui,
            width, height, 0,
            PixelFormat.RedInteger, PixelType.UnsignedByte, IntPtr.Zero);

        GL.Viewport(0, 0, Window.ClientSize.X, Window.ClientSize.Y);
    }

    /*
    ========================
    VID_AllocBuffers

    OS-agnostic subset: computes and allocates the z-buffer + surface-cache
    block for a given resolution (vid_win.c:260-296's Hunk_HighAllocName,
    here a plain NativeMemory block instead) and the indexed framebuffer
    it sits alongside, freeing whatever this was previously sized for first
    (the original's Hunk_FreeToHighMark before reallocating). Factored out
    of Init() so SetMode below can call it again for a mode change, mirroring
    VID_SetMode's own call to VID_AllocBuffers after picking window geometry.
    ========================
    */
    private static void AllocBuffers(int width, int height)
    {
        if (Vid.vid.Buffer != null)
        {
            NativeMemory.Free(Vid.vid.Buffer);
        }

        if (videoBuffer != null)
        {
            // D_FlushCaches (vid_win.c:283, inside VID_AllocBuffers's own
            // "if (d_pzbuffer)" guard): every msurface_t.CacheSpots[] still
            // points into the surface-cache block about to be freed below --
            // without nulling those backpointers first, the next
            // D_CacheSurface call dereferences freed memory (use-after-free)
            // the first time a level is loaded before a resize.
            DSurf.FlushCaches();
            NativeMemory.AlignedFree(videoBuffer);
        }

        // vid.buffer/rowbytes/width/height -- VID_AllocBuffers's OS-agnostic
        // equivalent: a plain NativeMemory block instead of a DIB section.
        byte* buffer = NativeMemory.AllocArrayZeroed<byte>(checked(width * height));
        Vid.vid.Buffer = buffer;
        Vid.vid.ConBuffer = buffer;
        Vid.vid.Direct = buffer;
        Vid.vid.RowBytes = (uint)width;
        Vid.vid.ConRowBytes = (uint)width;
        Vid.vid.Width = (uint)width;
        Vid.vid.ConWidth = (uint)width;
        Vid.vid.Height = (uint)height;
        Vid.vid.ConHeight = (uint)height;

        // vid.aspect -- deliberately NOT vid_win.c's (height/width)*(320/240)
        // (Rule 8): that formula assumes every mode is shown on a physically
        // 4:3 CRT, which was right for 320x200 (pixelAspect 0.8333, see
        // RMain.ViewChanged's own comment) but squashes the image vertically
        // on any modern widescreen display (e.g. 0.75 at 1920x1080). What
        // R_ViewChanged actually wants is the displayed pixel aspect, i.e.
        // on-screen pixel width / on-screen pixel height: 1.0 whenever the
        // buffer is shown 1:1 (every windowed mode, and fullscreen up to
        // RShared.MAXWIDTH/MAXHEIGHT), and only different when a larger
        // desktop forces the clamped buffer to be stretched to fill it.
        // The matching Hor+ field-of-view widening lives in
        // Screen.CalcRefdef/CalcHorPlusFov.
        Vector2i client = Window.ClientSize;
        Vid.vid.Aspect = client.X > 0 && client.Y > 0
            ? (client.X * (float)height) / (client.Y * (float)width)
            : 1.0f;

        // VID_AllocBuffers/D_InitCaches (vid_win.c's VID_AllocBuffers + the
        // VID_Init call site right after it): the z-buffer (d_pzbuffer) and
        // the software rasterizer's surface cache share one allocation in
        // the original (Hunk_HighAllocName(tbuffersize, "video")), with the
        // surface cache starting right after the z-buffer -- reproduced here
        // with a plain NativeMemory block instead of the Hunk. Without this,
        // D_SCAlloc's sc_base/sc_size are left at their zero defaults and
        // every surface cache allocation (i.e. every textured wall/floor)
        // fails immediately once real level geometry is rendered.
        int pzBufferSize = width * height * sizeof(short);
        int surfCacheSize = DSurf.SurfaceCacheForRes(width, height);
        videoBuffer = NativeMemory.AllocSimdAligned(pzBufferSize + surfCacheSize);
        DLocal.d_pzbuffer = (short*)videoBuffer;
        DSurf.InitCaches(videoBuffer + pzBufferSize, surfCacheSize);
    }

    /*
    ================
    VID_SetMode

    OS-agnostic subset (vid_win.c:1564-1715): switches the existing window
    between windowed (VID_SetWindowedMode) and borderless fullscreen
    (VID_SetFullDIBMode's role, see SetWindowGeometry) via
    SetWindowGeometry, reallocates every buffer sized for the old resolution
    (VID_AllocBuffers above), and sets vid.recalc_refdef (vid_win.c:1712) so
    SCR_CalcRefdef picks up the new dimensions on the next frame.
    width/height are the windowed size (vid_width/vid_height); fullscreen
    ignores them and uses the desktop resolution. Skips: the modenum/modelist
    indirection (this port keys mode changes directly off fullscreen/width/
    height, see vid_width/vid_height's own comment), the palette reapply
    (VID_SetPalette is only needed after a DIB/DirectDraw surface
    recreation, which doesn't happen here), and vid_testingmode's
    revert-after-5-seconds safety net.
    ================
    */
    public static void SetMode(bool fullscreen, int width, int height)
    {
        // Not in the original (vid_win.c's own MAXWIDTH/MAXHEIGHT guarded
        // EnumDisplaySettings against reporting something huge): defensive
        // insurance against vid_width/vid_height being pushed past the
        // renderer's ceiling directly via cvar/command line (e.g.
        // "+set vid_width 99999") now that RShared.MAXWIDTH/MAXHEIGHT is
        // high enough to be reachable -- exceeding it corrupts memory (the
        // scan-conversion tables/stackallocs sized off those constants),
        // same failure mode the original's own constants guarded against.
        // VidMenu's own list is already capped at the native resolution, so
        // this should never trigger from the menu itself.
        if (width > RShared.MAXWIDTH || height > RShared.MAXHEIGHT)
        {
            Con.Printf($"vid_width/vid_height clamped to {RShared.MAXWIDTH}x{RShared.MAXHEIGHT}\n");
            width = Math.Min(width, RShared.MAXWIDTH);
            height = Math.Min(height, RShared.MAXHEIGHT);
        }

        // Same insurance below DisplayModeList.MinWidth/MinHeight (e.g.
        // "+set vid_width 320" from the command line) -- VidMenu's list
        // never offers anything smaller, but a directly-set cvar could.
        if (width < DisplayModeList.MinWidth || height < DisplayModeList.MinHeight)
        {
            Con.Printf($"vid_width/vid_height clamped to {DisplayModeList.MinWidth}x{DisplayModeList.MinHeight}\n");
            width = Math.Max(width, DisplayModeList.MinWidth);
            height = Math.Max(height, DisplayModeList.MinHeight);
        }

        // The windowed size is remembered even while fullscreen, so
        // vid_windowed/the menu toggle return to it.
        Cvar.SetValue(vid_width->Name, width);
        Cvar.SetValue(vid_height->Name, height);
        Cvar.SetValue(_vid_fullscreen->Name, fullscreen ? 1 : 0);

        // "if (!force_mode_set && (modenum == vid_modenum)) return true;"
        // (vid_win.c:1593) -- fullscreen's size is always the desktop's, so
        // only the windowed case compares dimensions.
        if (fullscreen == IsFullscreen &&
            (fullscreen || (width == (int)Vid.vid.Width && height == (int)Vid.vid.Height)))
        {
            return;
        }

        Vector2i bufferSize = SetWindowGeometry(fullscreen, width, height);
        IsFullscreen = fullscreen;

        AllocBuffers(bufferSize.X, bufferSize.Y);
        AllocTexture(bufferSize.X, bufferSize.Y);

        Vid.vid.RecalcRefdef = 1;

        // "Con_SafePrintf ("%s\n", VID_GetModeDescription (vid_modenum));"
        // (vid_win.c:1707)
        Con.Printf($"{new DisplayMode(bufferSize.X, bufferSize.Y)} {(fullscreen ? "fullscreen"u8 : "windowed"u8)}\n");
    }

    /*
    ================
    SetWindowGeometry

    The window half of VID_SetWindowedMode/VID_SetFullDIBMode, returning
    the render buffer size for the new mode.

    Windowed: VID_SetWindowedMode's fixed-border style and client size
    (vid_win.c:1267-1313), centred on the window's monitor like gl_vidnt.c's
    CenterWindow (its "(CenterX < 0) ? 0" clamp included) rather than
    vid_win.c's remembered vid_window_x/vid_window_y.

    Fullscreen: borderless window covering the whole monitor at its current resolution.
    Resolutions above 4K are clamped.
    ================
    */
    private static Vector2i SetWindowGeometry(bool fullscreen, int width, int height)
    {
        MonitorInfo monitor = Monitors.GetMonitorFromWindow(Window);

        Window.WindowState = WindowState.Normal;

        if (fullscreen)
        {
            Box2i area = monitor.ClientArea;

            Window.WindowBorder = WindowBorder.Hidden;
            Window.ClientLocation = area.Min;
            Window.ClientSize = area.Size;

            int bufferWidth = area.Size.X;
            int bufferHeight = area.Size.Y;

            if (bufferWidth > RShared.MAXWIDTH || bufferHeight > RShared.MAXHEIGHT)
            {
                bufferWidth = Math.Min(bufferWidth, RShared.MAXWIDTH);
                bufferHeight = Math.Min(bufferHeight, RShared.MAXHEIGHT);
                Con.Printf($"desktop resolution {area.Size.X}x{area.Size.Y} rendered at {bufferWidth}x{bufferHeight}\n");
            }

            return new Vector2i(bufferWidth, bufferHeight);
        }

        Window.WindowBorder = WindowBorder.Fixed;
        Window.ClientSize = new Vector2i(width, height);

        // CenterWindow (gl_vidnt.c) -- positions the outer frame (Size, not
        // ClientSize) inside the monitor's work area so the title bar stays
        // on screen.
        Box2i work = monitor.WorkArea;
        Vector2i offset = (work.Size - Window.Size) / 2;
        Window.Location = work.Min + new Vector2i(Math.Max(offset.X, 0), Math.Max(offset.Y, 0));

        return new Vector2i(width, height);
    }

    /*
    ================
    CheckModeChange

    Applies _vid_fullscreen/vid_width/vid_height when they no
    longer match the current mode -- which is how the values archived in
    config.cfg take effect, since quake.rc execs it after VID_Init has
    already created the window, and how "+_vid_fullscreen 0" on the command
    line (stuffcmds runs last) or typing a cvar in the console work. Called
    from Program.cs's main loop, outside rendering, rather than from
    Present (the original's VID_Update), so buffers are never reallocated
    in the middle of an UpdateScreen call.
    ================
    */
    public static void CheckModeChange()
    {
        bool fullscreen = _vid_fullscreen->Value != 0;
        int width = (int)vid_width->Value;
        int height = (int)vid_height->Value;

        if (fullscreen != IsFullscreen ||
            (!fullscreen && (width != (int)Vid.vid.Width || height != (int)Vid.vid.Height)))
        {
            SetMode(fullscreen, width, height);
        }
    }

    /*
    =================
    VID_NumModes_f
    =================
    */
    private static void NumModes_f()
    {
        int nummodes = DisplayModeSource.GetModeList().Count;

        if (nummodes == 1)
        {
            Con.Printf($"{nummodes} video mode is available\n");
        }
        else
        {
            Con.Printf($"{nummodes} video modes are available\n");
        }
    }

    // VID_GetExtModeDescription -- the fullscreen entry is the desktop
    // resolution rather than a list index, so it has its own description.
    private static DisplayMode DesktopMode()
    {
        MonitorInfo monitor = Monitors.GetMonitorFromWindow(Window);
        return new DisplayMode(monitor.ClientArea.Size.X, monitor.ClientArea.Size.Y);
    }

    /*
    =================
    VID_DescribeCurrentMode_f
    =================
    */
    private static void DescribeCurrentMode_f()
    {
        Con.Printf($"{new DisplayMode((int)Vid.vid.Width, (int)Vid.vid.Height)} {(IsFullscreen ? "fullscreen"u8 : "windowed"u8)}\n");
    }

    /*
    =================
    VID_DescribeMode_f
    =================
    */
    private static void DescribeMode_f()
    {
        List<DisplayMode> modes = DisplayModeSource.GetModeList();
        int modenum = Q.Atoi(Cmd.Argv(1));

        if (modenum < 0 || modenum >= modes.Count)
        {
            Con.Printf("Bad mode\n"u8);
        }
        else
        {
            Con.Printf($"{modes[modenum]} windowed\n");
        }
    }

    /*
    =================
    VID_DescribeModes_f
    =================
    */
    private static void DescribeModes_f()
    {
        List<DisplayMode> modes = DisplayModeSource.GetModeList();

        for (int i = 0; i < modes.Count; i++)
        {
            Con.Printf($"{i,2}: {modes[i]} windowed\n");
        }

        Con.Printf($"{DesktopMode()} fullscreen (desktop)\n");
    }

    /*
    =================
    VID_Windowed_f
    =================
    */
    private static void Windowed_f()
    {
        SetMode(false, (int)vid_width->Value, (int)vid_height->Value);
    }

    /*
    =================
    VID_Fullscreen_f
    =================
    */
    private static void Fullscreen_f()
    {
        SetMode(true, (int)vid_width->Value, (int)vid_height->Value);
    }

    /*
    =================
    VID_Minimize_f
    =================
    */
    private static void Minimize_f()
    {
        // we only support minimizing windows; if you're fullscreen,
        // switch to windowed first
        if (!IsFullscreen)
        {
            Window.WindowState = WindowState.Minimized;
        }
    }

    // Not in the original (vid_win.c blits straight into GDI/MGL surfaces,
    // no GPU pipeline exists to set up)
    private static int vao, vbo, shaderProgram;

    private const string VertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aUV;
        out vec2 vUV;
        void main()
        {
            vUV = aUV;
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    // The palette lookup VID_SetPalette's hardware palette used to do:
    // fetch the 8-bit index for this pixel (texelFetch keeps exact
    // nearest-pixel sampling, including when a clamped buffer is stretched
    // over a larger desktop), then fetch that palette entry.
    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 vUV;
        out vec4 FragColor;
        uniform usampler2D uIndex;
        uniform sampler2D uPalette;
        void main()
        {
            ivec2 size = textureSize(uIndex, 0);
            ivec2 p = min(ivec2(vUV * vec2(size)), size - 1);
            uint idx = texelFetch(uIndex, p, 0).r;
            FragColor = texelFetch(uPalette, ivec2(int(idx), 0), 0);
        }
        """;

    private static void InitQuad()
    {
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, VertexShaderSource);
        GL.CompileShader(vertexShader);
        CheckShaderCompile(vertexShader);

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, FragmentShaderSource);
        GL.CompileShader(fragmentShader);
        CheckShaderCompile(fragmentShader);

        shaderProgram = GL.CreateProgram();
        GL.AttachShader(shaderProgram, vertexShader);
        GL.AttachShader(shaderProgram, fragmentShader);
        GL.LinkProgram(shaderProgram);
        GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
        {
            throw new InvalidOperationException(GL.GetProgramInfoLog(shaderProgram));
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        // Triangle strip, position (x,y) + UV (u,v) interleaved. Top of
        // vid.vid.Buffer (row 0) is the top of the screen; texcoord v=0
        // samples the texture's first uploaded row (TexImage2D's own row
        // order), so the top screen edge maps to v=0 -- no vertical flip.
        float[] vertices =
        [
            -1f, 1f, 0f, 0f,
            1f, 1f, 1f, 0f,
            -1f, -1f, 0f, 1f,
            1f, -1f, 1f, 1f,
        ];

        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
    }

    private static void CheckShaderCompile(int shader)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
        }
    }

    /*
    ===============
    FlipScreen

    Uploads Vid.vid.Buffer's indexed bytes as-is into the R8UI index texture
    and Vid.d_8to24table into the 256x1 palette texture, then draws a single
    fullscreen quad whose fragment shader does the palette lookup on the GPU
    (the role the hardware palette played for the original's 8-bit DIB/MGL
    surface). Wired to Screen.VID_Update (see that field's own comment), so
    it runs at the end of every Screen.UpdateScreen call -- not just
    Host_Frame's normal per-frame one -- mirroring the original's synchronous
    VID_Update/FlipScreen call inside SCR_UpdateScreen itself.
    ===============
    */
    public static void Present()
    {
        // Storage was allocated by Init()/AllocTexture; the textures,
        // program, VAO and viewport are still bound from Init()/SetMode().
        // Unit 0 is left active afterwards so AllocTexture's bind targets it.
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 256, 1,
            PixelFormat.Rgba, PixelType.UnsignedByte, (nint)Vid.d_8to24table);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
            (int)Vid.vid.Width, (int)Vid.vid.Height,
            PixelFormat.RedInteger, PixelType.UnsignedByte, (nint)Vid.vid.Buffer);

        // No glClear: the quad covers the whole viewport, so every pixel is
        // overwritten.
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

        Window.Context.SwapBuffers();
    }

    /*
    ============
    VID_Shutdown
    ============
    */
    public static void Shutdown()
    {
        if (indexTexture != 0) { GL.DeleteTexture(indexTexture); indexTexture = 0; }
        if (paletteTexture != 0) { GL.DeleteTexture(paletteTexture); paletteTexture = 0; }

        if (vbo != 0) { GL.DeleteBuffer(vbo); vbo = 0; }
        if (vao != 0) { GL.DeleteVertexArray(vao); vao = 0; }
        if (shaderProgram != 0) { GL.DeleteProgram(shaderProgram); shaderProgram = 0; }

        if (Vid.vid.Buffer != null)
        {
            NativeMemory.Free(Vid.vid.Buffer);
            Vid.vid.Buffer = null;
            Vid.vid.ConBuffer = null;
            Vid.vid.Direct = null;
        }

        if (videoBuffer != null)
        {
            NativeMemory.AlignedFree(videoBuffer);
            videoBuffer = null;
            DLocal.d_pzbuffer = null;
            DSurf.sc_base = null;
        }

        Window.Dispose();
    }
}
