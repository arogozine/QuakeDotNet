// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// r_edge.c: the active-edge-table scanline algorithm. For each screen
/// line, walks a sorted linked list of Edge_t (fixed16_t u-coordinate +
/// step), maintaining an active-surface stack (Surf_t.Next/Prev) and
/// emitting ESpan_t linked lists that DEdge.DrawSurfaces (d_edge.c)
/// consumes. Numerically sensitive (fixed-point stepping).
///
/// Only the !id386 fallback bodies exist in the original for
/// R_InsertNewEdges/R_RemoveEdges/R_StepActiveU/R_LeadingEdge/
/// R_GenerateSpans -- this port has no x86 asm, so those are the only
/// versions that exist here too.
///
/// R_EmitEdge/R_ClipEdge are NOT this file's functions -- they live in
/// r_draw.c (RDraw.cs), which builds the surfaces/edges this file walks.
/// R_SurfacePatch is x86 code-patching (nonintel.c's own fallback is a
/// no-op, "we only patch code on Intel"), so RMain.cs's calls to it are
/// dropped (commented at the call sites). R_GetSurf is
/// declared in r_local.h but has no body anywhere in the original tree --
/// a dead declaration even upstream, not ported.
/// </summary>
public static unsafe class REdge
{
    // r_bmodelactive (r_vars.c -- a single !id386 global consolidated there
    // in the original; declared here since this is its first consumer)
    public static int r_bmodelactive;

    // span_p, max_span_p
    public static ESpan_t* span_p, max_span_p;

    // r_currentkey
    public static int r_currentkey;

    // current_iv
    public static int current_iv;

    // edge_head_u_shift20, edge_tail_u_shift20
    public static int edge_head_u_shift20, edge_tail_u_shift20;

    // pdrawfunc
    private static delegate* managed<void> pdrawfunc;

    // edge_sentinel -- local to r_edge.c in the original (edge_head/
    // edge_tail/edge_aftertail are RShared.cs's, shared with DIface.cs's
    // driver interface; edge_sentinel isn't read anywhere else).
    // Natively allocated so its address is stable.
    internal static readonly Edge_t* edge_sentinel = (Edge_t*)NativeMemory.AllocZeroed((nuint)sizeof(Edge_t));

    // fv
    public static float fv;

    // basespans -- the original is a per-call C stack array
    // (MAXSPANS*sizeof(espan_t)+CACHE_SIZE), cache-line aligned via pointer
    // masking. Kept as a lazily-allocated, 64-byte-aligned native buffer
    // instead (content is fully overwritten every call).
    private static ESpan_t* basespans;

    /*
    ==============
    R_DrawCulledPolys
    ==============
    */
    public static void DrawCulledPolys()
    {
        RShared.currententity = ClEntities.cl_entities; // &cl_entities[0]

        if (DIface.r_worldpolysbacktofront)
        {
            for (Surf_t* s = RShared.surface_p - 1; s > &RShared.surfaces[1]; s--)
            {
                if (s->Spans == null)
                    continue;

                if ((s->Flags & Surf.DRAWBACKGROUND) == 0)
                {
                    MSurface_t* pface = (MSurface_t*)s->Data;
                    RDraw.RenderPoly(pface, 15);
                }
            }
        }
        else
        {
            for (Surf_t* s = &RShared.surfaces[1]; s < RShared.surface_p; s++)
            {
                if (s->Spans == null)
                    continue;

                if ((s->Flags & Surf.DRAWBACKGROUND) == 0)
                {
                    MSurface_t* pface = (MSurface_t*)s->Data;
                    RDraw.RenderPoly(pface, 15);
                }
            }
        }
    }

    /*
    ==============
    R_BeginEdgeFrame
    ==============
    */
    public static void BeginEdgeFrame()
    {
        RShared.edge_p = RShared.r_edges;
        RShared.edge_max = RShared.r_edges + RShared.r_numallocatededges;

        RShared.surface_p = &RShared.surfaces[2]; // background is surface 1, surface 0 is a dummy
        RShared.surfaces[1].Spans = null; // no background spans yet
        RShared.surfaces[1].Flags = Surf.DRAWBACKGROUND;

        // put the background behind everything in the world
        if (RMain.r_draworder->Value != 0)
        {
            pdrawfunc = &GenerateSpansBackward;
            RShared.surfaces[1].Key = 0;
            r_currentkey = 1;
        }
        else
        {
            pdrawfunc = &GenerateSpans;
            RShared.surfaces[1].Key = 0x7FFFFFFF;
            r_currentkey = 0;
        }

        // FIXME: set with memset
        for (int v = RShared.r_refdef->Vrect.Y; v < RShared.r_refdef->VrectBottom; v++)
        {
            RShared.newedges[v] = RShared.removeedges[v] = null;
        }
    }

