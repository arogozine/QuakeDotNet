// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Diagnostics;

namespace Quake.UnitTests;

[TestClass]
public sealed class SysTests
{
    // PAUSE_SLEEP/NOT_FOCUS_SLEEP -- sys_win.c's #defines (50ms/20ms).
    // MSTEST0032 flags these as "always true" because both sides are
    // compile-time constants -- that's the point: this pins the port's
    // constants to the original's values.
    [TestMethod]
    public void SleepConstants_MatchOriginal()
    {
#pragma warning disable MSTEST0032
        Assert.AreEqual(50, Sys.PAUSE_SLEEP);
        Assert.AreEqual(20, Sys.NOT_FOCUS_SLEEP);
#pragma warning restore MSTEST0032
    }

    // SleepUntilInput -- no Win32 wait handle to signal early in this port
    // (see its own comment), so it should just block for at least the
    // requested duration.
    [TestMethod]
    public void SleepUntilInput_BlocksForAtLeastRequestedDuration()
    {
        Stopwatch sw = Stopwatch.StartNew();
        Sys.SleepUntilInput(30);
        sw.Stop();

        Assert.IsGreaterThanOrEqualTo(25L, sw.ElapsedMilliseconds);
    }
}
