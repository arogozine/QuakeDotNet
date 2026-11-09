// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// keys.c -- key up/down events, line editing into the console, and the
/// keybinding table. Raw hardware input (reading the keyboard/mouse itself)
/// stays out of scope -- that is Quake.Desktop's OpenTK-specific job (see
/// open_tk.md) -- but Key_Event, the dispatch entry point every OS-specific
/// input source funnels through once it has a keynum + down/up bool, is
/// itself OS-agnostic and is fully ported here, along with Key_Console/
/// Key_Message (line editing, console scrollback, chat buffer) and the
/// bind/unbind console commands.
/// </summary>
public static unsafe class Keys
{
    // keydest_t (keys.h)

    // K_* (keys.h) -- keynums passed to Key_Event.
    public const int K_TAB = 9;
    public const int K_ENTER = 13;
    public const int K_ESCAPE = 27;
    public const int K_SPACE = 32;
    public const int K_BACKSPACE = 127;
    public const int K_UPARROW = 128;
    public const int K_DOWNARROW = 129;
    public const int K_LEFTARROW = 130;
    public const int K_RIGHTARROW = 131;
    public const int K_ALT = 132;
    public const int K_CTRL = 133;
    public const int K_SHIFT = 134;
    public const int K_F1 = 135;
    public const int K_F2 = 136;
    public const int K_F3 = 137;
    public const int K_F4 = 138;
    public const int K_F5 = 139;
    public const int K_F6 = 140;
    public const int K_F7 = 141;
    public const int K_F8 = 142;
    public const int K_F9 = 143;
    public const int K_F10 = 144;
    public const int K_F11 = 145;
    public const int K_F12 = 146;
    public const int K_INS = 147;
    public const int K_DEL = 148;
    public const int K_PGDN = 149;
    public const int K_PGUP = 150;
    public const int K_HOME = 151;
    public const int K_END = 152;
    public const int K_PAUSE = 255;
    public const int K_MOUSE1 = 200;
    public const int K_MOUSE2 = 201;
    public const int K_MOUSE3 = 202;
    public const int K_JOY1 = 203;
    public const int K_JOY2 = 204;
    public const int K_JOY3 = 205;
    public const int K_JOY4 = 206;

    // aux keys are for multi-buttoned joysticks to generate so they can use
    // the normal binding process: K_AUX1 .. K_AUX32 are consecutive.
    public const int K_AUX1 = 207;
    public const int K_AUX32 = 238;

    public const int K_MWHEELUP = 239;
    public const int K_MWHEELDOWN = 240;

    // Not in WinQuake's keys.h (which stops at MOUSE3); Mark V adds extra mouse
    // buttons. Placed in the unused 241..254 range, clear of K_AUX1..K_AUX32
    // (207..238), the wheel keys and K_PAUSE.
    public const int K_MOUSE4 = 241;
    public const int K_MOUSE5 = 242;

    // key_dest -- keydest_t's first enumerator is key_game (=0), and the
    // original's `keydest_t key_dest;` is a zero-initialized global with no
    // explicit assignment anywhere in Key_Init, so its real default is
    // key_game, not key_console.
    public static KeyDest key_dest = KeyDest.Game;

    public static int key_count;      // Key_Event's per-keydown counter
    public static int key_lastpress;  // last key pressed, read by SCR_ModalMessage

    // keybindings[256] -- Z_Malloc'd command strings per keynum.
    public static readonly byte** keybindings = NativeMemory.AllocPointerArray<byte>(256);

    // consolekeys[256]/menubound[256] -- rebind-eligibility filter tables.
    internal static readonly bool* consolekeys = NativeMemory.AllocArrayZeroed<bool>(256);
    internal static readonly bool* menubound = NativeMemory.AllocArrayZeroed<bool>(256);

    // keyshift[256] -- key to map to if shift held down in console.
    internal static readonly int* keyshift = NativeMemory.AllocArrayZeroed<int>(256);

