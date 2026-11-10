// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// menu.c -- the pause/options/single-player menu system. Ported faithfully;
/// M_Draw/M_Keydown are called directly from Screen.cs and Keys.cs's
/// Key_Event dispatch respectively. The only
/// things NOT faithfully reproduced are pieces this project explicitly
/// excludes:
///  - Serial/modem/IPX config screens are kept structurally (same states,
///    same cursor logic) but their actual hardware/transport calls
///    (GetComPortConfig/SetComPortConfig/GetModemConfig/SetModemConfig)
///    are private stubs returning safe defaults -- serial/IPX transports
///    are permanently out of scope (CLAUDE.md). The server browser
///    (NET_Slist_f, hostcache) is real: see Net.cs. The
///    GameOptions/LanConfig screens (the "start game" / host-config path)
///    are wired to the real TCP/IP networking stack: Svs (Sv.cs/Net.cs) and
///    every cvar they read/write (Cl.cl_name/cl_color, ClInput.sensitivity/
///    m_pitch/lookspring/lookstrafe, SndDma.bgmvolume/volume, Net.hostname,
///    Host.coop/teamplay/skill/fraglimit/timelimit, Com.registered_cvar)
///    are the real, registered ones, not local stand-ins -- previously
///    every GameOptions/Options adjustment silently failed with a
///    "Cvar_Set: variable not found" console error since none of those
///    names were actually registered under the menu's own copies, and
///    M_Menu_Save_f's Svs.maxclients guard could never pass since its
///    stand-in was permanently 0. M_ConfigureNetSubsystem also now writes
///    the real Net.net_hostport (Net.cs), the actual UDP bind port, instead
///    of a local shadow the "port" LAN-config field never actually reached.
///  - vid_menudrawfn/vid_menukeyfn (Video Options submenu) are hooks that
///    default to null; Quake.Desktop's VidWindow.Init points them at
///    VidMenu.cs's VID_MenuDraw/VID_MenuKey port, just as vid_win.c's
///    VID_Init does. The original's "if (vid_menudrawfn)" null checks are
///    kept, so a host without a video backend (unit tests) is unaffected.
///  - The Options menu's 14th (_WIN32-only) "Use Mouse" item/checkbox and
///    its backing _windowed_mouse cvar are omitted: that cvar only ever
///    gated vid_win.c's own IN_ActivateMouse/IN_DeactivateMouse calls (mouse
///    capture toggled independent of game state while windowed), which has
///    no equivalent under Quake.Desktop's OpenTK layer -- InputHost.cs's
///    UpdateCursorState captures the cursor automatically based on
///    key_dest==KeyDest.Game instead, so OPTIONS_ITEMS stays 13 (the non-Win32
///    value) rather than 14.
///  - Quake2-only (#ifdef QUAKE2) code paths are dropped.
/// </summary>
public static unsafe class Menu
{

    internal static MenuState m_state;

    // vid_menudrawfn, vid_menukeyfn -- Video Options submenu hooks, assigned
    // by Quake.Desktop's VidWindow.Init (see type-level doc comment).
    public static delegate* managed<void> vid_menudrawfn = null;
    public static delegate* managed<int, void> vid_menukeyfn = null;

    // Multiplayer transport availability. Serial/IPX are permanently out of
    // scope (CLAUDE.md rule 8: dead transports, see net_wipx.c's drop note in
    // Net.cs's type-level comment), so they are always false; they are
    // static readonly rather than const so the menu code that still tests
    // them stays structurally faithful without "unreachable code" warnings.
    // TCP/IP is the real NetIp.tcpipAvailable, set by WINS_Init (NetIp.cs).
    private static readonly bool serialAvailable = false;
    private static readonly bool ipxAvailable = false;
    private static bool tcpipAvailable => NetIp.tcpipAvailable;

    // GetComPortConfig/SetComPortConfig/GetModemConfig/SetModemConfig
    // (function pointers in net_main.c, filled in only by the serial
    // driver) -- plain stubs here since the serial driver isn't ported (see
    // type-level summary), so nothing would ever reassign them.
    private static void GetComPortConfig(int portNumber, int* port, int* irq, int* baud, bool* useModem)
    {
        *port = 0; *irq = 4; *baud = 9600; *useModem = false;
    }

    private static void SetComPortConfig(int portNumber, int port, int irq, int baud, bool useModem)
    {
    }

    private static void GetModemConfig(int portNumber, byte* dialType, byte* clear, byte* init, byte* hangup)
    {
        *dialType = (byte)'T'; *clear = 0; *init = 0; *hangup = 0;
    }

    // The editable text fields of menu.c (char setup_hostname[16] etc.), kept
    // as NUL-terminated native buffers and edited with strlen like the original.
    private static byte* AllocField([ConstantExpected] int size) => NativeMemory.AllocArrayZeroed<byte>(size);

    private static void SetModemConfig(int portNumber, byte* dialType, byte* clear, byte* init, byte* hangup)
    {
    }

    private static readonly Utf8String my_ipx_address = Utf8String.Empty;

    // server_static_t (server.h's svs) -- the real Svs (Sv.cs/Net.cs) now
    // exists; M_Menu_GameOptions_f/M_NetStart_Change read Svs.maxclients/
    // Svs.maxclientslimit directly off it.

    // Cvars genuinely owned by other files -- cl_main.c's cl_name/cl_color
    // (Cl.cs), cl_input.c's sensitivity/m_pitch/lookspring/lookstrafe
    // (ClInput.cs), snd_dma.c's bgmvolume/volume (SndDma.cs), net_main.c's
    // hostname (Net.cs), host.c's coop/teamplay/skill/fraglimit/timelimit
    // (Host.cs), and common.c's registered (Com.cs) -- are read/written
    // directly off those classes' real, registered cvars below; no local
    // stand-ins.


    private static readonly Random rand = new();

    public static bool m_entersound;   // play after drawing a frame
    public static bool m_recursiveDraw;

    // internal (not private), like Host.inerror, so Quake.UnitTests can arm/
    // inspect these directly instead of driving a full menu key sequence.
    internal static MenuState m_return_state;
    internal static bool m_return_onerror;
    internal const int M_RETURN_REASON_LENGTH = 32;
    internal static readonly byte* m_return_reason = NativeMemory.AllocArrayZeroed<byte>(M_RETURN_REASON_LENGTH);

    // Datagram_Connect's (net_dgrm.c) ErrorReturn/ErrorReturn2 tail -- called on
    // every connect failure. reason may be null (the original skips
    // Q_strcpy(m_return_reason, ...) for the sock-alloc/dial-failure cases, going
    // straight to the key_dest/m_state switch below).
    internal static void ConnectFailed(byte* reason)
    {
        if (reason != null)
        {
            Q.StrNCpy(m_return_reason, reason, M_RETURN_REASON_LENGTH - 1);
        }

        if (m_return_onerror)
        {
            Keys.key_dest = KeyDest.Menu;
            m_state = m_return_state;
            m_return_onerror = false;
        }
    }

    // Datagram_Connect's "m_return_onerror = false; return sock;" success tail.
    internal static void ConnectSucceeded()
    {
        m_return_onerror = false;
    }

    private static bool StartingGame => m_multiplayer_cursor == 1;
    private static bool JoiningGame => m_multiplayer_cursor == 0;
    private static bool SerialConfig => m_net_cursor == 0;
    private static bool DirectConfig => m_net_cursor == 1;
    private static bool IPXConfig => m_net_cursor == 2;
    private static bool TCPIPConfig => m_net_cursor == 3;

    /*
    ================
    M_DrawCharacter
    ================
    */
    public static void DrawCharacter(int cx, int line, int num)
        => Draw.Character(cx, line, num); // Menu canvas is centred (Canvas.cs), so no (vid.width - 320) >> 1 offset

    // M_Print -- NUL-terminated byte string (menu.c's char* argument).
    public static void Print(int cx, int cy, byte* str)
    {
        while (*str != 0)
        {
            DrawCharacter(cx, cy, *str + 128);
            str++;
            cx += 8;
        }
    }

    public static void Print(int cx, int cy, ReadOnlySpan<byte> u8Str)
    {
        foreach (byte ch in u8Str)
        {
            DrawCharacter(cx, cy, ch + 128);
            cx += 8;
        }
    }

    public static void PrintWhite(int cx, int cy, [InterpolatedStringHandlerArgument] scoped Utf8StringHandler u8String)
    {
        PrintWhite(cx, cy, u8String.GetWrittenBuffer());
    }

    public static void PrintWhite(int cx, int cy, scoped ReadOnlySpan<byte> u8Str)
    {
        foreach (byte ch in u8Str)
        {
            DrawCharacter(cx, cy, ch);
            cx += 8;
        }
    }

    // M_PrintWhite -- NUL-terminated byte string.
    public static void PrintWhite(int cx, int cy, byte* str)
    {
        while (*str != 0)
        {
            DrawCharacter(cx, cy, *str);
            str++;
            cx += 8;
        }
    }

    // M_Print(cx, cy, va("...%i...", n)) without allocating: $"COM{n}".
    public static void Print(int cx, int cy, [InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler text)
        => Print(cx, cy, text.GetWrittenBuffer());

    public static void DrawTransPic(int x, int y, QPic_t* pic)
    {
        if (pic == null)
        {
            return;
        }

        Draw.TransPic(x, y, pic);
    }

    public static void DrawPic(int x, int y, QPic_t* pic)
        => Draw.Pic(x, y, pic);

    public static readonly byte* identityTable = NativeMemory.AllocArrayZeroed<byte>(256);
    public static readonly byte* translationTable = NativeMemory.AllocArrayZeroed<byte>(256);


    public static void BuildTranslationTable(int top, int bottom)
    {
        for (int j = 0; j < 256; j++)
        {
            identityTable[j] = (byte)j;
        }

        new Span<byte>(identityTable, 256).CopyTo(new Span<byte>(translationTable, 256));

        if (top < 128)
        {
            new Span<byte>(identityTable + top, 16).CopyTo(new Span<byte>(translationTable + RMain.TOP_RANGE, 16));
        }
        else
        {
            for (int j = 0; j < 16; j++)
            {
                translationTable[RMain.TOP_RANGE + j] = identityTable[top + 15 - j];
            }
        }

        if (bottom < 128)
        {
            new Span<byte>(identityTable + bottom, 16).CopyTo(new Span<byte>(translationTable + RMain.BOTTOM_RANGE, 16));
        }
        else
        {
            for (int j = 0; j < 16; j++)
            {
                translationTable[RMain.BOTTOM_RANGE + j] = identityTable[bottom + 15 - j];
            }
        }
    }

    public static void DrawTransPicTranslate(int x, int y, QPic_t* pic)
    {
        Draw.TransPicTranslate(x, y, pic, translationTable);
    }

    public static void DrawTextBox(int x, int y, int width, int lines)
    {
        int cx = x, cy = y;
        QPic_t* p = Draw.CachePic("gfx/box_tl.lmp"u8);
        DrawTransPic(cx, cy, p);
        p = Draw.CachePic("gfx/box_ml.lmp"u8);
        for (int n = 0; n < lines; n++)
        {
            cy += 8;
            DrawTransPic(cx, cy, p);
        }
        p = Draw.CachePic("gfx/box_bl.lmp"u8);
        DrawTransPic(cx, cy + 8, p);

        cx += 8;
        while (width > 0)
        {
            cy = y;
            p = Draw.CachePic("gfx/box_tm.lmp"u8);
            DrawTransPic(cx, cy, p);
            p = Draw.CachePic("gfx/box_mm.lmp"u8);
            for (int n = 0; n < lines; n++)
            {
                cy += 8;
                if (n == 1)
                {
                    p = Draw.CachePic("gfx/box_mm2.lmp"u8);
                }

                DrawTransPic(cx, cy, p);
            }
            p = Draw.CachePic("gfx/box_bm.lmp"u8);
            DrawTransPic(cx, cy + 8, p);
            width -= 2;
            cx += 16;
        }

        cy = y;
        p = Draw.CachePic("gfx/box_tr.lmp"u8);
        DrawTransPic(cx, cy, p);
        p = Draw.CachePic("gfx/box_mr.lmp"u8);
        for (int n = 0; n < lines; n++)
        {
            cy += 8;
            DrawTransPic(cx, cy, p);
        }
        p = Draw.CachePic("gfx/box_br.lmp"u8);
        DrawTransPic(cx, cy + 8, p);
    }

    //=============================================================================

    private static int m_save_demonum;

    /*
    ================
    M_ToggleMenu_f
    ================
    */
    public static void ToggleMenu_f()
    {
        m_entersound = true;

        if (Keys.key_dest == KeyDest.Menu)
        {
            if (m_state != MenuState.Main)
            {
                Menu_Main_f();
                return;
            }
            Keys.key_dest = KeyDest.Game;
            m_state = MenuState.None;
            return;
        }
        if (Keys.key_dest == KeyDest.Console)
        {
            Con.ToggleConsole_f();
        }
        else
        {
            Menu_Main_f();
        }
    }

    //=============================================================================
    /* MAIN MENU */

    internal static int m_main_cursor;
    private const int MAIN_ITEMS = 5;

    public static void Menu_Main_f()
    {
        if (Keys.key_dest != KeyDest.Menu)
        {
            m_save_demonum = ClDemo.demonum;
            ClDemo.demonum = -1;
        }
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Main;
        m_entersound = true;
    }

    private static void Main_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/ttl_main.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);
        DrawTransPic(72, 32, Draw.CachePic("gfx/mainmenu.lmp"u8));

