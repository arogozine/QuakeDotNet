// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DrawTests
{
    private byte* _buf;
    private byte* _conBuf;

    [TestInitialize]
    public void Setup()
    {
        DIface.r_pixbytes = 1;

        _buf = NativeMemory.AllocArrayZeroed<byte>(64 * 64);
        Vid.vid.Buffer = _buf;
        Vid.vid.RowBytes = 64;
        Vid.vid.Width = 64;
        Vid.vid.Height = 64;

        _conBuf = NativeMemory.AllocArrayZeroed<byte>(64 * 64);
        Vid.vid.ConBuffer = _conBuf;
        Vid.vid.ConRowBytes = 64;
        Vid.vid.ConWidth = 64;
        Vid.vid.ConHeight = 64;
        Vid.vid.Direct = null;

        Draw.draw_chars = NativeMemory.AllocArrayZeroed<byte>(128 * 16);
        Draw.menu_numcachepics = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
        NativeMemory.Free(_conBuf);
        NativeMemory.Free(Draw.draw_chars);
    }

    [TestMethod]
    public void Character_OffscreenAbove_ReturnsWithoutWriting()
    {
        // y <= -8 -- totally off screen, matches "if (y <= -8) return;"
        for (int i = 0; i < 8; i++)
        {
            Draw.draw_chars[i] = 0xAA; // char 0, row 0, opaque pixels
        }

        Draw.Character(0, -8, 0);

        for (int i = 0; i < 64; i++)
        {
            Assert.AreEqual((byte)0, _conBuf[i]);
        }
    }

    [TestMethod]
    public void Character_ClippedAtTop_DrawsOnlyVisibleRows()
    {
        // char 0 -> source = draw_chars + 0. Fill all 8 rows of char 0 with
        // 0xAB (non-transparent) so every drawn pixel is checkable.
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                Draw.draw_chars[(row * 128) + col] = 0xAB;
            }
        }

        // y=-3: drawline = 8+(-3) = 5 visible rows, clipped to y=0.
        // source -= 128*y i.e. source += 128*3 -> starts at row index 3.
        Draw.Character(2, -3, 0);

        // Row 0 of the console buffer now holds original char row 3.
        Assert.AreEqual((byte)0xAB, _conBuf[2]);
        // 5 rows should have been drawn (rows 0..4 of conbuffer, each from
        // char source rows 3..7).
        Assert.AreEqual((byte)0xAB, _conBuf[(4 * 64) + 2]);
        // Row 5 (would be char source row 8, beyond the char) untouched.
        Assert.AreEqual((byte)0, _conBuf[(5 * 64) + 2]);
    }

    [TestMethod]
    public void Character_ZeroPixelsAreTransparentAndSkipped()
    {
        // Only pixel 3 of char 0 row 0 is opaque; rest must stay untouched.
        Draw.draw_chars[3] = 0x11;

        Draw.Character(0, 0, 0);

        Assert.AreEqual((byte)0x11, _conBuf[3]);
        Assert.AreEqual((byte)0, _conBuf[0]);
        Assert.AreEqual((byte)0, _conBuf[1]);
    }

    [TestMethod]
    public void Pic_CopiesSourceRowsVerbatim()
    {
        int pixelBytes = 4 * 3; // 4 wide, 3 tall
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(8 + pixelBytes);
        try
        {
            QPic_t* pic = (QPic_t*)buf;
            pic->Width = 4;
            pic->Height = 3;
            byte* data = buf + 8;
            for (int i = 0; i < pixelBytes; i++)
            {
                data[i] = (byte)(i + 1);
            }

            Draw.Pic(2, 1, pic);

            // dest row 0 starts at buffer offset rowbytes*1 + 2 = 66
            Assert.AreEqual((byte)1, _buf[(64 * 1) + 2]);
            Assert.AreEqual((byte)4, _buf[(64 * 1) + 5]);
            // row 1
            Assert.AreEqual((byte)5, _buf[(64 * 2) + 2]);
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void Pic_OutOfBoundsCoordinates_ThrowsSysError()
    {
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(8 + 4);
        try
        {
            QPic_t* pic = (QPic_t*)buf;
            pic->Width = 100; // wider than the 64x64 vid buffer
            pic->Height = 1;

            Assert.ThrowsExactly<QuakeSysErrorException>(() => Draw.Pic(0, 0, pic));
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void TransPic_SkipsTransparentColorAndCopiesOthers()
    {
        int width = 9; // not a multiple of 8 -- exercises the "general" path
        int pixelBytes = width * 1;
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(8 + pixelBytes);
        try
        {
            QPic_t* pic = (QPic_t*)buf;
            pic->Width = width;
            pic->Height = 1;
            byte* data = buf + 8;
            data[0] = 5;
            data[1] = DIface.TRANSPARENT_COLOR;
            data[2] = 6;

            // Pre-fill destination so the transparent pixel's "untouched"
            // status is observable.
            _buf[(64 * 0) + 0] = 0;
            _buf[(64 * 0) + 1] = 0xEE;
            _buf[(64 * 0) + 2] = 0;

            Draw.TransPic(0, 0, pic);

            Assert.AreEqual((byte)5, _buf[0]);
            Assert.AreEqual((byte)0xEE, _buf[1]); // transparent -- unchanged
            Assert.AreEqual((byte)6, _buf[2]);
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void TransPic_UnwoundPath_Width8Multiple_MatchesGeneralPath()
    {
        int width = 8;
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(8 + width);
        try
        {
            QPic_t* pic = (QPic_t*)buf;
            pic->Width = width;
            pic->Height = 1;
            byte* data = buf + 8;
            for (int i = 0; i < width; i++)
            {
                data[i] = (byte)(i == 3 ? DIface.TRANSPARENT_COLOR : i + 1);
            }

            Draw.TransPic(0, 0, pic);

            for (int i = 0; i < width; i++)
            {
                if (i == 3)
                {
                    Assert.AreEqual((byte)0, _buf[i]);
                }
                else
                {
                    Assert.AreEqual((byte)(i + 1), _buf[i]);
                }
            }
        }
        finally
        {
            NativeMemory.Free(buf);
        }
    }

    [TestMethod]
    public void Fill_WritesSolidColorRect()
    {
        Draw.Fill(1, 1, 3, 2, 0x42);

        Assert.AreEqual((byte)0x42, _buf[(64 * 1) + 1]);
        Assert.AreEqual((byte)0x42, _buf[(64 * 1) + 3]);
        Assert.AreEqual((byte)0x42, _buf[(64 * 2) + 1]);
        Assert.AreEqual((byte)0, _buf[(64 * 1) + 4]); // outside the rect
        Assert.AreEqual((byte)0, _buf[(64 * 0) + 1]); // outside the rect
    }

    [TestMethod]
    public void DrawRect8_Transparent_SkipsTransparentColorColumnsOnly()
    {
        VRect_t rect = default;
        rect.X = 0;
        rect.Y = 0;
        rect.Width = 3;
        rect.Height = 1;

        byte* src = stackalloc byte[3];
        src[0] = 9;
        src[1] = DIface.TRANSPARENT_COLOR;
        src[2] = 10;

        _buf[1] = 0x55; // pre-fill so "untouched" is observable

        Draw.DrawRect8(&rect, 3, src, 1);

        Assert.AreEqual((byte)9, _buf[0]);
        Assert.AreEqual((byte)0x55, _buf[1]);
        Assert.AreEqual((byte)10, _buf[2]);
    }

    [TestMethod]
    public void DrawRect8_Opaque_CopiesRowsViaMemcpy()
    {
        VRect_t rect = default;
        rect.X = 1;
        rect.Y = 1;
        rect.Width = 2;
        rect.Height = 2;

        byte* src = stackalloc byte[4];
        src[0] = 1; src[1] = 2; src[2] = 3; src[3] = 4;

        Draw.DrawRect8(&rect, 2, src, 0);

        Assert.AreEqual((byte)1, _buf[(64 * 1) + 1]);
        Assert.AreEqual((byte)2, _buf[(64 * 1) + 2]);
        Assert.AreEqual((byte)3, _buf[(64 * 2) + 1]);
        Assert.AreEqual((byte)4, _buf[(64 * 2) + 2]);
    }

    [TestMethod]
    public void TileClear_TilesSourceAcrossDestinationRect()
    {
        // A 4x4 tile repeated to cover a destination rect starting at a
        // non-tile-aligned origin -- exercises the tileoffsetx/tileoffsety
        // partial-tile-clip logic. r_rectdesc is set directly (internal,
        // see Draw.cs) rather than via Draw_Init, which requires a loaded
        // WAD file.
        byte* tile = NativeMemory.AllocArrayZeroed<byte>(4 * 4);
        for (int i = 0; i < 16; i++)
        {
            tile[i] = (byte)(i + 1);
        }

        Draw.r_rectdesc.Width = 4;
        Draw.r_rectdesc.Height = 4;
        Draw.r_rectdesc.PTexBytes = tile;
        Draw.r_rectdesc.RowBytes = 4;

        try
        {
            // dest rect at (2,1), size 4x4: tileoffsetx = 2%4 = 2,
            // tileoffsety = 1%4 = 1 -- exercises the clipped-first-tile path.
            Draw.TileClear(2, 1, 4, 4);

            // Top-left dest pixel (2,1) pulls from tile[(1)*4 + 2] = tile[6] = 7.
            Assert.AreEqual((byte)7, _buf[(64 * 1) + 2]);
        }
        finally
        {
            NativeMemory.Free(tile);
        }
    }

    [TestMethod]
    public void FadeScreen_AppliesDitherPatternMask()
    {
        for (int i = 0; i < 64 * 64; i++)
        {
            _buf[i] = 0xFF;
        }

        Draw.FadeScreen();

        // "if ((x&3)!=t) pbuf[x]=0" -- a pixel survives only when
        // (x&3)==t, so it's zeroed everywhere except that one phase.
        // y=0: t=0 -> x=0 kept, x=1..3 zeroed.
        Assert.AreEqual((byte)0xFF, _buf[0]);
        Assert.AreEqual((byte)0, _buf[1]);
        // y=1: t=2 -> x=2 kept, x=0 zeroed.
        Assert.AreEqual((byte)0xFF, _buf[64 + 2]);
        Assert.AreEqual((byte)0, _buf[64 + 0]);
    }

    [TestMethod]
    public void BeginDisc_EndDisc_AreNoOpsAndDoNotThrow()
    {
        Draw.BeginDisc();
        Draw.EndDisc();
    }

    [TestMethod]
    public void FadeScreen_OddSizedBuffer_MatchesScalarOriginal()
    {
        const int w = 77, h = 9, rowBytes = 96;
        byte* buf = NativeMemory.AllocArrayZeroed<byte>(rowBytes * h);
        try
        {
            Random rng = new(1);
            byte[] expected = new byte[rowBytes * h];
            for (int i = 0; i < expected.Length; i++)
            {
                buf[i] = expected[i] = (byte)rng.Next(1, 256);
            }

            for (int y = 0; y < h; y++)
            {
                int t = (y & 1) << 1;
                for (int x = 0; x < w; x++)
                {
                    if ((x & 3) != t)
                    {
                        expected[(y * rowBytes) + x] = 0;
                    }
                }
            }

            Vid.vid.Buffer = buf; Vid.vid.RowBytes = rowBytes; Vid.vid.Width = w; Vid.vid.Height = h;
            Draw.FadeScreen();

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], buf[i], $"byte {i}");
            }
        }
        finally { NativeMemory.Free(buf); }
    }

    [TestMethod]
    public void TransPic_AllWidths_MatchScalarOriginal()
    {
        Random rng = new(2);
        for (int width = 1; width <= 50; width++)
        {
            const int h = 3;
            byte* picMem = NativeMemory.AllocArrayZeroed<byte>(8 + (width * h));
            try
            {
                QPic_t* pic = (QPic_t*)picMem;
                pic->Width = width; pic->Height = h;
                byte* data = picMem + 8;
                for (int i = 0; i < width * h; i++)
                {
                    data[i] = rng.Next(3) == 0 ? (byte)DIface.TRANSPARENT_COLOR : (byte)rng.Next(255);
                }

                byte[] expected = new byte[64 * 64];
                for (int i = 0; i < expected.Length; i++)
                {
                    _buf[i] = expected[i] = (byte)rng.Next(256);
                }

                for (int v = 0; v < h; v++)
                {
                    for (int u = 0; u < width; u++)
                    {
                        if (data[(v * width) + u] != DIface.TRANSPARENT_COLOR)
                        {
                            expected[((v + 2) * 64) + 3 + u] = data[(v * width) + u];
                        }
                    }
                }

                Draw.TransPic(3, 2, pic);

                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.AreEqual(expected[i], _buf[i], $"width {width} byte {i}");
                }
            }
            finally { NativeMemory.Free(picMem); }
        }
    }

    [TestMethod]
    public void Fill_AllWidths_MatchScalarOriginal()
    {
        for (int w = 0; w <= 40; w++)
        {
            new Span<byte>(_buf, 64 * 64).Clear();
            Draw.Fill(5, 3, w, 4, 0x77);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool inside = y >= 3 && y < 7 && x >= 5 && x < 5 + w;
                    Assert.AreEqual(inside ? (byte)0x77 : (byte)0, _buf[(y * 64) + x], $"w {w} ({x},{y})");
                }
            }
        }
    }

    [TestMethod]
    public void ScaledPic_OpaqueAndClipped_MatchesPerPixelExpansion()
    {
        Random rng = new(3);
        foreach (int s in new[] { 2, 3 })
        {
            foreach ((int cx, int cy) in new[] { (0, 0), (2, 1), (-3, 2), (20, 18), (4, -2), (1, 3) })
            {
                const int pw = 6, ph = 5;
                byte* picMem = NativeMemory.AllocArrayZeroed<byte>(8 + (pw * ph));
                try
                {
                    QPic_t* pic = (QPic_t*)picMem;
                    pic->Width = pw; pic->Height = ph;
                    byte* data = picMem + 8;
                    for (int i = 0; i < pw * ph; i++)
                    {
                        data[i] = (byte)rng.Next(256);
                    }

                    byte[] expected = new byte[64 * 64];
                    for (int i = 0; i < expected.Length; i++)
                    {
                        _buf[i] = expected[i] = (byte)rng.Next(256);
                    }

                    int px = cx * s, py = cy * s;
                    for (int y = 0; y < ph * s; y++)
                    {
                        for (int x = 0; x < pw * s; x++)
                        {
                            int dx = px + x, dy = py + y;
                            if ((uint)dx < 64 && (uint)dy < 64)
                            {
                                expected[(dy * 64) + dx] = data[((y / s) * pw) + (x / s)];
                            }
                        }
                    }

                    Canvas.Scale = s; Canvas.OriginX = 0; Canvas.OriginY = 0;
                    Draw.Pic(cx, cy, pic);

                    for (int i = 0; i < expected.Length; i++)
                    {
                        Assert.AreEqual(expected[i], _buf[i], $"s{s} at ({cx},{cy}) byte {i}");
                    }
                }
                finally { NativeMemory.Free(picMem); Canvas.Scale = 1; }
            }
        }
    }

    [TestMethod]
    public void ConbackStretch8_MatchesOriginalRunningFraction()
    {
        Random rng = new(4);
        byte* conback = NativeMemory.AllocArrayZeroed<byte>(320 * 200);
        byte* big = NativeMemory.AllocArrayZeroed<byte>(700 * 400);
        try
        {
            for (int i = 0; i < 320 * 200; i++)
            {
                conback[i] = (byte)rng.Next(256);
            }

            foreach ((int w, int h, int scale, int lines) in new[] { (320, 200, 1, 100), (640, 400, 2, 150), (700, 350, 1, 350), (500, 300, 3, 60) })
            {
                Vid.vid.ConBuffer = big; Vid.vid.ConRowBytes = 700; Vid.vid.Buffer = big;
                Vid.vid.Width = (uint)w; Vid.vid.Height = (uint)h;
                Vid.vid.ConWidth = (uint)(w / scale); Vid.vid.ConHeight = (uint)(h / scale);
                Canvas.Scale = scale;
                new Span<byte>(big, 700 * 400).Clear();

                byte[] expected = new byte[700 * 400];
                int conHeight = (int)Vid.vid.ConHeight;
                int rows = (int)Math.Min(Vid.vid.Height, (uint)(lines * scale));
                int fstep = (int)(320L * 0x10000 / w);
                for (int y = 0; y < rows; y++)
                {
                    int v = Math.Clamp((conHeight - lines + (y / scale)) * 200 / conHeight, 0, 199);
                    byte* src = conback + (v * 320);
                    int f = 0;
                    for (int x = 0; x < w; x++) { expected[(y * 700) + x] = src[f >> 16]; f += fstep; }
                }

                Draw.ConbackStretch8(conback, lines);

                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.AreEqual(expected[i], big[i], $"{w}x{h} s{scale} byte {i}");
                }
            }
        }
        finally { NativeMemory.Free(conback); NativeMemory.Free(big); Canvas.Scale = 1; }
    }
}
