// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// r_sky.c: scrolling sky texture generation. d_sky.c's D_DrawSkyScans8
/// lives in DEdge.cs.
/// </summary>
public static unsafe class RSky
{
    private const int SKYSHIFT = 7;
    private const int SKYSIZE = 1 << SKYSHIFT; // 128
    private const int SKYMASK = SKYSIZE - 1;

    public static int iskyspeed = 8;
    public static int iskyspeed2 = 2;
    public static float skyspeed, skyspeed2;

    public static float skytime;

    public static byte* r_skysource;

    // r_skymade already lives in DEdge.cs (needed there since
    // D_DrawSurfaces already checks it) -- reused, not redeclared.
    public static int r_skydirect; // not used? (original comment)

    // TODO (original comment): clean up these routines
    internal static readonly byte* bottomsky = (byte*)NativeMemory.AllocZeroed(128 * 131);
    internal static readonly byte* bottommask = (byte*)NativeMemory.AllocZeroed(128 * 131);

    // newsky and topsky both pack in here, 128 bytes of newsky on the left
    // of each scan, 128 bytes of topsky on the right, because the low-level
    // drawers need 256-byte scan widths.
    internal static readonly byte* newsky = (byte*)NativeMemory.AllocZeroed(128 * 256);

    private static int xlast = -1, ylast = -1;

    // R_InitSky
    // A sky texture is 256*128, with the right side being a masked overlay
    public static void InitSky(Texture_t* mt)
    {
        byte* src = (byte*)mt + mt->Offsets[0];

        for (int i = 0; i < 128; i++)
            for (int j = 0; j < 128; j++)
                newsky[i * 256 + j + 128] = src[i * 256 + j + 128];

        for (int i = 0; i < 128; i++)
        {
            for (int j = 0; j < 131; j++)
            {
                if (src[i * 256 + (j & 0x7F)] != 0)
                {
                    bottomsky[i * 131 + j] = src[i * 256 + (j & 0x7F)];
                    bottommask[i * 131 + j] = 0;
                }
                else
                {
                    bottomsky[i * 131 + j] = 0;
                    bottommask[i * 131 + j] = 0xff;
                }
            }
        }

        r_skysource = newsky;
    }

    // R_MakeSky
    // Ported from the portable (!UNALIGNED_OK) byte-at-a-time branch --
    // both original branches produce identical bytes, the other is a pure
    // x86 dword-at-a-time micro-optimization.
    public static void MakeSky()
    {
        int xshift = float.ConvertToIntegerNative<int>(skytime * skyspeed);
        int yshift = float.ConvertToIntegerNative<int>(skytime * skyspeed);

        if (xshift == xlast && yshift == ylast)
            return;

        xlast = xshift;
        ylast = yshift;

        byte* pnewsky = newsky;

        for (int y = 0; y < SKYSIZE; y++)
        {
            int baseofs = ((y + yshift) & SKYMASK) * 131;

            for (int x = 0; x < SKYSIZE; x++)
            {
                int ofs = baseofs + ((x + xshift) & SKYMASK);

                *pnewsky = (byte)((pnewsky[128] & bottommask[ofs]) | bottomsky[ofs]);
                pnewsky++;
            }

            pnewsky += 128;
        }

        DEdge.r_skymade = true;
    }

    // R_GenSkyTile
    public static void GenSkyTile(void* pdest)
    {
        int xshift = (int)(skytime * skyspeed);
        int yshift = (int)(skytime * skyspeed);

        byte* pnewsky = newsky;
        byte* pd = (byte*)pdest;

        for (int y = 0; y < SKYSIZE; y++)
        {
            int baseofs = ((y + yshift) & SKYMASK) * 131;

            for (int x = 0; x < SKYSIZE; x++)
            {
                int ofs = baseofs + ((x + xshift) & SKYMASK);

                *pd = (byte)((pnewsky[128] & bottommask[ofs]) | bottomsky[ofs]);
                pnewsky++;
                pd++;
            }

            pnewsky += 128;
        }
    }

    // R_GenSkyTile16
    public static void GenSkyTile16(void* pdest)
    {
        int xshift = (int)(skytime * skyspeed);
        int yshift = (int)(skytime * skyspeed);

        byte* pnewsky = newsky;
        ushort* pd = (ushort*)pdest;

        for (int y = 0; y < SKYSIZE; y++)
        {
            int baseofs = ((y + yshift) & SKYMASK) * 131;

            // FIXME (original comment): do faster unaligned version?
            for (int x = 0; x < SKYSIZE; x++)
            {
                int ofs = baseofs + ((x + xshift) & SKYMASK);

                *pd = Vid.d_8to16table[(pnewsky[128] & bottommask[ofs]) | bottomsky[ofs]];
                pnewsky++;
                pd++;
            }

            pnewsky += DIface.TILE_SIZE;
        }
    }

    // R_SetSkyFrame
    public static void SetSkyFrame()
    {
        skyspeed = iskyspeed;
        skyspeed2 = iskyspeed2;

        int g = MathLib.GreatestCommonDivisor(iskyspeed, iskyspeed2);
        int s1 = iskyspeed / g;
        int s2 = iskyspeed2 / g;
        float temp = SKYSIZE * s1 * s2;

        skytime = (float)(Cl.time - (int)(Cl.time / temp) * temp);

        DEdge.r_skymade = false;
    }

}
