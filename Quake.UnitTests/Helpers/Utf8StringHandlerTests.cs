// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// The handler wraps a fixed MAX_SIZE byte ASCII buffer: appends must truncate
// rather than throw or write out of bounds, mark the cut with "...", and the
// result stays NUL-terminated.
[TestClass]
public sealed unsafe class Utf8StringHandlerTests
{
    private const int Max = Utf8StringHandler.MAX_SIZE;

    private static string Str(in Utf8StringHandler h)
    {
        return CStr.ToString(h.GetWrittenBufferPtr());
    }

    // What a result looks like after `text` of `length` chars was cut at
    // capacity: Max bytes, the last three replaced by "...".
    private static string Cut(char fill)
    {
        return new string(fill, Max - 3) + "...";
    }

    // ---- literal / string ------------------------------------------------

    [TestMethod]
    public void Append_FitsNormally()
    {
        int n = 42;
        Assert.AreEqual("abc 42 def", Str($"abc {n} def"));
    }

    [TestMethod]
    public void Append_Utf8String_WritesContentsNotTypeName()
    {
        Utf8String name = "e1m1"u8;
        string s = Str($"map {name}\n");
        Assert.AreEqual("map e1m1\n", s);
    }

    [TestMethod]
    public void Append_Utf8String_Empty_WritesNothing()
    {
        Utf8String empty = Utf8String.Empty;
        Assert.AreEqual("[]", Str($"[{empty}]"));
    }

    [TestMethod]
    public void Append_Utf8String_Null_WritesNothing()
    {
        Utf8String none = (byte*)null;
        Assert.AreEqual("[]", Str($"[{none}]"));
    }

