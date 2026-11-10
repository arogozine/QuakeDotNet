// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// cmd_functions/cmd_alias are Hunk/Zone-backed linked lists tied to whichever
// arena was active when they were populated. Since every test gets a fresh
// arena via MemoryManager.Memory_Init, they're reset to empty in Setup() too
// (internal, via InternalsVisibleTo) -- otherwise they'd dangle into a freed
// arena from a previous test/class and crash. This also means these tests
// can't run concurrently with anything else touching Cmd/Cvar/Con.
[TestClass]
public sealed unsafe class CmdTests
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
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
        Cmd.BufInit();
        Com.com_searchpaths = null;
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
        {
            if (s->Pack != null)
            {
                Sys.FileClose(s->Pack->Handle);
            }
        }

        NativeMemory.Free(_buf);
    }

    private static string Str(byte* s) => Marshal.PtrToStringAnsi((nint)s) ?? string.Empty;

    [TestMethod]
    public void TokenizeString_SplitsIntoArgcArgvArgs()
    {
        // Cmd_Args (cmd_args) is a raw pointer into the middle of the original
        // buffer, not a copy truncated at the line break, so it carries
        // whatever follows the first token verbatim -- no trailing newline
        // here so the expectation stays exact.
        Cmd.TokenizeString(Ansi("say hello world"));

        Assert.AreEqual(3, Cmd.Argc());
        Assert.AreEqual("say", Str(Cmd.Argv(0)));
        Assert.AreEqual("hello", Str(Cmd.Argv(1)));
        Assert.AreEqual("world", Str(Cmd.Argv(2)));
        Assert.AreEqual("hello world", Str(Cmd.Args()));
    }

    [TestMethod]
    public void Argv_ReturnsEmptyString_PastArgc()
    {
        Cmd.TokenizeString(Ansi("only\n"));
        Assert.AreEqual(string.Empty, Str(Cmd.Argv(5)));
    }

    private static int calledCount;

    private static void TestCommand_f() => calledCount++;

    [TestMethod]
    public void AddCommand_Then_ExecuteString_InvokesFunction()
    {
        calledCount = 0;
        Cmd.AddCommand(Ansi("cmdtest_invoke"), &TestCommand_f);

        Cmd.ExecuteString(Ansi("cmdtest_invoke\n"), CmdSource_t.SrcCommand);

        Assert.AreEqual(1, calledCount);
    }

    [TestMethod]
    public void Exists_TrueForRegistered_FalseOtherwise()
    {
        Cmd.AddCommand(Ansi("cmdtest_exists"), &TestCommand_f);

        Assert.IsTrue(Cmd.Exists(Ansi("cmdtest_exists")));
        Assert.IsFalse(Cmd.Exists(Ansi("cmdtest_doesnotexist")));
    }

    [TestMethod]
    public void CompleteCommand_MatchesPartialPrefix()
    {
        Cmd.AddCommand(Ansi("cmdtest_completeme"), &TestCommand_f);

        Assert.AreEqual("cmdtest_completeme", Str(Cmd.CompleteCommand(Ansi("cmdtest_comple"))));
        Assert.IsTrue(Cmd.CompleteCommand(Ansi("cmdtest_nomatchxyz")) == null);
    }

    [TestMethod]
    public void CheckParm_ReturnsIndex_OrZero()
    {
        Cmd.TokenizeString(Ansi("cmd -flag value\n"));

        Assert.AreEqual(1, Cmd.CheckParm(Ansi("-flag")));
        Assert.AreEqual(0, Cmd.CheckParm(Ansi("-missing")));
    }

    [TestMethod]
    public void Alias_DefineThenExecute_InvokesAliasedCommand()
    {
        calledCount = 0;
        Cmd.AddCommand(Ansi("cmdtest_aliastarget"), &TestCommand_f);

        Cmd.TokenizeString(Ansi("alias cmdtest_myalias cmdtest_aliastarget\n"));
        Cmd.Alias_f();

        // executing the alias inserts "cmdtest_aliastarget\n" at the front of
        // the command buffer rather than running it immediately
        Cmd.ExecuteString(Ansi("cmdtest_myalias\n"), CmdSource_t.SrcCommand);
        Cmd.BufExecute();

        Assert.AreEqual(1, calledCount);
    }

    [TestMethod]
    public void AddText_Then_BufExecute_RunsRegisteredCommand()
    {
        calledCount = 0;
        Cmd.AddCommand(Ansi("cmdtest_buffered"), &TestCommand_f);

        Cmd.AddText(Ansi("cmdtest_buffered\n"));
        Cmd.BufExecute();

        Assert.AreEqual(1, calledCount);
    }

    [TestMethod]
    public void Exec_f_LoadsAndInsertsFileContents()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            File.WriteAllText(Path.Combine(gamedir, "test.cfg"), "cmdtest_execd\n");
            Com.AddGameDirectory(Ansi(gamedir));

            calledCount = 0;
            Cmd.AddCommand(Ansi("cmdtest_execd"), &TestCommand_f);

            Cmd.TokenizeString(Ansi("exec test.cfg\n"));
            Cmd.Exec_f();
            Cmd.BufExecute();

            Assert.AreEqual(1, calledCount);
        }
        finally
        {
            for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            {
                if (s->Pack != null)
                {
                    Sys.FileClose(s->Pack->Handle);
                }
            }

            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void Exec_f_HandlesCommentsBlankLinesAndCvarAssignments_LikeARealConfigFile()
    {
        // A config.cfg-shaped file (comments, blank lines, a plain command,
        // and a cvar assignment that falls through ExecuteString to Cvar's
        // command handler) round-trips end to end.
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            File.WriteAllText(Path.Combine(gamedir, "config.cfg"),
                "// autogenerated config\n" +
                "\n" +
                "cmdtest_cfgcmd\n" +
                "// a comment before a cvar assignment\n" +
                "cmdtest_cfgcvar 7\n");
            Com.AddGameDirectory(Ansi(gamedir));

            calledCount = 0;
            Cmd.AddCommand(Ansi("cmdtest_cfgcmd"), &TestCommand_f);

            Cvar_t* v = NativeMemory.AllocZeroed<Cvar_t>();
            v->Name = Ansi("cmdtest_cfgcvar");
            v->String = Ansi("0");
            Cvar.RegisterVariable(v);

            Cmd.TokenizeString(Ansi("exec config.cfg\n"));
            Cmd.Exec_f();
            Cmd.BufExecute();

            Assert.AreEqual(1, calledCount);
            Assert.AreEqual(7f, Cvar.VariableValue(Ansi("cmdtest_cfgcvar")));
        }
        finally
        {
            for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            {
                if (s->Pack != null)
                {
                    Sys.FileClose(s->Pack->Handle);
                }
            }

            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void ExecuteString_FallsThroughToCvarCommand_ForUnknownCommandName()
    {
        Cvar_t* v = NativeMemory.AllocZeroed<Cvar_t>();
        v->Name = Ansi("cmdtest_fallthroughcvar");
        v->String = Ansi("1");
        Cvar.RegisterVariable(v);

        Cmd.ExecuteString(Ansi("cmdtest_fallthroughcvar 3\n"), CmdSource_t.SrcCommand);

        Assert.AreEqual(3f, Cvar.VariableValue(Ansi("cmdtest_fallthroughcvar")));
    }
}
