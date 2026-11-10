// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// draw.c -- this is the only file outside the refresh that touches the
/// vid buffer.
/// </summary>
public static unsafe class Draw
{
    // r_rectdesc -- internal (not private) so tests can populate it
    // directly without requiring a loaded WAD file (Draw_Init's only other
    // source), matching the InternalsVisibleTo precedent already used by
    // RSprite.cs's clip_verts/clip_current.
    internal static RectDesc_t r_rectdesc;

    // draw_chars
    public static byte* draw_chars;

    // draw_disc
    public static QPic_t* draw_disc;

    // draw_backtile
    public static QPic_t* draw_backtile;

    public const int MAX_CACHED_PICS = 128;

    // menu_cachepics
    public static readonly CachePic_t* menu_cachepics = NativeMemory.AllocArrayZeroed<CachePic_t>(MAX_CACHED_PICS);

    // menu_numcachepics
    public static int menu_numcachepics;

    // Draw_PicFromWad
    public static QPic_t* PicFromWad([InterpolatedStringHandlerArgument] scoped Utf8StringHandler name) => PicFromWad((Utf8String)name);
    public static QPic_t* PicFromWad(Utf8String name) => (QPic_t*)Wad.GetLumpName(name);

    // Not in the original -- scaled-canvas drawing (see Canvas.cs). With the
    // default canvas (scale 1, origin 0) every primitive below runs the
    // original unscaled code path unchanged.
    private static bool IsIdentityCanvas => Canvas.Scale == 1 && Canvas.OriginX == 0 && Canvas.OriginY == 0;

    // Expands a w x h block of source pixels into Scale x Scale blocks at
    // physical (px, py), clipped to the buffer. transparent < 0 means opaque;
    // translation (optional) remaps each drawn pixel.
    private static void BlitScaled(byte* dstBase, int dstRowBytes, int px, int py, byte* src, int srcStride,
        int w, int h, int s, int transparent, byte* translation)
    {
        int bufW = (int)Vid.vid.Width;
        int bufH = (int)Vid.vid.Height;

        // Opaque, untranslated, horizontally unclipped: every one of the s
        // output rows of a source row is identical, so expand the row once
        // and copy it for the remaining rows.
        if (transparent < 0 && translation == null && px >= 0 && px + (w * s) <= bufW)
        {
            int rowLen = w * s;
            for (int sy = 0; sy < h; sy++)
            {
                byte* srow = src + (sy * srcStride);
                byte* first = null;
                for (int k = 0; k < s; k++)
                {
                    int dy = py + (sy * s) + k;
                    if ((uint)dy >= (uint)bufH)
                    {
                        continue;
                    }

                    byte* drow = dstBase + (dy * dstRowBytes) + px;
                    if (first == null)
                    {
                        first = drow;
                        for (int sx = 0, dx = 0; sx < w; sx++)
                        {
                            byte p = srow[sx];
                            for (int j = 0; j < s; j++)
                            {
                                drow[dx++] = p;
                            }
                        }
                    }
                    else
                    {
                        Buffer.MemoryCopy(first, drow, rowLen, rowLen);
                    }
                }
            }
            return;
        }

        for (int sy = 0; sy < h; sy++)
        {
            byte* srow = src + (sy * srcStride);
            for (int k = 0; k < s; k++)
            {
                int dy = py + (sy * s) + k;
                if ((uint)dy >= (uint)bufH)
                {
                    continue;
                }

                byte* drow = dstBase + (dy * dstRowBytes);
                for (int sx = 0; sx < w; sx++)
                {
                    byte p = srow[sx];
                    if (p == transparent)
                    {
                        continue;
                    }

                    if (translation != null)
                    {
                        p = translation[p];
                    }

                    int dx = px + (sx * s);
                    for (int j = 0; j < s; j++, dx++)
                    {
                        if ((uint)dx < (uint)bufW)
                        {
                            drow[dx] = p;
                        }
                    }
                }
            }
        }
    }

    private static void CharacterScaled(int x, int y, int num)
    {
        int s = Canvas.Scale;
        int px = (x * s) + Canvas.OriginX;
        int py = (y * s) + Canvas.OriginY;

        if (y <= -8)
        {
            return; // totally off screen (clipped to the top for console scroll)
        }

        byte* source = draw_chars + ((num >> 4) << 10) + ((num & 15) << 3);
        BlitScaled(Vid.vid.ConBuffer, (int)Vid.vid.ConRowBytes, px, py, source, 128, 8, 8, s, 0, null);
    }