    [TestMethod]
    public void Append_Utf8String_HeapBacked_WritesContents()
    {
        byte* p = (byte*)NativeMemory.AllocZeroed(4);
        p[0] = (byte)'a'; p[1] = (byte)'b';
        try
        {
            Utf8String name = p;
            Assert.AreEqual("[ab]", Str($"[{name}]"));
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    [TestMethod]
    public void Append_Utf8String_Alignment_PadsLikePrintfS()
    {
        Utf8String name = "ab"u8;
        Utf8String none = (byte*)null;
        Assert.AreEqual("[  ab]", Str($"[{name,4}]"));
        Assert.AreEqual("[ab  ]", Str($"[{name,-4}]"));
        Assert.AreEqual("[ab]", Str($"[{name,1}]"));
        Assert.AreEqual("[]", Str($"[{none,4}]"));
    }

    [TestMethod]
    public void Append_Utf8String_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        Utf8String name = "zz"u8;
        Assert.AreEqual(Cut('c'), Str($"{big}{name}"));
    }

    [TestMethod]
    public void Empty_IsEmptyAndNulTerminated()
    {
        Utf8StringHandler h = $"";
        Assert.AreEqual(0, h.GetWrittenBuffer().Length);
        Assert.AreEqual(0, *h.GetWrittenBufferPtr());
        Assert.IsFalse(h.IsTruncated);
    }

    [TestMethod]
    public void String_EmptyAndNull_AreIgnored()
    {
        string e = "";
        string? ns = null;
        Assert.AreEqual("xy", Str($"x{e}{ns}y"));
    }

    [TestMethod]
    public void String_NonAscii_BecomesQuestionMarkPerChar()
    {
        string s = "aé€b😀c"; // e-acute, euro, surrogate pair
        string hi = "\u0080";
        string del = "\u007F";
        Assert.AreEqual("a??b??c", Str($"{s}"));
        Assert.AreEqual("?", Str($"{hi}"));
        Assert.AreEqual("\u007F", Str($"{del}"));
    }

    [TestMethod]
    public void String_ExactFit_IsNotTruncated()
    {
        string s = new('a', Max);
        Utf8StringHandler h = $"{s}";
        Assert.AreEqual(s, Str(h));
        Assert.IsFalse(h.IsTruncated);
    }

    // Capacity is Max bytes for every type, literals included.
    [TestMethod]
    public void Capacity_IsMaxBytes_ForEveryAppendType()
    {
        string fill = new('a', Max - 1);
        ReadOnlySpan<byte> one = "b"u8;
        byte* p = (byte*)NativeMemory.AllocZeroed(2);
        p[0] = (byte)'b';
        try
        {
            string str = "b";
            int digit = 7;
            Utf8String u = p;

            // One byte left: a single-byte append of each kind still fits.
            Utf8StringHandler lit = $"{fill}b";
            Assert.AreEqual(Max, lit.GetWrittenBuffer().Length);
            Assert.IsFalse(lit.IsTruncated);

            Utf8StringHandler s = $"{fill}{str}";
            Assert.AreEqual(Max, s.GetWrittenBuffer().Length);
            Assert.IsFalse(s.IsTruncated);

            Utf8StringHandler sp = $"{fill}{one}";
            Assert.IsFalse(sp.IsTruncated);
            Assert.AreEqual(Max, sp.GetWrittenBuffer().Length);

            Utf8StringHandler bp = $"{fill}{p}";
            Assert.IsFalse(bp.IsTruncated);
            Assert.AreEqual(Max, bp.GetWrittenBuffer().Length);

            Utf8StringHandler us = $"{fill}{u}";
            Assert.IsFalse(us.IsTruncated);
            Assert.AreEqual(Max, us.GetWrittenBuffer().Length);

            Utf8StringHandler n = $"{fill}{digit}";
            Assert.IsFalse(n.IsTruncated);
            Assert.AreEqual(Max, n.GetWrittenBuffer().Length);

            // The terminating NUL sits just past the last byte.
            Assert.AreEqual(0, n.GetWrittenBufferPtr()[Max]);
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    [TestMethod]
    public void Capacity_OneByteOver_IsTruncatedForEveryAppendType()
    {
        string full = new('a', Max);
        ReadOnlySpan<byte> one = "b"u8;
        string str = "b";
        int digit = 7;

        Assert.AreEqual(Cut('a'), Str($"{full}b"));
        Assert.AreEqual(Cut('a'), Str($"{full}{str}"));
        Assert.AreEqual(Cut('a'), Str($"{full}{one}"));
        Assert.AreEqual(Cut('a'), Str($"{full}{digit}"));
    }

    [TestMethod]
    public void String_TooLong_IsTruncatedWithEllipsis()
    {
        const int maxPlus15 = Max + 15;

        string big = new('a', maxPlus15);
        Utf8StringHandler h = $"{big}";
        Assert.AreEqual("...", Str(h));
        Assert.IsTrue(h.IsTruncated);
    }

    [TestMethod]
    public void String_NonAsciiFillingBuffer_IsNarrowedNotTruncated()
    {
        string s = new string('a', Max - 2) + "€€";
        Utf8StringHandler h = $"{s}";
        Assert.AreEqual($"{new string('a', Max - 2)}??", Str(h));
        Assert.IsFalse(h.IsTruncated);
    }

    [TestMethod]
    public void MultipleAppendsPastEnd_DoNotThrowAndStayTruncated()
    {
        string big = new('b', 2500);
        Utf8StringHandler h = $"{big}{big}{big}{12345}tail";
        Assert.AreEqual($"{big}...", Str(h));
        Assert.IsTrue(h.IsTruncated);
    }

    [TestMethod]
    public void String_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        Assert.AreEqual(Cut('c'), Str($"{big}tail"));
    }

    [TestMethod]
    public void EmptyAppendAtFullBuffer_IsNotTruncated()
    {
        string big = new('c', Max);
        string e = "";
        Utf8StringHandler h = $"{big}{e}";
        Assert.AreEqual(big, Str(h));
        Assert.IsFalse(h.IsTruncated);
    }

    [TestMethod]
    public void NestedInterpolatedString_KeepsPrefix()
    {
        Assert.AreEqual("abcdefghi", Str($"abc{$"def"}ghi"));
    }

    [TestMethod]
    public void String_Alignment_PadsLikePrintfS()
    {
        string s = "ab";
        string? ns = null;
        Assert.AreEqual("[  ab]", Str($"[{s,4}]"));
        Assert.AreEqual("[ab  ]", Str($"[{s,-4}]"));
        Assert.AreEqual("[ab]", Str($"[{s,1}]"));
        Assert.AreEqual("[  ]", Str($"[{ns,2}]"));
    }

    [TestMethod]
    public void String_Format_IsAcceptedAndIgnored()
    {
        string s = "ab";
        Assert.AreEqual("[  ab]", Str($"[{s,4:x}]"));
        Assert.AreEqual("[ab]", Str($"[{s:x}]"));
    }

    [TestMethod]
    public void String_Alignment_TruncatesAtCapacity()
    {
        string s = "ab";
        const int wide = Max + 15;
        Utf8StringHandler h = $"{s,wide}";
        Assert.AreEqual("ab...", Str(h));
        Assert.IsTrue(h.IsTruncated);
    }

    // ---- ReadOnlySpan<byte> ---------------------------------------------

    [TestMethod]
    public void ByteSpan_CopiesBytes()
    {
        ReadOnlySpan<byte> u8 = "hello"u8;
        Assert.AreEqual("[hello]", Str($"[{u8}]"));
    }

    [TestMethod]
    public void ByteSpan_Empty_IsIgnored()
    {
        ReadOnlySpan<byte> u8 = default;
        Assert.AreEqual("ab", Str($"a{u8}b"));
    }

    [TestMethod]
    public void ByteSpan_TruncatesAtCapacity()
    {
        Span<byte> input = stackalloc byte[Max + 5];
        input.Fill((byte)'a');
        ReadOnlySpan<byte> u8 = input;

        Assert.AreEqual("...", Str($"{u8}"));
    }

    [TestMethod]
    public void ByteSpan_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        ReadOnlySpan<byte> u8 = "zz"u8;
        Assert.AreEqual(Cut('c'), Str($"{big}{u8}"));
    }

    [TestMethod]
    public void ByteSpan_AlignmentAndFormat()
    {
        ReadOnlySpan<byte> u8 = "ab"u8;
        Assert.AreEqual("[  ab]", Str($"[{u8,4}]"));
        Assert.AreEqual("[ab  ]", Str($"[{u8,-4}]"));
        Assert.AreEqual("[ab]", Str($"[{u8:x}]"));
    }

    // ---- byte* ----------------------------------------------------------

    [TestMethod]
    public void BytePointer_Copies()
    {
        byte* p = (byte*)NativeMemory.Alloc(4);
        p[0] = (byte)'o'; p[1] = (byte)'k'; p[2] = 0; p[3] = (byte)'x';
        try
        {
            Assert.AreEqual("[ok]", Str($"[{p}]"));
            Assert.AreEqual("[ok]", Str($"[{p:x}]"));
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    [TestMethod]
    public void BytePointer_IsTruncated()
    {
        const int maxPlus15 = Max + 15;

        byte* src = (byte*)NativeMemory.Alloc(maxPlus15 + 1);
        new Span<byte>(src, maxPlus15).Fill((byte)'e');
        src[maxPlus15] = 0;

        try
        {
            Assert.AreEqual("...", Str($"{src}"));
        }
        finally
        {
            NativeMemory.Free(src);
        }
    }

    [TestMethod]
    public void BytePointer_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        byte* p = (byte*)NativeMemory.Alloc(3);
        p[0] = (byte)'z'; p[1] = (byte)'z'; p[2] = 0;
        try
        {
            Assert.AreEqual(Cut('c'), Str($"{big}{p}"));
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    [TestMethod]
    public void NullPointer_IsIgnored()
    {
        byte* np = null;
        Assert.AreEqual("xy", Str($"x{np}y"));
        Assert.AreEqual("xy", Str($"x{np,4}y"));
    }

    [TestMethod]
    public void BytePointer_Alignment_PadsLikePrintfS()
    {
        byte* p = (byte*)NativeMemory.Alloc(3);
        p[0] = (byte)'a'; p[1] = (byte)'b'; p[2] = 0;
        try
        {
            Assert.AreEqual("[  ab]", Str($"[{p,4}]"));
            Assert.AreEqual("[ab  ]", Str($"[{p,-4}]"));
            Assert.AreEqual("[ab]", Str($"[{p,1}]"));
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    // ---- int -------------------------------------------------------------

    [TestMethod]
    public void Int_DefaultAndFormat()
    {
        int n = -42;
        int h = 255;
        int min = int.MinValue;
        Assert.AreEqual("-42", Str($"{n}"));
        Assert.AreEqual("FF", Str($"{h:X}"));
        Assert.AreEqual("00ff", Str($"{h:x4}"));
        Assert.AreEqual(int.MinValue.ToString(), Str($"{min}"));
    }

    [TestMethod]
    public void Int_Alignment_PadsLikePrintfFieldWidth()
    {
        int n = 42;
        int ff = 255;
        int big = 123456;
        Assert.AreEqual("[   42]", Str($"[{n,5}]"));
        Assert.AreEqual("[42   ]", Str($"[{n,-5}]"));
        Assert.AreEqual("[123456]", Str($"[{big,3}]")); // wider than the field: written in full
        Assert.AreEqual("[  ff]", Str($"[{ff,4:x}]"));
    }

    [TestMethod]
    public void Int_Alignment_TruncatesAtCapacity()
    {
        int n = 1;
        const int wide = Max + 15;
        const int negWide = -(Max + 15);

        Utf8StringHandler left = $"{n,wide}";
        Assert.AreEqual("1...", Str(left));
        Assert.IsTrue(left.IsTruncated);

        Utf8StringHandler right = $"{n,negWide}";
        Assert.AreEqual("1...", Str(right));
        Assert.IsTrue(right.IsTruncated);
    }

    [TestMethod]
    public void Int_Alignment_IntMinValue_IsClampedNotThrown()
    {
        int n = 1;
        const int min = int.MinValue;
        Utf8StringHandler h = $"{n,min}";
        Assert.AreEqual("1...", Str(h));
        Assert.IsTrue(h.IsTruncated);
    }

    [TestMethod]
    public void Int_FillingBufferExactly_IsNotTruncated()
    {
        string big = new('d', Max - 10);
        int n = int.MaxValue; // 10 digits, exactly 10 bytes left
        Utf8StringHandler h = $"{big}{n}";
        Assert.AreEqual($"{big}{n}", Str(h));
        Assert.IsFalse(h.IsTruncated);
    }

    [TestMethod]
    public void Int_ThatDoesNotFitByOneByte_ReplacesLastThreeBytes()
    {
        string big = new('d', Max - 5); // 6 digits into 5 bytes
        int n = 123456;
        Assert.AreEqual(big + "...", Str($"{big}{n}"));
    }

    [TestMethod]
    public void Int_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        int n = int.MaxValue;
        int seven = 7;
        Assert.AreEqual(Cut('c'), Str($"{big}{n}"));
        Assert.AreEqual(Cut('c'), Str($"{big}{seven,5}"));
    }

    // ---- float / double --------------------------------------------------

    [TestMethod]
    public void Float_DefaultAndFormat()
    {
        float f = 1.5f;
        float neg = -0.25f;
        Assert.AreEqual("1.5", Str($"{f}"));
        Assert.AreEqual("1.500000", Str($"{f:F6}"));
        Assert.AreEqual("-0.25", Str($"{neg}"));
    }

    [TestMethod]
    public void Float_Alignment_PadsLikePrintfFieldWidth()
    {
        float f = 3.14159f;
        Assert.AreEqual("[  3.1]", Str($"[{f,5:F1}]"));
        Assert.AreEqual("[3.1  ]", Str($"[{f,-5:F1}]"));
    }

    [TestMethod]
    public void Float_NonFinite_UsesPrintfSpelling()
    {
        float nan = float.NaN;
        float pinf = float.PositiveInfinity;
        float ninf = float.NegativeInfinity;
        float negNan = -float.NaN;
        Assert.AreEqual("NaN", Str($"{nan}"));
        Assert.AreEqual("NaN", Str($"{negNan}"));
        Assert.AreEqual("inf", Str($"{pinf}"));
        Assert.AreEqual("-inf", Str($"{ninf}"));
        Assert.AreEqual("inf", Str($"{pinf:F2}"));
        Assert.AreEqual("[  NaN]", Str($"[{nan,5}]"));
        Assert.AreEqual("[-inf ]", Str($"[{ninf,-5:F1}]"));
    }

    [TestMethod]
    public void Double_DefaultFormatAndNonFinite()
    {
        double d = 2.5;
        double nan = double.NaN;
        double pinf = double.PositiveInfinity;
        double ninf = double.NegativeInfinity;
        Assert.AreEqual("2.5", Str($"{d}"));
        Assert.AreEqual("2.50", Str($"{d:F2}"));
        Assert.AreEqual("[ 2.50]", Str($"[{d,5:F2}]"));
        Assert.AreEqual("NaN", Str($"{nan}"));
        Assert.AreEqual("inf", Str($"{pinf}"));
        Assert.AreEqual("-inf", Str($"{ninf,4}"));
    }

    [TestMethod]
    public void Float_LongAlignedValue_IsNotDropped()
    {
        // Wider than any small scratch buffer: must still be written in full.
        float f = 1.5f;
        string s = Str($"{f,1:F900}");
        Assert.AreEqual(2 + 900, s.Length);
        Assert.StartsWith("1.5000", s);
    }

    [TestMethod]
    public void Float_ThatDoesNotFit_IsTruncated()
    {
        string big = new('d', Max - 4);
        float f = 1234.5f;
        Assert.AreEqual(big + "...", Str($"{big}{f}"));
    }

    [TestMethod]
    public void Float_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        float f = 1.5f;
        float nan = float.NaN;
        Assert.AreEqual(Cut('c'), Str($"{big}{f}"));
        Assert.AreEqual(Cut('c'), Str($"{big}{nan}"));
    }

    [TestMethod]
    public void Float_AlignedValueTooWideForScratchBuffer_IsTruncated()
    {
        float f = 1.5f;
        Utf8StringHandler h = $"{f,1:F5000}";
        Assert.IsTrue(h.IsTruncated);
        Assert.AreEqual("...", Str(h));
    }

    // ---- other IUtf8SpanFormattable --------------------------------------

    [TestMethod]
    public void Generic_LongUInt()
    {
        long l = 123456789012L;
        uint u = 7;
        Assert.AreEqual("123456789012", Str($"{l}"));
        Assert.AreEqual("[   7]", Str($"[{u,4}]"));
        Assert.AreEqual("[7   ]", Str($"[{u,-4}]"));
        Assert.AreEqual("ff", Str($"{255L:x}"));
    }

    [TestMethod]
    public void Generic_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        long l = 99;
        Assert.AreEqual(Cut('c'), Str($"{big}{l}"));
    }

    // ---- enums -----------------------------------------------------------

    private enum Color { Red = 1, Green = 2, Blue = 3 }

    [Flags]
    private enum Bits { None = 0, A = 1, B = 2, C = 4 }

    private enum Small : byte { Lo = 1 }

    private enum Wide : long { Huge = 5_000_000_000 }

    // Joined names exceed the 256-char scratch buffer, forcing the grow path.
    [Flags]
    private enum ManyFlags : uint
    {
        VeryLongFlagName00 = 1u << 0, VeryLongFlagName01 = 1u << 1, VeryLongFlagName02 = 1u << 2, VeryLongFlagName03 = 1u << 3,
        VeryLongFlagName04 = 1u << 4, VeryLongFlagName05 = 1u << 5, VeryLongFlagName06 = 1u << 6, VeryLongFlagName07 = 1u << 7,
        VeryLongFlagName08 = 1u << 8, VeryLongFlagName09 = 1u << 9, VeryLongFlagName10 = 1u << 10, VeryLongFlagName11 = 1u << 11,
        VeryLongFlagName12 = 1u << 12, VeryLongFlagName13 = 1u << 13, VeryLongFlagName14 = 1u << 14, VeryLongFlagName15 = 1u << 15,
        VeryLongFlagName16 = 1u << 16, VeryLongFlagName17 = 1u << 17, VeryLongFlagName18 = 1u << 18, VeryLongFlagName19 = 1u << 19,
    }

    [TestMethod]
    public void Enum_Default_FormatsName()
    {
        Color c = Color.Green;
        Small s = Small.Lo;
        Wide w = Wide.Huge;
        Assert.AreEqual("Green", Str($"{c}"));
        Assert.AreEqual("Lo", Str($"{s}"));
        Assert.AreEqual("Huge", Str($"{w}"));
        Assert.AreEqual("[Blue]", Str($"[{Color.Blue}]"));
    }

    [TestMethod]
    public void Enum_Undefined_FormatsNumber()
    {
        Color c = (Color)99;
        Small s = (Small)200;
        Wide w = (Wide)5_000_000_001;
        Assert.AreEqual("99", Str($"{c}"));
        Assert.AreEqual("200", Str($"{s}"));
        Assert.AreEqual("5000000001", Str($"{w}"));
    }

    [TestMethod]
    public void Enum_Flags_FormatsCommaSeparatedNames()
    {
        Bits none = Bits.None;
        Bits ab = Bits.A | Bits.B;
        Bits extra = Bits.A | (Bits)8;
        Assert.AreEqual("None", Str($"{none}"));
        Assert.AreEqual("A, B", Str($"{ab}"));
        Assert.AreEqual("9", Str($"{extra}"));
    }

    [TestMethod]
    public void Enum_Format_MatchesEnumToString()
    {
        Color c = Color.Blue;
        Assert.AreEqual("Blue", Str($"{c:G}"));
        Assert.AreEqual("3", Str($"{c:D}"));
        Assert.AreEqual("00000003", Str($"{c:X}"));
        Assert.AreEqual(c.ToString("F"), Str($"{c:F}"));
    }

    [TestMethod]
    public void Enum_Alignment_PadsLikePrintfFieldWidth()
    {
        Color c = Color.Red;
        Assert.AreEqual("[   Red]", Str($"[{c,6}]"));
        Assert.AreEqual("[Red   ]", Str($"[{c,-6}]"));
        Assert.AreEqual("[Red]", Str($"[{c,1}]"));
        Assert.AreEqual("[     1]", Str($"[{c,6:D}]"));
        Assert.AreEqual("[1     ]", Str($"[{c,-6:D}]"));
    }

    [TestMethod]
    public void Enum_LongFlagList_GrowsPastScratchBuffer()
    {
        ManyFlags all = (ManyFlags)((1u << 20) - 1);
        string expected = all.ToString();
        Assert.IsGreaterThan(256, expected.Length, "test enum must exceed the scratch buffer");

        Utf8StringHandler h = $"{all}";
        Assert.AreEqual(expected, Str(h));
        Assert.IsFalse(h.IsTruncated);
        Assert.AreEqual(expected, Str($"{all:G}"));
    }

    [TestMethod]
    public void Enum_AtFullBuffer_IsTruncated()
    {
        string big = new('c', Max);
        Color c = Color.Green;
        Assert.AreEqual(Cut('c'), Str($"{big}{c}"));
        Assert.AreEqual(Cut('c'), Str($"{big}{c:D}{c}"));
    }

    [TestMethod]
    public void Enum_Alignment_TruncatesAtCapacity()
    {
        Color c = Color.Red;
        const int wide = Max + 15;
        Utf8StringHandler h = $"{c,wide}";
        Assert.AreEqual("Red...", Str(h));
        Assert.IsTrue(h.IsTruncated);
    }

    [TestMethod]
    public void Enum_Mixed_WithOtherValues()
    {
        Color c = Color.Blue;
        int n = 7;
        Assert.AreEqual("Blue=3 n=7", Str($"{c}={c:D} n={n}"));
    }

    // ---- culture ---------------------------------------------------------

    [TestMethod]
    public void Numbers_CurrentCultureUsesCommaDecimal_StillFormatInvariant()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            float f = 1.5f;
            double d = -2.25;
            decimal m = 3.75m;
            int n = -1234;
            Assert.AreEqual("1.5", Str($"{f}"));
            Assert.AreEqual("1.50", Str($"{f:F2}"));
            Assert.AreEqual("[ 1.50]", Str($"[{f,5:F2}]"));
            Assert.AreEqual("-2.25", Str($"{d}"));
            Assert.AreEqual("3.75", Str($"{m}"));
            Assert.AreEqual("-1234", Str($"{n}"));
            Assert.AreEqual("1234.50", Str($"{1234.5f:F2}"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- results ---------------------------------------------------------

    [TestMethod]
    public void Results_AgreeAndAreNulTerminated()
    {
        Utf8StringHandler h = $"abc{1}";
        ReadOnlySpan<byte> span = h.GetWrittenBuffer();
        Assert.AreEqual("abc1", System.Text.Encoding.ASCII.GetString(span));

        byte* p = h.GetWrittenBufferPtr();
        Assert.AreEqual(0, p[4]);

        Utf8StringHandler h2 = $"xyz";
        Utf8String u = h2;
        Assert.AreEqual("xyz", CStr.ToString(u));
        Assert.AreEqual(3, u.Length);
    }

    [TestMethod]
    public void NestedHandlers_DoNotOverwriteEachOther()
    {
        // `$"outer {Inner()} tail"`: the hole builds its own handler while the
        // outer one is half-filled.
        Utf8StringHandler outer = new(0, 1);
        outer.AppendLiteral("outer ");
        string inner = Inner();
        outer.AppendFormatted(inner);
        outer.AppendLiteral(" tail");

        Assert.AreEqual("outer inner:7 tail", Encoding.ASCII.GetString(outer.GetWrittenBuffer()));

        static string Inner()
        {
            Utf8StringHandler h = $"inner:{7}";
            return Encoding.ASCII.GetString(h.GetWrittenBuffer());
        }
    }

    // The handlers share a ring of RING_SIZE buffers: a result stays valid
    // until RING_SIZE more handlers are created, then its slot is reused.
    [TestMethod]
    public void Ring_ResultSurvivesRingSizeMinusOneHandlers_ThenIsReused()
    {
        Utf8StringHandler firstHandler = $"first";
        Utf8String first = firstHandler;

        for (int i = 0; i < Utf8StringHandler.RING_SIZE - 1; i++)
        {
            Utf8StringHandler other = $"other{i}";
            _ = other.GetWrittenBuffer();
        }
        Assert.AreEqual("first", CStr.ToString(first));

        Utf8StringHandler wrap = $"wrapped";
        _ = wrap.GetWrittenBuffer();
        Assert.AreEqual("wrapped", CStr.ToString(first));
    }

    [TestMethod]
    public void HandlerReadWhileAnotherIsBuilt_KeepsItsText()
    {
        // Con.Print re-entrancy: a consumer is reading a handler's bytes when
        // something it calls formats (and logs) another string.
        Utf8StringHandler outer = $"chat line";
        ReadOnlySpan<byte> reading = outer.GetWrittenBuffer();

        Utf8StringHandler nested = $"FindFile: {"misc/talk.wav"}\n";
        Assert.AreEqual("FindFile: misc/talk.wav\n", Encoding.ASCII.GetString(nested.GetWrittenBuffer()));

        Assert.AreEqual("chat line", Encoding.ASCII.GetString(reading));
    }
}