    /*
    ==============
    R_InsertNewEdges

    Adds the edges in the linked list edgestoadd, adding them to the edges
    in the linked list edgelist. edgestoadd is assumed to be sorted on u,
    and non-empty (this is actually newedges[v]). edgelist is assumed to be
    sorted on u, with a sentinel at the end (actually, this is the active
    edge table starting at edge_head.next).
    ==============
    */
    public static void InsertNewEdges(Edge_t* edgestoadd, Edge_t* edgelist)
    {
        do
        {
            Edge_t* next_edge = edgestoadd->Next;

            edgesearch:
            if (edgelist->U >= edgestoadd->U)
                goto addedge;
            edgelist = edgelist->Next;
            if (edgelist->U >= edgestoadd->U)
                goto addedge;
            edgelist = edgelist->Next;
            if (edgelist->U >= edgestoadd->U)
                goto addedge;
            edgelist = edgelist->Next;
            if (edgelist->U >= edgestoadd->U)
                goto addedge;
            edgelist = edgelist->Next;
            goto edgesearch;

            // insert edgestoadd before edgelist
            addedge:
            edgestoadd->Next = edgelist;
            edgestoadd->Prev = edgelist->Prev;
            edgelist->Prev->Next = edgestoadd;
            edgelist->Prev = edgestoadd;

            edgestoadd = next_edge;
        } while (edgestoadd != null);
    }

    /*
    ==============
    R_RemoveEdges
    ==============
    */
    public static void RemoveEdges(Edge_t* pedge)
    {
        do
        {
            pedge->Next->Prev = pedge->Prev;
            pedge->Prev->Next = pedge->Next;
        } while ((pedge = pedge->NextRemove) != null);
    }

    /*
    ==============
    R_StepActiveU
    ==============
    */
    public static void StepActiveU(Edge_t* pedge)
    {
        Edge_t* edgeAfterTail = RShared.edge_aftertail;
        Edge_t* edgeTail = RShared.edge_tail;

        while (true)
        {
            nextedge:
            pedge->U += pedge->UStep;
            if (pedge->U < pedge->Prev->U)
                goto pushback;
            pedge = pedge->Next;

            pedge->U += pedge->UStep;
            if (pedge->U < pedge->Prev->U)
                goto pushback;
            pedge = pedge->Next;

            pedge->U += pedge->UStep;
            if (pedge->U < pedge->Prev->U)
                goto pushback;
            pedge = pedge->Next;

            pedge->U += pedge->UStep;
            if (pedge->U < pedge->Prev->U)
                goto pushback;
            pedge = pedge->Next;

            goto nextedge;

            pushback:
            if (pedge == edgeAfterTail)
                return;

            // push it back to keep it sorted
            Edge_t* pnext_edge = pedge->Next;

            // pull the edge out of the edge list
            pedge->Next->Prev = pedge->Prev;
            pedge->Prev->Next = pedge->Next;

            // find out where the edge goes in the edge list
            Edge_t* pwedge = pedge->Prev->Prev;

            while (pwedge->U > pedge->U)
            {
                pwedge = pwedge->Prev;
            }

            // put the edge back into the edge list
            pedge->Next = pwedge->Next;
            pedge->Prev = pwedge;
            pedge->Next->Prev = pedge;
            pwedge->Next = pedge;

            pedge = pnext_edge;
            if (pedge == edgeTail)
                return;
        }
    }

