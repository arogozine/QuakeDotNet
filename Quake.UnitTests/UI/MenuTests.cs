// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class MenuTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        // SinglePlayerKey_Enter_Cursor0.../Setup_Key's OK-cursor test call
        // Cmd.AddText, which writes into cmd_text -- a permanent static
        // buffer that must be (re)pointed at this test's own arena, or it
        // dangles into whatever arena a previous Cmd.BufInit() caller's
        // Teardown already freed.
        Cmd.BufInit();

        // Menu key handlers call S_LocalSound (SndDma.LocalSound) directly;
        // with the sound system not started it returns immediately, exactly
        // like the original's own !sound_started guard, regardless of what
        // other test classes did to it.
        SndDma.sound_started = false;

        Menu.m_state = (MenuState)0;
        Menu.m_main_cursor = 0;
        Menu.m_singleplayer_cursor = 0;
        Menu.setup_cursor = 4;
        *Menu.setup_myname = 0;
        *Menu.setup_hostname = 0;
        Menu.setup_top = Menu.setup_oldtop = 0;
        Menu.setup_bottom = Menu.setup_oldbottom = 0;
        Menu.m_quit_prevstate = 0;
        Keys.key_dest = KeyDest.Game;
        ClDemo.demoplayback = false;
        Cls.state = 0;

        // Net.hostname/Cl.cl_name/Cl.cl_color's String pointers may have been
        // repointed at a Zone-allocated string by some other test class's
        // Cvar.Set call, into memory that class's own Teardown already
        // freed -- CvarString() in Menu_Setup_f would then dereference a
        // dangling pointer. Resetting Net.hostname to an unregistered,
        // null-String cvar, and cl_name/cl_color back to their field-
        // initializer values (Utf8String points at u8 literal static data, never
        // freed, so this is always safe) undoes that regardless of run order.
        *Net.hostname = new() { Name = new Utf8String("_hostname"u8), String = new Utf8String(""u8) };
        *Cl.cl_name = new() { Name = new Utf8String("_cl_name"u8), String = new Utf8String("player"u8) };
        *Cl.cl_color = new() { Name = new Utf8String("_cl_color"u8), String = new Utf8String("0"u8) };
    }

    [TestCleanup]
    public void Teardown()
    {
        Com.rogue = false;
        Com.hipnotic = false;
        // Restore Cls.state to its field-initializer default -- other test
        // classes (e.g. NetTests, PrCmdsTests) branch on Cls.state ==
        // CActive.Dedicated and would otherwise inherit this class's CActive.Dedicated
        // override for the rest of the run.
        Cls.state = CActive.Disconnected;
        NativeMemory.Free(_buf);
    }

    [TestMethod]
    public void Init_RegistersExpectedCommands()
    {
        Menu.Init();

        Assert.IsTrue(Cmd.Exists(Ansi("togglemenu")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_main")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_singleplayer")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_load")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_save")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_multiplayer")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_setup")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_options")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_keys")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_video")));
        Assert.IsTrue(Cmd.Exists(Ansi("help")));
        Assert.IsTrue(Cmd.Exists(Ansi("menu_quit")));
    }

    [TestMethod]
    public void NetStartChange_Hipnotic_EpisodeCursor_WrapsAtHipnoticEpisodeCount()
    {
        Com.hipnotic = true;
        Menu.gameoptions_cursor = 7; // episode selector
        Menu.startepisode = 5;

        Menu.NetStart_Change(1); // -> 6, hipnoticepisodes.Length == 6, wraps to 0

        Assert.AreEqual(0, Menu.startepisode);
        Assert.AreEqual(0, Menu.startlevel);
    }

    [TestMethod]
    public void NetStartChange_Rogue_EpisodeCursor_WrapsAtRogueEpisodeCount()
    {
        Com.rogue = true;
        Menu.gameoptions_cursor = 7;
        Menu.startepisode = 3;

        Menu.NetStart_Change(1); // -> 4, rogueepisodes.Length == 4, wraps to 0

        Assert.AreEqual(0, Menu.startepisode);
    }

    [TestMethod]
    public void NetStartChange_Rogue_LevelCursor_WrapsAtRogueLevelCount()
    {
        Com.rogue = true;
        Menu.gameoptions_cursor = 8; // level selector
        Menu.startepisode = 0; // rogueepisodes[0] == ("Introduction", 0, 1)
        Menu.startlevel = 0;

        Menu.NetStart_Change(1); // -> 1, count == 1, wraps to 0

        Assert.AreEqual(0, Menu.startlevel);
    }

    [TestMethod]
    public void ToggleMenu_FromConsole_OpensMainMenu()
    {
        Menu.Init();
        Keys.key_dest = KeyDest.Game;

        Menu.ToggleMenu_f();

        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);
    }

    [TestMethod]
    public void ToggleMenu_FromMainMenu_ReturnsToGame()
    {
        Menu.Init();
        Menu.Menu_Main_f();
        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);

        Menu.ToggleMenu_f();

        Assert.AreEqual(KeyDest.Game, Keys.key_dest);
    }

    [TestMethod]
    public void ConnectFailed_WhenArmed_SwitchesBackToMenuWithReason()
    {
        Keys.key_dest = KeyDest.Game;
        Menu.m_state = (MenuState)99; // some menu currently showing
        Menu.m_return_state = (MenuState)7; // menu screen to switch back to
        Menu.m_return_onerror = true;

        Menu.ConnectFailed(Ansi("No Response"));

        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);
        Assert.AreEqual((MenuState)7, Menu.m_state);
        Assert.IsFalse(Menu.m_return_onerror);
        Assert.AreEqual("No Response", Marshal.PtrToStringAnsi((nint)System.Runtime.CompilerServices.Unsafe.AsPointer(ref Menu.m_return_reason[0])));
    }

    [TestMethod]
    public void ConnectFailed_WhenNotArmed_DoesNotTouchKeyDestOrMenuState()
    {
        Keys.key_dest = KeyDest.Game;
        Menu.m_state = (MenuState)99;
        Menu.m_return_onerror = false;

        Menu.ConnectFailed(Ansi("Network Error"));

        Assert.AreEqual(KeyDest.Game, Keys.key_dest);
        Assert.AreEqual((MenuState)99, Menu.m_state);
    }

    [TestMethod]
    public void ConnectFailed_NullReason_StillSwitchesBackWhenArmed()
    {
        Keys.key_dest = KeyDest.Game;
        Menu.m_return_state = (MenuState)3;
        Menu.m_return_onerror = true;

        Menu.ConnectFailed(null);

        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);
        Assert.AreEqual((MenuState)3, Menu.m_state);
        Assert.IsFalse(Menu.m_return_onerror);
    }

    [TestMethod]
    public void ConnectSucceeded_ClearsOnErrorFlag()
    {
        Menu.m_return_onerror = true;

        Menu.ConnectSucceeded();

        Assert.IsFalse(Menu.m_return_onerror);
    }

    [TestMethod]
    public void KeynumToString_PrintableAscii_ReturnsSingleChar()
    {
        Assert.AreEqual("a", CStr.ToString(Keys.KeynumToString('a')));
    }

    [TestMethod]
    public void KeynumToString_NamedKey_ReturnsName()
    {
        Assert.AreEqual("ENTER", CStr.ToString(Keys.KeynumToString(Keys.K_ENTER)));
    }

    [TestMethod]
    public void SetBinding_ThenLookup_RoundTrips()
    {
        byte* cmd = Ansi("+jump");
        Keys.SetBinding(Keys.K_SPACE, cmd);

        Assert.IsTrue(Keys.keybindings[Keys.K_SPACE] != null);
        Assert.AreEqual("+jump", Marshal.PtrToStringAnsi((nint)Keys.keybindings[Keys.K_SPACE]));
    }

    [TestMethod]
    public void MenuMainF_SetsKeyDestMenuStateAndEntersound()
    {
        Keys.key_dest = KeyDest.Game;
        ClDemo.demonum = 5;

        Menu.Menu_Main_f();

        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);
        Assert.AreEqual((MenuState)1, Menu.m_state); // MenuState.Main
        Assert.IsTrue(Menu.m_entersound);
    }

    [TestMethod]
    public void MainKey_DownArrow_WrapsCursorAtMainItems()
    {
        Menu.m_state = (MenuState)1; // MenuState.Main
        Menu.m_main_cursor = 4; // MAIN_ITEMS - 1

        Menu.Keydown(Keys.K_DOWNARROW);

        Assert.AreEqual(0, Menu.m_main_cursor);
    }

    [TestMethod]
    public void MainKey_UpArrow_WrapsCursorAtZero()
    {
        Menu.m_state = (MenuState)1; // MenuState.Main
        Menu.m_main_cursor = 0;

        Menu.Keydown(Keys.K_UPARROW);

        Assert.AreEqual(4, Menu.m_main_cursor); // MAIN_ITEMS - 1
    }

    [TestMethod]
    public void MainKey_Escape_ReturnsToGameAndRestoresDemonum()
    {
        Keys.key_dest = KeyDest.Game;
        // Connected, so M_Main_Key's escape doesn't call CL_NextDemo (which
        // would restart the demo loop and move demonum on).
        Cls.state = CActive.Connected;
        ClDemo.demonum = 7;
        Menu.Menu_Main_f(); // captures m_save_demonum == 7, sets demonum = -1

        Menu.Keydown(Keys.K_ESCAPE);

        Assert.AreEqual(KeyDest.Game, Keys.key_dest);
        Assert.AreEqual((MenuState)0, Menu.m_state); // MenuState.None
        Assert.AreEqual(7, ClDemo.demonum);
    }

    [TestMethod]
    public void MainKey_Enter_DispatchesToSinglePlayerAtCursor0()
    {
        Menu.m_state = (MenuState)1; // MenuState.Main
        Menu.m_main_cursor = 0;

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual((MenuState)2, Menu.m_state); // MenuState.SinglePlayer
    }

    [TestMethod]
    public void MainKey_Enter_DispatchesToMultiPlayerAtCursor1()
    {
        Menu.m_state = (MenuState)1; // MenuState.Main
        Menu.m_main_cursor = 1;

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual((MenuState)5, Menu.m_state); // MenuState.MultiPlayer
    }

    [TestMethod]
    public void MainKey_Enter_DispatchesToHelpAtCursor3()
    {
        Menu.m_state = (MenuState)1; // MenuState.Main
        Menu.m_main_cursor = 3;

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual((MenuState)11, Menu.m_state); // MenuState.Help
    }

    [TestMethod]
    public void MainKey_Enter_DispatchesToQuitAtCursor4()
    {
        Menu.m_state = (MenuState)1; // MenuState.Main
        Menu.m_main_cursor = 4;

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual((MenuState)12, Menu.m_state); // MenuState.Quit
    }

    [TestMethod]
    public void MenuSinglePlayerF_SetsState()
    {
        Menu.Menu_SinglePlayer_f();

        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);
        Assert.AreEqual((MenuState)2, Menu.m_state); // MenuState.SinglePlayer
    }

    [TestMethod]
    public void SinglePlayerKey_DownArrow_WrapsCursorAtSingleplayerItems()
    {
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer
        Menu.m_singleplayer_cursor = 2; // SINGLEPLAYER_ITEMS - 1

        Menu.Keydown(Keys.K_DOWNARROW);

        Assert.AreEqual(0, Menu.m_singleplayer_cursor);
    }

    [TestMethod]
    public void SinglePlayerKey_UpArrow_WrapsCursorAtZero()
    {
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer
        Menu.m_singleplayer_cursor = 0;

        Menu.Keydown(Keys.K_UPARROW);

        Assert.AreEqual(2, Menu.m_singleplayer_cursor); // SINGLEPLAYER_ITEMS - 1
    }

    [TestMethod]
    public void SinglePlayerKey_Escape_ReturnsToMainMenu()
    {
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer

        Menu.Keydown(Keys.K_ESCAPE);

        Assert.AreEqual((MenuState)1, Menu.m_state); // MenuState.Main
    }

    [TestMethod]
    public void SinglePlayerKey_Enter_Cursor0_NewGame_WhenServerNotActive_SwitchesToGame()
    {
        Sv.active = false;
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer
        Menu.m_singleplayer_cursor = 0;
        Keys.key_dest = KeyDest.Menu;

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual(KeyDest.Game, Keys.key_dest);
    }

    [TestMethod]
    public void SinglePlayerKey_Enter_Cursor1_Load_SwitchesToLoadMenu()
    {
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer
        Menu.m_singleplayer_cursor = 1;

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual((MenuState)3, Menu.m_state); // MenuState.Load
    }

    [TestMethod]
    public void MenuSetupF_InitializesFieldsFromCvars()
    {
        Cl.cl_color->Value = 0x25; // top nibble 2, bottom nibble 5

        Menu.Menu_Setup_f();

        Assert.AreEqual("player", CStr.ToString(Menu.setup_myname));
        Assert.AreEqual(2, Menu.setup_top);
        Assert.AreEqual(5, Menu.setup_bottom);
    }

    [TestMethod]
    public void SetupKey_UpDownArrow_WrapsCursorAtBounds()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 4; // NUM_SETUP_CMDS - 1

        Menu.Keydown(Keys.K_DOWNARROW);
        Assert.AreEqual(0, Menu.setup_cursor);

        Menu.Keydown(Keys.K_UPARROW);
        Assert.AreEqual(4, Menu.setup_cursor);
    }

    [TestMethod]
    public void SetupKey_LeftRightArrow_BelowCursor2_IsNoOp()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 0;
        Menu.setup_top = 5;

        Menu.Keydown(Keys.K_LEFTARROW);

        Assert.AreEqual(5, Menu.setup_top);
    }

    [TestMethod]
    public void SetupKey_LeftArrow_AtCursor2_DecrementsSetupTopWithWraparound()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 2;
        Menu.setup_top = 0;

        Menu.Keydown(Keys.K_LEFTARROW);

        Assert.AreEqual(13, Menu.setup_top); // wraps from 0 to 13
    }

    [TestMethod]
    public void SetupKey_RightArrow_AtCursor3_IncrementsSetupBottomWithWraparound()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 3;
        Menu.setup_bottom = 13;

        Menu.Keydown(Keys.K_RIGHTARROW);

        Assert.AreEqual(0, Menu.setup_bottom); // wraps from 13 to 0
    }

    [TestMethod]
    public void SetupKey_PrintableChar_AppendsToActiveFieldRespectingCap()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 0;
        *Menu.setup_hostname = 0;

        Menu.Keydown('a');
        Assert.AreEqual("a", CStr.ToString(Menu.setup_hostname));

        CStr.Write(Menu.setup_hostname, new string('x', 15));
        Menu.Keydown('b');
        Assert.AreEqual(new string('x', 15), CStr.ToString(Menu.setup_hostname)); // capped at 15 chars
    }

    [TestMethod]
    public void SetupKey_Backspace_TrimsActiveFieldAndNoOpsOnEmpty()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 1;
        CStr.Write(Menu.setup_myname, "ab");

        Menu.Keydown(Keys.K_BACKSPACE);
        Assert.AreEqual("a", CStr.ToString(Menu.setup_myname));

        *Menu.setup_myname = 0;
        Menu.Keydown(Keys.K_BACKSPACE);
        Assert.AreEqual("", CStr.ToString(Menu.setup_myname));
    }

    [TestMethod]
    public void LanConfigKey_PortField_AcceptsOnlyDigitsCappedAt5_AndRevertsAbove65535()
    {
        Menu.m_state = MenuState.LanConfig;
        Menu.lanConfig_cursor = 0;
        Menu.lanConfig_port = 26000;
        CStr.Write(Menu.lanConfig_portname, "26000");

        Menu.Keydown('x'); // not a digit
        Menu.Keydown('1'); // already 5 chars
        Assert.AreEqual("26000", CStr.ToString(Menu.lanConfig_portname));

        Menu.Keydown(Keys.K_BACKSPACE);
        Menu.Keydown(Keys.K_BACKSPACE);
        Menu.Keydown(Keys.K_BACKSPACE);
        Assert.AreEqual("26", CStr.ToString(Menu.lanConfig_portname));
        Assert.AreEqual(26, Menu.lanConfig_port);

        Menu.Keydown('9');
        Menu.Keydown('9');
        Menu.Keydown('9'); // 26999 is fine
        Assert.AreEqual(26999, Menu.lanConfig_port);

        CStr.Write(Menu.lanConfig_portname, "9999");
        Menu.Keydown('9'); // 99999 > 65535: port and text revert
        Assert.AreEqual(26999, Menu.lanConfig_port);
        Assert.AreEqual("26999", CStr.ToString(Menu.lanConfig_portname));
    }

    [TestMethod]
    public void LanConfigKey_JoinNameField_CappedAt21Chars()
    {
        Menu.m_state = MenuState.LanConfig;
        Menu.lanConfig_cursor = 2;
        CStr.Write(Menu.lanConfig_joinname, new string('a', 21));

        Menu.Keydown('b');

        Assert.AreEqual(new string('a', 21), CStr.ToString(Menu.lanConfig_joinname));
    }

    [TestMethod]
    public void ModemConfigKey_InitField_CappedAt29Chars()
    {
        Menu.m_state = MenuState.ModemConfig;
        Menu.modemConfig_cursor = 2;
        CStr.Write(Menu.modemConfig_init, new string('a', 29));

        Menu.Keydown('b');

        Assert.AreEqual(new string('a', 29), CStr.ToString(Menu.modemConfig_init));
    }

    [TestMethod]
    public void ScanSaves_FillsSlotsFromSaveCommentsAndMarksUnused()
    {
        string dir = Path.Combine(Path.GetTempPath(), "menu_scansaves_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        byte* saved = stackalloc byte[QuakeDef.MAX_OSPATH];
        Q.StrNCpy(saved, Com.com_gamedir, QuakeDef.MAX_OSPATH - 1);
        try
        {
            File.WriteAllText(Path.Combine(dir, "s1.sav"), "5\nmy_save_" + new string('z', 60) + "\n");
            CStr.Write(Com.com_gamedir, dir.Replace('\\', '/'));

            Menu.ScanSaves();

            Assert.AreEqual("--- UNUSED SLOT ---", CStr.ToString(Menu.m_filenames));
            Assert.IsFalse(Menu.loadable[0]);
            Assert.IsTrue(Menu.loadable[1]);
            string slot1 = CStr.ToString(Menu.m_filenames + 40);
            Assert.AreEqual(39, slot1.Length);
            Assert.IsTrue(slot1.StartsWith("my save zzz"));
        }
        finally
        {
            Q.StrCpy(Com.com_gamedir, new Utf8String(saved));
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void SetupKey_Escape_ReturnsToMultiPlayerMenu()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup

        Menu.Keydown(Keys.K_ESCAPE);

        Assert.AreEqual((MenuState)5, Menu.m_state); // MenuState.MultiPlayer
    }

    [TestMethod]
    public void SetupKey_Enter_AtOkCursor_ReturnsToMultiPlayerMenu()
    {
        Menu.m_state = (MenuState)6; // MenuState.Setup
        Menu.setup_cursor = 4; // OK

        Menu.Keydown(Keys.K_ENTER);

        Assert.AreEqual((MenuState)5, Menu.m_state); // MenuState.MultiPlayer
    }

    [TestMethod]
    public void MenuQuitF_CalledTwice_DoesNotClobberPrevState()
    {
        Keys.key_dest = KeyDest.Menu;
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer
        Menu.Menu_Quit_f();
        Assert.AreEqual(MenuState.SinglePlayer, Menu.m_quit_prevstate);

        Menu.Menu_Quit_f(); // guard: m_state == MenuState.Quit already -> no-op

        Assert.AreEqual(MenuState.SinglePlayer, Menu.m_quit_prevstate);
    }

    [TestMethod]
    public void QuitKey_Escape_WhenWasInMenus_ReturnsToPreviousState()
    {
        Keys.key_dest = KeyDest.Menu;
        Menu.m_state = (MenuState)2; // MenuState.SinglePlayer
        Menu.Menu_Quit_f(); // wasInMenus = true, prevstate = 2, state = MenuState.Quit

        Menu.Keydown(Keys.K_ESCAPE);

        Assert.AreEqual((MenuState)2, Menu.m_state); // MenuState.SinglePlayer
    }

    [TestMethod]
    public void MultiPlayerKey_Enter_OpensNetMenuOnTcpip_OnlyWhenTcpipAvailable()
    {
        bool old = NetIp.tcpipAvailable;
        try
        {
            NetIp.tcpipAvailable = false;
            Menu.Menu_MultiPlayer_f();
            Menu.Keydown(Keys.K_ENTER);
            Assert.AreEqual(MenuState.MultiPlayer, Menu.m_state);

            NetIp.tcpipAvailable = true;
            Menu.Menu_MultiPlayer_f();
            Menu.Keydown(Keys.K_ENTER);
            Assert.AreEqual(MenuState.Net, Menu.m_state);
        }
        finally
        {
            NetIp.tcpipAvailable = old;
        }
    }
}
