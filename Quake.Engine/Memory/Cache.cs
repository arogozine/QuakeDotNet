// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

/// <summary>
/// Z_zone.c — cache memory section.
/// </summary>
public static unsafe class Cache
{
    internal readonly static CacheSystem* cache_head = NativeMemory.AllocZeroed<CacheSystem>();

    // Cache_Move
    public static void Move(CacheSystem* c)
    {
        CacheSystem* @new;

        // we are clearing up space at the bottom, so only allocate it late
        @new = TryAlloc(c->Size, true);
        if (@new != null)
        {
            NativeMemory.Copy((byte*)(c + 1), (byte*)(@new + 1), c->Size - sizeof(CacheSystem));
            @new->User = c->User;
            NativeMemory.Copy(c->Name, @new->Name, 16);
            Free(c->User);
            @new->User->Data = (void*)(@new + 1);
        }
        else
        {
            Free(c->User);      // tough luck...
        }
    }

    // Cache_FreeLow
    public static void FreeLow(int new_low_hunk)
    {
        CacheSystem* c;

        while (true)
        {
            c = cache_head->Next;
            if (c == cache_head)
                return;     // nothing in cache at all
            if ((byte*)c >= Hunk.hunk_base + new_low_hunk)
                return;     // there is space to grow the hunk
            Move(c);        // reclaim the space
        }
    }

    // Cache_FreeHigh
    public static void FreeHigh(int new_high_hunk)
    {
        CacheSystem* c, prev;

        prev = null;
        while (true)
        {
            c = cache_head->Prev;
            if (c == cache_head)
                return;     // nothing in cache at all
            if ((byte*)c + c->Size <= Hunk.hunk_base + Hunk.hunk_size - new_high_hunk)
                return;     // there is space to grow the hunk
            if (c == prev)
                Free(c->User);   // didn't move out of the way
            else
            {
                Move(c);    // try to move it
                prev = c;
            }
        }
    }

    // Cache_UnlinkLRU
    public static void UnlinkLRU(CacheSystem* cs)
    {
        if (cs->LruNext == null || cs->LruPrev == null)
            Sys.Error("Cache_UnlinkLRU: NULL link"u8);

        cs->LruNext->LruPrev = cs->LruPrev;
        cs->LruPrev->LruNext = cs->LruNext;

        cs->LruPrev = cs->LruNext = null;
    }

    // Cache_MakeLRU
    public static void MakeLRU(CacheSystem* cs)
    {
        if (cs->LruNext != null || cs->LruPrev != null)
            Sys.Error("Cache_MakeLRU: active link"u8);

        cache_head->LruNext->LruPrev = cs;
        cs->LruNext = cache_head->LruNext;
        cs->LruPrev = cache_head;
        cache_head->LruNext = cs;
    }

    // Cache_TryAlloc
    public static CacheSystem* TryAlloc(int size, bool nobottom)
    {
        CacheSystem* cs, @new;

        // is the cache completely empty?

        if (!nobottom && cache_head->Prev == cache_head)
        {
            if (Hunk.hunk_size - Hunk.hunk_high_used - Hunk.hunk_low_used < size)
                Sys.Error($"Cache_TryAlloc: {size} is greater then free hunk");

            @new = (CacheSystem*)(Hunk.hunk_base + Hunk.hunk_low_used);
            NativeMemory.Clear(@new);
            @new->Size = size;

            cache_head->Prev = cache_head->Next = @new;
            @new->Prev = @new->Next = cache_head;

            MakeLRU(@new);
            return @new;
        }

        // search from the bottom up for space

        @new = (CacheSystem*)(Hunk.hunk_base + Hunk.hunk_low_used);
        cs = cache_head->Next;

        do
        {
            if (!nobottom || cs != cache_head->Next)
            {
                if ((byte*)cs - (byte*)@new >= size)
                {   // found space
                    NativeMemory.Clear(@new);
                    @new->Size = size;

                    @new->Next = cs;
                    @new->Prev = cs->Prev;
                    cs->Prev->Next = @new;
                    cs->Prev = @new;

                    MakeLRU(@new);

                    return @new;
                }
            }

            // continue looking
            @new = (CacheSystem*)((byte*)cs + cs->Size);
            cs = cs->Next;

        } while (cs != cache_head);

        // try to allocate one at the very end
        if (Hunk.hunk_base + Hunk.hunk_size - Hunk.hunk_high_used - (byte*)@new >= size)
        {
            NativeMemory.Clear(@new);
            @new->Size = size;

            @new->Next = cache_head;
            @new->Prev = cache_head->Prev;
            cache_head->Prev->Next = @new;
            cache_head->Prev = @new;

            MakeLRU(@new);

            return @new;
        }

        return null;        // couldn't allocate
    }

    // Cache_Flush
    public static void Flush()
    {
        while (cache_head->Next != cache_head)
            Free(cache_head->Next->User);   // reclaim the space
    }

    // Cache_Print

    // Cache_Report
    public static void Report()
    {
        Con.DPrintf($"{(Hunk.hunk_size - Hunk.hunk_high_used - Hunk.hunk_low_used) / (float)(1024 * 1024),4:F1} megabyte data cache\n");
    }

    // Cache_Compact

    // Cache_Init
    public static void Init()
    {
        cache_head->Next = cache_head->Prev = cache_head;
        cache_head->LruNext = cache_head->LruPrev = cache_head;

        // Cmd_AddCommand ("flush", Cache_Flush)
        Cmd.AddCommand("flush"u8, &Flush);
    }

    // Cache_Free
    public static void Free(CacheUser* c)
    {
        CacheSystem* cs;

        if (c->Data == null)
            Sys.Error("Cache_Free: not allocated"u8);

        cs = ((CacheSystem*)c->Data) - 1;

        cs->Prev->Next = cs->Next;
        cs->Next->Prev = cs->Prev;
        cs->Next = cs->Prev = null;

        c->Data = null;

        UnlinkLRU(cs);
    }

    // Cache_Check
    public static void* Check(CacheUser* c)
    {
        CacheSystem* cs;

        if (c->Data == null)
            return null;

        cs = ((CacheSystem*)c->Data) - 1;

        // move to head of LRU
        UnlinkLRU(cs);
        MakeLRU(cs);

        return c->Data;
    }

    // Cache_Alloc
    public static void* Alloc(CacheUser* c, int size, Utf8String name)
    {
        CacheSystem* cs;

        if (c->Data != null)
            Sys.Error("Cache_Alloc: allready allocated"u8);

        if (size <= 0)
            Sys.Error($"Cache_Alloc: size {size}");

        // No overflow check in WinQuake or Mark V; debug-only guard.
        Debug.Assert((long)size + sizeof(CacheSystem) + 15 <= int.MaxValue, "Cache_Alloc: size overflow");
        size = (size + sizeof(CacheSystem) + 15) & ~15;

        // find memory for it
        while (true)
        {
            cs = TryAlloc(size, false);
            if (cs != null)
            {
                CStr.Strncpy(cs->Name, name.AsSpan(), 15);
                c->Data = (void*)(cs + 1);
                cs->User = c;
                break;
            }

            // free the least recently used cahedat
            if (cache_head->LruPrev == cache_head)
                Sys.Error("Cache_Alloc: out of memory"u8);
            // not enough memory at all
            Free(cache_head->LruPrev->User);
        }

        return Check(c);
    }
}
