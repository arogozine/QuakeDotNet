// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

/// <summary>
/// sys_win.c -- Win32 system interface code.
/// WinMain's dialog/message-pump shell is out of scope (Quake.Desktop's
/// Program.cs is the real entry point -- see WinMain's own comment); the
/// CPU-throttle behavior from its main loop (SleepUntilInput, the
/// ActiveApp/Minimized-driven sleep branches) is ported for real, with
/// Program.cs polling Sys.ActiveApp/Sys.Minimized off its OpenTK window each
/// frame instead of Win32 WM_ACTIVATE/WM_SIZE. Sys_SendKeyEvents is ported
/// as a swappable hook (see its own comment) that Quake.Desktop points at
/// its OpenTK window-event pump. Sys_ConsoleInput is ported too, against
/// System.Console's OS-agnostic key API instead of Win32's
/// ReadConsoleInput (CLAUDE.md rule 5).
/// </summary>
public static unsafe class Sys
{
    // Sys_Error
    // Sys_Error for an interpolated message ($"... {byte*} ...") -- the text is
    // materialised as a managed string only here, on the (fatal) throw path.
    [DoesNotReturn]
    public static void Error([InterpolatedStringHandlerArgument] scoped Utf8StringHandler error)
    {
        throw new QuakeSysErrorException(Encoding.Latin1.GetString(error.GetWrittenBuffer()));
    }

    [DoesNotReturn]
    public static void Error(scoped ReadOnlySpan<byte> error)
    {
        throw new QuakeSysErrorException(Encoding.Latin1.GetString(error));
    }

    private const int MAX_HANDLES = 10;
    internal static readonly FileStream?[] sys_handles = new FileStream[MAX_HANDLES];

    // findhandle
    private static int Findhandle()
    {
        for (int i = 1; i < MAX_HANDLES; i++)
        {
            if (sys_handles[i] == null)
            {
                return i;
            }
        }

        Error("out of handles"u8);
        return -1;
    }


    // filelength
    public static int FileLength(FileStream f)
    {
        return (int)f.Length;
    }

    // Sys_FileOpenRead
    public static int FileOpenRead(byte* path, int* hndl)
    {
        int i = Findhandle();

        FileStream? f;
        try
        {
            f = new FileStream(CStr.ToString(path), FileMode.Open, FileAccess.Read);
        }
        catch (IOException)
        {
            f = null;
        }

        int retval;
        if (f == null)
        {
            *hndl = -1;
            retval = -1;
        }
        else
        {
            sys_handles[i] = f;
            *hndl = i;
            retval = FileLength(f);
        }

        return retval;
    }

    // Sys_FileOpenWrite
    public static int FileOpenWrite(byte* path)
    {
        int i = Findhandle();

        FileStream f;
        try
        {
            f = new FileStream(CStr.ToString(path), FileMode.Create, FileAccess.Write);
        }
        catch (IOException e)
        {
            Error($"Error opening {path}: {e.Message}");
            return -1;
        }

        sys_handles[i] = f;

        return i;
    }

    // Sys_FileClose
    public static void FileClose(int handle)
    {
        sys_handles[handle]!.Dispose();
        sys_handles[handle] = null;
    }

    // Sys_FileSeek
    public static void FileSeek(int handle, int position)
    {
        sys_handles[handle]!.Seek(position, SeekOrigin.Begin);
    }

    // Sys_FileRead
    public static int FileRead(int handle, void* dest, int count)
    {
        // fread semantics: FileStream.Read may return fewer bytes than asked
        // for before EOF, so keep reading until count is satisfied or EOF.
        Span<byte> span = new(dest, count);
        FileStream f = sys_handles[handle]!;
        int total = 0;
        while (total < count)
        {
            int n = f.Read(span.Slice(total));
            if (n <= 0)
            {
                break;
            }

            total += n;
        }
        return total;
    }

    // Sys_FileWrite
    public static int FileWrite(int handle, void* data, int count)
    {
        ReadOnlySpan<byte> span = new(data, count);
        sys_handles[handle]!.Write(span);
        return count;
    }

    public static int FileWrite(int handle, scoped ReadOnlySpan<byte> span)
    {
        sys_handles[handle]!.Write(span);
        return span.Length;
    }