    // key_repeats[256]/keydown[256] -- autorepeat + held-state tracking.
    internal static readonly int* key_repeats = NativeMemory.AllocArrayZeroed<int>(256);
    internal static readonly bool* keydown = NativeMemory.AllocArrayZeroed<bool>(256);

    private static bool shift_down;

    internal const int MAXCMDLINE = 256;

    // key_lines[32][MAXCMDLINE] -- flattened into one NativeMemory block
    // (32 * MAXCMDLINE bytes), indexed a line at a time by KeyLine(i) below;
    // avoids 32 separate small allocations for what is fixed-size C data.
    // internal (not private): Console.cs's ported Con_DrawInput needs
    // key_lines[edit_line]/key_linepos/edit_line directly, same as the
    // original's console.c reaching into keys.c's globals.
    internal static readonly byte* key_lines = NativeMemory.AllocArrayZeroed<byte>(32 * MAXCMDLINE);
    internal static byte* KeyLine(int i) => key_lines + i * MAXCMDLINE;

    internal static int key_linepos;
    internal static int edit_line;
    private static int history_line;

    // chat_buffer/team_message (keys.c globals used by Key_Message).
    // internal: Con_DrawNotify reads chat_buffer directly (console.c's
    // "extern char chat_buffer[]").
    internal static readonly byte* chat_buffer = NativeMemory.AllocArrayZeroed<byte>(32);
    private static int chat_bufferlen;
    public static bool team_message;

    // Key_Init -- default key bindings/consolekeys/menubound/keyshift setup,
    // plus the bind/unbind/unbindall commands. Actual default keybindings
    // (config.cfg's "bind" lines in the original) aren't ported: nothing in
    // this project ships a default config file, so keybindings[] starts
    // empty exactly like a fresh Z_Malloc'd table would with no config
    // exec'd, matching Host.Init's own dropped "exec quake.rc" call.
    public static void Init()
    {
        for (int i = 0; i < 32; i++)
        {
            KeyLine(i)[0] = (byte)']';
            KeyLine(i)[1] = 0;
        }
        key_linepos = 1;

        for (int i = 32; i < 128; i++)
            consolekeys[i] = true;

        consolekeys[K_ENTER] = true;
        consolekeys[K_TAB] = true;
        consolekeys[K_LEFTARROW] = true;
        consolekeys[K_RIGHTARROW] = true;
        consolekeys[K_UPARROW] = true;
        consolekeys[K_DOWNARROW] = true;
        consolekeys[K_BACKSPACE] = true;
        consolekeys[K_PGUP] = true;
        consolekeys[K_PGDN] = true;
        consolekeys[K_SHIFT] = true;
        consolekeys[K_MWHEELUP] = true;
        consolekeys[K_MWHEELDOWN] = true;
        consolekeys['`'] = false;
        consolekeys['~'] = false;

        for (int i = 0; i < 256; i++)
            keyshift[i] = i;

        for (int i = 'a'; i <= 'z'; i++)
            keyshift[i] = i - 'a' + 'A';

        keyshift['1'] = '!';
        keyshift['2'] = '@';
        keyshift['3'] = '#';
        keyshift['4'] = '$';
        keyshift['5'] = '%';
        keyshift['6'] = '^';
        keyshift['7'] = '&';
        keyshift['8'] = '*';
        keyshift['9'] = '(';
        keyshift['0'] = ')';
        keyshift['-'] = '_';
        keyshift['='] = '+';
        keyshift[','] = '<';
        keyshift['.'] = '>';
        keyshift['/'] = '?';
        keyshift[';'] = ':';
        keyshift['\''] = '"';
        keyshift['['] = '{';
        keyshift[']'] = '}';
        keyshift['`'] = '~';
        keyshift['\\'] = '|';

        menubound[K_ESCAPE] = true;
        for (int i = 0; i < 12; i++)
            menubound[K_F1 + i] = true;

        Cmd.AddCommand("bind"u8, &BindCmd);
        Cmd.AddCommand("unbind"u8, &UnbindCmd);
        Cmd.AddCommand("unbindall"u8, &UnbindAllCmd);
    }

