// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

/// <summary>
/// console.c -- the non-drawing half of the Quake console: the scrollback
/// buffer, logging, and Con_Printf/DPrintf/SafePrintf.
/// </summary>
public static unsafe class Con
{
    private const int CON_TEXTSIZE = 16384;
    internal const int NUM_CON_TIMES = 4;

    // con_linewidth
    // internal (not private): con_text below is Hunk-allocated, so it and
    // every field describing it dangle once a test's arena is freed --
    // tests must be able to reset all of them before calling Init() again.
    internal static int con_linewidth;

    // con_forcedup -- because no entities to refresh
    public static bool con_forcedup;

    // con_totallines -- total lines in console scrollback
    internal static int con_totallines;

    // con_backscroll -- lines up from bottom to display
    // internal (not private): Keys.cs's ported Key_Console (PGUP/PGDN/HOME/
    // END scrollback) needs to adjust this directly, same as keys.c's own
    // console.c-external global.
    internal static int con_backscroll;

    // con_current -- where next message will be printed
    internal static int con_current;

    // con_x -- offset in current line for next print
    internal static int con_x;

    // con_text -- Hunk-allocated; dangles once the owning test's arena frees
    internal static byte* con_text;

    // con_notifytime (cvar_t) -- seconds
    private static readonly Cvar_t* con_notifytime = NativeMemory.AllocZeroed<Cvar_t>();

    // con_times -- realtime the line was generated, for transparent notify lines
    internal static readonly float* con_times = NativeMemory.AllocArrayZeroed<float>(NUM_CON_TIMES);

    // con_debuglog
    internal static bool con_debuglog;

    // con_initialized
    internal static bool con_initialized;

    // con_notifylines -- number of notify lines screen.c's
    // SCR_SetUpToDrawConsole clears each frame while the notify area is
    // being cleared out; owned by console.c but not written anywhere in
    // this port until step 2n (screen.c) reads/writes it.
    public static int con_notifylines;

    // con_vislines -- set by Con_DrawConsole, read by Con_DrawInput.
    // internal (not private): ConsoleTests.cs drives Con_DrawInput directly
    // without going through Con_DrawConsole/Draw_ConsoleBackground (which
    // needs a real gfx/conback.lmp), same InternalsVisibleTo pattern as
    // Screen.cs's scr_centertime_off.
    internal static int con_vislines;

    // con_cursorspeed
    private const float con_cursorspeed = 4;


    /*
    ================
    Con_ClearNotify
    ================
    */
    public static void ClearNotify()
    {
        for (int i = 0; i < NUM_CON_TIMES; i++)
        {
            con_times[i] = 0;
        }
    }

    /*
    ================
    Con_CheckResize

    If the line width has changed, reformat the buffer.
    The original computes `width = (vid.width >> 3) - 2` from the global video
    state directly inside this function; the caller (Screen.cs) computes that
    same value from Vid.vid.Width and passes it in instead, keeping this class
    decoupled from a Vid dependency (pass a value < 1 to force the "video not
    initialized" path).
    ================
    */
    public static void CheckResize(int width)
    {
        if (width == con_linewidth)
        {
            return;
        }

        if (width < 1) // video hasn't been initialized yet
        {
            width = 38;
            con_linewidth = width;
            con_totallines = CON_TEXTSIZE / con_linewidth;
            Q.MemSet(con_text, ' ', CON_TEXTSIZE);
        }
        else
        {
            int oldwidth = con_linewidth;
            con_linewidth = width;
            int oldtotallines = con_totallines;
            con_totallines = CON_TEXTSIZE / con_linewidth;
            int numlines = oldtotallines;

            if (con_totallines < numlines)
            {
                numlines = con_totallines;
            }

            int numchars = oldwidth;

            if (con_linewidth < numchars)
            {
                numchars = con_linewidth;
            }

            byte* tbuf = stackalloc byte[CON_TEXTSIZE];
            Q.MemCpy(tbuf, con_text, CON_TEXTSIZE);
            Q.MemSet(con_text, ' ', CON_TEXTSIZE);

            for (int i = 0; i < numlines; i++)
            {
                for (int j = 0; j < numchars; j++)
                {
                    con_text[((con_totallines - 1 - i) * con_linewidth) + j] =
                        tbuf[(((con_current - i + oldtotallines) % oldtotallines) * oldwidth) + j];
                }
            }

            ClearNotify();
        }

        con_backscroll = 0;
        con_current = con_totallines - 1;
    }

