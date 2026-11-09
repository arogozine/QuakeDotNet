// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

/// <summary>
/// Z_zone.c — hunk allocator section.
/// </summary>
public static unsafe class Hunk
{
    private const int HUNK_SENTINAL = 0x1df001ed;

    internal static byte* hunk_base;
    internal static int hunk_size;

    internal static int hunk_low_used;
    internal static int hunk_high_used;

    internal static bool hunk_tempactive;
    internal static int hunk_tempmark;

    // Hunk_Check
    public static void Check()
    {
        HunkHeader* h;

        for (h = (HunkHeader*)hunk_base; (byte*)h != hunk_base + hunk_low_used;)
        {
            if (h->Sentinel != HUNK_SENTINAL)
                Sys.Error("Hunk_Check: trahsed sentinal"u8);
            if (h->Size < 16 || h->Size + (byte*)h - hunk_base > hunk_size)
                Sys.Error("Hunk_Check: bad size"u8);
            h = (HunkHeader*)((byte*)h + h->Size);
        }
    }

    // Hunk_Print

    // Hunk_AllocName
    public static void* AllocName(int size, Utf8String name)
    {
        return AllocName(size, name.AsSpan());
    }

    public static void* AllocName(int size, ReadOnlySpan<byte> u8Name)
    {
        HunkHeader* h;

        if (size < 0)
            Sys.Error($"Hunk_Alloc: bad size: {size}");

        size = sizeof(HunkHeader) + ((size + 15) & ~15);

        if (hunk_size - hunk_low_used - hunk_high_used < size)
            Sys.Error($"Hunk_Alloc: failed on {size} bytes");

        h = (HunkHeader*)(hunk_base + hunk_low_used);
        hunk_low_used += size;

        Cache.FreeLow(hunk_low_used);

        NativeMemory.Clear((byte*)h, size);

        h->Size = size;
        h->Sentinel = HUNK_SENTINAL;
        CStr.QStrncpy(h->Name, u8Name, 8);

        return (void*)(h + 1);
    }

    // Hunk_Alloc
    public static T* Alloc<T>()
        where T : unmanaged
    {
        return (T*)Alloc(sizeof(T));
    }

    public static void* Alloc(int size)
    {
        return AllocName(size, "unknown"u8);
    }

    // Hunk_LowMark
    public static int LowMark()
    {
        return hunk_low_used;
    }

    // Hunk_FreeToLowMark
    public static void FreeToLowMark(int mark)
    {
        if (mark < 0 || mark > hunk_low_used)
            Sys.Error($"Hunk_FreeToLowMark: bad mark {mark}");
        NativeMemory.Clear(hunk_base + mark, hunk_low_used - mark);
        hunk_low_used = mark;
    }

    // Hunk_HighMark
    public static int HighMark()
    {
        if (hunk_tempactive)
        {
            hunk_tempactive = false;
            FreeToHighMark(hunk_tempmark);
        }

        return hunk_high_used;
    }

    // Hunk_FreeToHighMark
    public static void FreeToHighMark(int mark)
    {
        if (hunk_tempactive)
        {
            hunk_tempactive = false;
            FreeToHighMark(hunk_tempmark);
        }
        if (mark < 0 || mark > hunk_high_used)
            Sys.Error($"Hunk_FreeToHighMark: bad mark {mark}");
        NativeMemory.Clear(hunk_base + hunk_size - hunk_high_used, hunk_high_used - mark);
        hunk_high_used = mark;
    }

    // Hunk_HighAllocName
    public static void* HighAllocName(int size, Utf8String name)
    {
        return HighAllocName(size, name.AsSpan());
    }

    public static void* HighAllocName(int size, ReadOnlySpan<byte> u8Name)
    {
        HunkHeader* h;

        if (size < 0)
            Sys.Error($"Hunk_HighAllocName: bad size: {size}");

        if (hunk_tempactive)
        {
            FreeToHighMark(hunk_tempmark);
            hunk_tempactive = false;
        }

        size = sizeof(HunkHeader) + ((size + 15) & ~15);

        if (hunk_size - hunk_low_used - hunk_high_used < size)
        {
            Con.Printf($"Hunk_HighAlloc: failed on {size} bytes\n");
            return null;
        }

        hunk_high_used += size;
        Cache.FreeHigh(hunk_high_used);

        h = (HunkHeader*)(hunk_base + hunk_size - hunk_high_used);

        NativeMemory.Clear((byte*)h, size);
        h->Size = size;
        h->Sentinel = HUNK_SENTINAL;
        CStr.QStrncpy(h->Name, u8Name, 8);

        return (void*)(h + 1);
    }

    // strncmp (h->name, next->name, 8)

    // Hunk_TempAlloc
    public static void* TempAlloc(int size)
    {
        void* buf;

        size = (size + 15) & ~15;

        if (hunk_tempactive)
        {
            FreeToHighMark(hunk_tempmark);
            hunk_tempactive = false;
        }

        hunk_tempmark = HighMark();

        buf = HighAllocName(size, "temp"u8);

        hunk_tempactive = true;

        return buf;
    }
}