    // keyname_t/keynames (keys.c) -- only Key_KeynumToString's table plus
    // Key_StringToKeynum's reverse lookup (bind/unbind need both directions
    // now).
    private readonly struct KeyName(Utf8String name, int keyNum)
    {
        public readonly byte* Name = name;
        public readonly int KeyNum = keyNum;
    }

    private static readonly KeyName[] keynames =
    {
        new("TAB"u8, K_TAB), new("ENTER"u8, K_ENTER), new("ESCAPE"u8, K_ESCAPE), new("SPACE"u8, K_SPACE),
        new("BACKSPACE"u8, K_BACKSPACE), new("UPARROW"u8, K_UPARROW), new("DOWNARROW"u8, K_DOWNARROW),
        new("LEFTARROW"u8, K_LEFTARROW), new("RIGHTARROW"u8, K_RIGHTARROW),
        new("ALT"u8, K_ALT), new("CTRL"u8, K_CTRL), new("SHIFT"u8, K_SHIFT),
        new("F1"u8, K_F1), new("F2"u8, K_F2), new("F3"u8, K_F3), new("F4"u8, K_F4), new("F5"u8, K_F5), new("F6"u8, K_F6),
        new("F7"u8, K_F7), new("F8"u8, K_F8), new("F9"u8, K_F9), new("F10"u8, K_F10), new("F11"u8, K_F11), new("F12"u8, K_F12),
        new("INS"u8, K_INS), new("DEL"u8, K_DEL), new("PGDN"u8, K_PGDN), new("PGUP"u8, K_PGUP), new("HOME"u8, K_HOME), new("END"u8, K_END),
        new("MOUSE1"u8, K_MOUSE1), new("MOUSE2"u8, K_MOUSE2), new("MOUSE3"u8, K_MOUSE3),
        new("MOUSE4"u8, K_MOUSE4), new("MOUSE5"u8, K_MOUSE5),
        new("JOY1"u8, K_JOY1), new("JOY2"u8, K_JOY2), new("JOY3"u8, K_JOY3), new("JOY4"u8, K_JOY4),
        new("AUX1"u8, K_AUX1 + 0), new("AUX2"u8, K_AUX1 + 1), new("AUX3"u8, K_AUX1 + 2), new("AUX4"u8, K_AUX1 + 3),
        new("AUX5"u8, K_AUX1 + 4), new("AUX6"u8, K_AUX1 + 5), new("AUX7"u8, K_AUX1 + 6), new("AUX8"u8, K_AUX1 + 7),
        new("AUX9"u8, K_AUX1 + 8), new("AUX10"u8, K_AUX1 + 9), new("AUX11"u8, K_AUX1 + 10), new("AUX12"u8, K_AUX1 + 11),
        new("AUX13"u8, K_AUX1 + 12), new("AUX14"u8, K_AUX1 + 13), new("AUX15"u8, K_AUX1 + 14), new("AUX16"u8, K_AUX1 + 15),
        new("AUX17"u8, K_AUX1 + 16), new("AUX18"u8, K_AUX1 + 17), new("AUX19"u8, K_AUX1 + 18), new("AUX20"u8, K_AUX1 + 19),
        new("AUX21"u8, K_AUX1 + 20), new("AUX22"u8, K_AUX1 + 21), new("AUX23"u8, K_AUX1 + 22), new("AUX24"u8, K_AUX1 + 23),
        new("AUX25"u8, K_AUX1 + 24), new("AUX26"u8, K_AUX1 + 25), new("AUX27"u8, K_AUX1 + 26), new("AUX28"u8, K_AUX1 + 27),
        new("AUX29"u8, K_AUX1 + 28), new("AUX30"u8, K_AUX1 + 29), new("AUX31"u8, K_AUX1 + 30), new("AUX32"u8, K_AUX32),
        new("PAUSE"u8, K_PAUSE), new("MWHEELUP"u8, K_MWHEELUP), new("MWHEELDOWN"u8, K_MWHEELDOWN),
        new("SEMICOLON"u8, ';'),
    };