    private static void PicScaled(int x, int y, QPic_t* pic, int transparent, byte* translation)
    {
        int s = Canvas.Scale;
        byte* source = (byte*)pic + (sizeof(int) * 2);
        BlitScaled(Vid.vid.Buffer, (int)Vid.vid.RowBytes, (x * s) + Canvas.OriginX, (y * s) + Canvas.OriginY,
            source, pic->Width, pic->Width, pic->Height, s, transparent, translation);
    }

    private static void FillScaled(int x, int y, int w, int h, int c)
    {
        int s = Canvas.Scale;
        int x0 = Math.Max(0, (x * s) + Canvas.OriginX);
        int y0 = Math.Max(0, (y * s) + Canvas.OriginY);
        int x1 = Math.Min((int)Vid.vid.Width, ((x + w) * s) + Canvas.OriginX);
        int y1 = Math.Min((int)Vid.vid.Height, ((y + h) * s) + Canvas.OriginY);

        for (int v = y0; v < y1; v++)
        {
            byte* dest = Vid.vid.Buffer + (v * Vid.vid.RowBytes);
            if (x1 > x0)
            {
                new Span<byte>(dest + x0, x1 - x0).Fill((byte)c);
            }
        }
    }

    // Draw_CachePic
    public static QPic_t* CachePic([InterpolatedStringHandlerArgument] scoped Utf8StringHandler path)
    {
        return CachePic((Utf8String)path);
    }

    public static QPic_t* CachePic(Utf8String path)
    {
        CachePic_t* pic = menu_cachepics;
        int i;
        for (i = 0; i < menu_numcachepics; pic++, i++)
        {
            if (Q.StrCmp(path, pic->Name) == 0)
            {
                break;
            }
        }

        if (i == menu_numcachepics)
        {
            if (menu_numcachepics == MAX_CACHED_PICS)
            {
                Sys.Error("menu_numcachepics == MAX_CACHED_PICS"u8);
            }

            menu_numcachepics++;
            Q.StrCpy(pic->Name, path);
        }

        CacheUser* cache = &pic->Cache;
        void* dat = Cache.Check(cache);
        if (dat != null)
        {
            return (QPic_t*)dat;
        }

        // load the pic from disk
        Com.LoadCacheFile(path, cache);

        dat = pic->Cache.Data;
        if (dat == null)
        {
            Sys.Error($"Draw_CachePic: failed to load {path}");
        }

        Wad.SwapPic((QPic_t*)dat);

        return (QPic_t*)dat;
    }

    // Draw_Init
    public static void Init()
    {
        draw_chars = (byte*)Wad.GetLumpName("conchars"u8);
        draw_disc = (QPic_t*)Wad.GetLumpName("disc"u8);
        draw_backtile = (QPic_t*)Wad.GetLumpName("backtile"u8);

        r_rectdesc.Width = draw_backtile->Width;
        r_rectdesc.Height = draw_backtile->Height;
        r_rectdesc.PTexBytes = (byte*)draw_backtile + (sizeof(int) * 2);
        r_rectdesc.RowBytes = draw_backtile->Width;
    }

    // Draw_Character
    //
    // Draws one 8*8 graphics character with 0 being transparent.
    // It can be clipped to the top of the screen to allow the console to be
    // smoothly scrolled off.
    public static void Character(int x, int y, int num)
    {
        num &= 255;

        if (!IsIdentityCanvas)
        {
            CharacterScaled(x, y, num);
            return;
        }

        if (y <= -8)
        {
            return; // totally off screen
        }

        int row = num >> 4;
        int col = num & 15;
        byte* source = draw_chars + (row << 10) + (col << 3);

        int drawline;
        if (y < 0)
        { // clipped
            drawline = 8 + y;
            source -= 128 * y;
            y = 0;
        }
        else
        {
            drawline = 8;
        }

        if (DIface.r_pixbytes == 1)
        {
            byte* dest = Vid.vid.ConBuffer + (y * Vid.vid.ConRowBytes) + x;

            while (drawline-- != 0)
            {
                DrawSimd.TransRow8(dest, source, 0);
                source += 128;
                dest += Vid.vid.ConRowBytes;
            }
        }
        else
        {
            // FIXME: pre-expand to native format?
            ushort* pusdest = (ushort*)(Vid.vid.ConBuffer + (y * Vid.vid.ConRowBytes) + (x << 1));

            while (drawline-- != 0)
            {
                if (source[0] != 0)
                {
                    pusdest[0] = Vid.d_8to16table[source[0]];
                }

                if (source[1] != 0)
                {
                    pusdest[1] = Vid.d_8to16table[source[1]];
                }

                if (source[2] != 0)
                {
                    pusdest[2] = Vid.d_8to16table[source[2]];
                }

                if (source[3] != 0)
                {
                    pusdest[3] = Vid.d_8to16table[source[3]];
                }

                if (source[4] != 0)
                {
                    pusdest[4] = Vid.d_8to16table[source[4]];
                }

                if (source[5] != 0)
                {
                    pusdest[5] = Vid.d_8to16table[source[5]];
                }

                if (source[6] != 0)
                {
                    pusdest[6] = Vid.d_8to16table[source[6]];
                }

                if (source[7] != 0)
                {
                    pusdest[7] = Vid.d_8to16table[source[7]];
                }

                source += 128;
                pusdest += Vid.vid.ConRowBytes >> 1;
            }
        }
    }

