// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Quake.Benchmarks;

// Candidate optimizations of DScan.Turbulent8Inner (d_scan.c Turbulent8 /
// D_DrawTurbulent8Span), each switchable so one change is measured at a time:
//  TexGather - fetch 8 texels with one dword gather + pshufb/vpermd pack
//              (instead of 8 scalar byte loads), store 16 bytes at once.
//  VecTail   - final <16 chunk runs one 8-wide block when >= 8 pixels remain.
public unsafe partial class Turbulent8Benchmarks
{
    private interface IOpts
    {
        static abstract bool TexGather { get; }
        static abstract bool VecTail { get; }
    }
    private struct OptTexGather : IOpts { public static bool TexGather => true; public static bool VecTail => false; }
    private struct OptVecTail : IOpts { public static bool TexGather => false; public static bool VecTail => true; }
    private struct OptBoth : IOpts { public static bool TexGather => true; public static bool VecTail => true; }

    [Benchmark] public void TexGather() => Turbulent8_Opt<OptTexGather>(&_spans[0]);
    [Benchmark] public void VecTail() => Turbulent8_Opt<OptVecTail>(&_spans[0]);
    [Benchmark] public void TexGatherVecTail() => Turbulent8_Opt<OptBoth>(&_spans[0]);

    // sturb/tturb -> (tturb << 6) + sturb for 8 consecutive pixels.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> TexIndex8(int* turb, Vector256<int> sV, Vector256<int> tV)
    {
        Vector256<int> cyc = Vector256.Create(DIface.CYCLE - 1);
        Vector256<int> m63 = Vector256.Create(63);
        Vector256<int> sturb = ((sV + Avx2.GatherVector256(turb, (tV >> 16) & cyc, sizeof(int))) >> 16) & m63;
        Vector256<int> tturb = ((tV + Avx2.GatherVector256(turb, (sV >> 16) & cyc, sizeof(int))) >> 16) & m63;
        return (tturb << 6) + sturb;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong FetchScalar8(byte* pbase, Vector256<int> idxV)
    {
        Vector256<ulong> q = idxV.AsUInt64();
        ulong i01 = q.GetElement(0), i23 = q.GetElement(1), i45 = q.GetElement(2), i67 = q.GetElement(3);
        return pbase[(uint)i01]
            | ((ulong)pbase[(uint)(i01 >> 32)] << 8)
            | ((ulong)pbase[(uint)i23] << 16)
            | ((ulong)pbase[(uint)(i23 >> 32)] << 24)
            | ((ulong)pbase[(uint)i45] << 32)
            | ((ulong)pbase[(uint)(i45 >> 32)] << 40)
            | ((ulong)pbase[(uint)i67] << 48)
            | ((ulong)pbase[(uint)(i67 >> 32)] << 56);
    }

    // Byte 0 of each dword -> bytes 0..3 (lo) or 4..7 (hi) of each 128-bit lane.
    private static readonly Vector256<byte> PackLo = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);
    private static readonly Vector256<byte> PackHi = Vector256.Create(
        0x80, 0x80, 0x80, 0x80, 0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, (byte)0x80);