    // Sys_FileTime
    public static int FileTime(byte* path)
    {
        int retval;
        try
        {
            using FileStream f = new(CStr.ToString(path), FileMode.Open, FileAccess.Read);
            retval = 1;
        }
        catch (IOException)
        {
            retval = -1;
        }

        return retval;
    }

    // Sys_mkdir
    public static void Mkdir(byte* path)
    {
        Directory.CreateDirectory(CStr.ToString(path));
    }

    /*
    ===============================================================================
    SYSTEM IO
    ===============================================================================
    */

    // Sys_MakeCodeWriteable
    // Sys_SetFPCW
    // x87 control word is not exposed to managed code; the original is itself
    // a no-op outside _M_IX86.
    public static void SetFPCW()
    {
    }

    // MaskExceptions
    public static void MaskExceptions()
    {
    }

    // Sys_Init
    // The OS-version-check portion of the original has no managed equivalent
    // and is not ported; timer setup is handled by Sys_FloatTime's Stopwatch.
    public static void Init()
    {
        MaskExceptions();
        SetFPCW();
        InitFloatTime();
    }

    // Sys_Printf
    // The original's separate dedicated-server console-handle output path
    // (a raw WriteFile to a console the -dedicated build allocates) is
    // dropped; CActive.Dedicated (Host.FindMaxClients) still selects
    // dedicated-server behavior, but its console output goes through the
    // same Con.Printf as every other build.
    public static void Printf([InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler u8String)
    {
        Con.Printf(u8String.GetWrittenBuffer());
    }

    // Sys_Printf for a "..."u8 literal (no format arguments).
    public static void Printf(scoped ReadOnlySpan<byte> u8String)
    {
        Con.Printf(u8String);
    }

    // Sys_Quit
    // The original calls VID_ForceUnlockedAndReturnState/Host_Shutdown then
    // exits the process outright. Host.Shutdown() (NET_Shutdown/S_Shutdown/
    // CDAudio_Shutdown) runs here, matching that call; VID_Shutdown/
    // IN_Shutdown are Quake.Desktop's job (VidWindow.Shutdown) rather than
    // Environment.Exit, since that would tear down the OpenTK window/GL
    // context uncleanly. Instead this sets a flag Quake.Desktop's Program.cs
    // main loop polls so it can break out and dispose its window on its own
    // terms once this frame finishes.
    public static bool quitRequested;

    public static void Quit()
    {
        Host.Shutdown();
        quitRequested = true;
    }

    private static readonly Stopwatch stopwatch = Stopwatch.StartNew();

    // Sys_FloatTime
    // Backed by Stopwatch instead of QueryPerformanceCounter; the manual
    // wraparound/"stuck clock" handling in the original isn't needed since
    // Stopwatch's 64-bit tick count doesn't turn over in practice.
    public static double FloatTime()
    {
        return curtime + stopwatch.Elapsed.TotalSeconds;
    }

    private static double curtime;

    // Sys_InitFloatTime
    public static void InitFloatTime()
    {
        FloatTime();

        int j = Com.CheckParm("-starttime"u8);

        curtime = j != 0 ? Q.Atof(Com.com_argv[j + 1]) : 0.0;
    }

    // Sys_ConsoleInput -- the original reads Win32 console INPUT_RECORDs
    // directly (ReadConsoleInput); ported here against System.Console's
    // OS-agnostic key API instead (CLAUDE.md rule 5), which behaves the same
    // way on a real terminal on any OS. `text` is a persistent 256-byte
    // buffer (matching the original's `static char text[256]`) since the
    // returned pointer is read by the caller after this call returns.
    // sc_return_on_enter's branch is omitted: it's only ever set by
    // Sys_Error's dedicated-server fallback loop, which this port's
    // Sys.Error (throws instead of looping) never reaches.
    internal static readonly byte* consoleInputText = NativeMemory.AllocArrayZeroed<byte>(256);
    private static int consoleInputLen;

    public static byte* ConsoleInput()
    {
        if (Cls.state != CActive.Dedicated)
        {
            return null;
        }

        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            char ch = key.KeyChar;

            switch (ch)
            {
                case '\r':
                    Console.Write("\r\n");
                    if (consoleInputLen != 0)
                    {
                        consoleInputText[consoleInputLen] = 0;
                        consoleInputLen = 0;
                        return consoleInputText;
                    }
                    break;

                case '\b':
                    Console.Write("\b \b");
                    if (consoleInputLen != 0)
                    {
                        consoleInputLen--;
                    }

                    break;

                default:
                    if (ch >= ' ')
                    {
                        Console.Write(ch);
                        consoleInputText[consoleInputLen] = (byte)ch;
                        consoleInputLen = (consoleInputLen + 1) & 0xff;
                    }
                    break;
            }
        }

        return null;
    }