    /*
    ==============
    R_CleanupSpan
    ==============
    */
    public static void CleanupSpan()
    {
        // now that we've reached the right edge of the screen, we're done
        // with any unfinished surfaces, so emit a span for whatever's on top
        Surf_t* surf = RShared.surfaces[1].Next;
        int iu = edge_tail_u_shift20;
        if (iu > surf->LastU)
        {
            ESpan_t* span = span_p++;
            span->U = surf->LastU;
            span->Count = iu - span->U;
            span->V = current_iv;
            span->PNext = surf->Spans;
            surf->Spans = span;
        }

        // reset spanstate for all surfaces in the surface stack
        do
        {
            surf->SpanState = 0;
            surf = surf->Next;
        } while (surf != &RShared.surfaces[1]);
    }

    /*
    ==============
    R_LeadingEdgeBackwards
    ==============
    */
    public static void LeadingEdgeBackwards(Edge_t* edge)
    {
        // it's adding a new surface in, so find the correct place
        Surf_t* surf = &RShared.surfaces[edge->Surfs[1]];

        // don't start a span if this is an inverted span, with the end
        // edge preceding the start edge (that is, we've already seen the
        // end edge)
        if (++surf->SpanState == 1)
        {
            Surf_t* surf2 = RShared.surfaces[1].Next;

            if (surf->Key > surf2->Key)
                goto newtop;

            // if it's two surfaces on the same plane, the one that's
            // already active is in front, so keep going unless it's a
            // bmodel
            if (surf->InSubmodel && surf->Key == surf2->Key)
            {
                // must be two bmodels in the same leaf; don't care, because
                // they'll never be farthest anyway
                goto newtop;
            }

            continue_search:
            do
            {
                surf2 = surf2->Next;
            } while (surf->Key < surf2->Key);

            if (surf->Key == surf2->Key)
            {
                // if it's two surfaces on the same plane, the one that's
                // already active is in front, so keep going unless it's a
                // bmodel
                if (!surf->InSubmodel)
                    goto continue_search;

                // must be two bmodels in the same leaf; don't care which is
                // really in front, because they'll never be farthest anyway
            }

            goto gotposition;

            newtop:
            {
                // emit a span (obscures current top)
                int iu = (int)(edge->U >> 20);

                if (iu > surf2->LastU)
                {
                    ESpan_t* span = span_p++;
                    span->U = surf2->LastU;
                    span->Count = iu - span->U;
                    span->V = current_iv;
                    span->PNext = surf2->Spans;
                    surf2->Spans = span;
                }

                // set last_u on the new span
                surf->LastU = iu;
            }

            gotposition:
            // insert before surf2
            surf->Next = surf2;
            surf->Prev = surf2->Prev;
            surf2->Prev->Next = surf;
            surf2->Prev = surf;
        }
    }

    /*
    ==============
    R_TrailingEdge
    ==============
    */
    public static void TrailingEdge(Surf_t* surf, Edge_t* edge)
    {
        // don't generate a span if this is an inverted span, with the end
        // edge preceding the start edge (that is, we haven't seen the
        // start edge yet)
        if (--surf->SpanState == 0)
        {
            if (surf->InSubmodel)
                r_bmodelactive--;

            if (surf == RShared.surfaces[1].Next)
            {
                // emit a span (current top going away)
                int iu = (int)(edge->U >> 20);
                if (iu > surf->LastU)
                {
                    ESpan_t* span = span_p++;
                    span->U = surf->LastU;
                    span->Count = iu - span->U;
                    span->V = current_iv;
                    span->PNext = surf->Spans;
                    surf->Spans = span;
                }

                // set last_u on the surface below
                surf->Next->LastU = iu;
            }

            surf->Prev->Next = surf->Next;
            surf->Next->Prev = surf->Prev;
        }
    }

