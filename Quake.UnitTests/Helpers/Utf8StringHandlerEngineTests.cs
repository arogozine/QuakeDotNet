// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Globalization;
using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// Utf8StringHandler against the shapes the engine formats (Con.Printf stat
// lines, PackFile paths, savegame vectors, HUD numbers, hex pointers), plus
// randomized comparisons with the managed formatting the C code's printf
// corresponds to.
[TestClass]
public sealed unsafe class Utf8StringHandlerEngineTests
{
    private const int Max = Utf8StringHandler.MAX_SIZE;

    private static string Str(in Utf8StringHandler h)
    {
        return CStr.ToString(h.GetWrittenBufferPtr());
    }

    private static byte* CString(string s)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(s);
        byte* p = (byte*)NativeMemory.AllocZeroed((nuint)bytes.Length + 1);
        bytes.AsSpan().CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }


    // ---- engine-shaped strings -------------------------------------------

    [TestMethod]
    public void StatLine_LiteralAndInt()
    {
        int sent = 12345;
        Assert.AreEqual("reliable messages sent     = 12345\n", Str($"reliable messages sent     = {sent}\n"));
    }

    [TestMethod]
    public void TwoCStrings_PackFile()
    {
        byte* pak = CString("id1/pak0.pak");
        byte* file = CString("progs/player.mdl");
        try
        {
            Assert.AreEqual("PackFile: id1/pak0.pak : progs/player.mdl\n", Str($"PackFile: {pak} : {file}\n"));
        }
        finally
        {
            NativeMemory.Free(pak);
            NativeMemory.Free(file);
        }
    }

    [TestMethod]
    public void ModelName_Expression()
    {
        int i = 6;
        Assert.AreEqual("*7", Str($"*{i + 1}"));
    }

    [TestMethod]
    public void ZoneDump_IntAndPointerInHex()
    {
        int size = 12345;
        nint location = 0x1A2B3C;
        Assert.AreEqual("zone size: 12345  location: 1A2B3C\n", Str($"zone size: {size}  location: {location:X}\n"));
    }

    [TestMethod]
    public void QuotedCommand_ConnectAddress()
    {
        string addr = "127.0.0.1:26000";
        Assert.AreEqual("connect \"127.0.0.1:26000\"\n", Str($"connect \"{addr}\"\n"));
    }

    [TestMethod]
    public void Vector_ThreeFloats()
    {
        float x = 512.5f, y = -128.25f, z = 24f;
        Assert.AreEqual("\"origin\" \"512.5 -128.25 24\"\n", Str($"\"origin\" \"{x} {y} {z}\"\n"));
        Assert.AreEqual("512.500000 -128.250000 24.000000", Str($"{x:F6} {y:F6} {z:F6}"));
    }

    [TestMethod]
    public void Hud_NumbersAreRightAlignedToThree()
    {
        int zero = 0, nine = 9, health = 100, negative = -5, big = 1000;
        Assert.AreEqual("  0", Str($"{zero,3}"));
        Assert.AreEqual("  9", Str($"{nine,3}"));
        Assert.AreEqual("100", Str($"{health,3}"));
        Assert.AreEqual(" -5", Str($"{negative,3}"));
        Assert.AreEqual("1000", Str($"{big,3}"));
    }

    [TestMethod]
    public void Table_PrintfFieldWidths()
    {
        int n = 42;
        string name = "maps/e1m1.bsp";
        Assert.AreEqual("   42 maps/e1m1.bsp   |", Str($"{n,5} {name,-16}|"));
    }

    [TestMethod]
    public void MapLine_Mixed()
    {
        string map = "e1m1";
        int edicts = 12345, clients = 4;
        float time = 3.14159f;
        Assert.AreEqual("e1m1: 12345 edicts, 4 clients, 3.14s", Str($"{map}: {edicts} edicts, {clients} clients, {time:F2}s"));
    }

    // ---- reuse / buffer hygiene -----------------------------------------

    [TestMethod]
    public void ConsecutiveHandlers_DoNotLeakEarlierText()
    {
        Assert.AreEqual("a long first message", Str($"a long first message"));

        Utf8StringHandler shorter = $"hi";
        Assert.AreEqual("hi", Str(shorter));
        // The old text beyond the new terminator must not be visible.
        byte* p = shorter.GetWrittenBufferPtr();
        Assert.AreEqual(0, p[2]);
    }

    [TestMethod]
    public void ByteSpan_NonAsciiBytes_AreCopiedAsIs()
    {
        // Quake's coloured console characters are 128-255; byte data is not
        // narrowed, only string chars are.
        ReadOnlySpan<byte> raw = [0xC8, (byte)'i', 0xFF];
        Utf8StringHandler h = $"{raw}";
        Assert.IsTrue(h.GetWrittenBuffer().SequenceEqual(raw));
    }

    // ---- numbers: formats and alignment ----------------------------------

    [TestMethod]
    public void Int_FormatSpecifiers()
    {
        int n = 42;
        int neg = -42;
        Assert.AreEqual("00042", Str($"{n:D5}"));
        Assert.AreEqual("-00042", Str($"{neg:D5}"));
        Assert.AreEqual("2A", Str($"{n:X}"));
        Assert.AreEqual("0x2a", Str($"0x{n:x}"));
    }

    [TestMethod]
    public void Float_FormatSpecifiers()
    {
        float f = 1234.5678f;
        Assert.AreEqual("1235", Str($"{f:F0}"));
        Assert.AreEqual("1234.6", Str($"{f:F1}"));
        Assert.AreEqual("1235", Str($"{f:G4}"));
    }

    [TestMethod]
    public void Int_MinAndMax_Aligned()
    {
        int min = int.MinValue, max = int.MaxValue;
        Assert.AreEqual("[-2147483648]", Str($"[{min,5}]"));
        Assert.AreEqual("[ 2147483647]", Str($"[{max,11}]"));
        Assert.AreEqual("[2147483647 ]", Str($"[{max,-11}]"));
    }

    [TestMethod]
    public void NegativeNumber_Aligned()
    {
        int n = -5;
        float f = -1.5f;
        Assert.AreEqual("[  -5]", Str($"[{n,4}]"));
        Assert.AreEqual("[-5  ]", Str($"[{n,-4}]"));
        Assert.AreEqual("[-1.50]", Str($"[{f,5:F2}]"));
        Assert.AreEqual("[ -1.50]", Str($"[{f,6:F2}]"));
    }

    [TestMethod]
    public void Alignment_ExactWidth_AddsNoPadding()
    {
        int n = 12345;
        Assert.AreEqual("[12345]", Str($"[{n,5}]"));
        Assert.AreEqual("[12345]", Str($"[{n,-5}]"));
    }

    // ---- truncation ------------------------------------------------------

    [TestMethod]
    public void Truncated_ResultIsNulTerminatedAtCapacity()
    {
        string big = new('a', Max + 10);
        Utf8StringHandler h = $"{big}";
        Assert.IsTrue(h.IsTruncated);

        ReadOnlySpan<byte> written = h.GetWrittenBuffer();
        Assert.AreEqual("...".Length, written.Length);
        Assert.IsTrue(written.EndsWith("..."u8));
        Assert.AreEqual(0, h.GetWrittenBufferPtr()["...".Length]);
    }

    [TestMethod]
    public void Truncated_LaterAppendsOfEveryTypeAreIgnored()
    {
        string big = new('a', Max + 10);
        string s = "str";
        ReadOnlySpan<byte> span = "span"u8;
        byte* p = CString("cstr");
        int i = 1;
        float f = 1.5f;
        double d = 2.5;
        long l = 3;
        try
        {
            Utf8StringHandler h = $"{big}{s}{span}{p}{i}{f}{d}{l}{s,10}{i,5}{f,-8:F2}tail";
            string result = Str(h);
            Assert.AreEqual("...", result);
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    [TestMethod]
    public void RightAlignedNumber_PaddingDoesNotFit_KeepsValueThenEllipsis()
    {
        // 6 bytes left, "{n,8}" wants 6 spaces + "42": the value is written
        // but the padding that would shift it right does not fit.
        string big = new('d', Max - 6);
        int n = 42;
        Assert.AreEqual(big + "42...", Str($"{big}{n,8}"));
    }

    [TestMethod]
    public void RightAlignedNumber_ExactlyFitting_IsNotTruncated()
    {
        string big = new('d', Max - 5);
        int n = 42;
        Utf8StringHandler h = $"{big}{n,5}";
        Assert.AreEqual(big + "   42", Str(h));
        Assert.IsFalse(h.IsTruncated);
    }

    [TestMethod]
    public void AlignedNumber_ThatCannotFitAtAll_IsReplacedByEllipsis()
    {
        string big = new('d', Max - 3);
        int n = 1234567;
        Utf8StringHandler h = $"{big}{n,10}";
        Assert.AreEqual(big + "...", Str(h));
        Assert.IsTrue(h.IsTruncated);
    }

    [TestMethod]
    public void LeftAlignedNumber_PaddingOverflow_EndsWithEllipsis()
    {
        string big = new('d', Max - 4);
        int n = 7;
        Utf8StringHandler h = $"{big}{n,-9}";
        string result = Str(h);
        Assert.AreEqual(Max, result.Length);
        Assert.StartsWith(big[..^3], result);
        Assert.EndsWith("...", result);
        Assert.IsTrue(h.IsTruncated);
    }

    // ---- randomized comparison against managed formatting ---------------

    [TestMethod]
    public void Random_IntsWithAlignment_MatchManagedPadding()
    {
        Random rng = new(12345);
        for (int iter = 0; iter < 400; iter++)
        {
            int value = rng.Next(int.MinValue, int.MaxValue);
            if (iter % 5 == 0)
                value %= 1000;

            // Alignment must be a constant in an interpolation hole, so the
            // random cases select from a fixed set.
            int a = rng.Next(0, 6);
            string t = value.ToString(CultureInfo.InvariantCulture);
            (string actual, string expected) = a switch
            {
                0 => (Str($"{value,0}"), t),
                1 => (Str($"{value,1}"), t.PadLeft(1)),
                2 => (Str($"{value,8}"), t.PadLeft(8)),
                3 => (Str($"{value,-8}"), t.PadRight(8)),
                4 => (Str($"{value,16}"), t.PadLeft(16)),
                _ => (Str($"{value,-16}"), t.PadRight(16)),
            };
            Assert.AreEqual(expected, actual, $"value={value} case={a}");
        }
    }

    [TestMethod]
    public void Random_FloatsWithFormat_MatchManagedFormatting()
    {
        Random rng = new(777);
        for (int iter = 0; iter < 300; iter++)
        {
            float f = (float)((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(0, 6)));
            string f2 = f.ToString("F2", CultureInfo.InvariantCulture);
            string f0 = f.ToString("F0", CultureInfo.InvariantCulture);
            string def = f.ToString(CultureInfo.InvariantCulture);

            Assert.AreEqual(def, Str($"{f}"), $"f={f:R}");
            Assert.AreEqual(f2, Str($"{f:F2}"), $"f={f:R}");
            Assert.AreEqual(f0, Str($"{f:F0}"), $"f={f:R}");
            Assert.AreEqual(f2.PadLeft(12), Str($"{f,12:F2}"), $"f={f:R}");
            Assert.AreEqual(f2.PadRight(12), Str($"{f,-12:F2}"), $"f={f:R}");
        }
    }

    // ---- manual handler use (what the compiler emits) -------------------

    [TestMethod]
    public void ManualAppends_MatchInterpolation()
    {
        Utf8StringHandler h = new(8, 3);
        h.AppendLiteral("n=");
        h.AppendFormatted(42, 5);
        h.AppendLiteral(" f=");
        h.AppendFormatted(1.5f, 0, "F1");
        h.AppendLiteral(" s=");
        h.AppendFormatted("ab", -4);
        Assert.AreEqual("n=   42 f=1.5 s=ab  ", Str(h));
        Assert.IsFalse(h.IsTruncated);
    }
}