    // Sys_Sleep
    public static void Sleep()
    {
        Thread.Sleep(1);
    }

    // Sys_SendKeyEvents -- the original pumps the Win32 message queue
    // directly (PeekMessage/GetMessage/TranslateMessage/DispatchMessage),
    // firing Win32's own WndProc key callbacks. This port has no message
    // queue of its own to pump: the display backend (Quake.Desktop's
    // VidWindow/InputHost, wired up via OpenTK) owns that instead, so this
    // is a swappable hook -- like Host.Init's videoInit callback -- that the
    // active display backend points at its own window-event pump. A caller
    // with no display backend (HostTests.cs, a future dedicated-server
    // entry point) leaves it unset, matching the original's behavior of
    // having nothing to pump when there's no window.
    public static Action? SendKeyEventsHook;

    public static void SendKeyEvents()
    {
        if (SendKeyEventsHook == null)
        {
            throw new NotImplementedException("Sys_SendKeyEvents has no display backend wired up (Sys.SendKeyEventsHook is unset).");
        }

        SendKeyEventsHook();
    }

    /*
    ==============================================================================
     WINDOWS CRAP
    ==============================================================================
    */

    // PAUSE_SLEEP -- sleep time (ms) on pause or minimization
    public const int PAUSE_SLEEP = 50;
    // NOT_FOCUS_SLEEP -- sleep time (ms) when not focus
    public const int NOT_FOCUS_SLEEP = 20;

    // SleepUntilInput -- the original waits on a Win32 event handle
    // (MsgWaitForMultipleObjects) that Sys_SendKeyEvents/the window proc
    // signal on the next queued input message, so it can wake early. There
    // is no equivalent wait-for-next-message primitive over an OpenTK/GLFW
    // window, so this just sleeps the full duration (Rule 8); Program.cs's
    // main loop still pumps window events and clears Screen.scr_skipupdate
    // itself once the throttle condition below no longer holds, so input
    // is never delayed by more than one PAUSE_SLEEP/NOT_FOCUS_SLEEP tick.
    public static void SleepUntilInput(int time)
    {
        Thread.Sleep(time);
    }

    // ActiveApp/Minimized -- the original's WM_ACTIVATEAPP/WM_SIZE-updated
    // globals, read by WinMain's loop (below) to decide when to throttle.
    // Ported as plain flags Quake.Desktop's Program.cs polls off its OpenTK
    // window each frame (Window.IsFocused / WindowState.Minimized) instead
    // of a Win32 WndProc setting them (Rule 5); no field for the original's
    // DDActive (DirectDraw was never active to begin with in this port, see
    // VidWindow.cs) or WinMain's own hwnd/dialog/QHOST handles below, which
    // WinMain's own comment covers.
    public static bool ActiveApp, Minimized;

    // WinMain -- fully superseded by Quake.Desktop's Program.Main, which
    // ports the meaningful parts of this function's body directly (argv
    // parsing via Com.InitArgv, the -heapsize override, Sys_Init/Host_Init,
    // and the while(1) { ...; Host_Frame(time); } loop, now throttled via
    // ActiveApp/Minimized/SleepUntilInput above exactly as this loop was).
    // What's left out is Windows-GUI-only and has no .NET equivalent worth
    // building: the startup dialog (IDD_DIALOG1/CreateDialog), GlobalMemoryStatus's
    // memsize heuristic (MemoryManager.AllocateHostMemory picks its own),
    // AllocConsole/QHOST's -HFILE/-HPARENT/-HCHILD child-process handshake,
    // and the OS-version check Sys_Init's own comment already excludes.
    // Kept only for name/signature parity (CLAUDE.md rule 3); never called.
    public static int WinMain(nint hInstance, nint hPrevInstance, string lpCmdLine, int nCmdShow)
    {
        throw new NotImplementedException(
            "WinMain is superseded by Quake.Desktop.Program.Main; see this method's own comment.");
    }
}

public sealed class QuakeSysErrorException(string message) : Exception(message);