    // Key_KeynumToString
    // Returns a static NUL-terminated string (tinystr for printable ascii, as in keys.c).
    internal static readonly byte* tinystr = NativeMemory.AllocArrayZeroed<byte>(2);

    public static byte* KeynumToString(int keynum)
    {
        if (keynum == -1)
            return new Utf8String("<KEY NOT FOUND>"u8);
        if (keynum > 32 && keynum < 127) // printable ascii
        {
            tinystr[0] = (byte)keynum;
            tinystr[1] = 0;
            return tinystr;
        }

        foreach (var kn in keynames)
            if (keynum == kn.KeyNum)
                return kn.Name;

        return new Utf8String("<UNKNOWN KEYNUM>"u8);
    }

    // Key_StringToKeynum
    public static int StringToKeynum(byte* str)
    {
        if (str == null || str[0] == 0)
            return -1;
        if (str[1] == 0)
        {
            // Not in keys.c / Mark V: KeyMap emits lowercase letters, so
            // `bind W` would bind keynum 87, which no key produces.
            if (str[0] >= (byte)'A' && str[0] <= (byte)'Z')
                return str[0] | 0x20;
            return str[0];
        }

        foreach (var kn in keynames)
        {
            if (Q.StrCaseCmp(str, kn.Name) == 0)
                return kn.KeyNum;
        }

        return -1;
    }

    // Key_SetBinding
    public static void SetBinding(int keynum, Utf8String binding)
    {
        if (keynum == -1)
            return;

        if (keybindings[keynum] != null)
        {
            Zone.Free(keybindings[keynum]);
            keybindings[keynum] = null;
        }

        int l = Q.StrLen(binding);
        byte* newBinding = (byte*)Zone.Malloc(l + 1);
        Q.StrCpy(newBinding, binding);
        keybindings[keynum] = newBinding;
    }

    private static void BindCmd() => Bind_f();
    private static void UnbindCmd() => Unbind_f();
    private static void UnbindAllCmd() => UnbindAll_f();

    // Key_Unbind_f
    public static void Unbind_f()
    {
        if (Cmd.Argc() != 2)
        {
            Con.Printf("unbind <key> : remove commands from a key\n"u8);
            return;
        }

        int b = StringToKeynum(Cmd.Argv(1));
        if (b == -1)
        {
            Con.Printf($"\"{Cmd.Argv(1)}\" isn't a valid key\n");
            return;
        }

        SetBinding(b, Utf8String.Empty);
    }

    // Key_WriteBindings
    // Writes lines containing "bind key value"
    public static void WriteBindings(int handle)
    {
        for (int i = 0; i < 256; i++)
        {
            if (keybindings[i] != null)
            {
                // fprintf(f, "bind \"%s\" \"%s\"\n", Key_KeynumToString(i), keybindings[i])
                byte* name = KeynumToString(i);
                Sys.FileWrite(handle, "bind \""u8);
                Sys.FileWrite(handle, name, Q.StrLen(name));
                Sys.FileWrite(handle, "\" \""u8);
                Sys.FileWrite(handle, keybindings[i], Q.StrLen(keybindings[i]));
                Sys.FileWrite(handle, "\"\n"u8);
            }
        }
    }

    // Key_Unbindall_f
    public static void UnbindAll_f()
    {
        for (int i = 0; i < 256; i++)
            if (keybindings[i] != null)
                SetBinding(i, Utf8String.Empty);
    }

    // c_strlcat (Mark V) -- strcat that never writes past dest[size - 1].
    internal static void StrLCat(byte* dest, byte* src, int size)
    {
        int d = Q.StrLen(dest);
        int i = 0;
        while (src[i] != 0 && d + i < size - 1)
        {
            dest[d + i] = src[i];
            i++;
        }
        dest[d + i] = 0;
    }

    internal static void StrLCat(byte* dest, ReadOnlySpan<byte> src, int size)
    {
        int d = Q.StrLen(dest);
        int i = 0;
        while (i < src.Length && d + i < size - 1)
        {
            dest[d + i] = src[i];
            i++;
        }
        dest[d + i] = 0;
    }