    /*
    ==============
    R_LeadingEdge
    ==============
    */
    public static void LeadingEdge(Edge_t* edge)
    {
        if (edge->Surfs[1] == 0)
            return;

        // it's adding a new surface in, so find the correct place
        Surf_t* surf = &RShared.surfaces[edge->Surfs[1]];

        // don't start a span if this is an inverted span, with the end
        // edge preceding the start edge (that is, we've already seen the
        // end edge)
        if (++surf->SpanState == 1)
        {
            if (surf->InSubmodel)
                r_bmodelactive++;

            Surf_t* surf2 = RShared.surfaces[1].Next;

            if (surf->Key < surf2->Key)
                goto newtop;

            // if it's two surfaces on the same plane, the one that's
            // already active is in front, so keep going unless it's a
            // bmodel
            if (surf->InSubmodel && surf->Key == surf2->Key)
            {
                // must be two bmodels in the same leaf; sort on 1/z
                double fu = (edge->U - 0xFFFFF) * (1.0 / 0x100000);
                double newzi = surf->DZiOrigin + fv * surf->DZiStepV + fu * surf->DZiStepU;
                double newzibottom = newzi * 0.99;

                double testzi = surf2->DZiOrigin + fv * surf2->DZiStepV + fu * surf2->DZiStepU;

                if (newzibottom >= testzi)
                    goto newtop;

                double newzitop = newzi * 1.01;
                if (newzitop >= testzi)
                {
                    if (surf->DZiStepU >= surf2->DZiStepU)
                        goto newtop;
                }
            }

            continue_search:
            do
            {
                surf2 = surf2->Next;
            } while (surf->Key > surf2->Key);

            if (surf->Key == surf2->Key)
            {
                // if it's two surfaces on the same plane, the one that's
                // already active is in front, so keep going unless it's a
                // bmodel
                if (!surf->InSubmodel)
                    goto continue_search;

                // must be two bmodels in the same leaf; sort on 1/z
                double fu = (edge->U - 0xFFFFF) * (1.0 / 0x100000);
                double newzi = surf->DZiOrigin + fv * surf->DZiStepV + fu * surf->DZiStepU;
                double newzibottom = newzi * 0.99;

                double testzi = surf2->DZiOrigin + fv * surf2->DZiStepV + fu * surf2->DZiStepU;

                if (newzibottom >= testzi)
                    goto gotposition;

                double newzitop = newzi * 1.01;
                if (newzitop >= testzi)
                {
                    if (surf->DZiStepU >= surf2->DZiStepU)
                        goto gotposition;
                }

                goto continue_search;
            }

            goto gotposition;

            newtop:
            {
                // emit a span (obscures current top)
                int iu = (int)(edge->U >> 20);

                if (iu > surf2->LastU)
                {
                    ESpan_t* span = span_p++;
                    span->U = surf2->LastU;
                    span->Count = iu - span->U;
                    span->V = current_iv;
                    span->PNext = surf2->Spans;
                    surf2->Spans = span;
                }

                // set last_u on the new span
                surf->LastU = iu;
            }

            gotposition:
            // insert before surf2
            surf->Next = surf2;
            surf->Prev = surf2->Prev;
            surf2->Prev->Next = surf;
            surf2->Prev = surf;
        }
    }

    /*
    ==============
    R_GenerateSpans
    ==============
    */
    public static void GenerateSpans()
    {
        r_bmodelactive = 0;

        // clear active surfaces to just the background surface
        RShared.surfaces[1].Next = RShared.surfaces[1].Prev = &RShared.surfaces[1];
        RShared.surfaces[1].LastU = edge_head_u_shift20;

        Edge_t* edgeTail = RShared.edge_tail;

        // generate spans
        for (Edge_t* edge = RShared.edge_head->Next; edge != edgeTail; edge = edge->Next)
        {
            if (edge->Surfs[0] != 0)
            {
                // it has a left surface, so a surface is going away for
                // this span
                Surf_t* surf = &RShared.surfaces[edge->Surfs[0]];

                TrailingEdge(surf, edge);

                if (edge->Surfs[1] == 0)
                    continue;
            }

            LeadingEdge(edge);
        }

        CleanupSpan();
    }

    /*
    ==============
    R_GenerateSpansBackward
    ==============
    */
    public static void GenerateSpansBackward()
    {
        r_bmodelactive = 0;

        // clear active surfaces to just the background surface
        RShared.surfaces[1].Next = RShared.surfaces[1].Prev = &RShared.surfaces[1];
        RShared.surfaces[1].LastU = edge_head_u_shift20;

        Edge_t* edgeTail = RShared.edge_tail;

        // generate spans
        for (Edge_t* edge = RShared.edge_head->Next; edge != edgeTail; edge = edge->Next)
        {
            if (edge->Surfs[0] != 0)
                TrailingEdge(&RShared.surfaces[edge->Surfs[0]], edge);

            if (edge->Surfs[1] != 0)
                LeadingEdgeBackwards(edge);
        }

        CleanupSpan();
    }

