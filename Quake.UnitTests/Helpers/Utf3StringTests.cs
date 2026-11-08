// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

// Utf3String.Format must match sbar.c's sprintf (num, "%3i", v) as read through num[0..2]:
// right-aligned, space padded, first 3 characters when the text is wider.
[TestClass]
public sealed unsafe class Utf3StringTests
{
    private static string Format(int v)
    {
        Utf3String num = default;
        num.Format(v);
        return new string([(char)num[0], (char)num[1], (char)num[2]]);
    }

    // sprintf("%3i") then num[0..2]
    private static string Expected(int v) => v.ToString().PadLeft(3)[..3];

    [TestMethod]
    [DataRow(0, "  0")]
    [DataRow(5, "  5")]
    [DataRow(42, " 42")]
    [DataRow(999, "999")]
    [DataRow(-5, " -5")]
    [DataRow(-42, "-42")]
    public void Format_RightAlignsLikePrintf(int v, string expected)
    {
        Assert.AreEqual(expected, Format(v));
    }

    [TestMethod]
    [DataRow(1000, "100")]
    [DataRow(123456, "123")]
    [DataRow(-100, "-10")]
    [DataRow(-1000, "-10")]
    public void Format_WiderThanThree_TruncatesToFirstThreeChars(int v, string expected)
    {
        Assert.AreEqual(expected, Format(v));
    }

    [TestMethod]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    [DataRow(-1)]
    [DataRow(9)]
    [DataRow(10)]
    [DataRow(99)]
    [DataRow(100)]
    [DataRow(-9)]
    [DataRow(-10)]
    [DataRow(-99)]
    [DataRow(-99999)]
    public void Format_MatchesPrintfReference(int v)
    {
        Assert.AreEqual(Expected(v), Format(v));
    }

    [TestMethod]
    public void Format_MatchesPrintfReference_Sweep()
    {
        for (int v = -2000; v <= 2000; v++)
            Assert.AreEqual(Expected(v), Format(v), $"v={v}");
    }

    [TestMethod]
    public void Format_Reused_FullyOverwritesPreviousValue()
    {
        Utf3String num = default;
        num.Format(123);
        num.Format(7);
        Assert.AreEqual((byte)' ', num[0]);
        Assert.AreEqual((byte)' ', num[1]);
        Assert.AreEqual((byte)'7', num[2]);
    }
}
