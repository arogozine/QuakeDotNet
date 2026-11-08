// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Quake.Engine;

namespace Quake.Benchmarks;

// Compares Q.MemSet/Q.MemCmp/Q.StrLen (now backed by BCL/JIT-intrinsic
// implementations, see Q.cs) against the original scalar Q_memset/Q_memcmp/
// Q_strlen loops from q_shared.c/common.c, reproduced locally as the baseline.
[MemoryDiagnoser]
public unsafe class QBenchmarks
{
    #region Setup / Teardown

    private const int Count = 1024 * 768;
    private byte* _buf1;
    private byte* _buf2;
    private byte* _setBuf;

    [GlobalSetup]
    public void Setup()
    {
        // _buf1 is Count+1 bytes: Count non-zero bytes followed by a single
        // NUL terminator, so both StrLen implementations scan the full
        // Count-byte buffer before finding it.
        _buf1 = (byte*)NativeMemory.Alloc((nuint)(Count + 1));
        _buf2 = (byte*)NativeMemory.Alloc((nuint)Count);
        // Separate scratch buffer for the MemSet benchmarks, so they don't
        // overwrite the contents _buf1/_buf2 rely on for MemCmp/StrLen.
        _setBuf = (byte*)NativeMemory.Alloc((nuint)Count);

        Random rng = new(1234);
        for (int i = 0; i < Count; i++)
        {
            byte b = (byte)rng.Next(1, 256); // avoid 0 so StrLen benchmarks scan the full buffer
            _buf1[i] = b;
            _buf2[i] = b;
        }
        _buf1[Count] = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_buf1);
        NativeMemory.Free(_buf2);
        NativeMemory.Free(_setBuf);
    }

    #endregion

    [Benchmark(Baseline = true)]
    public void Q_MemSet() => ReferenceMemSet(_setBuf, 0x2A, Count);

    [Benchmark]
    public void Native_MemSet() => Q.MemSet(_setBuf, 0x2A, Count);

    [Benchmark]
    public int Q_MemCmp() => ReferenceMemCmp(_buf1, _buf2, Count);

    [Benchmark]
    public int Native_MemCmp() => Q.MemCmp(_buf1, _buf2, Count);

    [Benchmark]
    public int Q_StrLen() => ReferenceStrLen(_buf1);

    [Benchmark]
    public int Native_StrLen() => Q.StrLen(_buf1);

    // StrCpy / StrCat / StrRChr on a Count-byte string (StrRChr searches for
    // NUL, which never matches, so the backwards search scans the whole string).
    [Benchmark]
    public void Q_StrCpy() => ReferenceStrCpy(_setBuf, _buf1);

    [Benchmark]
    public void Native_StrCpy() => Q.StrCpy(_setBuf, _buf1);

    [Benchmark]
    public void Q_StrCat()
    {
        _setBuf[0] = 0;
        ReferenceStrCat(_setBuf, _buf1);
    }

    [Benchmark]
    public void Native_StrCat()
    {
        _setBuf[0] = 0;
        Q.StrCat(_setBuf, _buf1);
    }

    [Benchmark]
    public bool Q_StrRChr() => ReferenceStrRChr(_buf1, 0) != null;

    [Benchmark]
    public bool Native_StrRChr() => Q.StrRChr(_buf1, 0) != null;

    // Q_strcpy
    private static void ReferenceStrCpy(byte* dest, byte* src)
    {
        while (*src != 0)
            *dest++ = *src++;
        *dest++ = 0;
    }

    // Q_strcat
    private static void ReferenceStrCat(byte* dest, byte* src)
    {
        dest += ReferenceStrLen(dest);
        ReferenceStrCpy(dest, src);
    }

    // Q_strrchr
    private static byte* ReferenceStrRChr(byte* s, byte c)
    {
        int len = ReferenceStrLen(s);
        s += len;
        while (len-- != 0)
            if (*--s == c) return s;
        return null;
    }

    // Q_memset
    private static void ReferenceMemSet(void* dest, int fill, int count)
    {
        if ((((long)dest | count) & 3) == 0)
        {
            count >>= 2;
            fill = fill | (fill << 8) | (fill << 16) | (fill << 24);
            for (int i = 0; i < count; i++)
                ((int*)dest)[i] = fill;
        }
        else
        {
            for (int i = 0; i < count; i++)
                ((byte*)dest)[i] = (byte)fill;
        }
    }

    // Q_memcmp
    private static int ReferenceMemCmp(void* m1, void* m2, int count)
    {
        while (count != 0)
        {
            count--;
            if (((byte*)m1)[count] != ((byte*)m2)[count])
                return -1;
        }
        return 0;
    }

    // Q_strlen
    private static int ReferenceStrLen(byte* str)
    {
        int count = 0;
        while (str[count] != 0)
            count++;

        return count;
    }
}
