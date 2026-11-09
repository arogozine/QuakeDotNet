// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DScanWarpTests
{
    // Statement-for-statement copy of the original D_WarpScreen (no caching).
    private static void Reference(byte* view, byte* outBuf, int outRowBytes, int outX, int outY)
    {
        int w = RShared.r_refdef->Vrect.Width, h = RShared.r_refdef->Vrect.Height;
        float wratio = w / (float)Screen.scr_vrect->Width, hratio = h / (float)Screen.scr_vrect->Height;
        byte*[] rowptr = new byte*[Screen.scr_vrect->Height + RLocal.AMP2 * 2];
        int[] column = new int[Screen.scr_vrect->Width + RLocal.AMP2 * 2];
        for (int v = 0; v < rowptr.Length; v++)
            rowptr[v] = view + RShared.r_refdef->Vrect.Y * RShared.screenwidth + RShared.screenwidth * (int)(v * hratio * h / (h + RLocal.AMP2 * 2));
        for (int u = 0; u < column.Length; u++)
            column[u] = RShared.r_refdef->Vrect.X + (int)(u * wratio * w / (w + RLocal.AMP2 * 2));
        int* turb = RShared.intsintable + ((int)(RShared.cl_time * 20) & (DIface.CYCLE - 1));
        byte* dest = outBuf + outY * outRowBytes + outX;
        for (int v = 0; v < Screen.scr_vrect->Height; v++, dest += outRowBytes)
            for (int u = 0; u < Screen.scr_vrect->Width; u++)
                dest[u] = rowptr[v + turb[u]][column[turb[v] + u]];
    }

    [TestMethod]
    public void WarpScreen_CachedTables_MatchOriginalAcrossGeometryChanges()
    {
        const int sw = 128;
        byte* view = NativeMemory.AllocArrayZeroed<byte>(sw * 100);
        byte* outA = NativeMemory.AllocArrayZeroed<byte>(sw * 100);
        byte* outB = NativeMemory.AllocArrayZeroed<byte>(sw * 100);
        try
        {
            Random rng = new(8);
            for (int i = 0; i < sw * 100; i++) view[i] = (byte)rng.Next(256);
            for (int i = 0; i < RShared.SIN_BUFFER_SIZE; i++) RShared.intsintable[i] = (int)(RLocal.AMP2 + MathF.Sin(i * 3.14159f * 2 / DIface.CYCLE) * RLocal.AMP2);

            DLocal.d_viewbuffer = view;
            RShared.screenwidth = sw;
            Vid.vid.Buffer = outB; Vid.vid.RowBytes = sw;

            foreach ((int w, int h, int sx, int sy, double time) in new[] { (64, 40, 4, 2, 0.0), (64, 40, 4, 2, 1.3), (80, 50, 6, 3, 0.4), (64, 40, 4, 2, 0.4) })
            {
                RShared.r_refdef->Vrect.X = 0; RShared.r_refdef->Vrect.Y = 0;
                RShared.r_refdef->Vrect.Width = w; RShared.r_refdef->Vrect.Height = h;
                Screen.scr_vrect->X = sx; Screen.scr_vrect->Y = sy;
                Screen.scr_vrect->Width = w - 8; Screen.scr_vrect->Height = h - 8;
                RShared.cl_time = (float)time;
                new Span<byte>(outA, sw * 100).Clear();
                new Span<byte>(outB, sw * 100).Clear();

                Reference(view, outA, sw, sx, sy);
                DScan.WarpScreen();

                Assert.IsTrue(new ReadOnlySpan<byte>(outA, sw * 100).SequenceEqual(new ReadOnlySpan<byte>(outB, sw * 100)), $"{w}x{h} t={time}");
            }
        }
        finally { NativeMemory.Free(view); NativeMemory.Free(outA); NativeMemory.Free(outB); }
    }
}
