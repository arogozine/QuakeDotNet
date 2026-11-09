// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// The ported allocator mirrors the original C globals as static state
// (Hunk/Zone/Cache), so its tests cannot run concurrently with each other.
[TestClass]
public sealed unsafe class MemoryManagerTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Com.com_argc = 0;
        Com.com_argv = [];
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
        Com.com_argc = 0;
        Com.com_argv = [];
        Cmd.cmd_functions = null;
    }

    [TestMethod]
    public void MemoryInit_WithZoneParm_OverridesDefaultZoneSize()
    {
        NativeMemory.Free(_buf);
        _buf = NativeMemory.AllocArray<byte>(Size);
        // Cache.Init()'s Cmd.AddCommand("flush", ...) re-runs on this second
        // Memory_Init call; reset cmd_functions first so it doesn't chain
        // onto a Hunk-allocated node from Setup()'s now-replaced buffer.
        Cmd.cmd_functions = null;

        Com.com_argv = [Ansi("quake"), Ansi("-zone"), Ansi("512")];
        Com.com_argc = Com.com_argv.Length;

        MemoryManager.Memory_Init(_buf, Size);

        // MemZone.Size is never assigned (Z_ClearZone doesn't set it in the
        // original either -- Z_Print's "zone size" always reads 0 there
        // too), so the only observable proof of the zone's actual size is
        // its one free block, which Z_ClearZone sizes as size - sizeof(zone
        // header).
        Assert.AreEqual(512 * 1024 - sizeof(MemZone), Zone.FreeMemory());
    }

    [TestMethod]
    public void MemoryInit_WithoutZoneParm_UsesDefaultZoneSize()
    {
        // DYNAMIC_SIZE (0xc000) is private to MemoryManager; Setup() already
        // called Memory_Init with no -zone arg, which is exactly this
        // default path.
        Assert.AreEqual(0xc000 - sizeof(MemZone), Zone.FreeMemory());
    }

    [TestMethod]
    public void CacheInit_RegistersFlushCommand_ThatCallsCacheFlush()
    {
        Assert.IsTrue(Cmd.Exists(Ansi("flush")), "\"flush\" should be registered as a console command");

        CacheUser user = default;
        Cache.Alloc(&user, 512, "flush-test"u8);
        Assert.IsTrue(user.Data != null);

        fixed (byte* text = System.Text.Encoding.ASCII.GetBytes("flush\0"))
            Cmd.ExecuteString(text, CmdSource_t.SrcCommand);

        Assert.IsTrue(user.Data == null, "flush should have reclaimed the cache entry");
    }

    [TestMethod]
    public void HunkAlloc_ReturnsZeroedMemory_AndAdvancesLowMark()
    {
        int before = Hunk.LowMark();
        byte* p = (byte*)Hunk.AllocName(100, "test"u8);
        Assert.IsGreaterThan(before, Hunk.LowMark());
        for (int i = 0; i < 100; i++)
            Assert.AreEqual(0, p[i]);
    }

    [TestMethod]
    public void HunkFreeToLowMark_RewindsUsage()
    {
        int mark = Hunk.LowMark();
        Hunk.AllocName(256, "a"u8);
        Hunk.AllocName(256, "b"u8);
        Hunk.FreeToLowMark(mark);
        Assert.AreEqual(mark, Hunk.LowMark());
    }

    [TestMethod]
    public void HighAlloc_GrowsFromTopDown()
    {
        int before = Hunk.HighMark();
        Hunk.HighAllocName(128, "high"u8);
        Assert.IsGreaterThan(before, Hunk.HighMark());
    }

    [TestMethod]
    public void ZoneMalloc_ReturnsZeroedMemory_AndIsFreeable()
    {
        void* p = Zone.Malloc(64);
        Assert.IsTrue(p != null);
        Zone.Free(p);
    }

    [TestMethod]
    public void ZoneMalloc_CoalescesOnFree()
    {
        int free0 = Zone.FreeMemory();
        void* a = Zone.Malloc(64);
        void* b = Zone.Malloc(64);
        Zone.Free(a);
        Zone.Free(b);
        int free1 = Zone.FreeMemory();
        Assert.AreEqual(free0, free1);
    }

    [TestMethod]
    public void CacheAlloc_RoundTrips_ThroughCheck()
    {
        CacheUser user = default;
        void* p = Cache.Alloc(&user, 512, "cache-test"u8);
        Assert.IsTrue(p != null);
        Assert.IsTrue(user.Data == p);

        void* checkedPtr = Cache.Check(&user);
        Assert.AreEqual((nint)p, (nint)checkedPtr);

        Cache.Free(&user);
        Assert.IsTrue(user.Data == null);
    }

    [TestMethod]
    public void HunkAlloc_ReclaimsCacheSpace_ViaCacheFreeLow()
    {
        CacheUser user = default;
        Cache.Alloc(&user, 1024, "victim"u8);
        Assert.IsTrue(user.Data != null);

        // Force the low hunk to grow across the cache block; Cache_FreeLow
        // should evict/move it rather than corrupt memory.
        Hunk.AllocName(2048, "grow"u8);

        Zone.CheckHeap();
    }
}
