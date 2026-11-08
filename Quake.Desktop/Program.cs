// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.Desktop;

/// <summary>
/// sys_win.c's WinMain -- the engine's entry point and per-frame main loop
/// driver. Everything Win32-message-pump-shaped in the original (the
/// startup dialog, dedicated-server console allocation, QHOST -HFILE/
/// -HPARENT/-HCHILD hookup) is out of scope; what's ported is the parts
/// that drive the engine itself: COM_InitArgv, the heap-size computation,
/// Sys_Init/Host_Init, and the `while(1) { time = ...; Host_Frame(time); }`
/// loop body, now polling an OpenTK window instead of a Win32 one -- including
/// the loop's ActiveApp/Minimized-driven sleep throttle (Sys.SleepUntilInput's
/// own comment).
/// </summary>
public static unsafe class Program
{
    // Not in the original: sys_win.c's Sys_Error reports fatal errors via a
    // MessageBox. This port's Sys.Error throws QuakeSysErrorException instead,
    // so it (and any other unhandled exception) is caught here and written to
    // the console and an error log next to the executable.
    private const string ErrorLogName = "error.log";

    public static int Main(string[] args)
    {
        // Exceptions on threads other than this one (e.g. audio callbacks)
        // can't be caught by the try/catch below; log them before the
        // runtime terminates the process.
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
            ReportException(e.ExceptionObject as Exception, "Unhandled exception");

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            ReportException(ex, ex is QuakeSysErrorException ? "Sys_Error" : "Unhandled exception");

            // Best-effort teardown (Sys_Error's Host_Shutdown call); the
            // window may never have been created, or may be what failed.
            try
            {
                Host.Shutdown();
            }
            catch (Exception shutdownEx)
            {
                ReportException(shutdownEx, "Exception during host shutdown");
            }

            try
            {
                if (VidWindow.Window != null)
                    VidWindow.Shutdown();
            }
            catch (Exception shutdownEx)
            {
                ReportException(shutdownEx, "Exception during shutdown");
            }

            return 1;
        }
    }

    private static readonly Lock errorLogLock = new();

    private static void ReportException(Exception? ex, string kind)
    {
        string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {ex?.ToString() ?? "(unknown exception)"}{Environment.NewLine}";

        lock (errorLogLock)
        {
            try
            {
                Console.Error.Write(text);
            }
            catch
            {
                // Console may be unavailable (no attached console); the log file is still written.
            }

            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, ErrorLogName), text);
            }
            catch (Exception logEx)
            {
                try { Console.Error.WriteLine($"Failed to write {ErrorLogName}: {logEx.Message}"); } catch { }
            }
        }
    }

    private static int Run(string[] args)
    {
        // COM_InitArgv -- argv[0] is the program name in C's calling
        // convention (COM_CheckParm's 1-based scan skips it); .NET's `args`
        // doesn't include it, so a placeholder is prepended to keep that
        // indexing faithful to the original.
        byte*[] argv = new byte*[args.Length + 1];
        argv[0] = new Utf8String("quake"u8);
        for (int i = 0; i < args.Length; i++)
        {
            argv[i + 1] = (byte*)NativeMemory.Alloc((nuint)args[i].Length + 1);
            CStr.Write(argv[i + 1], args[i]);
        }

        Com.InitArgv(argv.Length, argv);

        // -heapsize (WinMain's own override of the computed heap size,
        // ported verbatim: CheckParm's 1-based result plus 1 must still be
        // < com_argc for the value to be read).
        int? heapSizeKb = null;
        int heapsizeParm = Com.CheckParm("-heapsize"u8);
        if (heapsizeParm != 0)
        {
            int t = heapsizeParm + 1;
            if (t < Com.com_argc)
                heapSizeKb = Q.Atoi(Com.com_argv[t]);
        }

        MemoryManager.AllocateHostMemory(heapSizeKb);

        Sys.Init();

        // Host_Init(&parms) -- VID_Init/IN_Init's OS-agnostic subset (window
        // creation, framebuffer allocation, palette table) is spliced in at
        // the original's exact call site via this callback; see Host.Init's
        // own comment.
        Host.Init(MemoryManager.MemBase, MemoryManager.MemSize, VidWindow.Init);

        // Sys_SendKeyEvents's swappable hook (Sys.cs) -- points a fatal-error
        // Y/N modal (Screen.ModalMessage) at the same window-event pump the
        // main loop below uses, since ModalMessage blocks on it directly
        // rather than going through the loop.
        Sys.SendKeyEventsHook = () => NativeWindow.ProcessWindowEvents(false);


        // WinMain: "because sound is off until we become active"
        SndDma.BlockSound();
        bool sound_active = false; // vid_win.c sound_active

        double oldtime = Sys.FloatTime();

        while (!Sys.quitRequested && VidWindow.Window.Exists)
        {
            // Sys_SendKeyEvents -- pumps this process's window(s), firing
            // the KeyDown/KeyUp/MouseMove callbacks InputHost subscribed to
            // in VidWindow.Init.
            NativeWindow.ProcessWindowEvents(false);
            if (!VidWindow.Window.Exists)
                break;

            // VID_Update's vid_mode poll -- see CheckModeChange's own comment.
            VidWindow.CheckModeChange();

            // IN_Commands -- flush this frame's queued mouse delta into
            // ClInput's accumulator before Host.Frame reads it via
            // ClMain.SendCmd -> ClInput.Move.
            InputHost.Commands();
            InputHost.UpdateCursorState();

            // ActiveApp/Minimized -- WinMain's own WM_ACTIVATEAPP/WM_SIZE
            // globals, polled here off the OpenTK window instead (Sys.cs's
            // own comment on these fields).
            Sys.ActiveApp = VidWindow.Window.IsFocused;
            Sys.Minimized = VidWindow.Window.WindowState == WindowState.Minimized;

            // vid_win.c AppActivate: enable/disable sound on focus gain/loss
            if (!Sys.ActiveApp && sound_active)
            {
                SndDma.BlockSound();
                SndDma.ClearBuffer();
                sound_active = false;
            }
            else if (Sys.ActiveApp && !sound_active)
            {
                SndDma.UnblockSound();
                SndDma.ClearBuffer();
                sound_active = true;
            }

            // WinMain's main-loop throttle: yield the CPU when paused,
            // minimized, or not the focus (DDActive is never true in this
            // port -- see Sys.ActiveApp's own comment -- so it's dropped
            // from the condition below).
            if ((Cl.paused && !Sys.ActiveApp) || Sys.Minimized || Screen.block_drawing)
            {
                Sys.SleepUntilInput(Sys.PAUSE_SLEEP);
                Screen.scr_skipupdate = true; // no point in bothering to draw
            }
            else if (!Sys.ActiveApp)
            {
                Sys.SleepUntilInput(Sys.NOT_FOCUS_SLEEP);
                Screen.scr_skipupdate = false;
            }
            else
            {
                // Not in the original (see SleepUntilInput's own comment):
                // this port has no PeekMessage-driven "any event clears
                // scr_skipupdate" reset, so it's cleared explicitly once the
                // throttle condition above no longer holds, guaranteeing
                // rendering resumes instead of staying stuck skipped.
                Screen.scr_skipupdate = false;
            }

            double newtime = Sys.FloatTime();
            Host.Frame((float)(newtime - oldtime));
            oldtime = newtime;

            // Screen.UpdateScreen (called inside Host.Frame) already
            // presented the finished frame itself via Screen.VID_Update ->
            // VidWindow.Present (see VidWindow.Init's wiring and Present's
            // own comment) -- no separate flip needed here.
        }

        // Window closed via the X button (Sys.Quit already ran Host.Shutdown
        // for the `quit` command; Host.Shutdown is idempotent via isdown).
        Host.Shutdown();

        VidWindow.Shutdown();

        return 0;
    }
}
