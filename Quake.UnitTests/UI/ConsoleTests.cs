// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Con.Init() registers into Cvar's/Cmd's Hunk/Zone-backed linked lists, which
// are tied to whichever arena was active when they were populated. Since
// every test gets a fresh arena via MemoryManager.Memory_Init, those lists
// (internal, via InternalsVisibleTo) are reset to empty in Setup() too --
// otherwise they'd dangle into a freed arena from a previous test/class and
// crash. This also means these tests can't run concurrently with anything
// else touching Cmd/Cvar/Con, and it captures Console.Out besides.
[TestClass]
public sealed unsafe class ConsoleTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

        // con_text is Hunk-allocated -- every field describing it (and
        // con_initialized, which gates whether Printf ever touches it) must
        // be reset too, or it dangles into this test's freed arena the
        // moment any other test calls Sys.Printf/Con.Printf afterwards.
        Con.con_initialized = false;
        Con.con_text = null;
        Con.con_linewidth = 0;
        Con.con_totallines = 0;
        Con.con_current = 0;
        Con.con_x = 0;
        Con.con_debuglog = false;

        Con.Init();
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);

        // Leave Con uninitialized so any later test in another class that
        // triggers Sys.Printf/Con.Printf takes Printf's "not initialized"
        // early return instead of writing through con_text, which now
        // dangles into the arena just freed above.
        Con.con_initialized = false;
        Con.con_text = null;
    }

    private static string CaptureOutput(Action action)
    {
        TextWriter original = Console.Out;
        StringWriter sw = new();
        Console.SetOut(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    [TestMethod]
    public void Init_RegistersConsoleCommandAndCvars()
    {
        // "developer" is NOT registered here: the original only registers it
        // once, in host.c's Host_InitLocal (Host.cs), with console.c's
        // Con_DPrintf just reading the same extern cvar_t -- Con.Init used
        // to duplicate that registration (a pre-Host.cs stopgap), which
        // printed "Can't register variable developer, allready defined" on
        // every real boot; fixed to match the original's single registration.
        Assert.IsTrue(Cmd.Exists(Ansi("clear")));
        Assert.IsTrue(Cvar.FindVar(Ansi("con_notifytime")) != null);
        Assert.AreEqual(3f, Cvar.VariableValue(Ansi("con_notifytime")));
    }

    [TestMethod]
    public void Printf_WritesFormattedMessage_ToConsoleOutput()
    {
        string output = CaptureOutput(() => Con.Printf($"hello {"world"}\n"));
        StringAssert.Contains(output, "hello world");
    }

    [TestMethod]
    public void Printf_WithNoArgs_WritesLiteralMessage()
    {
        string output = CaptureOutput(() => Con.Printf("plain message\n"u8));
        StringAssert.Contains(output, "plain message");
    }

    [TestMethod]
    public void Printf_EmptySpan_WhenInitialized_DoesNotCrash()
    {
        Assert.IsTrue(Con.con_initialized);
        int before = Con.con_current;

        string output = CaptureOutput(() => Con.Printf(""u8));

        Assert.AreEqual("", output);
        Assert.AreEqual(before, Con.con_current);
    }

    [TestMethod]
    public void Printf_EmptyHandler_WhenInitialized_DoesNotCrash()
    {
        Assert.IsTrue(Con.con_initialized);
        string empty = "";

        string output = CaptureOutput(() => Con.Printf($"{empty}"));

        Assert.AreEqual("", output);
    }

    [TestMethod]
    public void Printf_KeepsHighBitCharacters_InConsoleBuffer()
    {
        // Bytes 128-255 are Quake's red/gold characters; Printf must pass
        // them through untouched (a u8 literal would UTF-8 encode the Á, so
        // the bytes are spelled out).
        Con.CheckResize(40);
        CaptureOutput(() => Con.Printf(new byte[] { (byte)'a', 0xC1, (byte)'b', (byte)'\n' }));

        int total = Con.con_totallines * Con.con_linewidth;
        bool found = false;
        for (int i = 0; i + 2 < total; i++)
        {
            if (Con.con_text[i] == (byte)'a' && Con.con_text[i + 1] == 0xC1 && Con.con_text[i + 2] == (byte)'b')
                found = true;
        }
        Assert.IsTrue(found);
    }

    [TestMethod]
    public void SafePrintf_BehavesLikePrintf()
    {
        string output = CaptureOutput(() => Con.SafePrintf($"safe {"text"}\n"));
        StringAssert.Contains(output, "safe text");
    }

    [TestMethod]
    public void DPrintf_ProducesNoOutput_WhenDeveloperCvarIsAtDefaultZero()
    {
        // "developer" defaults to "0" in Con.Init and nothing in this suite
        // ever changes it, so DPrintf should stay silent.
        string output = CaptureOutput(() => Con.DPrintf("should not appear\n"u8));
        Assert.AreEqual(string.Empty, output);
    }

    [TestMethod]
    public void DebugLog_AppendsSuccessiveMessages_ToFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Con.DebugLog(path, "first\n"u8);
            Con.DebugLog(path, "second\n"u8);

            string contents = File.ReadAllText(path);
            StringAssert.Contains(contents, "first");
            StringAssert.Contains(contents, "second");
            Assert.IsLessThan(contents.IndexOf("second", StringComparison.Ordinal),
                               contents.IndexOf("first", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ClearNotify_DoesNotThrow()
    {
        Con.ClearNotify();
    }

    [TestMethod]
    public void ClearF_DoesNotThrow_AfterInit()
    {
        Con.Clear_f();
    }

    [TestMethod]
    public void CheckResize_ThenPrint_HandlesWrappingWithoutThrowing()
    {
        Con.CheckResize(40);

        StringBuilder longLine = new();
        for (int i = 0; i < 20; i++)
            longLine.Append("word ");
        longLine.Append('\n');

        byte* text = Ansi(longLine.ToString());
        for (int i = 0; i < 50; i++)
            Con.Print(text);

        Con.Linefeed();
    }

    // Con_DrawInput/Con_DrawNotify/Con_DrawConsole (the drawing half) need a
    // real Vid.vid.ConBuffer + Draw.draw_chars, same fixture pattern as
    // DrawTests.cs/ScreenTests.cs.
    private byte* _conBuf;

    private void InstallDrawFixture()
    {
        DIface.r_pixbytes = 1;
        _conBuf = NativeMemory.AllocArrayZeroed<byte>(64 * 64);
        Vid.vid.ConBuffer = _conBuf;
        Vid.vid.ConRowBytes = 64;
        Vid.vid.ConWidth = 64;
        Vid.vid.ConHeight = 64;
        Draw.draw_chars = NativeMemory.AllocArrayZeroed<byte>(128 * 16);
        for (int i = 0; i < 128 * 16; i++)
            Draw.draw_chars[i] = 0xAB; // every glyph fully opaque, easy to detect
    }

    private void FreeDrawFixture()
    {
        if (_conBuf != null)
            NativeMemory.Free(_conBuf);
        if (Draw.draw_chars != null)
            NativeMemory.Free(Draw.draw_chars);
        _conBuf = null;
        Draw.draw_chars = null;
    }

    [TestMethod]
    public void DrawInput_KeyDestNotConsoleAndNotForcedUp_DrawsNothing()
    {
        InstallDrawFixture();
        try
        {
            Keys.key_dest = KeyDest.Game;
            Con.con_forcedup = false;

            Con.DrawInput();

            for (int i = 0; i < 64 * 64; i++)
                Assert.AreEqual((byte)0, _conBuf[i]);
        }
        finally
        {
            FreeDrawFixture();
        }
    }

    [TestMethod]
    public void DrawInput_KeyDestConsole_DrawsTypedLineAndCursor()
    {
        InstallDrawFixture();
        try
        {
            Con.CheckResize(20);
            Keys.key_dest = KeyDest.Console;
            Con.con_forcedup = false;

            // Con_DrawInput draws at y = con_vislines - 16; pin it to a
            // visible row, matching what Con_DrawConsole would have set.
            Con.con_vislines = 32;

            Con.DrawInput();

            bool anyPixelDrawn = false;
            for (int i = 0; i < 64 * 64; i++)
                if (_conBuf[i] != 0)
                    anyPixelDrawn = true;
            Assert.IsTrue(anyPixelDrawn);
        }
        finally
        {
            FreeDrawFixture();
        }
    }

    [TestMethod]
    public void DrawNotify_RecentPrint_DrawsLineAndTracksNotifyLines()
    {
        InstallDrawFixture();
        try
        {
            Con.CheckResize(20);
            Keys.key_dest = KeyDest.Game;
            Con.con_notifylines = 0;
            // Host.realtime must be nonzero, or the timestamp Con_Print
            // stamps into con_times[] is indistinguishable from an unset
            // slot's default 0 (Con_DrawNotify's own "if (time == 0)
            // continue" check, ported faithfully from the original).
            Host.realtime = 1;
            Con.Printf("hi\n"u8);

            Con.DrawNotify();

            // "hi\n" (plus its leading blank fill) is drawn as one notify
            // line: Con_DrawNotify tracks it via con_notifylines and (this
            // fixture's glyph table is uniformly opaque, so every drawn
            // character -- including spaces -- writes a nonzero pixel).
            Assert.IsGreaterThan(0, Con.con_notifylines);
        }
        finally
        {
            FreeDrawFixture();
        }
    }

    [TestMethod]
    public void DrawNotify_NoRecentOutput_LeavesConBufferUntouched()
    {
        InstallDrawFixture();
        try
        {
            Con.CheckResize(20);
            Keys.key_dest = KeyDest.Game;
            Con.ClearNotify();
            Con.con_notifylines = 0;

            Con.DrawNotify();

            for (int i = 0; i < 64 * 64; i++)
                Assert.AreEqual((byte)0, _conBuf[i]);
        }
        finally
        {
            FreeDrawFixture();
        }
    }

    [TestMethod]
    public void DrawConsole_LinesZero_ReturnsWithoutDrawing()
    {
        InstallDrawFixture();
        try
        {
            Con.DrawConsole(0, true);

            for (int i = 0; i < 64 * 64; i++)
                Assert.AreEqual((byte)0, _conBuf[i]);
        }
        finally
        {
            FreeDrawFixture();
        }
    }
}