    /*
    ================
    Con_Init
    ================
    */
    public static void Init()
    {
        con_debuglog = Com.CheckParm("-condebug"u8) != 0;

        if (con_debuglog)
        {
            try
            {
                File.Delete(CStr.ToString(Com.com_gamedir) + "/qconsole.log");
            }
            catch (IOException)
            {
            }
        }

        con_text = (byte*)Hunk.AllocName(CON_TEXTSIZE, "context"u8);
        Q.MemSet(con_text, ' ', CON_TEXTSIZE);
        con_linewidth = -1;
        CheckResize(0); // matches (vid.width >> 3) - 2 when vid.width is 0 pre-video-init; must differ from con_linewidth's -1 sentinel above or CheckResize's no-op short-circuit skips setup entirely

        Printf("Console initialized.\n"u8);

        // register our commands
        con_notifytime->Name = "con_notifytime"u8;
        con_notifytime->String = "3"u8;
        Cvar.RegisterVariable(con_notifytime);

        Cmd.AddCommand("clear"u8, &Clear_f);
        Cmd.AddCommand("toggleconsole"u8, &ToggleConsoleCmd);
        Cmd.AddCommand("messagemode"u8, &MessageModeCmd);
        Cmd.AddCommand("messagemode2"u8, &MessageMode2Cmd);

        con_initialized = true;
    }

    private static void ToggleConsoleCmd() => ToggleConsole_f();
    private static void MessageModeCmd() => MessageMode_f();
    private static void MessageMode2Cmd() => MessageMode2_f();

    /*
    ================
    Con_ToggleConsole_f
    ================
    */
    public static void ToggleConsole_f()
    {
        if (Keys.key_dest == KeyDest.Console)
        {
            if (Cls.state == CActive.Connected)
            {
                Keys.key_dest = KeyDest.Game;
                Keys.ClearTypedLine();
            }
            else
            {
                Menu.Menu_Main_f();
            }
        }
        else
        {
            Keys.key_dest = KeyDest.Console;
        }

        Screen.EndLoadingPlaque();
        ClearNotify();
    }

    /*
    ================
    Con_MessageMode_f
    ================
    */
    public static void MessageMode_f()
    {
        Keys.key_dest = KeyDest.Message;
        Keys.team_message = false;
    }

    /*
    ================
    Con_MessageMode2_f
    ================
    */
    public static void MessageMode2_f()
    {
        Keys.key_dest = KeyDest.Message;
        Keys.team_message = true;
    }


    // Con_NotifyBox

    /*
    ===============
    Con_Linefeed
    ===============
    */
    public static void Linefeed()
    {
        con_x = 0;
        con_current++;
        Q.MemSet(con_text + ((con_current % con_totallines) * con_linewidth), ' ', con_linewidth);
    }