    // Draw_String
    public static void String(int x, int y, Utf8String str_)
    {
        byte* str = str_;
        while (*str != 0)
        {
            Character(x, y, *str);
            str++;
            x += 8;
        }
    }

    // Draw_DebugChar

    // Draw_Pic
    public static void Pic(int x, int y, QPic_t* pic)
    {
        if (!IsIdentityCanvas)
        {
            PicScaled(x, y, pic, -1, null);
            return;
        }

        if (x < 0 || x + pic->Width > Vid.vid.Width || y < 0 || y + pic->Height > Vid.vid.Height)
        {
            Sys.Error("Draw_Pic: bad coordinates"u8);
        }

        byte* source = (byte*)pic + (sizeof(int) * 2);

        if (DIface.r_pixbytes == 1)
        {
            byte* dest = Vid.vid.Buffer + (y * Vid.vid.RowBytes) + x;

            for (int v = 0; v < pic->Height; v++)
            {
                Q.MemCpy(dest, source, pic->Width);
                dest += Vid.vid.RowBytes;
                source += pic->Width;
            }
        }
        else
        {
            // FIXME: pretranslate at load time?
            ushort* pusdest = (ushort*)Vid.vid.Buffer + (y * (Vid.vid.RowBytes >> 1)) + x;

            for (int v = 0; v < pic->Height; v++)
            {
                for (int u = 0; u < pic->Width; u++)
                {
                    pusdest[u] = Vid.d_8to16table[source[u]];
                }

                pusdest += Vid.vid.RowBytes >> 1;
                source += pic->Width;
            }
        }
    }

    // Draw_TransPic
    public static void TransPic(int x, int y, QPic_t* pic)
    {
        if (!IsIdentityCanvas)
        {
            PicScaled(x, y, pic, DIface.TRANSPARENT_COLOR, null);
            return;
        }

        if (x < 0 || (uint)(x + pic->Width) > Vid.vid.Width || y < 0 || (uint)(y + pic->Height) > Vid.vid.Height)
        {
            Sys.Error("Draw_TransPic: bad coordinates"u8);
        }

        byte* source = (byte*)pic + (sizeof(int) * 2);

        if (DIface.r_pixbytes == 1)
        {
            byte* dest = Vid.vid.Buffer + (y * Vid.vid.RowBytes) + x;

            // Original had a general loop and an 8-wide unwound loop; both
            // copy every non-transparent pixel, which TransRow does 16 at a time.
            for (int v = 0; v < pic->Height; v++)
            {
                DrawSimd.TransRow(dest, source, pic->Width, DIface.TRANSPARENT_COLOR);
                dest += Vid.vid.RowBytes;
                source += pic->Width;
            }
        }
        else
        {
            // FIXME: pretranslate at load time?
            ushort* pusdest = (ushort*)Vid.vid.Buffer + (y * (Vid.vid.RowBytes >> 1)) + x;

            for (int v = 0; v < pic->Height; v++)
            {
                for (int u = 0; u < pic->Width; u++)
                {
                    byte tbyte = source[u];
                    if (tbyte != DIface.TRANSPARENT_COLOR)
                    {
                        pusdest[u] = Vid.d_8to16table[tbyte];
                    }
                }

                pusdest += Vid.vid.RowBytes >> 1;
                source += pic->Width;
            }
        }
    }

