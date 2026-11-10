// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// common.c — message IO functions. Handles byte ordering and avoids alignment errors.
/// </summary>
public static unsafe class Msg
{
    // net_message (net_main.c) -- the one global sizebuf the MSG_Read* functions read
    public static SizeBuf_t* net_message => Net.net_message;

    // writing functions

    // MSG_WriteChar
    public static void WriteChar(SizeBuf_t* sb, int c)
    {
        byte* buf = (byte*)Sz.GetSpace(sb, 1);
        buf[0] = (byte)c;
    }

    // MSG_WriteByte
    public static void WriteByte(SizeBuf_t* sb, int c)
    {
        byte* buf = (byte*)Sz.GetSpace(sb, 1);
        buf[0] = (byte)c;
    }

    // MSG_WriteShort
    public static void WriteShort(SizeBuf_t* sb, int c)
    {
        byte* buf = (byte*)Sz.GetSpace(sb, 2);
        buf[0] = (byte)(c & 0xff);
        buf[1] = (byte)(c >> 8);
    }

    // MSG_WriteLong
    public static void WriteLong(SizeBuf_t* sb, int c)
    {
        byte* buf = (byte*)Sz.GetSpace(sb, 4);
        buf[0] = (byte)(c & 0xff);
        buf[1] = (byte)((c >> 8) & 0xff);
        buf[2] = (byte)((c >> 16) & 0xff);
        buf[3] = (byte)(c >> 24);
    }

    // MSG_WriteFloat
    public static void WriteFloat(SizeBuf_t* sb, float f)
    {
        int l = Com.LittleLong(*(int*)&f);
        Sz.Write(sb, &l, 4);
    }

    // MSG_WriteString
    public static void WriteString(SizeBuf_t* sb, Utf8String s_)
    {
        byte* s = s_;
        if (s == null)
        {
            byte empty = 0;
            Sz.Write(sb, &empty, 1);
        }
        else
        {
            Sz.Write(sb, s, Q.StrLen(s) + 1);
        }
    }

    public static void WriteString(SizeBuf_t* sb, [InterpolatedStringHandlerArgument] scoped Utf8StringHandler s_)
    {
        scoped ReadOnlySpan<byte> buffer = s_.GetWrittenBuffer();

        // MemoryMarshal.AsRef throws on an empty span; MSG_WriteString writes a lone NUL for "".
        if (buffer.IsEmpty)
        {
            byte empty = 0;
            Sz.Write(sb, &empty, 1);
        }
        else
        {
            // the handler NUL-terminates the written buffer
            byte* s = (byte*)Unsafe.AsPointer(in MemoryMarshal.GetReference(buffer));
            Sz.Write(sb, s, Q.StrLen(s) + 1);
        }
    }

    // MSG_WriteCoord
    public static void WriteCoord(SizeBuf_t* sb, float f)
    {
        WriteShort(sb, (int)(f * 8));
    }

    // MSG_WriteAngle
    public static void WriteAngle(SizeBuf_t* sb, float f)
    {
        WriteByte(sb, ((int)f * 256 / 360) & 255);
    }

    // MSG_WriteAngle16 (Mark V common.c)
    public static void WriteAngle16(SizeBuf_t* sb, float f)
    {
        WriteShort(sb, (int)MathF.Round(f * 65536 / 360, MidpointRounding.ToEven) & 65535);
    }

    // reading functions

    // msg_readcount
    public static int msg_readcount;

    // msg_badread
    public static bool msg_badread;

    // MSG_BeginReading
    public static void BeginReading()
    {
        msg_readcount = 0;
        msg_badread = false;
    }

    // MSG_ReadChar
    // returns -1 and sets msg_badread if no more characters are available
    public static int ReadChar()
    {
        if (msg_readcount + 1 > net_message->CurSize)
        {
            msg_badread = true;
            return -1;
        }

        int c = (sbyte)net_message->Data[msg_readcount];
        msg_readcount++;

        return c;
    }

    // MSG_ReadByte
    public static int ReadByte()
    {
        if (msg_readcount + 1 > net_message->CurSize)
        {
            msg_badread = true;
            return -1;
        }

        int c = net_message->Data[msg_readcount];
        msg_readcount++;

        return c;
    }

    // MSG_ReadShort
    public static int ReadShort()
    {
        if (msg_readcount + 2 > net_message->CurSize)
        {
            msg_badread = true;
            return -1;
        }

        int c = (short)(net_message->Data[msg_readcount]
            + (net_message->Data[msg_readcount + 1] << 8));

        msg_readcount += 2;

        return c;
    }

    // MSG_ReadLong
    public static int ReadLong()
    {
        if (msg_readcount + 4 > net_message->CurSize)
        {
            msg_badread = true;
            return -1;
        }

        int c = net_message->Data[msg_readcount]
            + (net_message->Data[msg_readcount + 1] << 8)
            + (net_message->Data[msg_readcount + 2] << 16)
            + (net_message->Data[msg_readcount + 3] << 24);

        msg_readcount += 4;

        return c;
    }

    // MSG_ReadFloat
    public static float ReadFloat()
    {
        // MSG_ReadFloat has no bounds check in WinQuake or Mark V (unlike the
        // other MSG_Read* functions); debug-only guard.
        Debug.Assert(msg_readcount + 4 <= net_message->CurSize, "MSG_ReadFloat: read past end of message");
        byte* b = stackalloc byte[4];
        b[0] = net_message->Data[msg_readcount];
        b[1] = net_message->Data[msg_readcount + 1];
        b[2] = net_message->Data[msg_readcount + 2];
        b[3] = net_message->Data[msg_readcount + 3];
        msg_readcount += 4;

        int l = Com.LittleLong(*(int*)b);

        return *(float*)&l;
    }

    private const int READ_STRING_SIZE = 2048;
    internal static readonly byte* readStringBuffer = NativeMemory.AllocArray<byte>(READ_STRING_SIZE);

    // MSG_ReadString
    public static byte* ReadString()
    {
        int l = 0;
        do
        {
            int c = ReadChar();
            if (c is -1 or 0)
            {
                break;
            }

            readStringBuffer[l] = (byte)c;
            l++;
        } while (l < READ_STRING_SIZE - 1);

        readStringBuffer[l] = 0;

        return readStringBuffer;
    }

    // MSG_ReadCoord
    public static float ReadCoord()
    {
        return ReadShort() * (1.0f / 8);
    }

    // MSG_ReadAngle
    public static float ReadAngle()
    {
        return ReadChar() * (360.0f / 256);
    }

    // MSG_ReadAngle16 (Mark V common.c)
    public static float ReadAngle16()
    {
        return ReadShort() * (360.0f / 65536);
    }
}