        int f = (int)(Host.host_time * 10) % 6;

        DrawTransPic(54, 32 + (m_main_cursor * 20), Draw.CachePic($"gfx/menudot{f + 1}.lmp"));
    }

    private static void Main_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Keys.key_dest = KeyDest.Game;
                m_state = MenuState.None;
                ClDemo.demonum = m_save_demonum;
                if (ClDemo.demonum != -1 && !ClDemo.demoplayback && Cls.state != CActive.Connected)
                {
                    ClDemo.NextDemo();
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                if (++m_main_cursor >= MAIN_ITEMS)
                {
                    m_main_cursor = 0;
                }

                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                if (--m_main_cursor < 0)
                {
                    m_main_cursor = MAIN_ITEMS - 1;
                }

                break;

            case Keys.K_ENTER:
                m_entersound = true;

                switch (m_main_cursor)
                {
                    case 0: Menu_SinglePlayer_f(); break;
                    case 1: Menu_MultiPlayer_f(); break;
                    case 2: Menu_Options_f(); break;
                    case 3: Menu_Help_f(); break;
                    case 4: Menu_Quit_f(); break;
                }
                break;
        }
    }

    //=============================================================================
    /* SINGLE PLAYER MENU */

    internal static int m_singleplayer_cursor;
    private const int SINGLEPLAYER_ITEMS = 3;

    public static void Menu_SinglePlayer_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.SinglePlayer;
        m_entersound = true;
    }

    private static void SinglePlayer_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/ttl_sgl.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);
        DrawTransPic(72, 32, Draw.CachePic("gfx/sp_menu.lmp"u8));

        int f = (int)(Host.host_time * 10) % 6;

        DrawTransPic(54, 32 + (m_singleplayer_cursor * 20), Draw.CachePic($"gfx/menudot{f + 1}.lmp"));
    }

    private static void SinglePlayer_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_Main_f();
                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                if (++m_singleplayer_cursor >= SINGLEPLAYER_ITEMS)
                {
                    m_singleplayer_cursor = 0;
                }

                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                if (--m_singleplayer_cursor < 0)
                {
                    m_singleplayer_cursor = SINGLEPLAYER_ITEMS - 1;
                }

                break;

            case Keys.K_ENTER:
                m_entersound = true;

                switch (m_singleplayer_cursor)
                {
                    case 0:
                        if (Sv.active)
                        {
                            if (!Screen.ModalMessage("Are you sure you want to\nstart a new game?\n"u8))
                            {
                                break;
                            }
                        }

                        Keys.key_dest = KeyDest.Game;
                        if (Sv.active)
                        {
                            Cmd.AddText("disconnect\n"u8);
                        }

                        Cmd.AddText("maxplayers 1\n"u8);
                        Cmd.AddText("map start\n"u8);
                        break;

                    case 1: Menu_Load_f(); break;
                    case 2: Menu_Save_f(); break;
                }
                break;
        }
    }

    //=============================================================================
    /* LOAD/SAVE MENU */

    private static int load_cursor;
    internal const int MAX_SAVEGAMES = 12;
    // char m_filenames[MAX_SAVEGAMES][SAVEGAME_COMMENT_LENGTH + 1]
    internal const int M_FILENAME_SIZE = SAVEGAME_COMMENT_LENGTH + 1;
    internal static readonly byte* m_filenames = NativeMemory.AllocArrayZeroed<byte>(MAX_SAVEGAMES * M_FILENAME_SIZE);
    internal static readonly bool* loadable = NativeMemory.AllocArrayZeroed<bool>(MAX_SAVEGAMES);

    internal static void ScanSaves()
    {
        for (int i = 0; i < MAX_SAVEGAMES; i++)
        {
            byte* slot = m_filenames + (i * M_FILENAME_SIZE);
            Q.StrCpy(slot, "--- UNUSED SLOT ---"u8);
            loadable[i] = false;

            // The .NET file API takes a managed path (rule 8).
            string name = $"{CStr.ToString(Com.com_gamedir)}/s{i}.sav";
            if (!File.Exists(name))
            {
                continue;
            }

            try
            {
                using StreamReader reader = new(name);
                reader.ReadLine(); // version
                string? comment = reader.ReadLine();
                if (comment == null)
                {
                    continue;
                }

                CStr.QStrncpy(slot, comment, SAVEGAME_COMMENT_LENGTH);
                slot[SAVEGAME_COMMENT_LENGTH] = 0;
                for (int j = 0; j < SAVEGAME_COMMENT_LENGTH; j++)
                {
                    if (slot[j] == (byte)'_')
                    {
                        slot[j] = (byte)' ';
                    }
                }

                loadable[i] = true;
            }
            catch (IOException)
            {
                // leave slot marked unused, matching the original's silent
                // fopen-failure handling
            }
        }
    }

    public static void Menu_Load_f()
    {
        m_entersound = true;
        m_state = MenuState.Load;
        Keys.key_dest = KeyDest.Menu;
        ScanSaves();
    }

    public static void Menu_Save_f()
    {
        if (!Sv.active)
        {
            return;
        }

        if (Cl.intermission != 0)
        {
            return;
        }

        if (Svs.maxclients != 1)
        {
            return;
        }

        m_entersound = true;
        m_state = MenuState.Save;
        Keys.key_dest = KeyDest.Menu;
        ScanSaves();
    }

    private static void Load_Draw()
    {
        QPic_t* p = Draw.CachePic("gfx/p_load.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        for (int i = 0; i < MAX_SAVEGAMES; i++)
        {
            Print(16, 32 + (8 * i), m_filenames + (i * M_FILENAME_SIZE));
        }

        DrawCharacter(8, 32 + (load_cursor * 8), 12 + ((int)(Host.realtime * 4) & 1));
    }

    private static void Save_Draw()
    {
        QPic_t* p = Draw.CachePic("gfx/p_save.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        for (int i = 0; i < MAX_SAVEGAMES; i++)
        {
            Print(16, 32 + (8 * i), m_filenames + (i * M_FILENAME_SIZE));
        }

        DrawCharacter(8, 32 + (load_cursor * 8), 12 + ((int)(Host.realtime * 4) & 1));
    }

    private static void Load_Key(int k)
    {
        switch (k)
        {
            case Keys.K_ESCAPE:
                Menu_SinglePlayer_f();
                break;

            case Keys.K_ENTER:
                SndDma.LocalSound("misc/menu2.wav"u8);
                if (!loadable[load_cursor])
                {
                    return;
                }

                m_state = MenuState.None;
                Keys.key_dest = KeyDest.Game;

                Screen.BeginLoadingPlaque();
                Cmd.AddText($"load s{load_cursor}\n");
                return;

            case Keys.K_UPARROW:
            case Keys.K_LEFTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                load_cursor--;
                if (load_cursor < 0)
                {
                    load_cursor = MAX_SAVEGAMES - 1;
                }

                break;

            case Keys.K_DOWNARROW:
            case Keys.K_RIGHTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                load_cursor++;
                if (load_cursor >= MAX_SAVEGAMES)
                {
                    load_cursor = 0;
                }

                break;
        }
    }

    private static void Save_Key(int k)
    {
        switch (k)
        {
            case Keys.K_ESCAPE:
                Menu_SinglePlayer_f();
                break;

            case Keys.K_ENTER:
                m_state = MenuState.None;
                Keys.key_dest = KeyDest.Game;
                Cmd.AddText($"save s{load_cursor}\n");
                return;

            case Keys.K_UPARROW:
            case Keys.K_LEFTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                load_cursor--;
                if (load_cursor < 0)
                {
                    load_cursor = MAX_SAVEGAMES - 1;
                }

                break;

            case Keys.K_DOWNARROW:
            case Keys.K_RIGHTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                load_cursor++;
                if (load_cursor >= MAX_SAVEGAMES)
                {
                    load_cursor = 0;
                }

                break;
        }
    }

    //=============================================================================
    /* MULTIPLAYER MENU */

    private static int m_multiplayer_cursor;
    private const int MULTIPLAYER_ITEMS = 3;

    public static void Menu_MultiPlayer_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.MultiPlayer;
        m_entersound = true;
    }

    private static void MultiPlayer_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);
        DrawTransPic(72, 32, Draw.CachePic("gfx/mp_menu.lmp"u8));

        int f = (int)(Host.host_time * 10) % 6;
        DrawTransPic(54, 32 + (m_multiplayer_cursor * 20), Draw.CachePic($"gfx/menudot{f + 1}.lmp"));

        if (serialAvailable || ipxAvailable || tcpipAvailable)
        {
            return;
        }

        PrintWhite((320 / 2) - ((27 * 8) / 2), 148, "No Communications Available"u8);
    }

    private static void MultiPlayer_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_Main_f();
                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                if (++m_multiplayer_cursor >= MULTIPLAYER_ITEMS)
                {
                    m_multiplayer_cursor = 0;
                }

                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                if (--m_multiplayer_cursor < 0)
                {
                    m_multiplayer_cursor = MULTIPLAYER_ITEMS - 1;
                }

                break;

            case Keys.K_ENTER:
                m_entersound = true;
                switch (m_multiplayer_cursor)
                {
                    case 0:
                    case 1:
                        if (serialAvailable || ipxAvailable || tcpipAvailable)
                        {
                            Menu_Net_f();
                        }

                        break;
                    case 2:
                        Menu_Setup_f();
                        break;
                }
                break;
        }
    }

    //=============================================================================
    /* SETUP MENU */

    internal static int setup_cursor = 4;
    private static readonly int[] setup_cursor_table = [40, 56, 80, 104, 140];

    internal static readonly byte* setup_hostname = AllocField(16);
    internal static readonly byte* setup_myname = AllocField(16);
    internal static int setup_oldtop, setup_oldbottom, setup_top, setup_bottom;

    private const int NUM_SETUP_CMDS = 5;

    public static void Menu_Setup_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Setup;
        m_entersound = true;
        Q.StrCpy(setup_myname, Cl.cl_name->String);
        Q.StrCpy(setup_hostname, Net.hostname->String);
        setup_top = setup_oldtop = ((int)Cl.cl_color->Value) >> 4;
        setup_bottom = setup_oldbottom = ((int)Cl.cl_color->Value) & 15;
    }

    private static void Setup_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        Print(64, 40, "Hostname"u8);
        DrawTextBox(160, 32, 16, 1);
        Print(168, 40, setup_hostname);

        Print(64, 56, "Your name"u8);
        DrawTextBox(160, 48, 16, 1);
        Print(168, 56, setup_myname);

        Print(64, 80, "Shirt color"u8);
        Print(64, 104, "Pants color"u8);

        DrawTextBox(64, 140 - 8, 14, 1);
        Print(72, 140, "Accept Changes"u8);

        p = Draw.CachePic("gfx/bigbox.lmp"u8);
        DrawTransPic(160, 64, p);
        p = Draw.CachePic("gfx/menuplyr.lmp"u8);
        BuildTranslationTable(setup_top * 16, setup_bottom * 16);
        DrawTransPicTranslate(172, 72, p);

        DrawCharacter(56, setup_cursor_table[setup_cursor], 12 + ((int)(Host.realtime * 4) & 1));

        if (setup_cursor == 0)
        {
            DrawCharacter(168 + (8 * Q.StrLen(setup_hostname)), setup_cursor_table[setup_cursor], 10 + ((int)(Host.realtime * 4) & 1));
        }

        if (setup_cursor == 1)
        {
            DrawCharacter(168 + (8 * Q.StrLen(setup_myname)), setup_cursor_table[setup_cursor], 10 + ((int)(Host.realtime * 4) & 1));
        }
    }

    private static void Setup_Key(int k)
    {
        switch (k)
        {
            case Keys.K_ESCAPE:
                Menu_MultiPlayer_f();
                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                setup_cursor--;
                if (setup_cursor < 0)
                {
                    setup_cursor = NUM_SETUP_CMDS - 1;
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                setup_cursor++;
                if (setup_cursor >= NUM_SETUP_CMDS)
                {
                    setup_cursor = 0;
                }

                break;

            case Keys.K_LEFTARROW:
                if (setup_cursor < 2)
                {
                    return;
                }

                SndDma.LocalSound("misc/menu3.wav"u8);
                if (setup_cursor == 2)
                {
                    setup_top--;
                }

                if (setup_cursor == 3)
                {
                    setup_bottom--;
                }

                break;

            case Keys.K_RIGHTARROW:
                if (setup_cursor < 2)
                {
                    return;
                }

                SetupForward();
                break;

            case Keys.K_ENTER:
                if (setup_cursor == 0 || setup_cursor == 1)
                {
                    return;
                }

                if (setup_cursor == 2 || setup_cursor == 3)
                {
                    SetupForward();
                    break;
                }

                // setup_cursor == 4 (OK)
                if (Q.StrCmp(setup_myname, Cl.cl_name->String) != 0)
                {
                    Cmd.AddText($"name \"{setup_myname}\"\n");
                }

                if (Q.StrCmp(setup_hostname, Net.hostname->String) != 0)
                {
                    Cvar.Set("hostname"u8, setup_hostname);
                }

                if (setup_top != setup_oldtop || setup_bottom != setup_oldbottom)
                {
                    Cmd.AddText($"color {setup_top} {setup_bottom}\n");
                }

                m_entersound = true;
                Menu_MultiPlayer_f();
                break;

            case Keys.K_BACKSPACE:
                if (setup_cursor == 0)
                {
                    StringHelper.FieldBackspace(setup_hostname);
                }

                if (setup_cursor == 1)
                {
                    StringHelper.FieldBackspace(setup_myname);
                }

                break;

            default:
                if (k < 32 || k > 127)
                {
                    break;
                }

                if (setup_cursor == 0)
                {
                    StringHelper.FieldAppend(setup_hostname, 15, k);
                }

                if (setup_cursor == 1)
                {
                    StringHelper.FieldAppend(setup_myname, 15, k);
                }

                break;
        }

        if (setup_top > 13)
        {
            setup_top = 0;
        }

        if (setup_top < 0)
        {
            setup_top = 13;
        }

        if (setup_bottom > 13)
        {
            setup_bottom = 0;
        }

        if (setup_bottom < 0)
        {
            setup_bottom = 13;
        }
    }

    private static void SetupForward()
    {
        SndDma.LocalSound("misc/menu3.wav"u8);
        if (setup_cursor == 2)
        {
            setup_top++;
        }

        if (setup_cursor == 3)
        {
            setup_bottom++;
        }
    }

    //=============================================================================
    /* NET MENU */

    private static int m_net_cursor;
    private static int m_net_items;

    private static readonly Utf8String[] net_helpMessage =
    {
        "                        "u8,
        " Two computers connected"u8,
        "   through two modems.  "u8,
        "                        "u8,
        "                        "u8,
        " Two computers connected"u8,
        " by a null-modem cable. "u8,
        "                        "u8,
        " Novell network LANs    "u8,
        " or Windows 95 DOS-box. "u8,
        "                        "u8,
        "(LAN=Local Area Network)"u8,
        " Commonly used to play  "u8,
        " over the Internet, but "u8,
        " also used on a Local   "u8,
        " Area Network.          "u8,
    };

    public static void Menu_Net_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Net;
        m_entersound = true;
        m_net_items = 4;

        if (m_net_cursor >= m_net_items)
        {
            m_net_cursor = 0;
        }

        m_net_cursor--;
        Net_Key(Keys.K_DOWNARROW);
    }

    private static void Net_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        int f = 32;
        p = serialAvailable ? Draw.CachePic("gfx/netmen1.lmp"u8) : null;
        if (p != null)
        {
            DrawTransPic(72, f, p);
        }

        f += 19;
        p = serialAvailable ? Draw.CachePic("gfx/netmen2.lmp"u8) : null;
        if (p != null)
        {
            DrawTransPic(72, f, p);
        }

        f += 19;
        p = ipxAvailable ? Draw.CachePic("gfx/netmen3.lmp"u8) : Draw.CachePic("gfx/dim_ipx.lmp"u8);
        DrawTransPic(72, f, p);

        f += 19;
        p = tcpipAvailable ? Draw.CachePic("gfx/netmen4.lmp"u8) : Draw.CachePic("gfx/dim_tcp.lmp"u8);
        DrawTransPic(72, f, p);

        f = (320 - (26 * 8)) / 2;
        DrawTextBox(f, 134, 24, 4);
        f += 8;
        Print(f, 142, net_helpMessage[(m_net_cursor * 4) + 0]);
        Print(f, 150, net_helpMessage[(m_net_cursor * 4) + 1]);
        Print(f, 158, net_helpMessage[(m_net_cursor * 4) + 2]);
        Print(f, 166, net_helpMessage[(m_net_cursor * 4) + 3]);

        int dotf = (int)(Host.host_time * 10) % 6;
        DrawTransPic(54, 32 + (m_net_cursor * 20), Draw.CachePic($"gfx/menudot{dotf + 1}.lmp"));
    }

    private static void Net_Key(int k)
    {
        while (true)
        {
            switch (k)
            {
                case Keys.K_ESCAPE:
                    Menu_MultiPlayer_f();
                    break;

                case Keys.K_DOWNARROW:
                    SndDma.LocalSound("misc/menu1.wav"u8);
                    if (++m_net_cursor >= m_net_items)
                    {
                        m_net_cursor = 0;
                    }

                    break;

                case Keys.K_UPARROW:
                    SndDma.LocalSound("misc/menu1.wav"u8);
                    if (--m_net_cursor < 0)
                    {
                        m_net_cursor = m_net_items - 1;
                    }

                    break;

                case Keys.K_ENTER:
                    m_entersound = true;
                    switch (m_net_cursor)
                    {
                        case 0:
                        case 1: Menu_SerialConfig_f(); break;
                        case 2:
                        case 3: Menu_LanConfig_f(); break;
                    }
                    break;
            }

            if (m_net_cursor == 0 && !serialAvailable) { k = Keys.K_DOWNARROW; continue; }
            if (m_net_cursor == 1 && !serialAvailable) { k = Keys.K_DOWNARROW; continue; }
            if (m_net_cursor == 2 && !ipxAvailable) { k = Keys.K_DOWNARROW; continue; }
            if (m_net_cursor == 3 && !tcpipAvailable) { k = Keys.K_DOWNARROW; continue; }
            break;
        }
    }

    //=============================================================================
    /* OPTIONS MENU */

    private const int OPTIONS_ITEMS = 13;
    private const int SLIDER_RANGE = 10;

    private static int options_cursor;

    public static void Menu_Options_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Options;
        m_entersound = true;
    }

    private static void AdjustSliders(int dir)
    {
        SndDma.LocalSound("misc/menu3.wav"u8);

        switch (options_cursor)
        {
            case 3: // screen size
                Screen.scr_viewsize->Value += dir * 10;
                if (Screen.scr_viewsize->Value < 30)
                {
                    Screen.scr_viewsize->Value = 30;
                }

                if (Screen.scr_viewsize->Value > 120)
                {
                    Screen.scr_viewsize->Value = 120;
                }

                Cvar.SetValue("viewsize"u8, Screen.scr_viewsize->Value);
                break;
            case 4: // gamma
                View.v_gamma->Value -= dir * 0.05f;
                if (View.v_gamma->Value < 0.5f)
                {
                    View.v_gamma->Value = 0.5f;
                }

                if (View.v_gamma->Value > 1)
                {
                    View.v_gamma->Value = 1;
                }

                Cvar.SetValue("gamma"u8, View.v_gamma->Value);
                break;
            case 5: // mouse speed
                ClInput.sensitivity->Value += dir * 0.5f;
                if (ClInput.sensitivity->Value < 1)
                {
                    ClInput.sensitivity->Value = 1;
                }

                if (ClInput.sensitivity->Value > 11)
                {
                    ClInput.sensitivity->Value = 11;
                }

                Cvar.SetValue("sensitivity"u8, ClInput.sensitivity->Value);
                break;
            case 6: // music volume
                // Original steps by dir*1.0 under _WIN32 (dir*0.1 on other builds);
                // WinQuake always built with _WIN32 defined.
                SndDma.bgmvolume->Value += dir * 1.0f;
                if (SndDma.bgmvolume->Value < 0)
                {
                    SndDma.bgmvolume->Value = 0;
                }

                if (SndDma.bgmvolume->Value > 1)
                {
                    SndDma.bgmvolume->Value = 1;
                }

                Cvar.SetValue("bgmvolume"u8, SndDma.bgmvolume->Value);
                break;
            case 7: // sfx volume
                SndDma.volume->Value += dir * 0.1f;
                if (SndDma.volume->Value < 0)
                {
                    SndDma.volume->Value = 0;
                }

                if (SndDma.volume->Value > 1)
                {
                    SndDma.volume->Value = 1;
                }

                Cvar.SetValue("volume"u8, SndDma.volume->Value);
                break;
            case 8: // always run
                if (View.cl_forwardspeed->Value > 200)
                {
                    Cvar.SetValue("cl_forwardspeed"u8, 200);
                    Cvar.SetValue("cl_backspeed"u8, 200);
                }
                else
                {
                    Cvar.SetValue("cl_forwardspeed"u8, 400);
                    Cvar.SetValue("cl_backspeed"u8, 400);
                }
                break;
            case 9: // invert mouse
                Cvar.SetValue("m_pitch"u8, -ClInput.m_pitch->Value);
                break;
            case 10: // lookspring
                Cvar.SetValue("lookspring"u8, ClInput.lookspring->Value != 0 ? 0 : 1);
                break;
            case 11: // lookstrafe
                Cvar.SetValue("lookstrafe"u8, ClInput.lookstrafe->Value != 0 ? 0 : 1);
                break;
        }
    }

    private static void DrawSlider(int x, int y, float range)
    {
        if (range < 0)
        {
            range = 0;
        }

        if (range > 1)
        {
            range = 1;
        }

        DrawCharacter(x - 8, y, 128);
        int i;
        for (i = 0; i < SLIDER_RANGE; i++)
        {
            DrawCharacter(x + (i * 8), y, 129);
        }

        DrawCharacter(x + (i * 8), y, 130);
        DrawCharacter((int)(x + ((SLIDER_RANGE - 1) * 8 * range)), y, 131);
    }

    private static void DrawCheckbox(int x, int y, bool on)
        => Print(x, y, on ? "on"u8 : "off"u8);

    private static void Options_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_option.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        Print(16, 32, "    Customize controls"u8);
        Print(16, 40, "         Go to console"u8);
        Print(16, 48, "     Reset to defaults"u8);

        Print(16, 56, "           Screen size"u8);
        DrawSlider(220, 56, (Screen.scr_viewsize->Value - 30) / (120 - 30));

        Print(16, 64, "            Brightness"u8);
        DrawSlider(220, 64, (1.0f - View.v_gamma->Value) / 0.5f);

        Print(16, 72, "           Mouse Speed"u8);
        DrawSlider(220, 72, (ClInput.sensitivity->Value - 1) / 10);

        Print(16, 80, "       CD Music Volume"u8);
        DrawSlider(220, 80, SndDma.bgmvolume->Value);

        Print(16, 88, "          Sound Volume"u8);
        DrawSlider(220, 88, SndDma.volume->Value);

        Print(16, 96, "            Always Run"u8);
        DrawCheckbox(220, 96, View.cl_forwardspeed->Value > 200);

        Print(16, 104, "          Invert Mouse"u8);
        DrawCheckbox(220, 104, ClInput.m_pitch->Value < 0);

        Print(16, 112, "            Lookspring"u8);
        DrawCheckbox(220, 112, ClInput.lookspring->Value != 0);

        Print(16, 120, "            Lookstrafe"u8);
        DrawCheckbox(220, 120, ClInput.lookstrafe->Value != 0);

        if (vid_menudrawfn != null)
        {
            Print(16, 128, "         Video Options"u8);
        }

        DrawCharacter(200, 32 + (options_cursor * 8), 12 + ((int)(Host.realtime * 4) & 1));
    }

    private static void Options_Key(int k)
    {
        switch (k)
        {
            case Keys.K_ESCAPE:
                Menu_Main_f();
                break;

            case Keys.K_ENTER:
                m_entersound = true;
                switch (options_cursor)
                {
                    case 0: Menu_Keys_f(); break;
                    case 1: m_state = MenuState.None; Con.ToggleConsole_f(); break;
                    case 2: Cmd.AddText("exec default.cfg\n"u8); break;
                    case 12: Menu_Video_f(); break;
                    default: AdjustSliders(1); break;
                }
                return;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                options_cursor--;
                if (options_cursor < 0)
                {
                    options_cursor = OPTIONS_ITEMS - 1;
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                options_cursor++;
                if (options_cursor >= OPTIONS_ITEMS)
                {
                    options_cursor = 0;
                }

                break;

            case Keys.K_LEFTARROW:
                AdjustSliders(-1);
                break;

            case Keys.K_RIGHTARROW:
                AdjustSliders(1);
                break;
        }

        if (options_cursor == 12 && vid_menudrawfn == null)
        {
            options_cursor = k == Keys.K_UPARROW ? 11 : 0;
        }
    }

    //=============================================================================
    /* KEYS MENU */

    // bindnames[][2] (menu.c): {command, description}, both NUL-terminated literals.
    private static readonly (Utf8String Command, Utf8String Description)[] bindnames =
    [
        new("+attack"u8, "attack"u8),
        new("impulse 10"u8, "change weapon"u8),
        new("+jump"u8, "jump / swim up"u8),
        new("+forward"u8, "walk forward"u8),
        new("+back"u8, "backpedal"u8),
        new("+left"u8, "turn left"u8),
        new("+right"u8, "turn right"u8),
        new("+speed"u8, "run"u8),
        new("+moveleft"u8, "step left"u8),
        new("+moveright"u8, "step right"u8),
        new("+strafe"u8, "sidestep"u8),
        new("+lookup"u8, "look up"u8),
        new("+lookdown"u8, "look down"u8),
        new("centerview"u8, "center view"u8),
        new("+mlook"u8, "mouse look"u8),
        new("+klook"u8, "keyboard look"u8),
        new("+moveup"u8, "swim up"u8),
        new("+movedown"u8, "swim down"u8),
    ];

    private static int keys_cursor;
    private static bool bind_grab;

    public static void Menu_Keys_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Keys;
        m_entersound = true;
    }

    // M_FindKeysForCommand
    private static void FindKeysForCommand(byte* command, int* twokeys)
    {
        twokeys[0] = twokeys[1] = -1;
        int l = Q.StrLen(command);
        int count = 0;

        for (int j = 0; j < 256 && count < 2; j++)
        {
            byte* b = Keys.keybindings[j];
            if (b == null)
            {
                continue;
            }

            if (Q.StrNCmp(b, command, l) == 0)
            {
                twokeys[count++] = j;
            }
        }
    }

    // M_UnbindCommand
    private static void UnbindCommand(byte* command)
    {
        int l = Q.StrLen(command);
        for (int j = 0; j < 256; j++)
        {
            byte* b = Keys.keybindings[j];
            if (b == null)
            {
                continue;
            }

            if (Q.StrNCmp(b, command, l) == 0)
            {
                Keys.SetBinding(j, Utf8String.Empty);
            }
        }
    }

    private static void Keys_Draw()
    {
        QPic_t* p = Draw.CachePic("gfx/ttl_cstm.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        if (bind_grab)
        {
            Print(12, 32, "Press a key or button for this action"u8);
        }
        else
        {
            Print(18, 32, "Enter to change, backspace to clear"u8);
        }

        int* keys = stackalloc int[2];
        for (int i = 0; i < bindnames.Length; i++)
        {
            int y = 48 + (8 * i);
            Print(16, y, bindnames[i].Description);

            FindKeysForCommand(bindnames[i].Command, keys);

            if (keys[0] == -1)
            {
                Print(140, y, "???"u8);
            }
            else
            {
                byte* name = Keys.KeynumToString(keys[0]);
                Print(140, y, name);
                int x = Q.StrLen(name) * 8;
                if (keys[1] != -1)
                {
                    Print(140 + x + 8, y, "or"u8);
                    Print(140 + x + 32, y, Keys.KeynumToString(keys[1]));
                }
            }
        }

        if (bind_grab)
        {
            DrawCharacter(130, 48 + (keys_cursor * 8), '=');
        }
        else
        {
            DrawCharacter(130, 48 + (keys_cursor * 8), 12 + ((int)(Host.realtime * 4) & 1));
        }
    }

    private static void Keys_Key(int k)
    {
        if (bind_grab)
        {
            SndDma.LocalSound("misc/menu1.wav"u8);
            if (k == Keys.K_ESCAPE)
            {
                bind_grab = false;
            }
            else if (k != '`')
            {
                Cmd.InsertText($"bind \"{Keys.KeynumToString(k)}\" \"{bindnames[keys_cursor].Command}\"\n");
            }

            bind_grab = false;
            return;
        }

        int* keys = stackalloc int[2];
        switch (k)
        {
            case Keys.K_ESCAPE:
                Menu_Options_f();
                break;

            case Keys.K_LEFTARROW:
            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                keys_cursor--;
                if (keys_cursor < 0)
                {
                    keys_cursor = bindnames.Length - 1;
                }

                break;

            case Keys.K_DOWNARROW:
            case Keys.K_RIGHTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                keys_cursor++;
                if (keys_cursor >= bindnames.Length)
                {
                    keys_cursor = 0;
                }

                break;

            case Keys.K_ENTER: // go into bind mode
                FindKeysForCommand(bindnames[keys_cursor].Command, keys);
                SndDma.LocalSound("misc/menu2.wav"u8);
                if (keys[1] != -1)
                {
                    UnbindCommand(bindnames[keys_cursor].Command);
                }

                bind_grab = true;
                break;

            case Keys.K_BACKSPACE:
            case Keys.K_DEL:
                SndDma.LocalSound("misc/menu2.wav"u8);
                UnbindCommand(bindnames[keys_cursor].Command);
                break;
        }
    }

    //=============================================================================
    /* VIDEO MENU */

    public static void Menu_Video_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Video;
        m_entersound = true;
    }

    private static void Video_Draw()
    {
        if (vid_menudrawfn != null)
        {
            vid_menudrawfn();
        }
    }

    private static void Video_Key(int key)
    {
        if (vid_menukeyfn != null)
        {
            vid_menukeyfn(key);
        }
    }

    //=============================================================================
    /* HELP MENU */

    private static int help_page;
    private const int NUM_HELP_PAGES = 6;

    public static void Menu_Help_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Help;
        m_entersound = true;
        help_page = 0;
    }

    private static void Help_Draw() => DrawPic(0, 0, Draw.CachePic($"gfx/help{help_page}.lmp"));

    private static void Help_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_Main_f();
                break;

            case Keys.K_UPARROW:
            case Keys.K_RIGHTARROW:
                m_entersound = true;
                if (++help_page >= NUM_HELP_PAGES)
                {
                    help_page = 0;
                }

                break;

            case Keys.K_DOWNARROW:
            case Keys.K_LEFTARROW:
                m_entersound = true;
                if (--help_page < 0)
                {
                    help_page = NUM_HELP_PAGES - 1;
                }

                break;
        }
    }

    //=============================================================================
    /* QUIT MENU */

    private static int msgNumber;
    internal static MenuState m_quit_prevstate;
    private static bool wasInMenus;

    // Original's #else (non-_WIN32) quit prompt text, kept for source parity;
    // WinQuake's _WIN32 build (what this port targets) never rendered it.
    private static readonly Utf8String[] quitMessage =
    {
        "  Are you gonna quit    "u8, "  this game just like   "u8, "   everything else?     "u8, "                        "u8,
        " Milord, methinks that  "u8, "   thou art a lowly     "u8, " quitter. Is this true? "u8, "                        "u8,
        " Do I need to bust your "u8, "  face open for trying  "u8, "        to quit?        "u8, "                        "u8,
        " Man, I oughta smack you"u8, "   for trying to quit!  "u8, "     Press Y to get     "u8, "      smacked out.      "u8,
        " Press Y to quit like a "u8, "   big loser in life.   "u8, "  Press N to stay proud "u8, "    and successful!     "u8,
        "   If you press Y to    "u8, "  quit, I will summon   "u8, "  Satan all over your   "u8, "      hard drive!       "u8,
        "  Um, Asmodeus dislikes "u8, " his children trying to "u8, " quit. Press Y to return"u8, "   to your Tinkertoys.  "u8,
        "  If you quit now, I'll "u8, "  throw a blanket-party "u8, "   for you next time!   "u8, "                        "u8,
    };

    public static void Menu_Quit_f()
    {
        if (m_state == MenuState.Quit)
        {
            return;
        }

        wasInMenus = Keys.key_dest == KeyDest.Menu;
        Keys.key_dest = KeyDest.Menu;
        m_quit_prevstate = m_state;
        m_state = MenuState.Quit;
        m_entersound = true;
        msgNumber = rand.Next() & 7;
    }

    private static void Quit_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
            case 'n':
            case 'N':
                if (wasInMenus)
                {
                    m_state = m_quit_prevstate;
                    m_entersound = true;
                }
                else
                {
                    Keys.key_dest = KeyDest.Game;
                    m_state = MenuState.None;
                }
                break;

            case 'Y':
            case 'y':
                Keys.key_dest = KeyDest.Console;
                HostCmd.Quit_f();
                break;
        }
    }

    private static void Quit_Draw()
    {
        if (wasInMenus)
        {
            m_state = m_quit_prevstate;
            m_recursiveDraw = true;
            Draw_();
            m_state = MenuState.Quit;
        }

        // Original used #ifdef _WIN32 for the credits screen and #else for
        // quitMessage[]; WinQuake always built with _WIN32 defined, so this
        // port always shows the credits screen.
        DrawTextBox(0, 0, 38, 23);
        PrintWhite(16, 12, "  Quake version 1.09 by id Software\n\n"u8);
        PrintWhite(16, 28, "Programming        Art \n"u8);
        Print(16, 36, " John Carmack       Adrian Carmack\n"u8);
        Print(16, 44, " Michael Abrash     Kevin Cloud\n"u8);
        Print(16, 52, " John Cash          Paul Steed\n"u8);
        Print(16, 60, " Dave 'Zoid' Kirsch\n"u8);
        PrintWhite(16, 68, "Design             Biz\n"u8);
        Print(16, 76, " John Romero        Jay Wilbur\n"u8);
        Print(16, 84, " Sandy Petersen     Mike Wilson\n"u8);
        Print(16, 92, " American McGee     Donna Jackson\n"u8);
        Print(16, 100, " Tim Willits        Todd Hollenshead\n"u8);
        PrintWhite(16, 108, "Support            Projects\n"u8);
        Print(16, 116, " Barrett Alexander  Shawn Green\n"u8);
        PrintWhite(16, 124, "Sound Effects\n"u8);
        Print(16, 132, " Trent Reznor and Nine Inch Nails\n\n"u8);
        PrintWhite(16, 140, "Quake is a trademark of Id Software,\n"u8);
        PrintWhite(16, 148, "inc., (c)1996 Id Software, inc. All\n"u8);
        PrintWhite(16, 156, "rights reserved. NIN logo is a\n"u8);
        PrintWhite(16, 164, "registered trademark licensed to\n"u8);
        PrintWhite(16, 172, "Nothing Interactive, Inc. All rights\n"u8);
        PrintWhite(16, 180, "reserved. Press y to exit\n"u8);
    }

    //=============================================================================
    /* SERIAL CONFIG MENU */

    private static int serialConfig_cursor;
    private static readonly int[] serialConfig_cursor_table = { 48, 64, 80, 96, 112, 132 };
    private const int NUM_SERIALCONFIG_CMDS = 6;

    private static readonly int[] ISA_uarts = { 0x3f8, 0x2f8, 0x3e8, 0x2e8 };
    private static readonly int[] ISA_IRQs = { 4, 3, 4, 3 };
    private static readonly int[] serialConfig_baudrate = { 9600, 14400, 19200, 28800, 38400, 57600 };

    private static int serialConfig_comport;
    private static int serialConfig_irq;
    private static int serialConfig_baud;
    internal static readonly byte* serialConfig_phone = AllocField(16);

    public static void Menu_SerialConfig_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.SerialConfig;
        m_entersound = true;
        serialConfig_cursor = JoiningGame && SerialConfig ? 4 : 5;

        int port, irq, baud;
        bool useModem;
        int* pPort = &port, pIrq = &irq, pBaud = &baud;
        bool* pUseModem = &useModem;
        GetComPortConfig(0, pPort, pIrq, pBaud, pUseModem);
        serialConfig_irq = irq;

        int n;
        for (n = 0; n < 4; n++)
        {
            if (ISA_uarts[n] == port)
            {
                break;
            }
        }

        if (n == 4)
        {
            n = 0;
            serialConfig_irq = 4;
        }
        serialConfig_comport = n + 1;

        for (n = 0; n < 6; n++)
        {
            if (serialConfig_baudrate[n] == baud)
            {
                break;
            }
        }

        if (n == 6)
        {
            n = 5;
        }

        serialConfig_baud = n;

        m_return_onerror = false;
        m_return_reason[0] = 0;
    }

    private static void SerialConfig_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        int basex = (320 - p->Width) / 2;
        DrawPic(basex, 4, p);

        ReadOnlySpan<byte> startJoin = StartingGame ? "New Game - "u8 : "Join Game - "u8;
        ReadOnlySpan<byte> directModem = SerialConfig ? "Modem"u8 : "Direct Connect"u8;
        Print(basex, 32, $"{startJoin} - {directModem}");
        basex += 8;

        Print(basex, serialConfig_cursor_table[0], "Port"u8);
        DrawTextBox(160, 40, 4, 1);
        Print(168, serialConfig_cursor_table[0], $"COM{serialConfig_comport}");

        Print(basex, serialConfig_cursor_table[1], "IRQ"u8);
        DrawTextBox(160, serialConfig_cursor_table[1] - 8, 1, 1);
        Print(168, serialConfig_cursor_table[1], $"{serialConfig_irq}");

        Print(basex, serialConfig_cursor_table[2], "Baud"u8);
        DrawTextBox(160, serialConfig_cursor_table[2] - 8, 5, 1);
        Print(168, serialConfig_cursor_table[2], $"{serialConfig_baudrate[serialConfig_baud]}");

        if (SerialConfig)
        {
            Print(basex, serialConfig_cursor_table[3], "Modem Setup..."u8);
            if (JoiningGame)
            {
                Print(basex, serialConfig_cursor_table[4], "Phone number"u8);
                DrawTextBox(160, serialConfig_cursor_table[4] - 8, 16, 1);
                Print(168, serialConfig_cursor_table[4], serialConfig_phone);
            }
        }

        if (JoiningGame)
        {
            DrawTextBox(basex, serialConfig_cursor_table[5] - 8, 7, 1);
            Print(basex + 8, serialConfig_cursor_table[5], "Connect"u8);
        }
        else
        {
            DrawTextBox(basex, serialConfig_cursor_table[5] - 8, 2, 1);
            Print(basex + 8, serialConfig_cursor_table[5], "OK"u8);
        }

        DrawCharacter(basex - 8, serialConfig_cursor_table[serialConfig_cursor], 12 + ((int)(Host.realtime * 4) & 1));

        if (serialConfig_cursor == 4)
        {
            DrawCharacter(168 + (8 * Q.StrLen(serialConfig_phone)), serialConfig_cursor_table[serialConfig_cursor], 10 + ((int)(Host.realtime * 4) & 1));
        }

        if (m_return_reason[0] != 0)
        {
            PrintWhite(basex, 148, m_return_reason);
        }
    }

    private static void SerialConfig_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_Net_f();
                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                serialConfig_cursor--;
                if (serialConfig_cursor < 0)
                {
                    serialConfig_cursor = NUM_SERIALCONFIG_CMDS - 1;
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                serialConfig_cursor++;
                if (serialConfig_cursor >= NUM_SERIALCONFIG_CMDS)
                {
                    serialConfig_cursor = 0;
                }

                break;

            case Keys.K_LEFTARROW:
                if (serialConfig_cursor > 2)
                {
                    break;
                }

                SndDma.LocalSound("misc/menu3.wav"u8);
                if (serialConfig_cursor == 0)
                {
                    serialConfig_comport--;
                    if (serialConfig_comport == 0)
                    {
                        serialConfig_comport = 4;
                    }

                    serialConfig_irq = ISA_IRQs[serialConfig_comport - 1];
                }
                if (serialConfig_cursor == 1)
                {
                    serialConfig_irq--;
                    if (serialConfig_irq == 6)
                    {
                        serialConfig_irq = 5;
                    }

                    if (serialConfig_irq == 1)
                    {
                        serialConfig_irq = 7;
                    }
                }
                if (serialConfig_cursor == 2)
                {
                    serialConfig_baud--;
                    if (serialConfig_baud < 0)
                    {
                        serialConfig_baud = 5;
                    }
                }
                break;

            case Keys.K_RIGHTARROW:
                if (serialConfig_cursor > 2)
                {
                    break;
                }

                SerialConfigForward();
                break;

            case Keys.K_ENTER:
                if (serialConfig_cursor < 3)
                {
                    SerialConfigForward();
                    break;
                }

                m_entersound = true;

                if (serialConfig_cursor == 3)
                {
                    SetComPortConfig(0, ISA_uarts[serialConfig_comport - 1], serialConfig_irq, serialConfig_baudrate[serialConfig_baud], SerialConfig);
                    Menu_ModemConfig_f();
                    break;
                }

                if (serialConfig_cursor == 4)
                {
                    serialConfig_cursor = 5;
                    break;
                }

                SetComPortConfig(0, ISA_uarts[serialConfig_comport - 1], serialConfig_irq, serialConfig_baudrate[serialConfig_baud], SerialConfig);
                ConfigureNetSubsystem();

                if (StartingGame)
                {
                    Menu_GameOptions_f();
                    break;
                }

                m_return_state = m_state;
                m_return_onerror = true;
                Keys.key_dest = KeyDest.Game;
                m_state = MenuState.None;

                if (SerialConfig)
                {
                    Cmd.AddText($"connect \"{serialConfig_phone}\"\n");
                }
                else
                {
                    Cmd.AddText("connect\n"u8);
                }
                break;

            case Keys.K_BACKSPACE:
                if (serialConfig_cursor == 4)
                {
                    StringHelper.FieldBackspace(serialConfig_phone);
                }

                break;

            default:
                if (key < 32 || key > 127)
                {
                    break;
                }

                if (serialConfig_cursor == 4)
                {
                    StringHelper.FieldAppend(serialConfig_phone, 15, key);
                }

                break;
        }

        if (DirectConfig && (serialConfig_cursor == 3 || serialConfig_cursor == 4))
        {
            serialConfig_cursor = key == Keys.K_UPARROW ? 2 : 5;
        }

        if (SerialConfig && StartingGame && serialConfig_cursor == 4)
        {
            serialConfig_cursor = key == Keys.K_UPARROW ? 3 : 5;
        }
    }

    private static void SerialConfigForward()
    {
        SndDma.LocalSound("misc/menu3.wav"u8);
        if (serialConfig_cursor == 0)
        {
            serialConfig_comport++;
            if (serialConfig_comport > 4)
            {
                serialConfig_comport = 1;
            }

            serialConfig_irq = ISA_IRQs[serialConfig_comport - 1];
        }
        if (serialConfig_cursor == 1)
        {
            serialConfig_irq++;
            if (serialConfig_irq == 6)
            {
                serialConfig_irq = 7;
            }

            if (serialConfig_irq == 8)
            {
                serialConfig_irq = 2;
            }
        }
        if (serialConfig_cursor == 2)
        {
            serialConfig_baud++;
            if (serialConfig_baud > 5)
            {
                serialConfig_baud = 0;
            }
        }
    }

    //=============================================================================
    /* MODEM CONFIG MENU */

    internal static int modemConfig_cursor;
    private static readonly int[] modemConfig_cursor_table = { 40, 56, 88, 120, 156 };
    private const int NUM_MODEMCONFIG_CMDS = 5;

    private static byte modemConfig_dialing;
    internal static readonly byte* modemConfig_clear = AllocField(16);
    internal static readonly byte* modemConfig_init = AllocField(32);
    internal static readonly byte* modemConfig_hangup = AllocField(16);

    public static void Menu_ModemConfig_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.ModemConfig;
        m_entersound = true;

        byte* dialing = stackalloc byte[2];
        GetModemConfig(0, dialing, modemConfig_clear, modemConfig_init, modemConfig_hangup);
        modemConfig_dialing = dialing[0];
    }

    private static void ModemConfig_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        int basex = (320 - p->Width) / 2;
        DrawPic(basex, 4, p);
        basex += 8;

        Print(basex, modemConfig_cursor_table[0], modemConfig_dialing == (byte)'P' ? "Pulse Dialing"u8 : "Touch Tone Dialing"u8);

        Print(basex, modemConfig_cursor_table[1], "Clear"u8);
        DrawTextBox(basex, modemConfig_cursor_table[1] + 4, 16, 1);
        Print(basex + 8, modemConfig_cursor_table[1] + 12, modemConfig_clear);
        if (modemConfig_cursor == 1)
        {
            DrawCharacter(basex + 8 + (8 * Q.StrLen(modemConfig_clear)), modemConfig_cursor_table[1] + 12, 10 + ((int)(Host.realtime * 4) & 1));
        }

        Print(basex, modemConfig_cursor_table[2], "Init"u8);
        DrawTextBox(basex, modemConfig_cursor_table[2] + 4, 30, 1);
        Print(basex + 8, modemConfig_cursor_table[2] + 12, modemConfig_init);
        if (modemConfig_cursor == 2)
        {
            DrawCharacter(basex + 8 + (8 * Q.StrLen(modemConfig_init)), modemConfig_cursor_table[2] + 12, 10 + ((int)(Host.realtime * 4) & 1));
        }

        Print(basex, modemConfig_cursor_table[3], "Hangup"u8);
        DrawTextBox(basex, modemConfig_cursor_table[3] + 4, 16, 1);
        Print(basex + 8, modemConfig_cursor_table[3] + 12, modemConfig_hangup);
        if (modemConfig_cursor == 3)
        {
            DrawCharacter(basex + 8 + (8 * Q.StrLen(modemConfig_hangup)), modemConfig_cursor_table[3] + 12, 10 + ((int)(Host.realtime * 4) & 1));
        }

        DrawTextBox(basex, modemConfig_cursor_table[4] - 8, 2, 1);
        Print(basex + 8, modemConfig_cursor_table[4], "OK"u8);

        DrawCharacter(basex - 8, modemConfig_cursor_table[modemConfig_cursor], 12 + ((int)(Host.realtime * 4) & 1));
    }

    private static void ModemConfig_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_SerialConfig_f();
                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                modemConfig_cursor--;
                if (modemConfig_cursor < 0)
                {
                    modemConfig_cursor = NUM_MODEMCONFIG_CMDS - 1;
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                modemConfig_cursor++;
                if (modemConfig_cursor >= NUM_MODEMCONFIG_CMDS)
                {
                    modemConfig_cursor = 0;
                }

                break;

            case Keys.K_LEFTARROW:
            case Keys.K_RIGHTARROW:
                if (modemConfig_cursor == 0)
                {
                    modemConfig_dialing = modemConfig_dialing == (byte)'P' ? (byte)'T' : (byte)'P';
                    SndDma.LocalSound("misc/menu1.wav"u8);
                }
                break;

            case Keys.K_ENTER:
                if (modemConfig_cursor == 0)
                {
                    modemConfig_dialing = modemConfig_dialing == (byte)'P' ? (byte)'T' : (byte)'P';
                    m_entersound = true;
                }

                if (modemConfig_cursor == 4)
                {
                    byte* dialing = stackalloc byte[2] { modemConfig_dialing, 0 };
                    SetModemConfig(0, dialing, modemConfig_clear, modemConfig_init, modemConfig_hangup);
                    m_entersound = true;
                    Menu_SerialConfig_f();
                }
                break;

            case Keys.K_BACKSPACE:
                if (modemConfig_cursor == 1)
                {
                    StringHelper.FieldBackspace(modemConfig_clear);
                }

                if (modemConfig_cursor == 2)
                {
                    StringHelper.FieldBackspace(modemConfig_init);
                }

                if (modemConfig_cursor == 3)
                {
                    StringHelper.FieldBackspace(modemConfig_hangup);
                }

                break;

            default:
                if (key < 32 || key > 127)
                {
                    break;
                }

                if (modemConfig_cursor == 1)
                {
                    StringHelper.FieldAppend(modemConfig_clear, 15, key);
                }

                if (modemConfig_cursor == 2)
                {
                    StringHelper.FieldAppend(modemConfig_init, 29, key);
                }

                if (modemConfig_cursor == 3)
                {
                    StringHelper.FieldAppend(modemConfig_hangup, 15, key);
                }

                break;
        }
    }

    //=============================================================================
    /* LAN CONFIG MENU */

    internal static int lanConfig_cursor = -1;
    private static readonly int[] lanConfig_cursor_table = { 72, 92, 124 };
    private const int NUM_LANCONFIG_CMDS = 3;

    internal static int lanConfig_port;
    internal static readonly byte* lanConfig_portname = AllocField(6);
    internal static readonly byte* lanConfig_joinname = AllocField(22);

    public static void Menu_LanConfig_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.LanConfig;
        m_entersound = true;
        if (lanConfig_cursor == -1)
        {
            lanConfig_cursor = JoiningGame && TCPIPConfig ? 2 : 1;
        }

        if (StartingGame && lanConfig_cursor == 2)
        {
            lanConfig_cursor = 1;
        }

        lanConfig_port = Net.DEFAULTnet_hostport;
        CStr.Write(lanConfig_portname, $"{lanConfig_port}");

        m_return_onerror = false;
        m_return_reason[0] = 0;
    }

    private static void LanConfig_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        int basex = (320 - p->Width) / 2;
        DrawPic(basex, 4, p);

        ReadOnlySpan<byte> startJoin = StartingGame ? "New Game - "u8 : "Join Game - "u8;
        ReadOnlySpan<byte> protocol = IPXConfig ? "IPX"u8 : "TCP/IP"u8;
        Print(basex, 32, $"{startJoin} - {protocol}");
        basex += 8;

        Print(basex, 52, "Address:"u8);
        if (IPXConfig)
        {
            Print(basex + (9 * 8), 52, my_ipx_address);
        }
        else
        {
            Print(basex + (9 * 8), 52, NetIp.my_tcpip_address);
        }

        Print(basex, lanConfig_cursor_table[0], "Port"u8);
        DrawTextBox(basex + (8 * 8), lanConfig_cursor_table[0] - 8, 6, 1);
        Print(basex + (9 * 8), lanConfig_cursor_table[0], lanConfig_portname);

        if (JoiningGame)
        {
            Print(basex, lanConfig_cursor_table[1], "Search for local games..."u8);
            Print(basex, 108, "Join game at:"u8);
            DrawTextBox(basex + 8, lanConfig_cursor_table[2] - 8, 22, 1);
            Print(basex + 16, lanConfig_cursor_table[2], lanConfig_joinname);
        }
        else
        {
            DrawTextBox(basex, lanConfig_cursor_table[1] - 8, 2, 1);
            Print(basex + 8, lanConfig_cursor_table[1], "OK"u8);
        }

        DrawCharacter(basex - 8, lanConfig_cursor_table[lanConfig_cursor], 12 + ((int)(Host.realtime * 4) & 1));

        if (lanConfig_cursor == 0)
        {
            DrawCharacter(basex + (9 * 8) + (8 * Q.StrLen(lanConfig_portname)), lanConfig_cursor_table[0], 10 + ((int)(Host.realtime * 4) & 1));
        }

        if (lanConfig_cursor == 2)
        {
            DrawCharacter(basex + 16 + (8 * Q.StrLen(lanConfig_joinname)), lanConfig_cursor_table[2], 10 + ((int)(Host.realtime * 4) & 1));
        }
    }

    private static void LanConfig_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_Net_f();
                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                lanConfig_cursor--;
                if (lanConfig_cursor < 0)
                {
                    lanConfig_cursor = NUM_LANCONFIG_CMDS - 1;
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                lanConfig_cursor++;
                if (lanConfig_cursor >= NUM_LANCONFIG_CMDS)
                {
                    lanConfig_cursor = 0;
                }

                break;

            case Keys.K_ENTER:
                if (lanConfig_cursor == 0)
                {
                    break;
                }

                m_entersound = true;
                ConfigureNetSubsystem();

                if (lanConfig_cursor == 1)
                {
                    if (StartingGame)
                    {
                        Menu_GameOptions_f();
                        break;
                    }
                    Menu_Search_f();
                    break;
                }

                if (lanConfig_cursor == 2)
                {
                    m_return_state = m_state;
                    m_return_onerror = true;
                    Keys.key_dest = KeyDest.Game;
                    m_state = MenuState.None;
                    Cmd.AddText($"connect \"{lanConfig_joinname}\"\n");
                    break;
                }
                break;

            case Keys.K_BACKSPACE:
                if (lanConfig_cursor == 0)
                {
                    StringHelper.FieldBackspace(lanConfig_portname);
                }

                if (lanConfig_cursor == 2)
                {
                    StringHelper.FieldBackspace(lanConfig_joinname);
                }

                break;

            default:
                if (key >= 32 && key <= 127 && lanConfig_cursor == 2)
                {
                    StringHelper.FieldAppend(lanConfig_joinname, 21, key);
                }

                if (key >= '0' && key <= '9' && lanConfig_cursor == 0)
                {
                    StringHelper.FieldAppend(lanConfig_portname, 5, key);
                }

                break;
        }

        if (StartingGame && lanConfig_cursor == 2)
        {
            lanConfig_cursor = key == Keys.K_UPARROW ? 1 : 0;
        }

        int l = Q.Atoi(lanConfig_portname);
        if (l <= 65535)
        {
            lanConfig_port = l;
        }

        CStr.Write(lanConfig_portname, $"{lanConfig_port}");
    }

    //=============================================================================
    /* GAME OPTIONS MENU */

    // level_t/episode_t (menu.c): name/description are NUL-terminated literals.
    internal readonly struct Level_t
    {
        public readonly Utf8String Name;
        public readonly Utf8String Description;
        internal Level_t(ReadOnlySpan<byte> name, ReadOnlySpan<byte> description)
        {
            Name = name;
            Description = description;
        }
    }

    internal readonly struct Episode_t
    {
        public readonly Utf8String Description;
        public readonly int FirstLevel;
        public readonly int Levels;
        internal Episode_t(ReadOnlySpan<byte> description, int firstLevel, int levels)
        {
            Description = description;
            FirstLevel = firstLevel;
            Levels = levels;
        }
    }

    private static readonly Level_t[] levels =
    {
        new("start"u8, "Entrance"u8),
        new("e1m1"u8, "Slipgate Complex"u8), new("e1m2"u8, "Castle of the Damned"u8), new("e1m3"u8, "The Necropolis"u8),
        new("e1m4"u8, "The Grisly Grotto"u8), new("e1m5"u8, "Gloom Keep"u8), new("e1m6"u8, "The Door To Chthon"u8),
        new("e1m7"u8, "The House of Chthon"u8), new("e1m8"u8, "Ziggurat Vertigo"u8),
        new("e2m1"u8, "The Installation"u8), new("e2m2"u8, "Ogre Citadel"u8), new("e2m3"u8, "Crypt of Decay"u8),
        new("e2m4"u8, "The Ebon Fortress"u8), new("e2m5"u8, "The Wizard's Manse"u8), new("e2m6"u8, "The Dismal Oubliette"u8),
        new("e2m7"u8, "Underearth"u8),
        new("e3m1"u8, "Termination Central"u8), new("e3m2"u8, "The Vaults of Zin"u8), new("e3m3"u8, "The Tomb of Terror"u8),
        new("e3m4"u8, "Satan's Dark Delight"u8), new("e3m5"u8, "Wind Tunnels"u8), new("e3m6"u8, "Chambers of Torment"u8),
        new("e3m7"u8, "The Haunted Halls"u8),
        new("e4m1"u8, "The Sewage System"u8), new("e4m2"u8, "The Tower of Despair"u8), new("e4m3"u8, "The Elder God Shrine"u8),
        new("e4m4"u8, "The Palace of Hate"u8), new("e4m5"u8, "Hell's Atrium"u8), new("e4m6"u8, "The Pain Maze"u8),
        new("e4m7"u8, "Azure Agony"u8), new("e4m8"u8, "The Nameless City"u8),
        new("end"u8, "Shub-Niggurath's Pit"u8),
        new("dm1"u8, "Place of Two Deaths"u8), new("dm2"u8, "Claustrophobopolis"u8), new("dm3"u8, "The Abandoned Base"u8),
        new("dm4"u8, "The Bad Place"u8), new("dm5"u8, "The Cistern"u8), new("dm6"u8, "The Dark Zone"u8),
    };

    private static readonly Episode_t[] episodes =
    {
        new("Welcome to Quake"u8, 0, 1),
        new("Doomed Dimension"u8, 1, 8),
        new("Realm of Black Magic"u8, 9, 7),
        new("Netherworld"u8, 16, 7),
        new("The Elder World"u8, 23, 8),
        new("Final Level"u8, 31, 1),
        new("Deathmatch Arena"u8, 32, 6),
    };

    // MED 01/06/97 added hipnotic levels
    internal static readonly Level_t[] hipnoticlevels =
    {
        new("start"u8, "Command HQ"u8),
        new("hip1m1"u8, "The Pumping Station"u8), new("hip1m2"u8, "Storage Facility"u8),
        new("hip1m3"u8, "The Lost Mine"u8), new("hip1m4"u8, "Research Facility"u8), new("hip1m5"u8, "Military Complex"u8),
        new("hip2m1"u8, "Ancient Realms"u8), new("hip2m2"u8, "The Black Cathedral"u8), new("hip2m3"u8, "The Catacombs"u8),
        new("hip2m4"u8, "The Crypt"u8), new("hip2m5"u8, "Mortum's Keep"u8), new("hip2m6"u8, "The Gremlin's Domain"u8),
        new("hip3m1"u8, "Tur Torment"u8), new("hip3m2"u8, "Pandemonium"u8), new("hip3m3"u8, "Limbo"u8),
        new("hip3m4"u8, "The Gauntlet"u8),
        new("hipend"u8, "Armagon's Lair"u8),
        new("hipdm1"u8, "The Edge of Oblivion"u8),
    };

    // PGM 01/07/97 added rogue levels
    // PGM 03/02/97 added dmatch level
    internal static readonly Level_t[] roguelevels =
    {
        new("start"u8, "Split Decision"u8),
        new("r1m1"u8, "Deviant's Domain"u8), new("r1m2"u8, "Dread Portal"u8), new("r1m3"u8, "Judgement Call"u8),
        new("r1m4"u8, "Cave of Death"u8), new("r1m5"u8, "Towers of Wrath"u8), new("r1m6"u8, "Temple of Pain"u8),
        new("r1m7"u8, "Tomb of the Overlord"u8),
        new("r2m1"u8, "Tempus Fugit"u8), new("r2m2"u8, "Elemental Fury I"u8), new("r2m3"u8, "Elemental Fury II"u8),
        new("r2m4"u8, "Curse of Osiris"u8), new("r2m5"u8, "Wizard's Keep"u8), new("r2m6"u8, "Blood Sacrifice"u8),
        new("r2m7"u8, "Last Bastion"u8), new("r2m8"u8, "Source of Evil"u8),
        new("ctf1"u8, "Division of Change"u8),
    };

    // MED 01/06/97 added hipnotic episodes
    internal static readonly Episode_t[] hipnoticepisodes =
    {
        new("Scourge of Armagon"u8, 0, 1),
        new("Fortress of the Dead"u8, 1, 5),
        new("Dominion of Darkness"u8, 6, 6),
        new("The Rift"u8, 12, 4),
        new("Final Level"u8, 16, 1),
        new("Deathmatch Arena"u8, 17, 1),
    };

    // PGM 01/07/97 added rogue episodes
    // PGM 03/02/97 added dmatch episode
    internal static readonly Episode_t[] rogueepisodes =
    {
        new("Introduction"u8, 0, 1),
        new("Hell's Fortress"u8, 1, 7),
        new("Corridors of Time"u8, 8, 8),
        new("Deathmatch Arena"u8, 16, 1),
    };

    internal static int startepisode;
    internal static int startlevel;
    private static int maxplayers;
    private static bool m_serverInfoMessage;
    private static double m_serverInfoMessageTime;

    public static void Menu_GameOptions_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.GameOptions;
        m_entersound = true;
        if (maxplayers == 0)
        {
            maxplayers = Svs.maxclients;
        }

        if (maxplayers < 2)
        {
            maxplayers = Svs.maxclientslimit;
        }
    }

    private static readonly int[] gameoptions_cursor_table = { 40, 56, 64, 72, 80, 88, 96, 112, 120 };
    private const int NUM_GAMEOPTIONS = 9;
    internal static int gameoptions_cursor;

    private static void GameOptions_Draw()
    {
        DrawTransPic(16, 4, Draw.CachePic("gfx/qplaque.lmp"u8));
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);

        DrawTextBox(152, 32, 10, 1);
        Print(160, 40, "begin game"u8);

        Print(0, 56, "      Max players"u8);
        Print(160, 56, $"{maxplayers}");

        Print(0, 64, "        Game Type"u8);
        Print(160, 64, Host.coop->Value != 0 ? "Cooperative"u8 : "Deathmatch"u8);

        Print(0, 72, "        Teamplay"u8);
        Print(160, 72, Com.rogue
            ? ((int)Host.teamplay->Value) switch
            {
                1 => "No Friendly Fire"u8,
                2 => "Friendly Fire"u8,
                3 => "Tag"u8,
                4 => "Capture the Flag"u8,
                5 => "One Flag CTF"u8,
                6 => "Three Team CTF"u8,
                _ => "Off"u8,
            }
            : ((int)Host.teamplay->Value) switch { 1 => "No Friendly Fire"u8, 2 => "Friendly Fire"u8, _ => "Off"u8 });

        Print(0, 80, "            Skill"u8);
        Print(160, 80, ((int)Host.skill->Value) switch { 0 => "Easy difficulty"u8, 1 => "Normal difficulty"u8, 2 => "Hard difficulty"u8, _ => "Nightmare difficulty"u8 });

        Print(0, 88, "       Frag Limit"u8);
        if (Host.fraglimit->Value == 0)
        {
            Print(160, 88, "none"u8);
        }
        else
        {
            Print(160, 88, $"{(int)Host.fraglimit->Value} frags");
        }

        Print(0, 96, "       Time Limit"u8);
        if (Host.timelimit->Value == 0)
        {
            Print(160, 96, "none"u8);
        }
        else
        {
            Print(160, 96, $"{(int)Host.timelimit->Value} minutes");
        }

        Print(0, 112, "         Episode"u8);
        // MED 01/06/97 added hipnotic episodes
        if (Com.hipnotic)
        {
            Print(160, 112, hipnoticepisodes[startepisode].Description);
        }
        // PGM 01/07/97 added rogue episodes
        else if (Com.rogue)
        {
            Print(160, 112, rogueepisodes[startepisode].Description);
        }
        else
        {
            Print(160, 112, episodes[startepisode].Description);
        }

        Print(0, 120, "           Level"u8);
        // MED 01/06/97 added hipnotic episodes
        if (Com.hipnotic)
        {
            Print(160, 120, hipnoticlevels[hipnoticepisodes[startepisode].FirstLevel + startlevel].Description);
            Print(160, 128, hipnoticlevels[hipnoticepisodes[startepisode].FirstLevel + startlevel].Name);
        }
        // PGM 01/07/97 added rogue episodes
        else if (Com.rogue)
        {
            Print(160, 120, roguelevels[rogueepisodes[startepisode].FirstLevel + startlevel].Description);
            Print(160, 128, roguelevels[rogueepisodes[startepisode].FirstLevel + startlevel].Name);
        }
        else
        {
            Print(160, 120, levels[episodes[startepisode].FirstLevel + startlevel].Description);
            Print(160, 128, levels[episodes[startepisode].FirstLevel + startlevel].Name);
        }

        DrawCharacter(144, gameoptions_cursor_table[gameoptions_cursor], 12 + ((int)(Host.realtime * 4) & 1));

        if (m_serverInfoMessage)
        {
            if ((Host.realtime - m_serverInfoMessageTime) < 5.0)
            {
                int x = (320 - (26 * 8)) / 2;
                DrawTextBox(x, 138, 24, 4);
                x += 8;
                Print(x, 146, "  More than 4 players   "u8);
                Print(x, 154, " requires using command "u8);
                Print(x, 162, "line parameters; please "u8);
                Print(x, 170, "   see techinfo.txt.    "u8);
            }
            else
            {
                m_serverInfoMessage = false;
            }
        }
    }

    internal static void NetStart_Change(int dir)
    {
        switch (gameoptions_cursor)
        {
            case 1:
                maxplayers += dir;
                if (maxplayers > Svs.maxclientslimit)
                {
                    maxplayers = Svs.maxclientslimit;
                    m_serverInfoMessage = true;
                    m_serverInfoMessageTime = Host.realtime;
                }
                if (maxplayers < 2)
                {
                    maxplayers = 2;
                }

                break;

            case 2:
                Cvar.SetValue("coop"u8, Host.coop->Value != 0 ? 0 : 1);
                break;

            case 3:
                {
                    int count = Com.rogue ? 6 : 2;
                    Cvar.SetValue("teamplay"u8, Host.teamplay->Value + dir);
                    if (Host.teamplay->Value > count)
                    {
                        Cvar.SetValue("teamplay"u8, 0);
                    }
                    else if (Host.teamplay->Value < 0)
                    {
                        Cvar.SetValue("teamplay"u8, count);
                    }

                    break;
                }

            case 4:
                Cvar.SetValue("skill"u8, Host.skill->Value + dir);
                if (Host.skill->Value > 3)
                {
                    Cvar.SetValue("skill"u8, 0);
                }

                if (Host.skill->Value < 0)
                {
                    Cvar.SetValue("skill"u8, 3);
                }

                break;

            case 5:
                Cvar.SetValue("fraglimit"u8, Host.fraglimit->Value + (dir * 10));
                if (Host.fraglimit->Value > 100)
                {
                    Cvar.SetValue("fraglimit"u8, 0);
                }

                if (Host.fraglimit->Value < 0)
                {
                    Cvar.SetValue("fraglimit"u8, 100);
                }

                break;

            case 6:
                Cvar.SetValue("timelimit"u8, Host.timelimit->Value + (dir * 5));
                if (Host.timelimit->Value > 60)
                {
                    Cvar.SetValue("timelimit"u8, 0);
                }

                if (Host.timelimit->Value < 0)
                {
                    Cvar.SetValue("timelimit"u8, 60);
                }

                break;

            case 7:
                {
                    startepisode += dir;
                    // MED 01/06/97 added hipnotic count
                    // PGM 01/07/97 added rogue count
                    // PGM 03/02/97 added 1 for dmatch episode
                    int count = Com.hipnotic ? 6 : Com.rogue ? 4 : Com.registered_cvar->Value != 0 ? 7 : 2;
                    if (startepisode < 0)
                    {
                        startepisode = count - 1;
                    }

                    if (startepisode >= count)
                    {
                        startepisode = 0;
                    }

                    startlevel = 0;
                    break;
                }

            case 8:
                {
                    startlevel += dir;
                    // MED 01/06/97 added hipnotic episodes
                    // PGM 01/06/97 added hipnotic episodes
                    int count = Com.hipnotic ? hipnoticepisodes[startepisode].Levels
                        : Com.rogue ? rogueepisodes[startepisode].Levels
                        : episodes[startepisode].Levels;
                    if (startlevel < 0)
                    {
                        startlevel = count - 1;
                    }

                    if (startlevel >= count)
                    {
                        startlevel = 0;
                    }

                    break;
                }
        }
    }

    internal static void GameOptions_Key(int key)
    {
        switch (key)
        {
            case Keys.K_ESCAPE:
                Menu_Net_f();
                break;

            case Keys.K_UPARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                gameoptions_cursor--;
                if (gameoptions_cursor < 0)
                {
                    gameoptions_cursor = NUM_GAMEOPTIONS - 1;
                }

                break;

            case Keys.K_DOWNARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                gameoptions_cursor++;
                if (gameoptions_cursor >= NUM_GAMEOPTIONS)
                {
                    gameoptions_cursor = 0;
                }

                break;

            case Keys.K_LEFTARROW:
                if (gameoptions_cursor == 0)
                {
                    break;
                }

                SndDma.LocalSound("misc/menu3.wav"u8);
                NetStart_Change(-1);
                break;

            case Keys.K_RIGHTARROW:
                if (gameoptions_cursor == 0)
                {
                    break;
                }

                SndDma.LocalSound("misc/menu3.wav"u8);
                NetStart_Change(1);
                break;

            case Keys.K_ENTER:
                SndDma.LocalSound("misc/menu2.wav"u8);
                if (gameoptions_cursor == 0)
                {
                    if (Sv.active)
                    {
                        Cmd.AddText("disconnect\n"u8);
                    }

                    Cmd.AddText("listen 0\n"u8);
                    Cmd.AddText($"maxplayers {maxplayers}\n");
                    Screen.BeginLoadingPlaque();

                    if (Com.hipnotic)
                    {
                        Cmd.AddText($"map {hipnoticlevels[hipnoticepisodes[startepisode].FirstLevel + startlevel].Name}\n");
                    }
                    else if (Com.rogue)
                    {
                        Cmd.AddText($"map {roguelevels[rogueepisodes[startepisode].FirstLevel + startlevel].Name}\n");
                    }
                    else
                    {
                        Cmd.AddText($"map {levels[episodes[startepisode].FirstLevel + startlevel].Name}\n");
                    }

                    return;
                }

                NetStart_Change(1);
                break;
        }
    }

    //=============================================================================
    /* SEARCH MENU */

    private static bool searchComplete;
    private static double searchCompleteTime;

    public static void Menu_Search_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.Search;
        m_entersound = false;
        Net.slistSilent = true;
        Net.slistLocal = false;
        searchComplete = false;
        Net.Slist_f();
    }

    private static void Search_Draw()
    {
        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);
        int x = (320 / 2) - ((12 * 8) / 2) + 4;
        DrawTextBox(x - 8, 32, 12, 1);
        Print(x, 40, "Searching..."u8);

        if (Net.slistInProgress)
        {
            Net.Poll();
            return;
        }

        if (!searchComplete)
        {
            searchComplete = true;
            searchCompleteTime = Host.realtime;
        }

        if (Net.hostCacheCount != 0)
        {
            Menu_ServerList_f();
            return;
        }

        PrintWhite((320 / 2) - ((22 * 8) / 2), 64, "No Quake servers found"u8);
        if ((Host.realtime - searchCompleteTime) < 3.0)
        {
            return;
        }

        Menu_LanConfig_f();
    }

    private static void Search_Key(int key)
    {
    }

    //=============================================================================
    /* SLIST MENU */

    private static int slist_cursor;
    internal static bool slist_sorted;

    public static void Menu_ServerList_f()
    {
        Keys.key_dest = KeyDest.Menu;
        m_state = MenuState.SList;
        m_entersound = true;
        slist_cursor = 0;
        m_return_onerror = false;
        m_return_reason[0] = 0;
        slist_sorted = false;
    }

    private static void ServerList_Draw()
    {
        if (!slist_sorted)
        {
            if (Net.hostCacheCount > 1)
            {
                HostCache_t* hc = Net.hostcache;
                for (int i = 0; i < Net.hostCacheCount; i++)
                {
                    for (int j = i + 1; j < Net.hostCacheCount; j++)
                    {
                        if (Q.StrCmp(hc[j].Name, hc[i].Name) < 0)
                        {
                            HostCache_t temp = hc[j];
                            hc[j] = hc[i];
                            hc[i] = temp;
                        }
                    }
                }
            }
            slist_sorted = true;
        }

        QPic_t* p = Draw.CachePic("gfx/p_multi.lmp"u8);
        DrawPic((320 - p->Width) / 2, 4, p);
        for (int n = 0; n < Net.hostCacheCount; n++)
        {
            Print(16, 32 + (8 * n), Net.FormatSlistEntry(n));
        }

        DrawCharacter(0, 32 + (slist_cursor * 8), 12 + ((int)(Host.realtime * 4) & 1));

        if (*m_return_reason != 0)
        {
            PrintWhite(16, 148, m_return_reason);
        }
    }

    internal static void ServerList_Key(int k)
    {
        switch (k)
        {
            case Keys.K_ESCAPE:
                Menu_LanConfig_f();
                break;

            case Keys.K_SPACE:
                Menu_Search_f();
                break;

            case Keys.K_UPARROW:
            case Keys.K_LEFTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                slist_cursor--;
                if (slist_cursor < 0)
                {
                    slist_cursor = Net.hostCacheCount - 1;
                }

                break;

            case Keys.K_DOWNARROW:
            case Keys.K_RIGHTARROW:
                SndDma.LocalSound("misc/menu1.wav"u8);
                slist_cursor++;
                if (slist_cursor >= Net.hostCacheCount)
                {
                    slist_cursor = 0;
                }

                break;

            case Keys.K_ENTER:
                SndDma.LocalSound("misc/menu2.wav"u8);
                m_return_state = m_state;
                m_return_onerror = true;
                slist_sorted = false;
                Keys.key_dest = KeyDest.Game;
                m_state = MenuState.None;
                Cmd.AddText($"connect \"{Net.hostcache[slist_cursor].CName}\"\n");
                break;
        }
    }

    //=============================================================================
    /* Menu Subsystem */

    private static void ToggleMenuCmd() => ToggleMenu_f();
    private static void MenuMainCmd() => Menu_Main_f();
    private static void MenuSingleplayerCmd() => Menu_SinglePlayer_f();
    private static void MenuLoadCmd() => Menu_Load_f();
    private static void MenuSaveCmd() => Menu_Save_f();
    private static void MenuMultiplayerCmd() => Menu_MultiPlayer_f();
    private static void MenuSetupCmd() => Menu_Setup_f();
    private static void MenuOptionsCmd() => Menu_Options_f();
    private static void MenuKeysCmd() => Menu_Keys_f();
    private static void MenuVideoCmd() => Menu_Video_f();
    private static void HelpCmd() => Menu_Help_f();
    private static void MenuQuitCmd() => Menu_Quit_f();

    public static void Init()
    {
        Cmd.AddCommand("togglemenu"u8, &ToggleMenuCmd);

        Cmd.AddCommand("menu_main"u8, &MenuMainCmd);
        Cmd.AddCommand("menu_singleplayer"u8, &MenuSingleplayerCmd);
        Cmd.AddCommand("menu_load"u8, &MenuLoadCmd);
        Cmd.AddCommand("menu_save"u8, &MenuSaveCmd);
        Cmd.AddCommand("menu_multiplayer"u8, &MenuMultiplayerCmd);
        Cmd.AddCommand("menu_setup"u8, &MenuSetupCmd);
        Cmd.AddCommand("menu_options"u8, &MenuOptionsCmd);
        Cmd.AddCommand("menu_keys"u8, &MenuKeysCmd);
        Cmd.AddCommand("menu_video"u8, &MenuVideoCmd);
        Cmd.AddCommand("help"u8, &HelpCmd);
        Cmd.AddCommand("menu_quit"u8, &MenuQuitCmd);
    }

    public static void Draw_()
    {
        if (m_state == MenuState.None || Keys.key_dest != KeyDest.Menu)
        {
            return;
        }

        if (!m_recursiveDraw)
        {
            Screen.scr_copyeverything = 1;

            if (Screen.scr_con_current != 0)
            {
                Canvas.Set(CanvasType.Console);
                Draw.ConsoleBackground((int)Vid.vid.ConHeight);
            }
            else
            {
                Draw.FadeScreen();
            }

            Screen.scr_fullupdate = 0;
        }
        else
        {
            m_recursiveDraw = false;
        }

        Canvas.Set(CanvasType.Menu);

        switch (m_state)
        {
            case MenuState.Main: Main_Draw(); break;
            case MenuState.SinglePlayer: SinglePlayer_Draw(); break;
            case MenuState.Load: Load_Draw(); break;
            case MenuState.Save: Save_Draw(); break;
            case MenuState.MultiPlayer: MultiPlayer_Draw(); break;
            case MenuState.Setup: Setup_Draw(); break;
            case MenuState.Net: Net_Draw(); break;
            case MenuState.Options: Options_Draw(); break;
            case MenuState.Keys: Keys_Draw(); break;
            case MenuState.Video: Video_Draw(); break;
            case MenuState.Help: Help_Draw(); break;
            case MenuState.Quit: Quit_Draw(); break;
            case MenuState.SerialConfig: SerialConfig_Draw(); break;
            case MenuState.ModemConfig: ModemConfig_Draw(); break;
            case MenuState.LanConfig: LanConfig_Draw(); break;
            case MenuState.GameOptions: GameOptions_Draw(); break;
            case MenuState.Search: Search_Draw(); break;
            case MenuState.SList: ServerList_Draw(); break;
        }

        if (m_entersound)
        {
            SndDma.LocalSound("misc/menu2.wav"u8);
            m_entersound = false;
        }

        // VID_UnlockBuffer/S_ExtraUpdate/VID_LockBuffer -- dropped (same
        // status as RMain.cs's EdgeDrawing, which drops the same triad:
        // both display layer and sound mixer are ported, this call just
        // isn't needed by OpenTK/OpenAL's model).
    }

    public static void Keydown(int key)
    {
        switch (m_state)
        {
            case MenuState.None: return;
            case MenuState.Main: Main_Key(key); return;
            case MenuState.SinglePlayer: SinglePlayer_Key(key); return;
            case MenuState.Load: Load_Key(key); return;
            case MenuState.Save: Save_Key(key); return;
            case MenuState.MultiPlayer: MultiPlayer_Key(key); return;
            case MenuState.Setup: Setup_Key(key); return;
            case MenuState.Net: Net_Key(key); return;
            case MenuState.Options: Options_Key(key); return;
            case MenuState.Keys: Keys_Key(key); return;
            case MenuState.Video: Video_Key(key); return;
            case MenuState.Help: Help_Key(key); return;
            case MenuState.Quit: Quit_Key(key); return;
            case MenuState.SerialConfig: SerialConfig_Key(key); return;
            case MenuState.ModemConfig: ModemConfig_Key(key); return;
            case MenuState.LanConfig: LanConfig_Key(key); return;
            case MenuState.GameOptions: GameOptions_Key(key); return;
            case MenuState.Search: Search_Key(key); break;
            case MenuState.SList: ServerList_Key(key); return;
        }
    }

    public static void ConfigureNetSubsystem()
    {
        Cmd.AddText("stopdemo\n"u8);
        if (SerialConfig || DirectConfig)
        {
            Cmd.AddText("com1 enable\n"u8);
        }

        if (IPXConfig || TCPIPConfig)
        {
            Net.net_hostport = lanConfig_port;
        }
    }
}
