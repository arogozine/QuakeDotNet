// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class CacheReportTests
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

    // zone.c Cache_Report: Con_DPrintf ("%4.1f megabyte data cache\n", ...)
    [TestMethod]
    public void Cache_Report_UnderTenMegabytes_PadsToWidthFour()
    {
        float savedDeveloper = Host.developer->Value;
        int savedLow = Hunk.hunk_low_used;
        int savedHigh = Hunk.hunk_high_used;
        try
        {
            Host.developer->Value = 1;
            Hunk.hunk_low_used = Hunk.hunk_size - 512 * 1024;
            Hunk.hunk_high_used = 0;

            string output = CaptureOutput(Cache.Report);

            Assert.AreEqual(" 0.5 megabyte data cache\n", output);
        }
        finally
        {
            Host.developer->Value = savedDeveloper;
            Hunk.hunk_low_used = savedLow;
            Hunk.hunk_high_used = savedHigh;
        }
    }
}
