// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// CStr's managed<->native conversions must be lossless for all 8-bit Quake
// text (bytes 128-255 are the red/gold console characters) on every OS, and
// Com.Va must behave like common.c's va(): one static buffer, reused.
[TestClass]
public sealed unsafe class CStrTests
{
    [TestMethod]
    public void ToString_Write_RoundTripEveryNonZeroByte()
    {
        byte* src = (byte*)NativeMemory.Alloc(256);
        byte* dst = (byte*)NativeMemory.Alloc(256);
        try
        {
            for (int i = 1; i < 256; i++)
                src[i - 1] = (byte)i;
            src[255] = 0;

            string s = CStr.ToString(src);
            Assert.AreEqual(255, s.Length);
            for (int i = 1; i < 256; i++)
                Assert.AreEqual((char)i, s[i - 1]);

            CStr.Write(dst, s);
            Assert.AreEqual(0, Q.StrCmp(src, dst));
        }
        finally
        {
            NativeMemory.Free(src);
            NativeMemory.Free(dst);
        }
    }

    [TestMethod]
    public void ToString_Null_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, CStr.ToString(null));
    }

    [TestMethod]
    public void ToBytes_AppendsTerminator_AndKeepsHighBytes()
    {
        byte[] bytes = CStr.ToBytes("aÁ");
        CollectionAssert.AreEqual(new byte[] { (byte)'a', 0xC1, 0 }, bytes);
    }

    /*
    [TestMethod]
    public void Va_FormatsIntoSharedStaticBuffer()
    {
        byte* a = Com.Va($"gfx/menudot{3}.lmp");
        string test = CStr.ToString(a);
        Assert.AreEqual("gfx/menudot3.lmp", CStr.ToString(a));

        // va() reuses one static buffer -- the previous result is overwritten.
        byte* b = Com.Va("load s1\n");
        Assert.AreEqual((nint)a, (nint)b);
        Assert.AreEqual("load s1\n", CStr.ToString(a));
    }

    [TestMethod]
    public void Va_InterpolatedHandler_FormatsBytePtrIntAndFloat()
    {
        byte* name = new Utf8String("Ab"u8);
        byte* p = Com.Va($"bind \"{name}\" {-42} {1.5f:F2} {"s"}\n");
        Assert.AreEqual("bind \"Ab\" -42 1.50 s\n", CStr.ToString(p));
    }

    [TestMethod]
    public void Va_InterpolatedHandler_TruncatesAt1023Bytes()
    {
        byte* big = (byte*)NativeMemory.AllocZeroed(2001);
        new Span<byte>(big, 2000).Fill((byte)'x');
        byte* p = Com.Va($"{big}{big}");
        Assert.AreEqual(1023, Q.StrLen(p));
        NativeMemory.Free(big);
    }
    */

    [TestMethod]
    public void Va_TruncatesAt1023Bytes()
    {
        byte* p = Com.Va(new string('x', 2000));
        Assert.AreEqual(1023, Q.StrLen(p));
    }

    // The "..."u8 overloads must behave exactly like the string ones
    // (Q_strncpy: no zero-pad; strncpy: zero-pads the remainder).
    [TestMethod]
    public void QStrncpy_U8_MatchesStringOverload()
    {
        byte* a = stackalloc byte[16];
        byte* b = stackalloc byte[16];
        foreach (string s in new[] { "", "abc", "abcdefgh", "abcdefghijkl" })
        {
            for (int i = 0; i < 16; i++)
                a[i] = b[i] = 0x55;
            CStr.QStrncpy(a, s, 8);
            CStr.QStrncpy(b, System.Text.Encoding.Latin1.GetBytes(s), 8);
            Assert.IsTrue(new ReadOnlySpan<byte>(a, 16).SequenceEqual(new ReadOnlySpan<byte>(b, 16)), s);
        }
    }

    [TestMethod]
    public void Strncpy_U8_MatchesStringOverload()
    {
        byte* a = stackalloc byte[16];
        byte* b = stackalloc byte[16];
        foreach (string s in new[] { "", "abc", "abcdefgh", "abcdefghijkl" })
        {
            for (int i = 0; i < 16; i++)
                a[i] = b[i] = 0x55;
            CStr.Strncpy(a, s, 8);
            CStr.Strncpy(b, System.Text.Encoding.Latin1.GetBytes(s), 8);
            Assert.IsTrue(new ReadOnlySpan<byte>(a, 16).SequenceEqual(new ReadOnlySpan<byte>(b, 16)), s);
        }
    }
}
