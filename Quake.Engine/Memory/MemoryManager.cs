// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

/// <summary>
/// Port of the WinMain heap-sizing/allocation logic (sys_win.c) plus
/// Z_zone.c's Memory_Init top-level entry point.
/// </summary>
public static unsafe class MemoryManager
{
    private const int MINIMUM_WIN_MEMORY = 0x0880000;
    // quakedef.h MEM_DEFAULT_MEMORY (Mark V: 256 MB; was 16 MB) -- 32000 edicts alone need tens of MB
    private const int MAXIMUM_WIN_MEMORY = 256 * 1024 * 1024;


    public static byte* MemBase;
    public static int MemSize;

    // WinMain's heap sizing + malloc(parms.membase) + Sys_PageIn, entry point.
    // Deviates from the original's Win32 GlobalMemoryStatusEx call (rule 5):
    // GC.GetGCMemoryInfo() reports the same total/available physical memory
    // figures cross-platform instead of relying on a Windows-only API.
    public static void AllocateHostMemory(int? heapSizeKbOverride)
    {
        GCMemoryInfo status = GC.GetGCMemoryInfo();
        long totalPhys = status.TotalAvailableMemoryBytes;
        long availPhys = Math.Max(0, totalPhys - status.MemoryLoadBytes);

        AllocateHostMemory(availPhys, totalPhys, heapSizeKbOverride);
    }

    public static void AllocateHostMemory(long availPhys, long totalPhys, int? heapSizeKbOverride)
    {
        long memsize = availPhys;

        if (memsize < MINIMUM_WIN_MEMORY)
        {
            memsize = MINIMUM_WIN_MEMORY;
        }

        if (memsize < (totalPhys >> 1))
        {
            memsize = totalPhys >> 1;
        }

        if (memsize > MAXIMUM_WIN_MEMORY)
        {
            memsize = MAXIMUM_WIN_MEMORY;
        }

        if (heapSizeKbOverride is int kb)
        {
            memsize = kb * 1024L;
        }

        // checked deliberately (rule 2 deviation, justified per rule 8): the original silently
        // truncates memsize to a 32-bit int here; throwing on overflow
        // surfaces a misconfigured heap size instead of corrupting it.
        MemSize = checked((int)memsize);
        MemBase = NativeMemory.AllocArray<byte>(MemSize);

        if (MemBase == null)
        {
            Sys.Error("Not enough memory free; check disk space\n"u8);
        }


        // Memory_Init is NOT called here: the original only calls it once,
        // from inside Host_Init (host.c:854), not from WinMain's heap-sizing
        // code. Calling it here too would re-run it a second time when
        // Host.Init calls it itself, re-carving the Hunk/zone out from under
        // anything Cache_Init/Cmd_AddCommand("flush") already allocated in
        // this first pass and corrupting the still-referenced cmd_functions
        // list.
    }

    // Sys_PageIn - Removed.

    // Memory_Init
    public static void Memory_Init(void* buf, int size)
    {
        int zonesize = Zone.DYNAMIC_SIZE;

        Hunk.hunk_base = (byte*)buf;
        Hunk.hunk_size = size;
        Hunk.hunk_low_used = 0;
        Hunk.hunk_high_used = 0;

        Cache.Init();

        int p = Com.CheckParm("-zone"u8);
        if (p != 0)
        {
            if (p < Com.com_argc - 1)
            {
                zonesize = Q.Atoi(Com.com_argv[p + 1]) * 1024;
            }
            else
            {
                Sys.Error("Memory_Init: you must specify a size in KB after -zone"u8);
            }
        }

        Zone.mainzone = (MemZone*)Hunk.AllocName(zonesize, "zone"u8);
        Zone.ClearZone(Zone.mainzone, zonesize);
    }

    // Free the backing block; no C equivalent (the original process just exits).
    public static void Shutdown()
    {
        if (MemBase != null)
        {
            NativeMemory.Free(MemBase);
            MemBase = null;
            MemSize = 0;
        }
    }
}
