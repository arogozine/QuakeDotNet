// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

// D_PolysetDrawSpans8: original (statics read per pixel) vs hoisted locals.
public unsafe class PolysetDrawSpans8Benchmarks
{
    #region Setup / Teardown

    private const int SpanCount = 120, MaxCount = 40, Skin = 64;
    private SpanPackage_t* _spans;
    private byte* _dest, _dest2, _tex, _colormap;
    private short* _z, _z2;
    private FieldInfo _aspancount = null!, _countextra = null!;

    [GlobalSetup]
    public void Setup()
    {
        Random rng = new(3);
        _spans = NativeMemory.AllocArrayZeroed<SpanPackage_t>(SpanCount + 1);
        _dest = NativeMemory.AllocArrayZeroed<byte>(SpanCount * MaxCount);
        _dest2 = NativeMemory.AllocArrayZeroed<byte>(SpanCount * MaxCount);
        _z = NativeMemory.AllocArrayZeroed<short>(SpanCount * MaxCount);
        _z2 = NativeMemory.AllocArrayZeroed<short>(SpanCount * MaxCount);
        _tex = NativeMemory.AllocArrayZeroed<byte>(Skin * Skin * 2);
        _colormap = NativeMemory.AllocArrayZeroed<byte>(16384);
        for (int i = 0; i < Skin * Skin * 2; i++)
        {
            _tex[i] = (byte)rng.Next(256);
        }

        for (int i = 0; i < 16384; i++)
        {
            _colormap[i] = (byte)rng.Next(256);
        }

        for (int i = 0; i < SpanCount; i++)
        {
            _spans[i] = new SpanPackage_t
            {
                PDest = _dest + (i * MaxCount),
                Pz = _z + (i * MaxCount),
                Count = rng.Next(2, MaxCount - 4),
                PTex = _tex + rng.Next(0, 200),
                Sfrac = rng.Next(0, 0x10000),
                Tfrac = rng.Next(0, 0x10000),
                Light = rng.Next(0, 0x3F00),
                Zi = rng.Next(1000, 20000) << 16,
            };
        }
        _spans[SpanCount].Count = -999999;

        DIface.acolormap = _colormap;
        DIface.r_affinetridesc.SkinWidth = Skin;
        DPolyse.r_zistepx = 3000;
        DPolyse.r_lstepx = 17;
        DPolyse.a_ststepxwhole = 1;
        DPolyse.a_sstepxfrac = 0x4000;
        DPolyse.a_tstepxfrac = 0x2000;
        _aspancount = typeof(DPolyse).GetField("d_aspancount", BindingFlags.NonPublic | BindingFlags.Static)!;
        _countextra = typeof(DPolyse).GetField("d_countextrastep", BindingFlags.NonPublic | BindingFlags.Static)!;

        RunOrig(_dest, _z);
        RunNew(_dest2, _z2);
        // the benchmark spans write into _dest/_z; compare the two output sets
        if (!new ReadOnlySpan<byte>(_dest, SpanCount * MaxCount).SequenceEqual(new ReadOnlySpan<byte>(_dest2, SpanCount * MaxCount)))
        {
            throw new Exception("mismatch");
        }
    }

    [GlobalCleanup]
    public void Cleanup() { NativeMemory.Free(_spans); NativeMemory.Free(_dest); NativeMemory.Free(_dest2); NativeMemory.Free(_z); NativeMemory.Free(_z2); NativeMemory.Free(_tex); NativeMemory.Free(_colormap); }

    #endregion

    private void Prep(byte* dest, short* z)
    {
        for (int i = 0; i < SpanCount; i++) { _spans[i].PDest = dest + (i * MaxCount); _spans[i].Pz = z + (i * MaxCount); }
        new Span<short>(z, SpanCount * MaxCount).Clear();
        _aspancount.SetValue(null, MaxCount);
        _countextra.SetValue(null, 0);
        DEdge.errorterm = -5; DEdge.erroradjustup = 3; DEdge.erroradjustdown = 7; DEdge.ubasestep = 0;
    }

    private void RunOrig(byte* dest, short* z) { Prep(dest, z); Orig(_spans, (int)_aspancount.GetValue(null)!, (int)_countextra.GetValue(null)!); }

    private void RunNew(byte* dest, short* z) { Prep(dest, z); DPolyse.PolysetDrawSpans8(_spans); }

    [Benchmark(Baseline = true)] public void Original() => RunOrig(_dest, _z);

    [Benchmark] public void Hoisted() => RunNew(_dest2, _z2);

    private static void Orig(SpanPackage_t* pspanpackage, int aspancount, int countextrastep)
    {
        byte* acolormap = DIface.acolormap;
        do
        {
            int lcount = aspancount - pspanpackage->Count;
            DEdge.errorterm += DEdge.erroradjustup;
            if (DEdge.errorterm >= 0) { aspancount += countextrastep; DEdge.errorterm -= DEdge.erroradjustdown; }
            else
            {
                aspancount += DEdge.ubasestep;
            }

            if (lcount != 0)
            {
                byte* lpdest = (byte*)pspanpackage->PDest; byte* lptex = pspanpackage->PTex; short* lpz = pspanpackage->Pz;
                int lsfrac = pspanpackage->Sfrac, ltfrac = pspanpackage->Tfrac, llight = pspanpackage->Light, lzi = pspanpackage->Zi;
                do
                {
                    if (lzi >> 16 >= *lpz) { *lpdest = acolormap[*lptex + (llight & 0xFF00)]; *lpz = (short)(lzi >> 16); }
                    lpdest++; lzi += DPolyse.r_zistepx; lpz++; llight += DPolyse.r_lstepx;
                    lptex += DPolyse.a_ststepxwhole; lsfrac += DPolyse.a_sstepxfrac; lptex += lsfrac >> 16; lsfrac &= 0xFFFF;
                    ltfrac += DPolyse.a_tstepxfrac;
                    if ((ltfrac & 0x10000) != 0) { lptex += DIface.r_affinetridesc.SkinWidth; ltfrac &= 0xFFFF; }
                } while (--lcount != 0);
            }
            pspanpackage++;
        } while (pspanpackage->Count != -999999);
    }
}