    /*
    ================
    Con_Print

    Handles cursor positioning, line wrapping, etc
    All console printing must go through this in order to be logged to disk
    ================
    */
    public static void Print(byte* txt)
    {
        con_backscroll = 0;

        int mask;
        if (txt[0] == 1)
        {
            mask = 128; // go to colored text
            SndDma.LocalSound("misc/talk.wav"u8); // play talk wav
            txt++;
        }
        else if (txt[0] == 2)
        {
            mask = 128; // go to colored text
            txt++;
        }
        else
        {
            mask = 0;
        }

        bool cr = false;
        int c;
        while ((c = *txt) != 0)
        {
            // count word length
            int l;
            for (l = 0; l < con_linewidth; l++)
            {
                if (txt[l] <= (byte)' ')
                {
                    break;
                }
            }

            // word wrap
            if (l != con_linewidth && con_x + l > con_linewidth)
            {
                con_x = 0;
            }

            txt++;

            if (cr)
            {
                con_current--;
                cr = false;
            }

            if (con_x == 0)
            {
                Linefeed();
                // mark time for transparent overlay
                if (con_current >= 0)
                {
                    con_times[con_current % NUM_CON_TIMES] = (float)Host.realtime;
                }
            }

            switch (c)
            {
                case '\n':
                    con_x = 0;
                    break;

                case '\r':
                    con_x = 0;
                    cr = true;
                    break;

                default: // display character and advance
                    int y = con_current % con_totallines;
                    con_text[(y * con_linewidth) + con_x] = (byte)(c | mask);
                    con_x++;
                    if (con_x >= con_linewidth)
                    {
                        con_x = 0;
                    }

                    break;
            }
        }
    }

    /*
    ================
    Con_DebugLog
    ================
    */
    public static void DebugLog(string file, scoped ReadOnlySpan<byte> bytes)
    {
        using FileStream fs = new(file, FileMode.Append, FileAccess.Write);
        fs.Write(bytes);
    }

    /*
    ================
    Con_Printf

    Handles cursor positioning, line wrapping, etc
    ================
    */
    public static void Printf([InterpolatedStringHandlerArgument] scoped Utf8StringHandler u8String)
    {
        ReadOnlySpan<byte> written = u8String.GetWrittenBuffer();
        Echo(written);

        if (!con_initialized || written.IsEmpty)
        {
            return;
        }

        // the handler's buffer is a native ring slot: no pinning needed
        Print(u8String.GetWrittenBufferPtr());
    }