    // Key_Bind_f
    public static void Bind_f()
    {
        int c = Cmd.Argc();

        if (c != 2 && c != 3)
        {
            Con.Printf("bind <key> [command] : attach a command to a key\n"u8);
            return;
        }

        int b = StringToKeynum(Cmd.Argv(1));
        if (b == -1)
        {
            Con.Printf($"\"{Cmd.Argv(1)}\" isn't a valid key\n");
            return;
        }

        if (c == 2)
        {
            if (keybindings[b] != null)
                Con.Printf($"\"{Cmd.Argv(1)}\" = \"{keybindings[b]}\"\n");
            else
                Con.Printf($"\"{Cmd.Argv(1)}\" is not bound\n");
            return;
        }

        // Mark V bounds this with c_strlcat; WinQuake's strcat overflows.
        // Arguments that don't fit are truncated, always leaving the NUL.
        const int cmdSize = 1024;
        byte* cmd = stackalloc byte[cmdSize];
        cmd[0] = 0;
        for (int i = 2; i < c; i++)
        {
            if (i > 2)
                StrLCat(cmd, " "u8, cmdSize);
            StrLCat(cmd, Cmd.Argv(i), cmdSize);
        }

        SetBinding(b, cmd);
    }


    /*
    ====================
    Key_Console

    Interactive line editing and console scrollback
    ====================
    */
    public static void Console(int key)
    {
        if (key == K_ENTER)
        {
            Cmd.AddText(KeyLine(edit_line) + 1); // skip the >
            Cmd.AddText("\n"u8);
            Con.Printf($"{KeyLine(edit_line)}\n");
            edit_line = (edit_line + 1) & 31;
            history_line = edit_line;
            KeyLine(edit_line)[0] = (byte)']';
            key_linepos = 1;
            if (Cls.state == CActive.Disconnected)
                Screen.UpdateScreen(); // force an update, because the command
                                       // may take some time
            return;
        }

        if (key == K_TAB)
        {
            // command completion
            byte* cmd = Cmd.CompleteCommand(KeyLine(edit_line) + 1);
            if (cmd == null)
                cmd = Cvar.CompleteVariable(KeyLine(edit_line) + 1);
            if (cmd != null)
            {
                Q.StrCpy(KeyLine(edit_line) + 1, cmd);
                key_linepos = Q.StrLen(cmd) + 1;
                KeyLine(edit_line)[key_linepos] = (byte)' ';
                key_linepos++;
                KeyLine(edit_line)[key_linepos] = 0;
                return;
            }
        }

        if (key == K_BACKSPACE || key == K_LEFTARROW)
        {
            if (key_linepos > 1)
                key_linepos--;
            return;
        }

        if (key == K_UPARROW)
        {
            do
            {
                history_line = (history_line - 1) & 31;
            } while (history_line != edit_line && KeyLine(history_line)[1] == 0);
            if (history_line == edit_line)
                history_line = (edit_line + 1) & 31;
            Q.StrCpy(KeyLine(edit_line), KeyLine(history_line));
            key_linepos = Q.StrLen(KeyLine(edit_line));
            return;
        }

        if (key == K_DOWNARROW)
        {
            if (history_line == edit_line)
                return;
            do
            {
                history_line = (history_line + 1) & 31;
            } while (history_line != edit_line && KeyLine(history_line)[1] == 0);
            if (history_line == edit_line)
            {
                KeyLine(edit_line)[0] = (byte)']';
                key_linepos = 1;
            }
            else
            {
                Q.StrCpy(KeyLine(edit_line), KeyLine(history_line));
                key_linepos = Q.StrLen(KeyLine(edit_line));
            }
            return;
        }

        if (key == K_PGUP || key == K_MWHEELUP)
        {
            Con.con_backscroll += 2;
            if (Con.con_backscroll > Con.con_totallines - ((int)Vid.vid.Height >> 3) - 1)
                Con.con_backscroll = Con.con_totallines - ((int)Vid.vid.Height >> 3) - 1;
            return;
        }

        if (key == K_PGDN || key == K_MWHEELDOWN)
        {
            Con.con_backscroll -= 2;
            if (Con.con_backscroll < 0)
                Con.con_backscroll = 0;
            return;
        }

        if (key == K_HOME)
        {
            Con.con_backscroll = Con.con_totallines - ((int)Vid.vid.Height >> 3) - 1;
            return;
        }

        if (key == K_END)
        {
            Con.con_backscroll = 0;
            return;
        }

        if (key < 32 || key > 127)
            return; // non printable

        if (key_linepos < MAXCMDLINE - 1)
        {
            KeyLine(edit_line)[key_linepos] = (byte)key;
            key_linepos++;
            KeyLine(edit_line)[key_linepos] = 0;
        }
    }

