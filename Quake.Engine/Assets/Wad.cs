// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// wad.c
/// </summary>
public static unsafe class Wad
{
    // wad_numlumps
    public static int wad_numlumps;

    // wad_lumps
    public static LumpInfo_t* wad_lumps;

    // wad_base
    public static byte* wad_base;

    // W_CleanupName
    // Lowercases name and pads with a terminating 0 to the length of
    // lumpinfo_t->name. Used so lumpname lookups can proceed rapidly by
    // comparing 4 chars at a time. Can safely be performed in place.
    public static void CleanupName(byte* @in, byte* @out)
    {
        int i;
        for (i = 0; i < 16; i++)
        {
            int c = @in[i];
            if (c == 0)
                break;

            if (c is >= (byte)'A' and <= (byte)'Z')
                c += 'a' - 'A';
            @out[i] = (byte)c;
        }

        for (; i < 16; i++)
            @out[i] = 0;
    }

    // W_LoadWadFile
    public static void LoadWadFile(Utf8String filename)
    {
        wad_base = Com.LoadHunkFile(filename);
        if (wad_base == null)
            Sys.Error($"W_LoadWadFile: couldn't load {filename}");

        WadInfo_t* header = (WadInfo_t*)wad_base;

        if (header->Identification[0] != (byte)'W' || header->Identification[1] != (byte)'A' ||
            header->Identification[2] != (byte)'D' || header->Identification[3] != (byte)'2')
            Sys.Error($"Wad file {filename} doesn't have WAD2 id\n");

        wad_numlumps = Com.LittleLong(header->NumLumps);
        int infotableofs = Com.LittleLong(header->InfoTableOfs);
        wad_lumps = (LumpInfo_t*)(wad_base + infotableofs);

        LumpInfo_t* lump_p = wad_lumps;
        for (int i = 0; i < wad_numlumps; i++, lump_p++)
        {
            lump_p->FilePos = Com.LittleLong(lump_p->FilePos);
            lump_p->Size = Com.LittleLong(lump_p->Size);
            CleanupName(lump_p->Name, lump_p->Name);
            if (lump_p->Type == (int)WadLumpType.QPic)
                SwapPic((QPic_t*)(wad_base + lump_p->FilePos));
        }
    }

    // W_GetLumpinfo
    [SkipLocalsInit]
    public static LumpInfo_t* GetLumpinfo(byte* name)
    {
        byte* clean = stackalloc byte[16];
        CleanupName(name, clean);

        LumpInfo_t* lump_p = wad_lumps;
        for (int i = 0; i < wad_numlumps; i++, lump_p++)
            if (Q.StrCmp(clean, lump_p->Name) == 0)
                return lump_p;

        Sys.Error($"W_GetLumpinfo: {name} not found");
        return null;
    }

    // W_GetLumpName
    public static void* GetLumpName(Utf8String name)
    {
        LumpInfo_t* lump = GetLumpinfo(name);
        return wad_base + lump->FilePos;
    }

    // W_GetLumpNum
    public static void* GetLumpNum(int num)
    {
        if (num < 0 || num > wad_numlumps)
            Sys.Error($"W_GetLumpNum: bad number: {num}");

        LumpInfo_t* lump = wad_lumps + num;
        return wad_base + lump->FilePos;
    }

    // SwapPic
    public static void SwapPic(QPic_t* pic)
    {
        pic->Width = Com.LittleLong(pic->Width);
        pic->Height = Com.LittleLong(pic->Height);
    }
}