    // Draw_TransPicTranslate
    public static void TransPicTranslate(int x, int y, QPic_t* pic, byte* translation)
    {
        if (!IsIdentityCanvas)
        {
            PicScaled(x, y, pic, DIface.TRANSPARENT_COLOR, translation);
            return;
        }

        if (x < 0 || (uint)(x + pic->Width) > Vid.vid.Width || y < 0 || (uint)(y + pic->Height) > Vid.vid.Height)
        {
            Sys.Error("Draw_TransPic: bad coordinates"u8);
        }

        byte* source = (byte*)pic + (sizeof(int) * 2);

        if (DIface.r_pixbytes == 1)
        {
            byte* dest = Vid.vid.Buffer + (y * Vid.vid.RowBytes) + x;

            if ((pic->Width & 7) != 0)
            { // general
                for (int v = 0; v < pic->Height; v++)
                {
                    for (int u = 0; u < pic->Width; u++)
                    {
                        byte tbyte = source[u];
                        if (tbyte != DIface.TRANSPARENT_COLOR)
                        {
                            dest[u] = translation[tbyte];
                        }
                    }

                    dest += Vid.vid.RowBytes;
                    source += pic->Width;
                }
            }
            else
            { // unwound
                for (int v = 0; v < pic->Height; v++)
                {
                    for (int u = 0; u < pic->Width; u += 8)
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            byte tbyte = source[u + k];
                            if (tbyte != DIface.TRANSPARENT_COLOR)
                            {
                                dest[u + k] = translation[tbyte];
                            }
                        }
                    }
                    dest += Vid.vid.RowBytes;
                    source += pic->Width;
                }
            }
        }
        else
        {
            // FIXME: pretranslate at load time?
            ushort* pusdest = (ushort*)Vid.vid.Buffer + (y * (Vid.vid.RowBytes >> 1)) + x;

            for (int v = 0; v < pic->Height; v++)
            {
                for (int u = 0; u < pic->Width; u++)
                {
                    byte tbyte = source[u];
                    if (tbyte != DIface.TRANSPARENT_COLOR)
                    {
                        pusdest[u] = Vid.d_8to16table[tbyte];
                    }
                }

                pusdest += Vid.vid.RowBytes >> 1;
                source += pic->Width;
            }
        }
    }

    // Draw_CharToConback
    public static void CharToConback(int num, byte* dest)
    {
        int row = num >> 4;
        int col = num & 15;
        byte* source = draw_chars + (row << 10) + (col << 3);

        int drawline = 8;

        while (drawline-- != 0)
        {
            for (int x = 0; x < 8; x++)
            {
                if (source[x] != 0)
                {
                    dest[x] = (byte)(0x60 + source[x]);
                }
            }

            source += 128;
            dest += 320;
        }
    }

    // Draw_ConsoleBackground
    public static void ConsoleBackground(int lines)
    {
        QPic_t* conback = CachePic("gfx/conback.lmp"u8);

        ReadOnlySpan<byte> ver = "(WinQuake) 1.09f"u8;
        byte* conbackData = (byte*)conback + (sizeof(int) * 2);
        byte* verDest = conbackData + (320 * 186) + 320 - 11 - (8 * ver.Length);

        for (int x = 0; x < ver.Length; x++)
        {
            CharToConback(ver[x], verDest + (x << 3));
        }

        // draw the pic
        if (DIface.r_pixbytes == 1)
        {
            ConbackStretch8(conbackData, lines);
        }
        else
        {
            ushort* pusdest = (ushort*)Vid.vid.ConBuffer;

            for (int y = 0; y < lines; y++, pusdest += Vid.vid.ConRowBytes >> 1)
            {
                // FIXME: pre-expand to native format?
                // FIXME: does the endian switching go away in production?
                int v = (int)((Vid.vid.ConHeight - lines + y) * 200 / Vid.vid.ConHeight);
                byte* src = conbackData + (v * 320);
                int f = 0;
                int fstep = (int)(320 * 0x10000 / Vid.vid.ConWidth);
                for (int x = 0; x < Vid.vid.ConWidth; x += 4)
                {
                    pusdest[x] = Vid.d_8to16table[src[f >> 16]];
                    f += fstep;
                    pusdest[x + 1] = Vid.d_8to16table[src[f >> 16]];
                    f += fstep;
                    pusdest[x + 2] = Vid.d_8to16table[src[f >> 16]];
                    f += fstep;
                    pusdest[x + 3] = Vid.d_8to16table[src[f >> 16]];
                    f += fstep;
                }
            }
        }
    }

    // The 8-bit half of Draw_ConsoleBackground: stretches the 320x200 pic
    // into the console buffer. Split out so tests can feed it a pic directly.
    internal static void ConbackStretch8(byte* conbackData, int lines)
    {
        // lines is in console-canvas units (ConHeight); the scaled canvas
        // (Canvas.Set) stretches each console row over Scale physical rows
        // and the 320-wide pic over the whole physical width.
        int s = Canvas.Scale;
        int conHeight = (int)Vid.vid.ConHeight;
        int physWidth = (int)Vid.vid.Width;
        int rows = (int)Math.Min(Vid.vid.Height, (uint)(lines * s));
        int fstep = (int)(320L * 0x10000 / physWidth);

        byte* dest = Vid.vid.ConBuffer;

        // The source column for each dest x is the same on every row:
        // (x * fstep) >> 16, which is what the original's running
        // f += fstep reaches. Cached per width.
        ushort* cols = ConbackColumns(physWidth, fstep);

        int prevV = -1;
        byte* prevDest = null;
        for (int y = 0; y < rows; y++, dest += Vid.vid.ConRowBytes)
        {
            int v = Math.Clamp((conHeight - lines + (y / s)) * 200 / conHeight, 0, 199);
            if (v == prevV)
            {
                // same source line as the previous row: identical output
                Buffer.MemoryCopy(prevDest, dest, physWidth, physWidth);
                continue;
            }

            prevV = v;
            prevDest = dest;
            byte* src = conbackData + (v * 320);
            for (int x = 0; x < physWidth; x++)
            {
                dest[x] = src[cols[x]];
            }
        }
    }

    // Not in the original -- per-width table of source columns for
    // Draw_ConsoleBackground's horizontal stretch (see above).
    private static ushort* conbackCols;
    private static int conbackColsWidth;

    private static ushort* ConbackColumns(int physWidth, int fstep)
    {
        if (conbackColsWidth != physWidth)
        {
            conbackCols = (ushort*)NativeMemory.Realloc(conbackCols, (nuint)(physWidth * sizeof(ushort)));
            int f = 0;
            for (int x = 0; x < physWidth; x++)
            {
                conbackCols[x] = (ushort)(f >> 16);
                f += fstep;
            }
            conbackColsWidth = physWidth;
        }

        return conbackCols;
    }

    // R_DrawRect8
    public static void DrawRect8(VRect_t* prect, int rowbytes, byte* psrc, int transparent)
    {
        byte* pdest = Vid.vid.Buffer + (prect->Y * Vid.vid.RowBytes) + prect->X;

        int srcdelta = rowbytes - prect->Width;
        int destdelta = (int)Vid.vid.RowBytes - prect->Width;

        if (transparent != 0)
        {
            for (int i = 0; i < prect->Height; i++)
            {
                for (int j = 0; j < prect->Width; j++)
                {
                    byte t = *psrc;
                    if (t != DIface.TRANSPARENT_COLOR)
                    {
                        *pdest = t;
                    }

                    psrc++;
                    pdest++;
                }

                psrc += srcdelta;
                pdest += destdelta;
            }
        }
        else
        {
            for (int i = 0; i < prect->Height; i++)
            {
                Q.MemCpy(pdest, psrc, prect->Width);
                psrc += rowbytes;
                pdest += Vid.vid.RowBytes;
            }
        }
    }

    // R_DrawRect16
    public static void DrawRect16(VRect_t* prect, int rowbytes, byte* psrc, int transparent)
    {
        // FIXME: would it be better to pre-expand native-format versions?
        ushort* pdest = (ushort*)Vid.vid.Buffer + (prect->Y * (Vid.vid.RowBytes >> 1)) + prect->X;

        int srcdelta = rowbytes - prect->Width;
        int destdelta = (int)(Vid.vid.RowBytes >> 1) - prect->Width;

        if (transparent != 0)
        {
            for (int i = 0; i < prect->Height; i++)
            {
                for (int j = 0; j < prect->Width; j++)
                {
                    byte t = *psrc;
                    if (t != DIface.TRANSPARENT_COLOR)
                    {
                        *pdest = Vid.d_8to16table[t];
                    }

                    psrc++;
                    pdest++;
                }

                psrc += srcdelta;
                pdest += destdelta;
            }
        }
        else
        {
            for (int i = 0; i < prect->Height; i++)
            {
                for (int j = 0; j < prect->Width; j++)
                {
                    *pdest = Vid.d_8to16table[*psrc];
                    psrc++;
                    pdest++;
                }

                psrc += srcdelta;
                pdest += destdelta;
            }
        }
    }

    // Draw_TileClear
    //
    // This repeats a 64*64 tile graphic to fill the screen around a sized
    // down refresh window.
    public static void TileClear(int x, int y, int w, int h)
    {
        r_rectdesc.Rect.X = x;
        r_rectdesc.Rect.Y = y;
        r_rectdesc.Rect.Width = w;
        r_rectdesc.Rect.Height = h;

        VRect_t vr = default;
        vr.Y = r_rectdesc.Rect.Y;
        int height = r_rectdesc.Rect.Height;

        int tileoffsety = vr.Y % r_rectdesc.Height;

        while (height > 0)
        {
            vr.X = r_rectdesc.Rect.X;
            int width = r_rectdesc.Rect.Width;

            vr.Height = tileoffsety != 0 ? r_rectdesc.Height - tileoffsety : r_rectdesc.Height;

            if (vr.Height > height)
            {
                vr.Height = height;
            }

            int tileoffsetx = vr.X % r_rectdesc.Width;

            while (width > 0)
            {
                vr.Width = tileoffsetx != 0 ? r_rectdesc.Width - tileoffsetx : r_rectdesc.Width;

                if (vr.Width > width)
                {
                    vr.Width = width;
                }

                byte* psrc = r_rectdesc.PTexBytes + (tileoffsety * r_rectdesc.RowBytes) + tileoffsetx;

                if (DIface.r_pixbytes == 1)
                {
                    DrawRect8(&vr, r_rectdesc.RowBytes, psrc, 0);
                }
                else
                {
                    DrawRect16(&vr, r_rectdesc.RowBytes, psrc, 0);
                }

                vr.X += vr.Width;
                width -= vr.Width;
                tileoffsetx = 0; // only the left tile can be left-clipped
            }

            vr.Y += vr.Height;
            height -= vr.Height;
            tileoffsety = 0; // only the top tile can be top-clipped
        }
    }

    // Draw_Fill
    //
    // Fills a box of pixels with a single color
    public static void Fill(int x, int y, int w, int h, int c)
    {
        if (!IsIdentityCanvas)
        {
            FillScaled(x, y, w, h, c);
            return;
        }

        if (DIface.r_pixbytes == 1)
        {
            byte* dest = Vid.vid.Buffer + (y * Vid.vid.RowBytes) + x;
            for (int v = 0; v < h; v++, dest += Vid.vid.RowBytes)
            {
                new Span<byte>(dest, w).Fill((byte)c);
            }
        }
        else
        {
            ushort uc = Vid.d_8to16table[c];

            ushort* pusdest = (ushort*)Vid.vid.Buffer + (y * (Vid.vid.RowBytes >> 1)) + x;
            for (int v = 0; v < h; v++, pusdest += Vid.vid.RowBytes >> 1)
            {
                for (int u = 0; u < w; u++)
                {
                    pusdest[u] = uc;
                }
            }
        }
    }

    // Draw_FadeScreen
    public static void FadeScreen()
    {
        // VID_UnlockBuffer/S_ExtraUpdate/VID_LockBuffer -- dropped (both the
        // display layer and sound mixer are ported now, but this mid-render
        // buffer-lock/audio-pump triad is unneeded by OpenTK/OpenAL's
        // model), matching the convention established in RMain.cs/REdge.cs.

        for (int y = 0; y < Vid.vid.Height; y++)
        {
            byte* pbuf = Vid.vid.Buffer + (Vid.vid.RowBytes * y);
            int t = (y & 1) << 1;

            DrawSimd.FadeRow(pbuf, (int)Vid.vid.Width, t);
        }
    }

    // Draw_BeginDisc
    //
    // Draws the little blue disc in the corner of the screen.
    // Call before beginning any disc IO.
    //
    // The draw_disc null check is gl_draw.c's, not draw.c's: Host_Init calls
    // VID_Init (which sets vid.direct) before Draw_Init loads draw_disc, so
    // any COM_LoadFile in that window would otherwise dereference null.
    public static void BeginDisc()
    {
        if (draw_disc == null)
        {
            return;
        }

        Vid.D_BeginDirectRect((int)Vid.vid.Width - 24, 0, draw_disc->Data, 24, 24);
    }

    // Draw_EndDisc
    //
    // Erases the disc icon.
    // Call after completing any disc IO
    public static void EndDisc()
    {
        if (draw_disc == null)
        {
            return;
        }

        Vid.D_EndDirectRect((int)Vid.vid.Width - 24, 0, 24, 24);
    }
}