    // Key_ClearTypedLine -- Con_ToggleConsole_f's `key_lines[edit_line][1] = 0;
    // key_linepos = 1;` inline snippet, pulled out since Console.cs (a
    // different static class) needs it too.
    public static void ClearTypedLine()
    {
        KeyLine(edit_line)[1] = 0;
        key_linepos = 1;
    }

    /*
    ============================================================================
    */

    // Key_Message
    public static void Message(int key)
    {
        if (key == K_ENTER)
        {
            Cmd.AddText(team_message ? "say_team \""u8 : "say \""u8);
            Cmd.AddText(chat_buffer);
            Cmd.AddText("\"\n"u8);

            key_dest = KeyDest.Game;
            chat_bufferlen = 0;
            chat_buffer[0] = 0;
            return;
        }

        if (key == K_ESCAPE)
        {
            key_dest = KeyDest.Game;
            chat_bufferlen = 0;
            chat_buffer[0] = 0;
            return;
        }

        if (key < 32 || key > 127)
            return; // non printable

        if (key == K_BACKSPACE)
        {
            if (chat_bufferlen != 0)
            {
                chat_bufferlen--;
                chat_buffer[chat_bufferlen] = 0;
            }
            return;
        }

        if (chat_bufferlen == 31)
            return; // all full

        chat_buffer[chat_bufferlen++] = (byte)key;
        chat_buffer[chat_bufferlen] = 0;
    }

