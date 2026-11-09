// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

// Shared, deliberately narrow test helpers -- only what's identical across
// test files gets pulled in here (test files import it with
// `using static Quake.UnitTests.TestHelpers;`). Engine state is reset to
// process-fresh before every test by EngineStateGuard; each class's own
// [TestInitialize] then sets up just the subset of engine state it needs.
internal static unsafe class TestHelpers
{
    // Intentionally leaks the HGlobal allocation: engine statics (cvar
    // strings, command args, ...) may keep pointing at it after the call
    // returns, and freeing it would reintroduce a dangling pointer.
    public static byte* Ansi(string s) => (byte*)Marshal.StringToHGlobalAnsi(s);

    // Blocks until the NetIp socket `handle` has a datagram waiting (or the
    // timeout expires), instead of guessing with a fixed Thread.Sleep --
    // loopback delivery is normally immediate, but not guaranteed on a
    // loaded machine. Reads NetIp's private socket table because the driver
    // has no non-consuming "is anything pending" call for arbitrary sockets.
    public static void WaitForData(int handle, int timeoutMs = 2000)
    {
        var sockets = (Dictionary<int, System.Net.Sockets.Socket>)typeof(Quake.Engine.NetIp)
            .GetField("sockets", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;
        Assert.IsTrue(sockets[handle].Poll(timeoutMs * 1000, System.Net.Sockets.SelectMode.SelectRead),
            $"no datagram arrived on NetIp socket {handle} within {timeoutMs} ms");
    }

    // Matches SvPhysTests.cs's private AllocZeroed<T>.
    public static T* AllocZeroed<T>(List<nint> tracker, int count) where T : unmanaged
    {
        T* p = NativeMemory.AllocArrayZeroed<T>(count);
        tracker.Add((nint)p);
        return p;
    }
}
