// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// keys.c's Keys.cs -- open_tk.md's Section 2/5: Key_Event's dispatch logic
// (keydest-gated behavior, binding lookup) is pure state with no hardware
// dependency, so it's covered here the same way the rest of keys.c-adjacent
// state is tested elsewhere. Shares Cmd/Cvar/Con/Keys/Cls's static state
// with the other DoNotParallelize classes.
[TestClass]
public sealed unsafe class KeysTests
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
        Cmd.BufInit();

        NativeMemory.ClearPointerArray<byte>(Keys.keybindings, 256);
        Keys.key_dest = KeyDest.Game;
        Keys.key_count = 0;
        Keys.key_lastpress = 0;
        Keys.ClearStates();
        Keys.team_message = false;

        Con.con_forcedup = false;
        ClDemo.demoplayback = false;
        Cls.state = CActive.Disconnected;

        // Key_Console's K_ENTER handler calls SCR_UpdateScreen while
        // cls.state == ca_disconnected; keep Screen.UpdateScreen a no-op
        // (its own scr_initialized gate) since no renderer is set up here.
        Screen.scr_initialized = false;

        Keys.Init();
    }

    [TestCleanup]
    public void Teardown() => NativeMemory.Free(_buf);

    private static string Str(byte* s) => Marshal.PtrToStringAnsi((nint)s) ?? string.Empty;

    private static int plainInvokeCount;
    private static void PlainCommand_f() => plainInvokeCount++;

    private static string? lastButtonArg;
    private static void ButtonDownCommand_f() => lastButtonArg = Str(Cmd.Argv(1));
    private static void ButtonUpCommand_f() => lastButtonArg = Str(Cmd.Argv(1));

    [TestMethod]
    public void Console_Tab_FallsBackToCvarCompletion_WhenNoCommandMatches()
    {
        Cvar_t* v = (Cvar_t*)NativeMemory.AllocZeroed((nuint)sizeof(Cvar_t));
        v->Name = Ansi("cv_tabtest");
        v->String = Ansi("0");
        Cvar.RegisterVariable(v);

        Q.StrCpy(Keys.KeyLine(Keys.edit_line) + 1, Ansi("cv_tabt"));
        Keys.key_linepos = 8; // 1 (']') + "cv_tabt".Length

        Keys.Console(Keys.K_TAB);

        Assert.AreEqual("]cv_tabtest ", Str(Keys.KeyLine(Keys.edit_line)));

        // Unlink before freeing -- RegisterVariable linked v in as the head
        // of the process-wide Cvar.cvar_vars list, so freeing it without
        // this would leave that list's head a dangling pointer into freed
        // memory for whichever test runs next.
        Cvar.cvar_vars = v->Next;
        NativeMemory.Free(v);
    }

    [TestMethod]
    public void Event_EscapeFromGame_OpensMenu()
    {
        Keys.key_dest = KeyDest.Game;

        Keys.Event(Keys.K_ESCAPE, true);

        Assert.AreEqual(KeyDest.Menu, Keys.key_dest);
    }

    [TestMethod]
    public void Event_EscapeKeyUp_IsIgnored()
    {
        Keys.key_dest = KeyDest.Game;

        Keys.Event(Keys.K_ESCAPE, false);

        Assert.AreEqual(KeyDest.Game, Keys.key_dest);
    }

    [TestMethod]
    public void Event_BoundPlainCommand_QueuedAndRunsOnBufExecute()
    {
        plainInvokeCount = 0;
        Cmd.AddCommand(Ansi("keytest_plain"), &PlainCommand_f);
        fixed (byte* binding = "keytest_plain"u8)
            Keys.SetBinding(Keys.K_F1, binding);

        Keys.Event(Keys.K_F1, true);
        Cmd.BufExecute();

        Assert.AreEqual(1, plainInvokeCount);
    }

    [TestMethod]
    public void Event_ButtonBindingDown_AppendsKeynumParameter()
    {
        lastButtonArg = null;
        Cmd.AddCommand(Ansi("+testbtn"), &ButtonDownCommand_f);
        fixed (byte* binding = "+testbtn"u8)
            Keys.SetBinding(Keys.K_MOUSE1, binding);

        Keys.Event(Keys.K_MOUSE1, true);
        Cmd.BufExecute();

        Assert.AreEqual(Keys.K_MOUSE1.ToString(), lastButtonArg);
    }

    [TestMethod]
    public void Event_ButtonBindingUp_SendsMinusCommandWithKeynum()
    {
        lastButtonArg = null;
        Cmd.AddCommand(Ansi("-testbtn"), &ButtonUpCommand_f);
        fixed (byte* binding = "+testbtn"u8)
            Keys.SetBinding(Keys.K_MOUSE2, binding);

        // key-up doesn't need a matching down first for the button-command
        // path: Key_Event's up branch fires unconditionally off keybindings,
        // matching keys.c's own "keep the character from continuing an
        // action started before a console switch" comment.
        Keys.Event(Keys.K_MOUSE2, false);
        Cmd.BufExecute();

        Assert.AreEqual(Keys.K_MOUSE2.ToString(), lastButtonArg);
    }

    [TestMethod]
    public void Event_UnboundNonConsoleKeyInGame_DoesNothing()
    {
        Keys.key_dest = KeyDest.Game;
        Con.con_forcedup = false;

        // must not throw, and must not fall through into Key_Console
        Keys.Event(Keys.K_F5, true);
    }

    [TestMethod]
    public void Console_TypedLineOnEnter_QueuedAndRunsOnBufExecute()
    {
        plainInvokeCount = 0;
        Cmd.AddCommand(Ansi("keytest_plain"), &PlainCommand_f);

        Keys.key_dest = KeyDest.Console;
        foreach (char c in "keytest_plain")
        {
            // down+up per character, like a real keystroke -- a down with no
            // matching up first would hit Key_Event's own autorepeat
            // suppression (key_repeats[key] > 1) on the second 'e'/'t'.
            Keys.Event(c, true);
            Keys.Event(c, false);
        }
        Keys.Event(Keys.K_ENTER, true);
        Cmd.BufExecute();

        Assert.AreEqual(1, plainInvokeCount);
    }

    [TestMethod]
    public void Key_Event_BackspaceAutorepeat_IsNotSuppressed()
    {
        Keys.key_dest = KeyDest.Console;
        foreach (char c in "abc")
        {
            Keys.Event(c, true);
            Keys.Event(c, false);
        }
        Assert.AreEqual(4, Keys.key_linepos);

        // held key: repeated downs with no intervening up (keys.c lets only
        // K_BACKSPACE/K_PAUSE autorepeat)
        Keys.Event(Keys.K_BACKSPACE, true);
        Keys.Event(Keys.K_BACKSPACE, true);
        Keys.Event(Keys.K_BACKSPACE, true);

        Assert.AreEqual(1, Keys.key_linepos);
    }

    [TestMethod]
    public void Key_Event_OrdinaryKeyAutorepeat_IsSuppressed()
    {
        Keys.key_dest = KeyDest.Console;
        Keys.Event('a', true);
        Keys.Event('a', true);
        Keys.Event('a', true);

        Assert.AreEqual(2, Keys.key_linepos); // ']' + a single 'a'
    }

    [TestMethod]
    public void Key_Event_MouseWheelDownInConsole_ScrollsBackward()
    {
        Keys.key_dest = KeyDest.Console;
        Con.con_backscroll = 5;
        Con.con_totallines = 1000;

        Keys.Event(Keys.K_MWHEELDOWN, true);
        Keys.Event(Keys.K_MWHEELDOWN, false);

        Assert.AreEqual(3, Con.con_backscroll);
    }

    [TestMethod]
    public void Key_Event_MouseWheelBoundInGame_RunsBinding()
    {
        plainInvokeCount = 0;
        Cmd.AddCommand(Ansi("keytest_plain"), &PlainCommand_f);
        fixed (byte* binding = "keytest_plain"u8)
            Keys.SetBinding(Keys.K_MWHEELUP, binding);

        Keys.Event(Keys.K_MWHEELUP, true);
        Keys.Event(Keys.K_MWHEELUP, false);
        Cmd.BufExecute();

        Assert.AreEqual(1, plainInvokeCount);
    }

    [TestMethod]
    public void Key_StringToKeynum_Mouse4AndMouse5_RoundTrip()
    {
        Assert.AreEqual(Keys.K_MOUSE4, Keys.StringToKeynum(Ansi("MOUSE4")));
        Assert.AreEqual(Keys.K_MOUSE5, Keys.StringToKeynum(Ansi("mouse5")));
        Assert.AreEqual("MOUSE4", Str(Keys.KeynumToString(Keys.K_MOUSE4)));
        Assert.AreEqual("MOUSE5", Str(Keys.KeynumToString(Keys.K_MOUSE5)));
    }

    [TestMethod]
    public void Key_Keynums_MouseWheelJoyPause_AreDistinctAndInTable()
    {
        int[] nums = [Keys.K_MOUSE1, Keys.K_MOUSE2, Keys.K_MOUSE3, Keys.K_MOUSE4, Keys.K_MOUSE5,
            Keys.K_JOY1, Keys.K_JOY4, Keys.K_MWHEELUP, Keys.K_MWHEELDOWN, Keys.K_PAUSE];
        Assert.AreEqual(nums.Length, nums.Distinct().Count());
        Assert.IsTrue(nums.All(n => n >= 200 && n < 256));
    }

    [TestMethod]
    public void Key_Event_Mouse4BoundToButtonCommand_AppendsKeynum()
    {
        lastButtonArg = null;
        Cmd.AddCommand(Ansi("+testbtn"), &ButtonDownCommand_f);
        fixed (byte* binding = "+testbtn"u8)
            Keys.SetBinding(Keys.K_MOUSE4, binding);

        Keys.Event(Keys.K_MOUSE4, true);
        Cmd.BufExecute();

        Assert.AreEqual(Keys.K_MOUSE4.ToString(), lastButtonArg);
    }

    [TestMethod]
    public void Key_ClearStates_HeldKey_ResetsHeldAndRepeatState()
    {
        Keys.Event('w', true);
        Assert.IsTrue(Keys.keydown['w']);
        Assert.AreEqual(1, Keys.key_repeats['w']);

        Keys.ClearStates();

        Assert.IsFalse(Keys.keydown['w']);
        Assert.AreEqual(0, Keys.key_repeats['w']);
    }

    [TestMethod]
    public void Key_ReleaseAll_HeldButtonBinding_SendsMinusCommandWithKeynum()
    {
        lastButtonArg = null;
        Cmd.AddCommand(Ansi("-testbtn"), &ButtonUpCommand_f);
        fixed (byte* binding = "+testbtn"u8)
            Keys.SetBinding('w', binding);

        Keys.Event('w', true);
        Keys.ReleaseAll();
        Cmd.BufExecute();

        Assert.AreEqual(((int)'w').ToString(), lastButtonArg);
        Assert.IsFalse(Keys.keydown['w']);
    }

    [TestMethod]
    public void Key_ReleaseAll_ShiftHeld_NextKeyIsNotShifted()
    {
        Keys.key_dest = KeyDest.Console;
        Keys.Event(Keys.K_SHIFT, true);
        Keys.ReleaseAll();

        Keys.Event('a', true);

        Assert.AreEqual((byte)'a', Keys.KeyLine(Keys.edit_line)[1]);
    }

    [TestMethod]
    public void Key_ReleaseAll_NothingHeld_SendsNoEvents()
    {
        int before = Keys.key_count;
        Keys.ReleaseAll();
        Assert.AreEqual(before, Keys.key_count);
    }

    [TestMethod]
    public void Key_Event_ShiftedKeyUp_ReleasesBindingOnShiftedKeynum()
    {
        lastButtonArg = null;
        Cmd.AddCommand(Ansi("-testbtn"), &ButtonUpCommand_f);
        fixed (byte* binding = "+testbtn"u8)
            Keys.SetBinding('A', binding);

        // keys.c: key-up also checks keybindings[keyshift[key]]
        Keys.Event('a', false);
        Cmd.BufExecute();

        Assert.AreEqual(((int)'a').ToString(), lastButtonArg);
    }

    [TestMethod]
    public void Key_StrLCat_Overlong_TruncatesAndTerminates()
    {
        byte* dest = stackalloc byte[8];
        dest[0] = 0;

        Keys.StrLCat(dest, "abcde"u8, 8);
        Keys.StrLCat(dest, Ansi("fghij"), 8);

        Assert.AreEqual("abcdefg", Str(dest));
        Assert.AreEqual(0, dest[7]);
    }

    [TestMethod]
    public void Key_Bind_f_LongestTokenCommand_FitsCmdBufferExactly()
    {
        // Bind_f takes at most one command token and Com_Parse caps a token
        // at 1023 bytes (a longer one overflows com_token, as in the
        // original), so cmd[1024] holds it exactly; StrLCat's truncation is
        // covered by Key_StrLCat_Overlong_TruncatesAndTerminates.
        string big = new('x', 1023);
        Cmd.TokenizeString(Ansi($"bind a \"{big}\""));

        Keys.Bind_f();

        Assert.AreEqual(1023, Str(Keys.keybindings['a']).Length);
    }

    [TestMethod]
    [DataRow("W", 'w')]
    [DataRow("w", 'w')]
    [DataRow("Z", 'z')]
    [DataRow("5", '5')]
    [DataRow(";", ';')]
    public void Key_StringToKeynum_SingleCharacter_LowercasesLetters(string name, char expected)
    {
        Assert.AreEqual(expected, Keys.StringToKeynum(Ansi(name)));
    }

    [TestMethod]
    public void Key_StringToKeynum_Aux1ToAux32_RoundTrip()
    {
        for (int i = 1; i <= 32; i++)
        {
            int keynum = Keys.StringToKeynum(Ansi($"aux{i}"));
            Assert.AreEqual(Keys.K_AUX1 + i - 1, keynum, $"AUX{i}");
            Assert.AreEqual($"AUX{i}", Str(Keys.KeynumToString(keynum)), $"AUX{i}");
        }
        Assert.AreEqual(Keys.K_AUX32, Keys.StringToKeynum(Ansi("AUX32")));
    }

    // Sets the destination, then a key press that doesn't type anything, the
    // way a real keystroke precedes its TextInput character.
    private static void PressNonTextKey(KeyDest dest)
    {
        Keys.key_dest = dest;
        Keys.Event(Keys.K_F12, true);
        Keys.Event(Keys.K_F12, false);
    }

    [TestMethod]
    public void Key_CharEvent_InConsole_TypesCharacter()
    {
        PressNonTextKey(KeyDest.Console);

        Keys.CharEvent('é'); // non-ASCII is dropped
        Keys.CharEvent('q');

        Assert.AreEqual(2, Keys.key_linepos);
    }

    [TestMethod]
    public void Key_CharEvent_InGame_IsIgnored()
    {
        PressNonTextKey(KeyDest.Game);

        Keys.CharEvent('q');

        Assert.AreEqual(1, Keys.key_linepos);
    }

    [TestMethod]
    public void Key_CharEvent_BackquoteAndTilde_AreIgnored()
    {
        PressNonTextKey(KeyDest.Console);

        Keys.CharEvent('`');
        Keys.CharEvent('~');

        Assert.AreEqual(1, Keys.key_linepos);
    }

    [TestMethod]
    public void Key_CharEvent_DestinationChangedByKeyPress_IsIgnored()
    {
        PressNonTextKey(KeyDest.Game);
        Keys.Event(Keys.K_F12, true); // e.g. bind t messagemode
        Keys.key_dest = KeyDest.Console;

        Keys.CharEvent('t');

        Assert.AreEqual(1, Keys.key_linepos);
    }

    [TestMethod]
    public void Key_CharEvent_CtrlHeld_IsIgnored()
    {
        PressNonTextKey(KeyDest.Console);
        Keys.Event(Keys.K_CTRL, true);

        Keys.CharEvent('q');

        Assert.AreEqual(1, Keys.key_linepos);
    }

    [TestMethod]
    public void Key_Event_TextInputActive_PrintableKeyDoesNotTypeButEnterAndBackspaceDo()
    {
        Keys.TextInputActive = true;
        try
        {
            Keys.key_dest = KeyDest.Console;
            Keys.Event('a', true);
            Keys.Event('a', false);
            Assert.AreEqual(1, Keys.key_linepos, "physical key must not type");

            Keys.CharEvent('a');
            Assert.AreEqual(2, Keys.key_linepos);

            Keys.Event(Keys.K_BACKSPACE, true);
            Keys.Event(Keys.K_BACKSPACE, false);
            Assert.AreEqual(1, Keys.key_linepos, "Backspace still uses the keynum path");
        }
        finally
        {
            Keys.TextInputActive = false;
        }
    }

    [TestMethod]
    public void Key_Event_TextInputActive_BindingsStillFireFromPhysicalKey()
    {
        plainInvokeCount = 0;
        Cmd.AddCommand(Ansi("keytest_plain"), &PlainCommand_f);
        Keys.SetBinding('w', Ansi("keytest_plain"));
        Keys.TextInputActive = true;
        try
        {
            Keys.key_dest = KeyDest.Game;
            Keys.Event('w', true);
            Cmd.BufExecute();
        }
        finally
        {
            Keys.TextInputActive = false;
        }

        Assert.AreEqual(1, plainInvokeCount);
    }
}
