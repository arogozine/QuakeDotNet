// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine;

namespace Quake.Benchmarks;

/// <summary>
/// d_draw.s: C(D_DrawSpans8), the hand-tuned x86 assembly version of
/// D_DrawSpans8 (d_scan.c), ported here for benchmark comparison only.
///
/// Only the x86 asm's C fallback is ported into the production engine
/// (DScan.DrawSpans8) -- this file is the explicitly requested exception
/// to that (rule 8): a "closest possible" translation of the
/// assembly itself, kept in Quake.Benchmarks so it never gets wired into the
/// actual render path.
///
/// Two things can't be reproduced bit-exactly in managed C#, and both are
/// called out inline below rather than silently ignored:
///  - the initial s/t/z floating point math ran on the x87 FPU with 80-bit
///    extended-precision intermediates; .NET has no equivalent, so this uses
///    ordinary 32-bit float math (the same formula DScan.DrawSpans8 uses).
///  - the asm's own comments are occasionally wrong (e.g. "tstep >>= 16"
///    labels a shift by 19) -- flagged where it matters, not repeated here.
///
/// What IS reproduced exactly (this part is pure 32-bit integer arithmetic,
/// so it translates losslessly) is the asm's real point of divergence from
/// the C fallback: instead of `sstep = delta &gt;&gt; 3` and `s += sstep` per pixel
/// (DScan.DrawSpans8's approach, which truncates the same amount every
/// pixel and can drift up to 7/65536 texel short of the true endpoint), the
/// asm distributes an 8-pixel chunk's delta via a 2D DDA/Bresenham
/// accumulator: a coarse whole-texel step (delta &gt;&gt; 19, i.e. delta / 8 with
/// the 16.16 fixed point removed) applied every pixel, plus a 32-bit
/// fractional accumulator (the chunk delta's remainder, left-justified via
/// &lt;&lt; 13) that overflows (carries) at the exact pixels needed to make the
/// cumulative step sum to the delta precisely. The final (&lt;=8-pixel, reused
/// for a span's tail or a whole short span) segment uses the identical
/// technique with a per-pixel step already divided by (count-1) -- the asm
/// computes that division via a reciprocal_table multiply-and-take-high-word
/// trick (a speed hack for the missing hardware DIV), which is arithmetically
/// equivalent to plain truncating integer division and is done that way here.
///
/// The instruction-level software pipelining (the "FDIV in flight" overlap,
/// the load/store staggering across loop iterations, the Entry2_8..Entry8_8
/// jump-table "duff's device" for short final segments) is a scheduling
/// optimization with no effect on output, so it is not reproduced -- this
/// implementation is a plain loop computing the same values in the same
/// order.
/// </summary>
public static unsafe class DrawSpans8AsmPort
{
    // D_DrawSpans8 (d_draw.s)
    public static void DrawSpans8(ESpan_t* pspan)
    {
        byte* pbase = RShared.cacheblock;
        int cachewidth = RShared.cachewidth;
        byte* viewbuffer = DLocal.d_viewbuffer;
        int screenwidth = RShared.screenwidth;
        int bbextents = DLocal.bbextents;
        int bbextentt = DLocal.bbextentt;
        int sadjust = DLocal.sadjust;
        int tadjust = DLocal.tadjust;

        // "set up scaled-by-8 steps, for 8-long segments" (d_draw.s:98-114)
        float sdivz8stepu = DLocal.d_sdivzstepu * 8f;
        float tdivz8stepu = DLocal.d_tdivzstepu * 8f;
        float zi8stepu = DLocal.d_zistepu * 8f;

        do
        {
            int count = pspan->Count;
            byte* pdest = viewbuffer + screenwidth * pspan->V + pspan->U;

            // "set up the initial s/z, t/z, and 1/z ... and generate the
            // initial s and t values" (d_draw.s:116-171) -- same formula as
            // DScan.DrawSpans8; see the class comment re: x87 precision.
            float du = pspan->U;
            float dv = pspan->V;

            float sdivz = DLocal.d_sdivzorigin + dv * DLocal.d_sdivzstepv + du * DLocal.d_sdivzstepu;
            float tdivz = DLocal.d_tdivzorigin + dv * DLocal.d_tdivzstepv + du * DLocal.d_tdivzstepu;
            float zi = DLocal.d_ziorigin + dv * DLocal.d_zistepv + du * DLocal.d_zistepu;
            float z = 65536.0f / zi;

            // "calculate and clamp s & t" (d_draw.s:172-286) -- the asm's
            // unsigned-compare clamp trick (LClampHighOrLow0/1) is exactly
            // equivalent to this straightforward clamp against [0,bbextents]
            // / [0,bbextentt] for any bbextents/bbextentt >= 0 (always true
            // here), so there's no need to reproduce the trick itself.
            int s = (int)(sdivz * z) + sadjust;
            if (s > bbextents) s = bbextents;
            else if (s < 0) s = 0;

            int t = (int)(tdivz * z) + tadjust;
            if (t > bbextentt) t = bbextentt;
            else if (t < 0) t = 0;

            // sfracf/tfracf (d_draw.s:284,292): 32-bit fractional-position
            // accumulators seeded from s/t's low 16 bits (the shift discards
            // the integer part -- only relative growth matters from here).
            int sfracf = s << 16;
            int tfracf = t << 16;

            // "psource = pbase + (sfrac >> 16) + (tfrac >> 16) * cachewidth"
            // (d_draw.s:294-302)
            byte* psource = pbase + (s >> 16) + (t >> 16) * cachewidth;

            // LNotLastSegment (d_draw.s:313-427): full 8-pixel chunks, using
            // the fixed +8-step FP formula and the raw-delta ">>19/<<13" DDA
            // split described in the class comment.
            while (count > 8)
            {
                sdivz += sdivz8stepu;
                tdivz += tdivz8stepu;
                zi += zi8stepu;
                z = 65536.0f / zi;

                int snext = (int)(sdivz * z) + sadjust;
                int tnext = (int)(tdivz * z) + tadjust;

                // Chunk-boundary clamp: the asm clamps to [2048, bbextents]
                // here (LClampLow2/3, d_draw.s:346-356), NOT [0, bbextents]
                // like the initial-pixel clamp above or like the C
                // fallback's chunk clamp (which uses 8/16) -- a genuine,
                // deliberate divergence between the asm and its own C
                // fallback that already exists in the original source.
                if (snext < 2048) snext = 2048;
                else if (snext > bbextents) snext = bbextents;
                if (tnext < 2048) tnext = 2048;
                else if (tnext > bbextentt) tnext = bbextentt;

                int ds = snext - s;
                int dt = tnext - t;

                pdest[0] = *psource;

                // advancetable (d_draw.s:365-391): coarse whole-texel step
                // per pixel (the delta's integer part after dividing by the
                // 8-pixel chunk width), plus the same +1 row when the
                // t-fraction accumulator overflows this pixel.
                int dtCoarse = dt >> 19; // really delta/8, despite the asm's own ">>=16" comment (d_draw.s:369)
                int dsCoarse = ds >> 19;
                int advBase = dtCoarse * cachewidth + dsCoarse;
                int advExtra = advBase + cachewidth;

                int sstepFrac = ds << 13;
                int tstepFrac = dt << 13;

                for (int i = 1; i < 8; i++)
                {
                    uint oldT = (uint)tfracf;
                    tfracf = unchecked(tfracf + tstepFrac);
                    bool tCarry = (uint)tfracf < oldT; // sbbl %ecx,%ecx (d_draw.s:396)

                    uint oldS = (uint)sfracf;
                    sfracf = unchecked(sfracf + sstepFrac);
                    bool sCarry = (uint)sfracf < oldS; // folded into the adcl below (d_draw.s:398)

                    psource += (tCarry ? advExtra : advBase) + (sCarry ? 1 : 0);
                    pdest[i] = *psource;
                }

                pdest += 8;
                s = snext;
                t = tnext;
                count -= 8;
            }

            // LLastSegment (d_draw.s:514-620): the final 1..8 pixels of the
            // span, reusing the same DDA technique with a per-pixel step
            // that's already divided by (count-1), so it needs only a
            // ">>16/<<16" split instead of ">>19/<<13".
            if (count == 1)
            {
                // LNoSteps (d_draw.s:609-612): single pixel, no interpolation.
                pdest[0] = *psource;
            }
            else
            {
                int divisor = count - 1; // 1..7

                float sdivzEnd = sdivz + DLocal.d_sdivzstepu * divisor;
                float tdivzEnd = tdivz + DLocal.d_tdivzstepu * divisor;
                float ziEnd = zi + DLocal.d_zistepu * divisor;
                float zEnd = 65536.0f / ziEnd;

                int snext = (int)(sdivzEnd * zEnd) + sadjust;
                int tnext = (int)(tdivzEnd * zEnd) + tadjust;

                if (snext < 2048) snext = 2048;
                else if (snext > bbextents) snext = bbextents;
                if (tnext < 2048) tnext = 2048;
                else if (tnext > bbextentt) tnext = bbextentt;

                int ds = snext - s;
                int dt = tnext - t;

                int sstepPixel, tstepPixel;
                if (divisor == 1)
                {
                    // LOnlyOneStep (d_draw.s:615-620): with 2 pixels total
                    // there's only one step to take, of the full delta.
                    sstepPixel = ds;
                    tstepPixel = dt;
                }
                else
                {
                    // reciprocal_table[divisor-2] * delta, taking the high
                    // 32 bits of the 64-bit product (d_draw.s:568-572) --
                    // arithmetically equivalent to truncating integer
                    // division by `divisor` for the range of deltas produced
                    // here.
                    sstepPixel = ds / divisor;
                    tstepPixel = dt / divisor;
                }

                pdest[0] = *psource;

                int dtCoarse = tstepPixel >> 16;
                int dsCoarse = sstepPixel >> 16;
                int advBase = dtCoarse * cachewidth + dsCoarse;
                int advExtra = advBase + cachewidth;

                int sstepFrac = sstepPixel << 16;
                int tstepFrac = tstepPixel << 16;

                for (int i = 1; i < count; i++)
                {
                    uint oldT = (uint)tfracf;
                    tfracf = unchecked(tfracf + tstepFrac);
                    bool tCarry = (uint)tfracf < oldT;

                    uint oldS = (uint)sfracf;
                    sfracf = unchecked(sfracf + sstepFrac);
                    bool sCarry = (uint)sfracf < oldS;

                    psource += (tCarry ? advExtra : advBase) + (sCarry ? 1 : 0);
                    pdest[i] = *psource;
                }
            }

        } while ((pspan = pspan->PNext) != null);
    }
}
