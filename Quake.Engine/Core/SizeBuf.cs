// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// common.c — growable byte-buffer helpers backing message serialization.
/// </summary>
public static unsafe class Sz
{
    // Allocates a native sizebuf_t (rule 9: stable pointer, no pinning of a
    // managed static) that wraps an existing, already-allocated data buffer.
    public static SizeBuf_t* Wrap(byte* data, int maxsize)
    {
        SizeBuf_t* buf = NativeMemory.AllocZeroed<SizeBuf_t>();
        buf->Data = data;
        buf->MaxSize = maxsize;
        return buf;
    }

    // SZ_Alloc
    public static void Alloc(SizeBuf_t* buf, int startsize)
    {
        if (startsize < 256)
            startsize = 256;
        buf->Data = (byte*)Hunk.AllocName(startsize, "sizebuf"u8);
        buf->MaxSize = startsize;
        buf->CurSize = 0;
    }

    // SZ_Free
    public static void Free(SizeBuf_t* buf)
    {
        buf->CurSize = 0;
    }

    // SZ_Clear
    public static void Clear(SizeBuf_t* buf)
    {
        buf->CurSize = 0;
    }

    // SZ_GetSpace
    public static void* GetSpace(SizeBuf_t* buf, int length)
    {
        if (buf->CurSize + length > buf->MaxSize)
        {
            if (!buf->AllowOverflow)
                Sys.Error("SZ_GetSpace: overflow without allowoverflow set"u8);

            if (length > buf->MaxSize)
                Sys.Error($"SZ_GetSpace: {length} is > full buffer size");

            buf->Overflowed = true;
            Con.Printf("SZ_GetSpace: overflow"u8);
            Clear(buf);
        }

        void* data = buf->Data + buf->CurSize;
        buf->CurSize += length;

        return data;
    }

    // SZ_Write
    public static void Write(SizeBuf_t* buf, void* data, int length)
    {
        Q.MemCpy(GetSpace(buf, length), data, length);
    }

    public static void Write(SizeBuf_t* buf, scoped ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return; // MemoryMarshal.AsRef throws on an empty span; SZ_Write with length 0 is a no-op

        ref readonly byte _data = ref MemoryMarshal.AsRef<byte>(data);
        Buffer.MemoryCopy(Unsafe.AsPointer(in _data), GetSpace(buf, data.Length), data.Length, data.Length);
    }

    // SZ_Print
    public static void Print(SizeBuf_t* buf, Utf8String data)
    {
        int len = Q.StrLen(data) + 1;

        if (buf->Data[buf->CurSize - 1] != 0)
            Q.MemCpy(GetSpace(buf, len), data, len); // no trailing 0
        else
            Q.MemCpy((byte*)GetSpace(buf, len - 1) - 1, data, len); // write over trailing 0
    }
}