    private static void Turbulent8_Opt<TOpt>(ESpan_t* pspan) where TOpt : struct, IOpts
    {
        int* turb = RShared.sintable + (float.ConvertToIntegerNative<int>(RShared.cl_time * SPEED) & (DIface.CYCLE - 1));

        int r_turb_sstep = 0;
        int r_turb_tstep = 0;
        byte* pbase = RShared.cacheblock;

        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        float d_sdivzstepu = DLocal.d_sdivzstepu, d_sdivzstepv = DLocal.d_sdivzstepv, d_sdivzorigin = DLocal.d_sdivzorigin;
        float d_tdivzstepu = DLocal.d_tdivzstepu, d_tdivzstepv = DLocal.d_tdivzstepv, d_tdivzorigin = DLocal.d_tdivzorigin;
        float d_zistepu = DLocal.d_zistepu, d_zistepv = DLocal.d_zistepv, d_ziorigin = DLocal.d_ziorigin;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        float sdivz16stepu = d_sdivzstepu * 16;
        float tdivz16stepu = d_tdivzstepu * 16;
        float zi16stepu = d_zistepu * 16;

        Vector256<int> indexV = Vector256.CreateSequence(0, 1);
        Vector256<byte> packLo = PackLo, packHi = PackHi;
        Vector256<int> order = Vector256.Create(0, 4, 1, 5, 0, 0, 0, 0);

        do
        {
            byte* pdest = viewbuffer + (screenwidth * pspan->V) + pspan->U;
            int count = pspan->Count;
            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = MathF.FusedMultiplyAdd(du, d_sdivzstepu, MathF.FusedMultiplyAdd(dv, d_sdivzstepv, d_sdivzorigin));
            float tdivz = MathF.FusedMultiplyAdd(du, d_tdivzstepu, MathF.FusedMultiplyAdd(dv, d_tdivzstepv, d_tdivzorigin));
            float zi = MathF.FusedMultiplyAdd(du, d_zistepu, MathF.FusedMultiplyAdd(dv, d_zistepv, d_ziorigin));
            float z = 0x10000 / zi;

            int s = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
            if (s > bbextents)
            {
                s = bbextents;
            }
            else if (s < 0)
            {
                s = 0;
            }

            int t = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
            if (t > bbextentt)
            {
                t = bbextentt;
            }
            else if (t < 0)
            {
                t = 0;
            }

            int snext, tnext;
            do
            {
                int spancount = count >= 16 ? 16 : count;
                count -= spancount;

                if (count != 0)
                {
                    sdivz += sdivz16stepu;
                    tdivz += tdivz16stepu;
                    zi += zi16stepu;
                    z = 0x10000 / zi;
                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 16)
                    {
                        snext = 16;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 16)
                    {
                        tnext = 16;
                    }

                    r_turb_sstep = (snext - s) >> 4;
                    r_turb_tstep = (tnext - t) >> 4;
                }
                else
                {
                    float spancountminus1 = spancount - 1;
                    sdivz += d_sdivzstepu * spancountminus1;
                    tdivz += d_tdivzstepu * spancountminus1;
                    zi += d_zistepu * spancountminus1;
                    z = 0x10000 / zi;
                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 16)
                    {
                        snext = 16;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 16)
                    {
                        tnext = 16;
                    }

                    if (spancount > 1)
                    {
                        r_turb_sstep = (snext - s) / (spancount - 1);
                        r_turb_tstep = (tnext - t) / (spancount - 1);
                    }
                }

                s &= (DIface.CYCLE << 16) - 1;
                t &= (DIface.CYCLE << 16) - 1;

                if (Avx2.IsSupported && spancount >= 8 && (spancount == 16 || TOpt.VecTail))
                {
                    Vector256<int> sstepV = Vector256.Create(r_turb_sstep);
                    Vector256<int> tstepV = Vector256.Create(r_turb_tstep);
                    Vector256<int> sV = Vector256.Create(s) + (indexV * sstepV);
                    Vector256<int> tV = Vector256.Create(t) + (indexV * tstepV);
                    Vector256<int> idx0 = TexIndex8(turb, sV, tV);

                    if (spancount == 16)
                    {
                        Vector256<int> idx1 = TexIndex8(turb, sV + (sstepV << 3), tV + (tstepV << 3));
                        if (TOpt.TexGather)
                        {
                            Vector256<byte> g0 = Avx2.GatherVector256((int*)pbase, idx0, 1).AsByte();
                            Vector256<byte> g1 = Avx2.GatherVector256((int*)pbase, idx1, 1).AsByte();
                            Vector256<byte> packed = Avx2.Shuffle(g0, packLo) | Avx2.Shuffle(g1, packHi);
                            Avx2.PermuteVar8x32(packed.AsInt32(), order).GetLower().AsByte().Store(pdest);
                        }
                        else
                        {
                            Unsafe.WriteUnaligned(pdest, FetchScalar8(pbase, idx0));
                            Unsafe.WriteUnaligned(pdest + 8, FetchScalar8(pbase, idx1));
                        }
                        pdest += 16;
                        spancount = 0;
                    }
                    else
                    {
                        if (TOpt.TexGather)
                        {
                            Vector256<byte> g0 = Avx2.GatherVector256((int*)pbase, idx0, 1).AsByte();
                            Unsafe.WriteUnaligned(pdest, Avx2.PermuteVar8x32(Avx2.Shuffle(g0, packLo).AsInt32(), order).AsUInt64().ToScalar());
                        }
                        else
                        {
                            Unsafe.WriteUnaligned(pdest, FetchScalar8(pbase, idx0));
                        }
                        pdest += 8;
                        s += r_turb_sstep << 3;
                        t += r_turb_tstep << 3;
                        spancount -= 8;
                    }
                }

                while (spancount > 0)
                {
                    int sturb = ((s + turb[(t >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    int tturb = ((t + turb[(s >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    *pdest++ = *(pbase + (tturb << 6) + sturb);
                    s += r_turb_sstep;
                    t += r_turb_tstep;
                    spancount--;
                }

                s = snext;
                t = tnext;
            } while (count > 0);
        } while ((pspan = pspan->PNext) != null);
    }

    private static void Turbulent8_Classic(ESpan_t* pspan)
    {
        int* turb = RShared.sintable + (float.ConvertToIntegerNative<int>(RShared.cl_time * SPEED) & (DIface.CYCLE - 1));

        int r_turb_sstep = 0;
        int r_turb_tstep = 0;
        byte* pbase = RShared.cacheblock;

        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        float d_sdivzstepu = DLocal.d_sdivzstepu, d_sdivzstepv = DLocal.d_sdivzstepv, d_sdivzorigin = DLocal.d_sdivzorigin;
        float d_tdivzstepu = DLocal.d_tdivzstepu, d_tdivzstepv = DLocal.d_tdivzstepv, d_tdivzorigin = DLocal.d_tdivzorigin;
        float d_zistepu = DLocal.d_zistepu, d_zistepv = DLocal.d_zistepv, d_ziorigin = DLocal.d_ziorigin;
        int sadjust = DLocal.sadjust, tadjust = DLocal.tadjust;
        int bbextents = DLocal.bbextents, bbextentt = DLocal.bbextentt;

        float sdivz16stepu = d_sdivzstepu * 16;
        float tdivz16stepu = d_tdivzstepu * 16;
        float zi16stepu = d_zistepu * 16;

        do
        {
            byte* pdest = viewbuffer + (screenwidth * pspan->V) + pspan->U;

            int count = pspan->Count;

            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = d_sdivzorigin + (dv * d_sdivzstepv) + (du * d_sdivzstepu);
            float tdivz = d_tdivzorigin + (dv * d_tdivzstepv) + (du * d_tdivzstepu);
            float zi = d_ziorigin + (dv * d_zistepv) + (du * d_zistepu);
            float z = 0x10000 / zi;

            int r_turb_s = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
            if (r_turb_s > bbextents)
            {
                r_turb_s = bbextents;
            }
            else if (r_turb_s < 0)
            {
                r_turb_s = 0;
            }

            int r_turb_t = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
            if (r_turb_t > bbextentt)
            {
                r_turb_t = bbextentt;
            }
            else if (r_turb_t < 0)
            {
                r_turb_t = 0;
            }

            int snext, tnext;

            do
            {
                int spancount = count >= 16 ? 16 : count;
                count -= spancount;

                if (count != 0)
                {
                    sdivz += sdivz16stepu;
                    tdivz += tdivz16stepu;
                    zi += zi16stepu;
                    z = 0x10000 / zi;

                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 16)
                    {
                        snext = 16;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 16)
                    {
                        tnext = 16;
                    }

                    r_turb_sstep = (snext - r_turb_s) >> 4;
                    r_turb_tstep = (tnext - r_turb_t) >> 4;
                }
                else
                {
                    float spancountminus1 = spancount - 1;
                    sdivz += d_sdivzstepu * spancountminus1;
                    tdivz += d_tdivzstepu * spancountminus1;
                    zi += d_zistepu * spancountminus1;
                    z = 0x10000 / zi;
                    snext = float.ConvertToIntegerNative<int>(sdivz * z) + sadjust;
                    if (snext > bbextents)
                    {
                        snext = bbextents;
                    }
                    else if (snext < 16)
                    {
                        snext = 16;
                    }

                    tnext = float.ConvertToIntegerNative<int>(tdivz * z) + tadjust;
                    if (tnext > bbextentt)
                    {
                        tnext = bbextentt;
                    }
                    else if (tnext < 16)
                    {
                        tnext = 16;
                    }

                    if (spancount > 1)
                    {
                        r_turb_sstep = (snext - r_turb_s) / (spancount - 1);
                        r_turb_tstep = (tnext - r_turb_t) / (spancount - 1);
                    }
                }

                r_turb_s &= (DIface.CYCLE << 16) - 1;
                r_turb_t &= (DIface.CYCLE << 16) - 1;

                // D_DrawTurbulent8Span
                do
                {
                    int sturb = ((r_turb_s + turb[(r_turb_t >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    int tturb = ((r_turb_t + turb[(r_turb_s >> 16) & (DIface.CYCLE - 1)]) >> 16) & 63;
                    *pdest++ = *(pbase + (tturb << 6) + sturb);
                    r_turb_s += r_turb_sstep;
                    r_turb_t += r_turb_tstep;
                } while (--spancount > 0);

                r_turb_s = snext;
                r_turb_t = tnext;

            } while (count > 0);

        } while ((pspan = pspan->PNext) != null);
    }
}