    /*
    ==============
    R_ScanEdges

    Input:
    newedges[] array
        this has links to edges, which have links to surfaces

    Output:
    Each surface has a linked list of its visible spans
    ==============
    */
    public static void ScanEdges()
    {
        Edge_t* edgeHead = RShared.edge_head;
        Edge_t* edgeTail = RShared.edge_tail;
        Edge_t* edgeAfterTail = RShared.edge_aftertail;
        Edge_t* edgeSentinel = edge_sentinel;

        if (basespans == null)
            basespans = (ESpan_t*)NativeMemory.AlignedAlloc((nuint)(RShared.MAXSPANS * sizeof(ESpan_t)), 64);
        ESpan_t* basespan_p = basespans;
        max_span_p = &basespan_p[RShared.MAXSPANS - RShared.r_refdef->Vrect.Width];

        span_p = basespan_p;

        // clear active edges to just the background edges around the whole
        // screen
        // FIXME: most of this only needs to be set up once
        edgeHead->U = (long)RShared.r_refdef->Vrect.X << 20;
        edge_head_u_shift20 = (int)(edgeHead->U >> 20);
        edgeHead->UStep = 0;
        edgeHead->Prev = null;
        edgeHead->Next = edgeTail;
        edgeHead->Surfs[0] = 0;
        edgeHead->Surfs[1] = 1;

        edgeTail->U = ((long)RShared.r_refdef->VrectRight << 20) + 0xFFFFF;
        edge_tail_u_shift20 = (int)(edgeTail->U >> 20);
        edgeTail->UStep = 0;
        edgeTail->Prev = edgeHead;
        edgeTail->Next = edgeAfterTail;
        edgeTail->Surfs[0] = 1;
        edgeTail->Surfs[1] = 0;

        edgeAfterTail->U = -1; // force a move
        edgeAfterTail->UStep = 0;
        edgeAfterTail->Next = edgeSentinel;
        edgeAfterTail->Prev = edgeTail;

        // FIXME: do we need this now that we clamp x in r_draw.c?
        // 2000L, not the original's int 2000 << 24 (which wraps negative in
        // 32 bits): with Edge_t.U widened to long, this now actually is the
        // largest value, as the original comment intends.
        edgeSentinel->U = 2000L << 24; // make sure nothing sorts past this
        edgeSentinel->Prev = edgeAfterTail;

        //
        // process all scan lines
        //
        int bottom = RShared.r_refdef->VrectBottom - 1;

        int iv;
        for (iv = RShared.r_refdef->Vrect.Y; iv < bottom; iv++)
        {
            current_iv = iv;
            fv = iv;

            // mark that the head (background start) span is pre-included
            RShared.surfaces[1].SpanState = 1;

            if (RShared.newedges[iv] != null)
                InsertNewEdges(RShared.newedges[iv], edgeHead->Next);

            pdrawfunc();

            // flush the span list if we can't be sure we have enough spans
            // left for the next scan
            if (span_p >= max_span_p)
            {
                // VID_UnlockBuffer/S_ExtraUpdate/VID_LockBuffer -- dropped
                // (both display layer and sound mixer are ported now, but
                // this mid-render buffer-lock/audio-pump triad is unneeded
                // by OpenTK/OpenAL's model)

                if (DIface.r_drawculledpolys)
                    DrawCulledPolys();
                else
                    DEdge.DrawSurfaces();

                // clear the surface span pointers
                for (Surf_t* s = &RShared.surfaces[1]; s < RShared.surface_p; s++)
                    s->Spans = null;

                span_p = basespan_p;
            }

            if (RShared.removeedges[iv] != null)
                RemoveEdges(RShared.removeedges[iv]);

            if (edgeHead->Next != edgeTail)
                StepActiveU(edgeHead->Next);
        }

        // do the last scan (no need to step or sort or remove on the last
        // scan)

        current_iv = iv;
        fv = iv;

        // mark that the head (background start) span is pre-included
        RShared.surfaces[1].SpanState = 1;

        if (RShared.newedges[iv] != null)
            InsertNewEdges(RShared.newedges[iv], edgeHead->Next);

        pdrawfunc();

        // draw whatever's left in the span list
        if (DIface.r_drawculledpolys)
            DrawCulledPolys();
        else
            DEdge.DrawSurfaces();
    }

}
