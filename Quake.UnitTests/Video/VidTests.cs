// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// vid.h/Vid.cs -- open_tk.md's Section 5: the 8-bit-index -> RGBA32
// expansion table build is a pure function (palette bytes in, uint[256]
// out), so it's covered here without needing an actual window.
[TestClass]
public sealed unsafe class VidTests
{
    [TestMethod]
    public void SetPalette_PacksRgbWithFullAlpha()
    {
        byte* palette = stackalloc byte[256 * 3];
        palette[5 * 3 + 0] = 10;
        palette[5 * 3 + 1] = 20;
        palette[5 * 3 + 2] = 30;

        Vid.SetPalette(palette);

        uint expected = 10u | (20u << 8) | (30u << 16) | (0xFFu << 24);
        Assert.AreEqual(expected, Vid.d_8to24table[5]);
    }

    [TestMethod]
    public void SetPalette_Index0Black_Index255White()
    {
        byte* palette = stackalloc byte[256 * 3];
        for (int i = 0; i < 3; i++)
            palette[255 * 3 + i] = 255;

        Vid.SetPalette(palette);

        Assert.AreEqual(0xFF000000u, Vid.d_8to24table[0]);
        Assert.AreEqual(0xFFFFFFFFu, Vid.d_8to24table[255]);
    }

    [TestMethod]
    public void ShiftPalette_MatchesSetPalette()
    {
        byte* palette = stackalloc byte[256 * 3];
        palette[7 * 3 + 0] = 1;
        palette[7 * 3 + 1] = 2;
        palette[7 * 3 + 2] = 3;

        Vid.ShiftPalette(palette);

        uint expected = 1u | (2u << 8) | (3u << 16) | (0xFFu << 24);
        Assert.AreEqual(expected, Vid.d_8to24table[7]);
    }
    private static int _flipCount;
    private static void CountFlip() => _flipCount++;

    // D_BeginDirectRect/D_EndDirectRect (vid_win.c) -- numpages == 1 path:
    // the bitmap is drawn over vid.direct, the original pixels are saved and
    // later restored, and FlipScreen (Screen.VID_Update) runs after each.
    [TestMethod]
    public void DirectRect_DrawsThenRestores_AndFlipsEachTime()
    {
        const int w = 64, h = 48;
        byte* screen = NativeMemory.AllocArray<byte>(w * h);
        byte* bitmap = stackalloc byte[24 * 24];
        try
        {
            for (int i = 0; i < w * h; i++)
                screen[i] = (byte)(i * 7);
            for (int i = 0; i < 24 * 24; i++)
                bitmap[i] = (byte)(200 + (i % 24));

            byte[] original = new Span<byte>(screen, w * h).ToArray();

            Vid.vid.Direct = screen;
            Vid.vid.RowBytes = w;
            Vid.vid.Aspect = 1.0f;
            _flipCount = 0;
            Screen.VID_Update = &CountFlip;

            Vid.D_BeginDirectRect(w - 24, 0, bitmap, 24, 24);

            Assert.AreEqual(1, _flipCount);
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                    Assert.AreEqual(bitmap[y * 24 + x], screen[y * w + w - 24 + x]);
            // outside the rect untouched
            Assert.AreEqual(original[24 * w + w - 1], screen[24 * w + w - 1]);
            Assert.AreEqual(original[w - 25], screen[w - 25]);

            Vid.D_EndDirectRect(w - 24, 0, 24, 24);

            Assert.AreEqual(2, _flipCount);
            CollectionAssert.AreEqual(original, new Span<byte>(screen, w * h).ToArray());
        }
        finally
        {
            Vid.vid.Direct = null;
            NativeMemory.Free(screen);
        }
    }

    // vid.aspect > 1.5 -- each bitmap row is written twice (reps = 2) over
    // 48 screen rows, and all 48 are restored.
    [TestMethod]
    public void DirectRect_TallAspect_DoublesRows()
    {
        const int w = 32, h = 64;
        byte* screen = NativeMemory.AllocArray<byte>(w * h);
        byte* bitmap = stackalloc byte[24 * 24];
        try
        {
            for (int i = 0; i < w * h; i++)
                screen[i] = (byte)(i * 3);
            for (int i = 0; i < 24 * 24; i++)
                bitmap[i] = (byte)(i / 24);

            byte[] original = new Span<byte>(screen, w * h).ToArray();

            Vid.vid.Direct = screen;
            Vid.vid.RowBytes = w;
            Vid.vid.Aspect = 2.0f;

            Vid.D_BeginDirectRect(8, 0, bitmap, 24, 24);

            for (int y = 0; y < 48; y++)
                Assert.AreEqual((byte)(y >> 1), screen[y * w + 8]);
            Assert.AreEqual(original[48 * w + 8], screen[48 * w + 8]);

            Vid.D_EndDirectRect(8, 0, 24, 24);

            CollectionAssert.AreEqual(original, new Span<byte>(screen, w * h).ToArray());
        }
        finally
        {
            Vid.vid.Direct = null;
            NativeMemory.Free(screen);
        }
    }

    // !vid_initialized -- no direct buffer means nothing is drawn or flipped.
    [TestMethod]
    public void DirectRect_NoDirectBuffer_IsNoOp()
    {
        byte* bitmap = stackalloc byte[24 * 24];
        Vid.vid.Direct = null;
        _flipCount = 0;
        Screen.VID_Update = &CountFlip;

        Vid.D_BeginDirectRect(0, 0, bitmap, 24, 24);
        Vid.D_EndDirectRect(0, 0, 24, 24);

        Assert.AreEqual(0, _flipCount);
    }

    [TestMethod]
    public void SetPalette_FullRandomPalette_MatchesScalarPacking()
    {
        byte* palette = NativeMemory.AllocArrayZeroed<byte>(768);
        try
        {
            new Random(6).NextBytes(new Span<byte>(palette, 768));
            Vid.SetPalette(palette);
            for (int i = 0; i < 256; i++)
            {
                uint expected = palette[i * 3] | ((uint)palette[i * 3 + 1] << 8) | ((uint)palette[i * 3 + 2] << 16) | 0xFF000000u;
                Assert.AreEqual(expected, Vid.d_8to24table[i], $"entry {i}");
            }
        }
        finally { NativeMemory.Free(palette); }
    }
}
