// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// Verifies Q.MemSet/Q.MemCmp/Q.StrLen (which now delegate to BCL/JIT-intrinsic
// implementations for speed -- see the comments in Q.cs) produce byte-for-byte
// identical results to the original scalar Q_memset/Q_memcmp/Q_strlen loops
// from q_shared.c/common.c, which are reproduced locally as the reference.
[TestClass]
public sealed unsafe class QTests
{
    private static void ReferenceMemSet(void* dest, int fill, int count)
    {
        if ((((nint)dest | (nint)count) & 3) == 0)
        {
            count >>= 2;
            fill = fill | (fill << 8) | (fill << 16) | (fill << 24);
            for (int i = 0; i < count; i++)
            {
                ((int*)dest)[i] = fill;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                ((byte*)dest)[i] = (byte)fill;
            }
        }
    }

    private static int ReferenceMemCmp(void* m1, void* m2, int count)
    {
        while (count != 0)
        {
            count--;
            if (((byte*)m1)[count] != ((byte*)m2)[count])
            {
                return -1;
            }
        }
        return 0;
    }

    private static int ReferenceStrLen(byte* str)
    {
        int count = 0;
        while (str[count] != 0)
        {
            count++;
        }

        return count;
    }

    [TestMethod]
    [DataRow(0, 16)]
    [DataRow(1, 16)]
    [DataRow(0xFF, 16)]
    [DataRow(0x7F, 17)]   // count not a multiple of 4 -> unaligned branch in the original
    [DataRow(0x12345678, 64)]
    public void MemSet_MatchesOriginal_Aligned(int fill, int count)
    {
        byte* expected = NativeMemory.AllocArrayZeroed<byte>(count);
        byte* actual = NativeMemory.AllocArrayZeroed<byte>(count);
        try
        {
            ReferenceMemSet(expected, fill, count);
            Q.MemSet(actual, fill, count);

            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(expected[i], actual[i], $"byte {i} differs for fill=0x{fill:X}, count={count}");
            }
        }
        finally
        {
            NativeMemory.Free(expected);
            NativeMemory.Free(actual);
        }
    }

    [TestMethod]
    public void MemSet_MatchesOriginal_UnalignedPointer()
    {
        const int count = 15; // odd count, and we also offset the pointer below
        byte* buf1 = NativeMemory.AllocArrayZeroed<byte>(count + 1);
        byte* buf2 = NativeMemory.AllocArrayZeroed<byte>(count + 1);
        try
        {
            ReferenceMemSet(buf1 + 1, 0xAB, count);
            Q.MemSet(buf2 + 1, 0xAB, count);

            for (int i = 0; i < count + 1; i++)
            {
                Assert.AreEqual(buf1[i], buf2[i], $"byte {i} differs");
            }
        }
        finally
        {
            NativeMemory.Free(buf1);
            NativeMemory.Free(buf2);
        }
    }

    [TestMethod]
    public void MemCmp_EqualBuffers_ReturnsZero()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        fixed (byte* p1 = data)
        fixed (byte* p2 = data)
        {
            Assert.AreEqual(0, ReferenceMemCmp(p1, p2, data.Length));
            Assert.AreEqual(0, Q.MemCmp(p1, p2, data.Length));
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(7)]
    public void MemCmp_DifferingByte_ReturnsMinusOne(int diffIndex)
    {
        byte[] a = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] b = (byte[])a.Clone();
        b[diffIndex] = (byte)(b[diffIndex] + 1);

        fixed (byte* pa = a)
        fixed (byte* pb = b)
        {
            Assert.AreEqual(ReferenceMemCmp(pa, pb, a.Length), Q.MemCmp(pa, pb, a.Length));
            Assert.AreEqual(-1, Q.MemCmp(pa, pb, a.Length));
        }
    }

    [TestMethod]
    public void MemCmp_ZeroCount_ReturnsZero()
    {
        byte dummy = 0;
        Assert.AreEqual(0, Q.MemCmp(&dummy, &dummy, 0));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("a")]
    [DataRow("hello world")]
    [DataRow("this is a somewhat longer string used to exercise the vectorized path")]
    public void StrLen_MatchesOriginal(string s)
    {
        byte[] bytes = [.. System.Text.Encoding.ASCII.GetBytes(s), (byte)0];
        fixed (byte* p = bytes)
        {
            Assert.AreEqual(ReferenceStrLen(p), Q.StrLen(p));
            Assert.AreEqual(s.Length, Q.StrLen(p));
        }
    }

    // Lengths around the vector-width boundaries (16/32 bytes), in a buffer
    // sized exactly to the string so a read past the terminator would be
    // out of bounds.
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(15)]
    [DataRow(16)]
    [DataRow(31)]
    [DataRow(32)]
    [DataRow(33)]
    [DataRow(100)]
    public void StrLen_MatchesOriginal_AllLengths(int len)
    {
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(len + 1);
        try
        {
            for (int i = 0; i < len; i++)
            {
                buf[i] = (byte)(1 + (i % 255));
            }

            Assert.AreEqual(ReferenceStrLen(buf), Q.StrLen(buf));
            Assert.AreEqual(len, Q.StrLen(buf));
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    // Q_strcpy
    private static void ReferenceStrCpy(byte* dest, byte* src)
    {
        while (*src != 0)
        {
            *dest++ = *src++;
        }

        *dest++ = 0;
    }

    // Q_strrchr
    private static byte* ReferenceStrRChr(byte* s, byte c)
    {
        int len = ReferenceStrLen(s);
        s += len;
        while (len-- != 0)
        {
            if (*--s == c)
            {
                return s;
            }
        }

        return null;
    }

    private static byte[] Z(string s) => [.. System.Text.Encoding.ASCII.GetBytes(s), (byte)0];

    [TestMethod]
    [DataRow("")]
    [DataRow("a")]
    [DataRow("hello world")]
    [DataRow("this is a somewhat longer string used to exercise the vectorized path")]
    public void StrCpy_MatchesOriginal(string s)
    {
        byte[] src = Z(s);
        byte[] expected = new byte[src.Length + 8];
        byte[] actual = new byte[src.Length + 8];
        Array.Fill(expected, (byte)0xCC);
        Array.Fill(actual, (byte)0xCC);

        fixed (byte* ps = src)
        fixed (byte* pe = expected)
        fixed (byte* pa = actual)
        {
            ReferenceStrCpy(pe, ps);
            Q.StrCpy(pa, ps);
        }

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("abc", "")]
    [DataRow("", "def")]
    [DataRow("hello ", "world")]
    [DataRow("a fairly long prefix string, ", "followed by a fairly long suffix string")]
    public void StrCat_MatchesOriginal(string a, string b)
    {
        byte[] bytesA = Z(a);
        byte[] bytesB = Z(b);
        int size = bytesA.Length + bytesB.Length + 8;
        byte[] expected = new byte[size];
        byte[] actual = new byte[size];
        Array.Fill(expected, (byte)0xCC);
        Array.Fill(actual, (byte)0xCC);
        bytesA.CopyTo(expected, 0);
        bytesA.CopyTo(actual, 0);

        fixed (byte* pb = bytesB)
        fixed (byte* pe = expected)
        fixed (byte* pa = actual)
        {
            ReferenceStrCpy(pe + ReferenceStrLen(pe), pb);
            Q.StrCat(pa, pb);
        }

        CollectionAssert.AreEqual(expected, actual);
        fixed (byte* pa = actual)
        {
            Assert.AreEqual(a.Length + b.Length, Q.StrLen(pa));
        }
    }

    [TestMethod]
    [DataRow("", 'a')]
    [DataRow("abc", 'a')]
    [DataRow("abc", 'c')]
    [DataRow("abc", 'x')]
    [DataRow("a/b/c.txt", '/')]
    [DataRow("a/b/c.txt", '.')]
    [DataRow("no slashes here", '/')]
    [DataRow("a long string with the char only near the very start: /x then lots of filler text after it", '/')]
    public void StrRChr_MatchesOriginal(string s, char c)
    {
        byte[] bytes = Z(s);
        fixed (byte* p = bytes)
        {
            Assert.IsTrue(ReferenceStrRChr(p, (byte)c) == Q.StrRChr(p, (byte)c));
        }
    }

    [TestMethod]
    public void StrRChr_NulNeverMatches()
    {
        byte[] bytes = Z("abc");
        fixed (byte* p = bytes)
        {
            Assert.IsTrue(Q.StrRChr(p, 0) == null);
            Assert.IsTrue(ReferenceStrRChr(p, 0) == null);
        }
    }
}
