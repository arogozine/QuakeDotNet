// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Benchmarks;

// R_DrawSurfaceBlock8_mip0: original body (statics, runtime shift/count) vs
// locals + constant shift/count vs AVX2 gather.
public unsafe class SurfaceBlockBenchmarks
{
    #region Setup / Teardown

    private const int Blocks = 4;       // vertical blocks
    private const int HBlocks = 8;      // horizontal blocks per surface (128 px wide)
    private const int RowBytes = 128;
    private byte* _colormap, _source, _dest, _dest2, _dest3;
    private uint* _lights;
    private const int LightWidth = HBlocks + 1;

    [GlobalSetup]
    public void Setup()
    {
        Random rng = new(5);
        _colormap = NativeMemory.AllocArrayZeroed<byte>(16384 + 16);
        for (int i = 0; i < 16384; i++) _colormap[i] = (byte)rng.Next(256);
        _source = NativeMemory.AllocArrayZeroed<byte>(128 * 128);
        for (int i = 0; i < 128 * 128; i++) _source[i] = (byte)rng.Next(256);
        _dest = NativeMemory.AllocArrayZeroed<byte>(RowBytes * 16 * Blocks);
        _dest2 = NativeMemory.AllocArrayZeroed<byte>(RowBytes * 16 * Blocks);
        _dest3 = NativeMemory.AllocArrayZeroed<byte>(RowBytes * 16 * Blocks);
        _lights = NativeMemory.AllocArrayZeroed<uint>(LightWidth * (Blocks + 1));
        for (int i = 0; i < LightWidth * (Blocks + 1); i++) _lights[i] = (uint)rng.Next(64, 16320);

        Quake.Engine.Vid.vid.Colormap = _colormap;
        RSurf.surfrowbytes = RowBytes;
        RSurf.sourcetstep = 128;
        RSurf.r_numvblocks = Blocks;
        RSurf.r_lightwidth = LightWidth;
        RSurf.r_source = _source;
        RSurf.r_sourcemax = _source + 128 * 128;
        RSurf.r_stepback = 128 * 128;

        Run(_dest, 0);
        Run(_dest2, 1);
        Run(_dest3, 2);
        if (new ReadOnlySpan<byte>(_dest, RowBytes * 16 * Blocks).SequenceEqual(new ReadOnlySpan<byte>(_dest2, RowBytes * 16 * Blocks)) == false
            || new ReadOnlySpan<byte>(_dest, RowBytes * 16 * Blocks).SequenceEqual(new ReadOnlySpan<byte>(_dest3, RowBytes * 16 * Blocks)) == false)
            throw new Exception("variant mismatch");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_colormap); NativeMemory.Free(_source); NativeMemory.Free(_dest);
        NativeMemory.Free(_dest2); NativeMemory.Free(_dest3); NativeMemory.Free(_lights);
    }

    #endregion

    private void Run(byte* dest, int variant)
    {
        for (int u = 0; u < HBlocks; u++)
        {
            RSurf.r_lightptr = _lights + u;
            RSurf.prowdestbase = dest + u * 16;
            RSurf.pbasesource = _source + u * 16;
            if (variant == 0) Original();
            else if (variant == 1) RSurf.DrawSurfaceBlock8_mip0();
            else Gather();
        }
    }

    [Benchmark(Baseline = true)] public void Orig() => Run(_dest, 0);

    [Benchmark] public void Current() => Run(_dest2, 1);

    [Benchmark] public void Gathered() => Run(_dest3, 2);

    static void Original() => Orig(4, 16);

    static void Orig(int shift, int count)
    {
        byte* psource = RSurf.pbasesource;
        byte* prowdest = (byte*)RSurf.prowdestbase;
        byte* colorMap = Quake.Engine.Vid.vid.Colormap;
        for (int v = 0; v < RSurf.r_numvblocks; v++)
        {
            RSurf.lightleft = (int)RSurf.r_lightptr[0];
            RSurf.lightright = (int)RSurf.r_lightptr[1];
            RSurf.r_lightptr += RSurf.r_lightwidth;
            RSurf.lightleftstep = ((int)RSurf.r_lightptr[0] - RSurf.lightleft) >> shift;
            RSurf.lightrightstep = ((int)RSurf.r_lightptr[1] - RSurf.lightright) >> shift;
            for (int i = 0; i < count; i++)
            {
                int lighttemp = RSurf.lightleft - RSurf.lightright;
                int lightstep = lighttemp >> shift;
                int light = RSurf.lightright;
                for (int b = count - 1; b >= 0; b--)
                {
                    byte pix = psource[b];
                    prowdest[b] = colorMap[(light & 0xFF00) + pix];
                    light += lightstep;
                }
                psource += RSurf.sourcetstep;
                RSurf.lightright += RSurf.lightrightstep;
                RSurf.lightleft += RSurf.lightleftstep;
                prowdest += RSurf.surfrowbytes;
            }
            if (psource >= RSurf.r_sourcemax) psource -= RSurf.r_stepback;
        }
    }

    static void Gather()
    {
        byte* psource = RSurf.pbasesource;
        byte* prowdest = (byte*)RSurf.prowdestbase;
        byte* colorMap = Quake.Engine.Vid.vid.Colormap;
        int sourcetstep = RSurf.sourcetstep, rowbytes = RSurf.surfrowbytes;
        uint* lp = RSurf.r_lightptr;
        int lw = RSurf.r_lightwidth;
        // lane b has light = lightright + (15-b)*lightstep
        Vector256<int> iota0 = Vector256.Create(15, 14, 13, 12, 11, 10, 9, 8);
        Vector256<int> iota1 = Vector256.Create(7, 6, 5, 4, 3, 2, 1, 0);
        Vector256<int> ff00 = Vector256.Create(0xFF00);
        Vector256<int> ff = Vector256.Create(0xFF);
        for (int v = 0; v < RSurf.r_numvblocks; v++)
        {
            int lightleft = (int)lp[0], lightright = (int)lp[1];
            lp += lw;
            int lls = ((int)lp[0] - lightleft) >> 4, lrs = ((int)lp[1] - lightright) >> 4;
            for (int i = 0; i < 16; i++)
            {
                int lightstep = (lightleft - lightright) >> 4;
                Vector256<int> vs = Vector256.Create(lightstep);
                Vector256<int> vb = Vector256.Create(lightright);
                Vector256<int> l0 = vb + vs * iota0;
                Vector256<int> l1 = vb + vs * iota1;
                Vector256<int> p0 = Avx2.ConvertToVector256Int32(psource);
                Vector256<int> p1 = Avx2.ConvertToVector256Int32(psource + 8);
                Vector256<int> i0 = (l0 & ff00) + p0;
                Vector256<int> i1 = (l1 & ff00) + p1;
                Vector256<int> g0 = Avx2.GatherVector256((int*)colorMap, i0, 1) & ff;
                Vector256<int> g1 = Avx2.GatherVector256((int*)colorMap, i1, 1) & ff;
                Vector128<short> s = Sse2.PackSignedSaturate(g0.GetLower(), g0.GetUpper());
                Vector128<short> s2 = Sse2.PackSignedSaturate(g1.GetLower(), g1.GetUpper());
                Sse2.Store(prowdest, Sse2.PackUnsignedSaturate(s, s2));
                psource += sourcetstep;
                lightright += lrs; lightleft += lls;
                prowdest += rowbytes;
            }
            if (psource >= RSurf.r_sourcemax) psource -= RSurf.r_stepback;
        }
        RSurf.r_lightptr = lp;
    }
}
