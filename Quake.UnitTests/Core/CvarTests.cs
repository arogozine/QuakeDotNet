// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// cvar_vars is a Hunk/Zone-backed linked list tied to whichever arena was
// active when it was populated (RegisterVariable/Set allocate through
// Zone.Malloc). Since every test gets a fresh arena via
// MemoryManager.Memory_Init, cvar_vars (and Cmd's cmd_functions/cmd_argc,
// which RegisterVariable/Command touch via Cmd.Exists/Cmd.Argv) are reset to
// empty in Setup() too -- otherwise they'd dangle into a freed arena from a
// previous test/class and crash. This also means these tests can't run
// concurrently with anything else touching Cmd/Cvar/Con. Each test uses a
// unique variable name regardless, as a second layer of isolation.
[TestClass]
public sealed unsafe class CvarTests
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
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
    }

    private static string Str(byte* s) => Marshal.PtrToStringAnsi((nint)s) ?? string.Empty;

    private static Cvar_t* MakeCvar(string name, string value, bool archive = false)
    {
        Cvar_t* v = NativeMemory.AllocZeroed<Cvar_t>();
        v->Name = Ansi(name);
        v->String = Ansi(value);
        v->Archive = archive;
        return v;
    }

    [TestMethod]
    public void RegisterVariable_Then_FindVar_ReturnsIt()
    {
        Cvar_t* v = MakeCvar("cv_findme", "1");
        Cvar.RegisterVariable(v);

        Cvar_t* found = Cvar.FindVar(Ansi("cv_findme"));
        Assert.IsTrue(found != null);
        Assert.AreEqual(1f, found->Value);
    }

    [TestMethod]
    public void VariableValue_And_VariableString_ReflectRegisteredValue()
    {
        Cvar.RegisterVariable(MakeCvar("cv_valtest", "3.5"));

        Assert.AreEqual(3.5f, Cvar.VariableValue(Ansi("cv_valtest")));
        Assert.AreEqual("3.5", Str(Cvar.VariableString(Ansi("cv_valtest"))));
    }

    [TestMethod]
    public void VariableValue_ReturnsZero_ForUnknownVariable()
    {
        Assert.AreEqual(0f, Cvar.VariableValue(Ansi("cv_doesnotexist")));
    }

    [TestMethod]
    public void VariableString_ReturnsEmptyString_ForUnknownVariable()
    {
        Assert.AreEqual(string.Empty, Str(Cvar.VariableString(Ansi("cv_alsomissing"))));
    }

    [TestMethod]
    public void Set_UpdatesValueAndString()
    {
        Cvar.RegisterVariable(MakeCvar("cv_settest", "0"));

        Cvar.Set(Ansi("cv_settest"), Ansi("42"));

        Assert.AreEqual(42f, Cvar.VariableValue(Ansi("cv_settest")));
        Assert.AreEqual("42", Str(Cvar.VariableString(Ansi("cv_settest"))));
    }

    [TestMethod]
    public void Set_ServerVariableChanged_BroadcastsToActiveClients()
    {
        Cvar_t* v = MakeCvar("cv_srvtest", "0");
        v->Server = true;
        Cvar.RegisterVariable(v);

        Sv.active = true;
        Svs.maxclients = 1;
        Svs.clients[0] = default;
        Svs.clients[0].Active = true;
        Svs.clients[0].Spawned = true;
        Svs.clients[0].Message.Data = NativeMemory.AllocArrayZeroed<byte>(QuakeDef.MAX_MSGLEN);
        Svs.clients[0].Message.MaxSize = QuakeDef.MAX_MSGLEN;
        try
        {
            Cvar.Set(Ansi("cv_srvtest"), Ansi("1"));

            Assert.IsGreaterThan(0, Svs.clients[0].Message.CurSize);
        }
        finally
        {
            NativeMemory.Free(Svs.clients[0].Message.Data);
            Svs.clients[0] = default;
            Sv.active = false;
        }
    }

    [TestMethod]
    public void Set_ServerVariableUnchanged_DoesNotBroadcast()
    {
        Cvar_t* v = MakeCvar("cv_srvtest2", "1");
        v->Server = true;
        Cvar.RegisterVariable(v);

        Sv.active = true;
        Svs.maxclients = 1;
        Svs.clients[0] = default;
        Svs.clients[0].Active = true;
        Svs.clients[0].Spawned = true;
        Svs.clients[0].Message.Data = NativeMemory.AllocArrayZeroed<byte>(QuakeDef.MAX_MSGLEN);
        Svs.clients[0].Message.MaxSize = QuakeDef.MAX_MSGLEN;
        try
        {
            Cvar.Set(Ansi("cv_srvtest2"), Ansi("1")); // same value -> "changed" is false

            Assert.AreEqual(0, Svs.clients[0].Message.CurSize);
        }
        finally
        {
            NativeMemory.Free(Svs.clients[0].Message.Data);
            Svs.clients[0] = default;
            Sv.active = false;
        }
    }

    [TestMethod]
    public void SetValue_FormatsFloatAndUpdatesValue()
    {
        Cvar.RegisterVariable(MakeCvar("cv_setvaluetest", "0"));

        Cvar.SetValue(Ansi("cv_setvaluetest"), 7f);

        Assert.AreEqual(7f, Cvar.VariableValue(Ansi("cv_setvaluetest")));
    }

    [TestMethod]
    public void SetValue_CommaDecimalCulture_StillWritesDecimalPoint()
    {
        // sprintf("%f") is locale-independent in practice; a culture-aware
        // ToString would write "0,500000", which Q.Atof reads back as 0
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            Cvar.RegisterVariable(MakeCvar("cv_setvalueculture", "0"));

            Cvar.SetValue(Ansi("cv_setvalueculture"), 0.5f);

            Assert.AreEqual("0.500000", Str(Cvar.VariableString(Ansi("cv_setvalueculture"))));
            Assert.AreEqual(0.5f, Cvar.VariableValue(Ansi("cv_setvalueculture")));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void CompleteVariable_MatchesPartialPrefix()
    {
        Cvar.RegisterVariable(MakeCvar("cv_completeme", "0"));

        byte* match = Cvar.CompleteVariable(Ansi("cv_comple"));
        Assert.AreEqual("cv_completeme", Str(match));
        Assert.IsTrue(Cvar.CompleteVariable(Ansi("cv_nomatchxyz")) == null);
    }

    [TestMethod]
    public void RegisterVariable_RefusesDuplicateName()
    {
        Cvar.RegisterVariable(MakeCvar("cv_duptest", "1"));
        Cvar_t* dup = MakeCvar("cv_duptest", "2");
        Cvar.RegisterVariable(dup);

        // the original value should be untouched; the duplicate registration
        // is rejected (with a console warning), matching Cvar_RegisterVariable
        Assert.AreEqual(1f, Cvar.VariableValue(Ansi("cv_duptest")));
    }

    [TestMethod]
    public void Command_PrintsExistingVariable_WhenNoValueGiven()
    {
        Cvar.RegisterVariable(MakeCvar("cv_cmdtest", "9"));
        Cmd.TokenizeString(Ansi("cv_cmdtest\n"));

        Assert.IsTrue(Cvar.Command());
    }

    [TestMethod]
    public void Command_SetsVariable_WhenValueGiven()
    {
        Cvar.RegisterVariable(MakeCvar("cv_cmdsettest", "0"));
        Cmd.TokenizeString(Ansi("cv_cmdsettest 5\n"));

        Assert.IsTrue(Cvar.Command());
        Assert.AreEqual(5f, Cvar.VariableValue(Ansi("cv_cmdsettest")));
    }

    [TestMethod]
    public void Command_ReturnsFalse_ForUnknownName()
    {
        Cmd.TokenizeString(Ansi("cv_totally_unknown_name\n"));
        Assert.IsFalse(Cvar.Command());
    }

    [TestMethod]
    public void WriteVariables_OnlyEmitsArchivedVariables()
    {
        Cvar.RegisterVariable(MakeCvar("cv_archived", "1", archive: true));
        Cvar.RegisterVariable(MakeCvar("cv_notarchived", "2", archive: false));

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            int handle = Sys.FileOpenWrite(Ansi(path));
            Cvar.WriteVariables(handle);
            Sys.FileClose(handle);

            string contents = File.ReadAllText(path);
            StringAssert.Contains(contents, "cv_archived \"1\"");
            StringAssert.DoesNotMatch(contents, new System.Text.RegularExpressions.Regex("cv_notarchived"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
