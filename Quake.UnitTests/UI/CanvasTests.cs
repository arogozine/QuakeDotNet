// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// Canvas.cs is a port of Mark V's Draw_SetCanvas and the scr_scaleauto block
// of SCR_UpdateScreen (screen.c); these tests pin the scale math and the
// scaled draw primitives. EngineStateGuard restores Canvas's statics.
[TestClass]
public sealed unsafe class CanvasTests
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

        Draw.draw_chars = NativeMemory.AllocArrayZeroed<byte>(128 * 16);
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);
        NativeMemory.Free(_conBuf);
        NativeMemory.Free(Draw.draw_chars);
    }

    [TestMethod]
    [DataRow(1280, 720, 1, 2, 2, 2)]
    [DataRow(1024, 768, 1, 2, 2, 2)]
    [DataRow(1920, 1080, 1, 3, 3, 3)]
    [DataRow(3840, 2160, 1, 6, 6, 6)]
    [DataRow(1920, 1080, 3, 5, 5, 5)] // 320x200 virtual: smaller text, larger scale
    [DataRow(640, 480, 1, 1, 1, 1)]
    [DataRow(320, 200, 3, 1, 1, 1)]
    public void Compute_Auto_FitsLargestIntegerScale(int w, int h, int auto, int con, int menu, int sbar)
    {
        Assert.IsTrue(Canvas.Compute(w, h, auto, 1, 1, 1, 1, out int c, out int m, out int s, out _));
        Assert.AreEqual(con, c);
        Assert.AreEqual(menu, m);
        Assert.AreEqual(sbar, s);
    }

    [TestMethod]
    public void Compute_AutoOff_KeepsEverythingAtOne()
    {
        Assert.IsTrue(Canvas.Compute(1920, 1080, -1, 4, 4, 4, 1, out int c, out int m, out int s, out _));
        Assert.AreEqual(1, c);
        Assert.AreEqual(1, m);
        Assert.AreEqual(1, s);
    }

    [TestMethod]
    public void Compute_Manual_UsesCvarsClampedToWindow()
    {
        // 640x400: width allows 2x at most (320 virtual pixels minimum)
        Assert.IsTrue(Canvas.Compute(640, 400, 0, 5, 5, 5, 3, out int c, out int m, out int s, out int x));
        Assert.AreEqual(2, c);
        Assert.AreEqual(2, m);
        Assert.AreEqual(2, s);
        Assert.AreEqual(2, x);
    }

    [TestMethod]
    public void Compute_Manual_ZeroOrNegativeCvarsFloorAtOne()
    {
        Assert.IsTrue(Canvas.Compute(1920, 1080, 0, 0, -3, 0, 0, out int c, out int m, out int s, out int x));
        Assert.AreEqual(1, c);
        Assert.AreEqual(1, m);
        Assert.AreEqual(1, s);
        Assert.AreEqual(1, x);
    }

    [TestMethod]
    public void Compute_ZeroSizedWindow_ReturnsFalse()
    {
        Assert.IsFalse(Canvas.Compute(0, 0, 1, 1, 1, 1, 1, out int c, out int m, out int s, out _));
        Assert.AreEqual(1, c);
        Assert.AreEqual(1, m);
        Assert.AreEqual(1, s);
    }

    [TestMethod]
    public void Update_DefaultCvars_LeavesConsoleAtWindowSize()
    {
        Vid.vid.ConWidth = 10;
        Vid.vid.ConHeight = 10;

        Canvas.Update();

        Assert.AreEqual(1, Canvas.ConScale);
        Assert.AreEqual(64u, Vid.vid.ConWidth);
        Assert.AreEqual(64u, Vid.vid.ConHeight);
    }

    [TestMethod]
    public void Set_Menu_CentresBothAxes()
    {
        Vid.vid.Width = 1000;
        Vid.vid.Height = 603;
        Canvas.MenuScale = 2;

        Canvas.Set(CanvasType.Menu);

        Assert.AreEqual(2, Canvas.Scale);
        Assert.AreEqual((1000 - 640) / 2, Canvas.OriginX);
        Assert.AreEqual((603 - 400) / 2, Canvas.OriginY);
    }

    [TestMethod]
    public void Set_Sbar_AnchorsToBottomEdge()
    {
        Vid.vid.Width = 640;
        Vid.vid.Height = 403;
        Canvas.SbarScale = 3;

        Canvas.Set(CanvasType.Sbar);

        // 134 virtual rows * 3 = 402 -> one spare physical row goes on top
        Assert.AreEqual(3, Canvas.Scale);
        Assert.AreEqual(1, Canvas.OriginY);
        Assert.AreEqual(134, Canvas.Height);
        Assert.AreEqual(213, Canvas.Width);
    }

    [TestMethod]
    public void Set_Default_IsIdentity()
    {
        Canvas.Scale = 4;
        Canvas.OriginX = 9;
        Canvas.OriginY = 9;

        Canvas.Set(CanvasType.Default);

        Assert.AreEqual(1, Canvas.Scale);
        Assert.AreEqual(0, Canvas.OriginX);
        Assert.AreEqual(0, Canvas.OriginY);
    }

    [TestMethod]
    public void Fill_Scaled_ExpandsRectAndAppliesOrigin()
    {
        Canvas.Scale = 2;
        Canvas.OriginX = 3;
        Canvas.OriginY = 1;

        Draw.Fill(1, 2, 2, 1, 7);

        // virtual (1,2) 2x1 -> physical x 5..8, y 5..6
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                bool inside = x >= 5 && x < 9 && y >= 5 && y < 7;
                Assert.AreEqual(inside ? (byte)7 : (byte)0, _buf[y * 64 + x], $"({x},{y})");
            }
    }

    [TestMethod]
    public void Fill_Scaled_ClipsToBuffer()
    {
        Canvas.Scale = 3;

        Draw.Fill(20, 20, 10, 10, 9); // physical 60..90 overflows the 64x64 buffer

        Assert.AreEqual((byte)9, _buf[63 * 64 + 63]);
        Assert.AreEqual((byte)9, _buf[60 * 64 + 60]);
        Assert.AreEqual((byte)0, _buf[59 * 64 + 60]);
    }

    [TestMethod]
    public void Pic_Scaled_ExpandsEachPixelIntoBlock()
    {
        byte* pic = stackalloc byte[8 + 4];
        ((QPic_t*)pic)->Width = 2;
        ((QPic_t*)pic)->Height = 2;
        pic[8] = 1; pic[9] = 2; pic[10] = 3; pic[11] = 4;

        Canvas.Scale = 2;
        Canvas.OriginX = 1;
        Canvas.OriginY = 1;

        Draw.Pic(0, 0, (QPic_t*)pic);

        Assert.AreEqual((byte)1, _buf[1 * 64 + 1]);
        Assert.AreEqual((byte)1, _buf[2 * 64 + 2]);
        Assert.AreEqual((byte)2, _buf[1 * 64 + 3]);
        Assert.AreEqual((byte)2, _buf[2 * 64 + 4]);
        Assert.AreEqual((byte)3, _buf[3 * 64 + 1]);
        Assert.AreEqual((byte)4, _buf[4 * 64 + 4]);
        Assert.AreEqual((byte)0, _buf[1 * 64 + 5]); // past the 4x4 block
        Assert.AreEqual((byte)0, _buf[0]);          // before the origin
    }

    [TestMethod]
    public void Pic_Scaled_PartiallyOffscreenIsClippedNotAnError()
    {
        byte* pic = stackalloc byte[8 + 4];
        ((QPic_t*)pic)->Width = 2;
        ((QPic_t*)pic)->Height = 2;
        pic[8] = 5; pic[9] = 5; pic[10] = 5; pic[11] = 5;

        Canvas.Scale = 2;
        Draw.Pic(31, 31, (QPic_t*)pic); // physical 62..65 -> clipped at 64

        Assert.AreEqual((byte)5, _buf[63 * 64 + 63]);
        Assert.AreEqual((byte)5, _buf[62 * 64 + 62]);
    }

    [TestMethod]
    public void TransPic_Scaled_SkipsTransparentColor()
    {
        byte* pic = stackalloc byte[8 + 2];
        ((QPic_t*)pic)->Width = 2;
        ((QPic_t*)pic)->Height = 1;
        pic[8] = DIface.TRANSPARENT_COLOR; pic[9] = 6;

        Canvas.Scale = 2;
        Draw.TransPic(0, 0, (QPic_t*)pic);

        Assert.AreEqual((byte)0, _buf[0]);
        Assert.AreEqual((byte)0, _buf[1]);
        Assert.AreEqual((byte)6, _buf[2]);
        Assert.AreEqual((byte)6, _buf[3]);
        Assert.AreEqual((byte)6, _buf[64 + 3]);
    }

    [TestMethod]
    public void Character_Scaled_ExpandsAndSkipsTransparentZero()
    {
        // char 0, pixel (1,0) opaque only
        Draw.draw_chars[1] = 0xAB;
        Canvas.Scale = 2;

        Draw.Character(0, 0, 0);

        Assert.AreEqual((byte)0xAB, _conBuf[2]);
        Assert.AreEqual((byte)0xAB, _conBuf[3]);
        Assert.AreEqual((byte)0xAB, _conBuf[64 + 2]);
        Assert.AreEqual((byte)0, _conBuf[0]);
        Assert.AreEqual((byte)0, _conBuf[4]);
    }

    [TestMethod]
    public void Character_Scaled_ClippedAtTopDrawsVisibleRowsOnly()
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                Draw.draw_chars[row * 128 + col] = 0xAB;
        Canvas.Scale = 2;

        Draw.Character(0, -3, 0); // 5 visible source rows -> 10 physical rows

        Assert.AreEqual((byte)0xAB, _conBuf[9 * 64]);
        Assert.AreEqual((byte)0, _conBuf[10 * 64]);
    }

    [TestMethod]
    public void Character_Scaled_OffscreenAboveReturns()
    {
        for (int i = 0; i < 8; i++)
            Draw.draw_chars[i] = 0xAA;
        Canvas.Scale = 2;

        Draw.Character(0, -8, 0);

        for (int i = 0; i < 64; i++)
            Assert.AreEqual((byte)0, _conBuf[i]);
    }
}
