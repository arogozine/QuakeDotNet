// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine.Memory;

/// <summary>
/// Z_zone.c
/// </summary>
public static unsafe class Zone
{
    internal const int DYNAMIC_SIZE = 0xc000; // DYNAMIC_SIZE (zone.c)

    private const int ZONEID = 0x1d4a11;
    private const int MINFRAGMENT = 64;

    internal static MemZone* mainzone;

    // Z_ClearZone
    public static void ClearZone(MemZone* zone, int size)
    {
        MemBlock* block;

        // set the entire zone to one free block

        zone->BlockList.Next = zone->BlockList.Prev = block =
            (MemBlock*)((byte*)zone + sizeof(MemZone));
        zone->BlockList.Tag = 1;   // in use block
        zone->BlockList.Id = 0;
        zone->BlockList.Size = 0;
        zone->Rover = block;

        block->Prev = block->Next = &zone->BlockList;
        block->Tag = 0;         // free block
        block->Id = ZONEID;
        block->Size = size - sizeof(MemZone);
    }

    // Z_Free
    public static void Free(void* ptr)
    {
        MemBlock* block, other;

        if (ptr == null)
            Sys.Error("Z_Free: NULL pointer"u8);

        block = (MemBlock*)((byte*)ptr - sizeof(MemBlock));
        if (block->Id != ZONEID)
            Sys.Error("Z_Free: freed a pointer without ZONEID"u8);
        if (block->Tag == 0)
            Sys.Error("Z_Free: freed a freed pointer"u8);

        block->Tag = 0;     // mark as free

        other = block->Prev;
        if (other->Tag == 0)
        {   // merge with previous free block
            other->Size += block->Size;
            other->Next = block->Next;
            other->Next->Prev = other;
            if (block == mainzone->Rover)
                mainzone->Rover = other;
            block = other;
        }

        other = block->Next;
        if (other->Tag == 0)
        {   // merge the next free block onto the end
            block->Size += other->Size;
            block->Next = other->Next;
            block->Next->Prev = block;
            if (other == mainzone->Rover)
                mainzone->Rover = block;
        }
    }

    // Z_Malloc
    public static void* Malloc(int size)
    {
        void* buf;

        CheckHeap(); // DEBUG
        buf = TagMalloc(size, 1);
        if (buf == null)
            Sys.Error($"Z_Malloc: failed on allocation of {size} bytes");
        NativeMemory.Clear(buf, (nuint)size);

        return buf;
    }

    // Z_TagMalloc
    public static void* TagMalloc(int size, int tag)
    {
        int extra;
        MemBlock* start, rover, @new, @base;

        if (tag == 0)
            Sys.Error("Z_TagMalloc: tried to use a 0 tag"u8);

        //
        // scan through the block list looking for the first free block
        // of sufficient size
        //
        size += sizeof(MemBlock);  // account for size of block header
        size += 4;                 // space for memory trash tester
        size = (size + 7) & ~7;    // align to 8-byte boundary

        @base = rover = mainzone->Rover;
        start = @base->Prev;

        do
        {
            if (rover == start)    // scaned all the way around the list
                return null;
            if (rover->Tag != 0)
                @base = rover = rover->Next;
            else
                rover = rover->Next;
        } while (@base->Tag != 0 || @base->Size < size);

        //
        // found a block big enough
        //
        extra = @base->Size - size;
        if (extra > MINFRAGMENT)
        {   // there will be a free fragment after the allocated block
            @new = (MemBlock*)((byte*)@base + size);
            @new->Size = extra;
            @new->Tag = 0;          // free block
            @new->Prev = @base;
            @new->Id = ZONEID;
            @new->Next = @base->Next;
            @new->Next->Prev = @new;
            @base->Next = @new;
            @base->Size = size;
        }

        @base->Tag = tag;              // no longer a free block

        mainzone->Rover = @base->Next; // next allocation will start looking here

        @base->Id = ZONEID;

        // marker for memory trash testing
        *(int*)((byte*)@base + @base->Size - 4) = ZONEID;

        return (void*)((byte*)@base + sizeof(MemBlock));
    }

    // Z_Print
    public static void Print(MemZone* zone)
    {
        MemBlock* block;

        Con.Printf($"zone size: {mainzone->Size}  location: {(nint)mainzone:X}\n");

        for (block = zone->BlockList.Next; ; block = block->Next)
        {
            Con.Printf($"block:{(nint)block:X}    size:{block->Size,7}    tag:{block->Tag,3}\n");

            if (block->Next == &zone->BlockList)
                break;          // all blocks have been hit
            if ((byte*)block + block->Size != (byte*)block->Next)
                Con.Printf("ERROR: block size does not touch the next block\n"u8);
            if (block->Next->Prev != block)
                Con.Printf("ERROR: next block doesn't have proper back link\n"u8);
            if (block->Tag == 0 && block->Next->Tag == 0)
                Con.Printf("ERROR: two consecutive free blocks\n"u8);
        }
    }

    // Z_CheckHeap
    public static void CheckHeap()
    {
        MemBlock* block;

        for (block = mainzone->BlockList.Next; ; block = block->Next)
        {
            if (block->Next == &mainzone->BlockList)
                break;          // all blocks have been hit
            if ((byte*)block + block->Size != (byte*)block->Next)
                Sys.Error("Z_CheckHeap: block size does not touch the next block\n"u8);
            if (block->Next->Prev != block)
                Sys.Error("Z_CheckHeap: next block doesn't have proper back link\n"u8);
            if (block->Tag == 0 && block->Next->Tag == 0)
                Sys.Error("Z_CheckHeap: two consecutive free blocks\n"u8);
        }
    }

    // Z_DumpHeap

    // Z_FreeMemory
    public static int FreeMemory()
    {
        int free = 0;
        for (MemBlock* block = mainzone->BlockList.Next; block != &mainzone->BlockList; block = block->Next)
        {
            if (block->Tag == 0)
                free += block->Size;
        }
        return free;
    }
}