    /*
    ===================
    Key_Event

    Called by the system between frames for both key up and key down events
    Should NOT be called during an interrupt!
    ===================
    */
    public static void Event(int key, bool down)
    {
        // No range check in WinQuake or Mark V; debug-only guard.
        Debug.Assert((uint)key < 256, "Key_Event: keynum out of range");

        if (down)
            eventDest = key_dest;

        keydown[key] = down;

        if (!down)
            key_repeats[key] = 0;

        key_lastpress = key;
        key_count++;
        if (key_count <= 0)
            return; // just catching keys for Con_NotifyBox

        // update auto-repeat status
        if (down)
        {
            key_repeats[key]++;
            if (key != K_BACKSPACE && key != K_PAUSE && key_repeats[key] > 1)
                return; // ignore most autorepeats

            if (key >= 200 && keybindings[key] == null)
                Con.Printf($"{KeynumToString(key)} is unbound, hit F4 to set.\n");
        }

        if (key == K_SHIFT)
            shift_down = down;

        // handle escape specialy, so the user can never unbind it
        if (key == K_ESCAPE)
        {
            if (!down)
                return;
            switch (key_dest)
            {
                case KeyDest.Message:
                    Message(key);
                    break;
                case KeyDest.Menu:
                    Menu.Keydown(key);
                    break;
                case KeyDest.Game:
                case KeyDest.Console:
                    Menu.ToggleMenu_f();
                    break;
                default:
                    Sys.Error("Bad key_dest"u8);
                    break;
            }
            return;
        }

        // key up events only generate commands if the game key binding is
        // a button command (leading + sign). These will occur even in
        // console mode, to keep the character from continuing an action
        // started before a console switch. Button commands include the
        // keynum as a parameter, so multiple downs can be matched with ups.
        if (!down)
        {
            byte* kb = keybindings[key];
            if (kb != null && kb[0] == (byte)'+')
                AddButtonCommand(kb, key, down: false);

            if (keyshift[key] != key)
            {
                kb = keybindings[keyshift[key]];
                if (kb != null && kb[0] == (byte)'+')
                    AddButtonCommand(kb, key, down: false);
            }
            return;
        }

        // during demo playback, most keys bring up the main menu
        if (ClDemo.demoplayback && down && consolekeys[key] && key_dest == KeyDest.Game)
        {
            Menu.ToggleMenu_f();
            return;
        }

        // if not a consolekey, send to the interpreter no matter what mode is
        if ((key_dest == KeyDest.Menu && menubound[key])
            || (key_dest == KeyDest.Console && !consolekeys[key])
            || (key_dest == KeyDest.Game && (!Con.con_forcedup || !consolekeys[key])))
        {
            byte* kb = keybindings[key];
            if (kb != null)
            {
                if (kb[0] == (byte)'+')
                    AddButtonCommand(kb, key, down: true);
                else
                {
                    Cmd.AddText(kb);
                    Cmd.AddText("\n"u8);
                }
            }
            return;
        }

        if (!down)
            return; // other systems only care about key down events

        // Not in the original: when the host feeds typed text through
        // CharEvent, printable keys must not also be typed from the physical
        // (US-layout) keynum. The menu keeps using keynums (key grab, y/n).
        if (TextInputActive && key_dest != KeyDest.Menu && key >= 32 && key <= 126)
            return;

        if (shift_down)
            key = keyshift[key];

        switch (key_dest)
        {
            case KeyDest.Message:
                Message(key);
                break;
            case KeyDest.Menu:
                Menu.Keydown(key);
                break;
            case KeyDest.Game:
            case KeyDest.Console:
                Console(key);
                break;
            default:
                Sys.Error("Bad key_dest"u8);
                break;
        }
    }

    // Not in the original (Mark V's Key_Event_Ex emission path): typed text
    // from the OS text-input event, so non-US layouts (AZERTY, QWERTZ, AltGr,
    // dead keys) type the right characters in the console and chat. Physical
    // keys still drive bindings through Event. Menu text keeps using keynums.
    public static bool TextInputActive;

    // key_dest when the latest key-down was processed; a character that
    // arrives after that key changed the destination (e.g. bind t
    // messagemode) belongs to the key press, not to the new text field.
    private static KeyDest eventDest;

    public static void CharEvent(int ch)
    {
        if (ch < 32 || ch > 126)
            return;
        if (ch == '`' || ch == '~') // toggleconsole keys, as in Mark V
            return;
        if (key_dest != eventDest || keydown[K_CTRL])
            return;

        switch (key_dest)
        {
            case KeyDest.Message:
                Message(ch);
                break;
            case KeyDest.Console:
                Console(ch);
                break;
            case KeyDest.Game:
                if (Con.con_forcedup)
                    Console(ch);
                break;
        }
    }

    // sprintf(cmd, "%s %i\n", kb, key) / sprintf(cmd, "-%s %i\n", kb+1, key)
    // -- Key_Event's two button-command-parameter formats, factored out
    // since both the key-up and key-down paths above build the same shape.
    private static void AddButtonCommand(byte* kb, int key, bool down)
    {
        if (down)
        {
            Cmd.AddText($"{kb} {key}\n");
        }
        else
        {
            Cmd.AddText($"-{kb + 1} {key}\n");
        }
    }

    // Key_ClearStates
    public static void ClearStates()
    {
        for (int i = 0; i < 256; i++)
        {
            keydown[i] = false;
            key_repeats[i] = 0;
        }
    }

    // Not in the original: on focus loss the host never sees the key-up
    // events, so a held "+forward" would stay active. Sends a release for
    // every held key (generating the matching "-command <key>" lines), then
    // Key_ClearStates.
    public static void ReleaseAll()
    {
        for (int i = 0; i < 256; i++)
        {
            if (keydown[i])
                Event(i, false);
        }

        ClearStates();
    }
}
