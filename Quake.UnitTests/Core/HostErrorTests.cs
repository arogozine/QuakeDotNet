// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// host.c's Host_Error/Host_EndGame -- both unwind via Host.QuakeHostAbortException
// (the setjmp(host_abortserver)/longjmp equivalent Host.Frame's own try/catch
// recovers from), shutting down an active server and disconnecting the
// client first, same as the original. Shares Cls/Sv/Svs/ClDemo static state
// with the other DoNotParallelize classes touching them.
[TestClass]
public sealed unsafe class HostErrorTests
{
    [TestInitialize]
    public void Setup()
    {
        Sv.active = false;
        Cls.state = CActive.Disconnected;
        ClDemo.demoplayback = false;
        ClDemo.demorecording = false;
        ClDemo.demonum = -1;
        NativeMemory.Clear(ClDemo.demos, ClDemo.MAX_DEMOS * ClDemo.MAX_DEMONAME);
        Host.inerror = false;
    }

    [TestCleanup]
    public void Teardown()
    {
        Host.inerror = false;
    }

    [TestMethod]
    public void Error_ServerInactive_Disconnects_ThenThrowsAbort()
    {
        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => Host.Error($"boom {42}"));

        Assert.AreEqual(-1, ClDemo.demonum);
        Assert.IsFalse(Host.inerror); // reset before the throw, matching the original
    }

    [TestMethod]
    public void Error_InterpolatedHandler_Disconnects_ThenThrowsAbort()
    {
        int n = 42;
        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => Host.Error($"boom {n}"));

        Assert.AreEqual(-1, ClDemo.demonum);
        Assert.IsFalse(Host.inerror);
    }

    [TestMethod]
    public void SysError_InterpolatedHandler_MessageMatchesStringOverload()
    {
        int n = 7;
        var ex = Assert.ThrowsExactly<QuakeSysErrorException>(() => Sys.Error($"model {n} has no vertices"));

        Assert.AreEqual("model 7 has no vertices", ex.Message);
    }

    [TestMethod]
    public void Error_Dedicated_ThrowsSysErrorInstead()
    {
        Cls.state = CActive.Dedicated;

        Assert.ThrowsExactly<QuakeSysErrorException>(() => Host.Error($"dedicated crash"));
    }

    [TestMethod]
    public void Error_RecursiveCall_ThrowsSysError()
    {
        Host.inerror = true;

        Assert.ThrowsExactly<QuakeSysErrorException>(() => Host.Error($"nested"));
    }

    [TestMethod]
    public void EndGame_ServerInactive_DemoLoopOff_Disconnects_ThenThrowsAbort()
    {
        ClDemo.demonum = -1;

        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => Host.EndGame($"goodbye"));
    }

    [TestMethod]
    public void EndGame_Dedicated_ThrowsSysErrorInstead()
    {
        Cls.state = CActive.Dedicated;

        Assert.ThrowsExactly<QuakeSysErrorException>(() => Host.EndGame($"dedicated end"));
    }

    [TestMethod]
    public void EndGame_DemoLoopOn_NoDemosListed_CallsNextDemo_ThenThrowsAbort()
    {
        // demos[] is all-zero (Setup) -- CL_NextDemo's "no demos listed"
        // branch resets demonum to -1 and returns without needing a real
        // Cmd_InsertText/"playdemo" round trip.
        ClDemo.demonum = 0;

        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => Host.EndGame($"loop"));

        Assert.AreEqual(-1, ClDemo.demonum);
    }
}
