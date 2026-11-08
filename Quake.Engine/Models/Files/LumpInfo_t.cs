// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// lumpinfo_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct LumpInfo_t
{
    public int FilePos;
    public int DiskSize;
    public int Size; // uncompressed
    public byte Type;
    public byte Compression;
    public byte Pad1, Pad2;
    public fixed byte Name[16]; // must be null terminated
}