    // stdout and qconsole.log half of Con_Printf
    [SkipLocalsInit]
    private static void Echo(scoped ReadOnlySpan<byte> u8String)
    {
        char[]? rented = null;
        Span<char> chars = u8String.Length <= 512
            ? stackalloc char[u8String.Length]
            : (rented = ArrayPool<char>.Shared.Rent(u8String.Length));

        try
        {
            int written = Encoding.Latin1.GetChars(u8String, chars);
            Console.Out.Write(chars[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        if (con_debuglog)
        {
            DebugLog(CStr.ToString(Com.com_gamedir) + "/qconsole.log", u8String);
        }
    }

    // Printf of arbitrary (possibly managed) bytes. Justified `fixed`
    // (rule 8): the span's backing store is unknown here -- unit tests pass
    // managed byte[]s -- so it must be pinned for Print(byte*).
    [SkipLocalsInit]
    public static void Printf(scoped ReadOnlySpan<byte> u8String)
    {
        Echo(u8String);

        if (!con_initialized)
        {
            return;
        }

        // an empty span pins to a null pointer, which Print would dereference
        if (u8String.IsEmpty)
        {
            return;
        }

        // write it to the scrollable buffer
        fixed (byte* p = u8String)
        {
            Print(p);
        }
    }

    /*
    ================
    Con_DPrintf

    A Con_Printf that only shows up if the "developer" cvar is set
    ================
    */
    public static void DPrintf([InterpolatedStringHandlerArgument] scoped Utf8StringHandler u8String)
    {
        DPrintf(u8String.GetWrittenBuffer());
    }

    public static void DPrintf(scoped ReadOnlySpan<byte> u8String)
    {
        if (Host.developer->Value == 0)
        {
            return; // don't confuse non-developers with techie stuff...
        }

        Printf(u8String);
    }

    /*
    ==================
    Con_SafePrintf

    Okay to call even when the screen can't be updated
    ==================
    */
    public static void SafePrintf([InterpolatedStringHandlerArgument] scoped Utf8StringHandler u8String)
    {
        SafePrintf(u8String.GetWrittenBuffer());
    }

    public static void SafePrintf(scoped ReadOnlySpan<byte> u8String)
    {
        bool temp = Screen.scr_disabled_for_loading;
        Screen.scr_disabled_for_loading = true;
        Printf(u8String);
        Screen.scr_disabled_for_loading = temp;
    }

    /*
    ================
    Con_Clear_f
    ================
    */
    public static void Clear_f()
    {
        if (con_text != null)
        {
            Q.MemSet(con_text, ' ', CON_TEXTSIZE);
        }
    }

    /*
    ==============================================================================

    DRAWING

    ==============================================================================
    */

    /*
    ================
    Con_DrawInput

    The input line scrolls horizontally if typing goes beyond the right edge
    ================
    */
    public static void DrawInput()
    {
        if (Keys.key_dest != KeyDest.Console && !con_forcedup)
        {
            return; // don't draw anything
        }

        byte* text = Keys.KeyLine(Keys.edit_line);

        // add the cursor frame
        text[Keys.key_linepos] = (byte)(10 + ((int)(Host.realtime * con_cursorspeed) & 1));

        // fill out remainder with spaces
        for (int i = Keys.key_linepos + 1; i < con_linewidth; i++)
        {
            text[i] = (byte)' ';
        }

        // prestep if horizontally scrolling
        if (Keys.key_linepos >= con_linewidth)
        {
            text += 1 + Keys.key_linepos - con_linewidth;
        }

        // draw it
        for (int i = 0; i < con_linewidth; i++)
        {
            Draw.Character((i + 1) << 3, con_vislines - 16, text[i]);
        }

        // remove cursor
        Keys.KeyLine(Keys.edit_line)[Keys.key_linepos] = 0;
    }

    /*
    ================
    Con_DrawNotify

    Draws the last few lines of output transparently over the game top
    ================
    */
    public static void DrawNotify()
    {
        int v = 0;
        for (int i = con_current - NUM_CON_TIMES + 1; i <= con_current; i++)
        {
            if (i < 0)
            {
                continue;
            }

            float time = con_times[i % NUM_CON_TIMES];
            if (time == 0)
            {
                continue;
            }

            time = (float)Host.realtime - time;
            if (time > con_notifytime->Value)
            {
                continue;
            }

            byte* text = con_text + ((i % con_totallines) * con_linewidth);

            Screen.clearnotify = 0;
            Screen.scr_copytop = 1;

            for (int x = 0; x < con_linewidth; x++)
            {
                Draw.Character((x + 1) << 3, v, text[x]);
            }

            v += 8;
        }

        if (Keys.key_dest == KeyDest.Message)
        {
            Screen.clearnotify = 0;
            Screen.scr_copytop = 1;

            int x = 0;

            Draw.String(8, v, "say:"u8);
            while (Keys.chat_buffer[x] != 0)
            {
                Draw.Character((x + 5) << 3, v, Keys.chat_buffer[x]);
                x++;
            }
            Draw.Character((x + 5) << 3, v, (int)(10 + ((int)(Host.realtime * con_cursorspeed) & 1)));
            v += 8;
        }

        if (v > con_notifylines)
        {
            con_notifylines = v;
        }
    }

    /*
    ================
    Con_DrawConsole

    Draws the console with the solid background
    The typing input line at the bottom should only be drawn if typing is allowed
    ================
    */
    public static void DrawConsole(float lines, bool drawinput)
    {
        if (lines <= 0)
        {
            return;
        }

        // draw the background
        Draw.ConsoleBackground((int)lines);

        // draw the text
        con_vislines = (int)lines;

        int rows = ((int)lines - 16) >> 3; // rows of text to draw
        int y = (int)lines - 16 - (rows << 3); // may start slightly negative

        for (int i = con_current - rows + 1; i <= con_current; i++, y += 8)
        {
            int j = i - con_backscroll;
            if (j < 0)
            {
                j = 0;
            }

            byte* text = con_text + ((j % con_totallines) * con_linewidth);

            for (int x = 0; x < con_linewidth; x++)
            {
                Draw.Character((x + 1) << 3, y, text[x]);
            }
        }

        // draw the input prompt, user text, and cursor if desired
        if (drawinput)
        {
            DrawInput();
        }
    }
}
