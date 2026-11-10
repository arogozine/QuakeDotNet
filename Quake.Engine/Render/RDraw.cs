// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// r_draw.c: edge emission/clipping and surface posting -- the core of the
/// active-edge-table renderer's setup phase (REdge.cs, step 2c, consumes
/// the surfaces/edges this file builds).
/// </summary>
public static unsafe class RDraw
{
    // !!! if these are changed, they must be changed in asm_draw.h too !!!
    private const uint FULLY_CLIPPED_CACHED = 0x80000000;
    private const uint FRAMECOUNT_MASK = 0x7FFFFFFF;

    public static uint cacheoffset;

    // c_faceclip already lives in RMain.cs (r_main.c's own global) -- reused.
    // r_zpointdesc/r_polydesc already live in DIface.cs -- reused.
    // view_clipplanes[4]/sintable/intsintable are textually declared in
    // r_draw.c in the original, but were already correctly placed in
    // RMain.cs/RShared.cs per the "declare in earliest consumer" convention
    // (r_main.c's R_Init needed sintable/intsintable in step 2b; r_bsp.c/
    // r_misc.c/r_sprite.c all needed view_clipplanes before this step) --
    // reused, not redeclared here.

    // entity_clipplanes/world_clipplanes -- declared in the original but
    // never referenced anywhere in the WinQuake tree (dead globals even
    // upstream); ported for faithfulness per CLAUDE.md rule 2 only.
    public static ClipPlane_t* entity_clipplanes;
    public static readonly ClipPlane_t* world_clipplanes = NativeMemory.AllocArrayZeroed<ClipPlane_t>(16);

    public static MEdge_t* r_pedge;

    public static bool r_leftclipped, r_rightclipped;
    private static bool makeleftedge, makerightedge;
    public static bool r_nearzionly;

    // r_leftenter/r_leftexit/r_rightenter/r_rightexit -- native-backed
    // pointers (not plain static fields) since &r_leftexit etc. are passed
    // by address into ClipEdge across calls; matches the
    // RAlias.aliastransform/RBsp.entity_rotation convention.
    internal static readonly MVertex_t* _clipverts = NativeMemory.AllocArrayZeroed<MVertex_t>(4);
    public static readonly MVertex_t* r_leftenter = _clipverts;
    public static readonly MVertex_t* r_leftexit = _clipverts + 1;
    public static readonly MVertex_t* r_rightenter = _clipverts + 2;
    public static readonly MVertex_t* r_rightexit = _clipverts + 3;

    public static int r_emitted;
    public static float r_nearzi;
    public static float r_u1, r_v1, r_lzi1;
    public static int r_ceilv1;

    public static bool r_lastvertvalid;

    // R_EmitEdge
    public static void EmitEdge(MVertex_t* pv0, MVertex_t* pv1)
    {
        float u0, v0, lzi0;
        int ceilv0;

        if (r_lastvertvalid)
        {
            u0 = r_u1;
            v0 = r_v1;
            lzi0 = r_lzi1;
            ceilv0 = r_ceilv1;
        }
        else
        {
            ref Vec3_t world = ref *pv0;

            // transform and project
            Vec3_t local = world - RShared.modelorg;
            RMisc.TransformVector(local, out Vec3_t transformed);

            if (transformed[2] < RLocal.NEAR_CLIP)
                transformed[2] = RLocal.NEAR_CLIP;

            lzi0 = 1.0f / transformed[2];

            // FIXME (original comment): build x/yscale into transform?
            float scale = RShared.xscale * lzi0;
            u0 = RShared.xcenter + scale * transformed[0];
            if (u0 < RShared.r_refdef->FVrectxAdj)
                u0 = RShared.r_refdef->FVrectxAdj;
            if (u0 > RShared.r_refdef->FVrectRightAdj)
                u0 = RShared.r_refdef->FVrectRightAdj;

            scale = RShared.yscale * lzi0;
            v0 = RShared.ycenter - scale * transformed[1];
            if (v0 < RShared.r_refdef->FVrectyAdj)
                v0 = RShared.r_refdef->FVrectyAdj;
            if (v0 > RShared.r_refdef->FVrectBottomAdj)
                v0 = RShared.r_refdef->FVrectBottomAdj;

            ceilv0 = (int)MathF.Ceiling(v0);
        }

        {
            ref Vec3_t world = ref *pv1;

            // transform and project
            Vec3_t local = world - RShared.modelorg;
            RMisc.TransformVector(local, out Vec3_t transformed);

            if (transformed[2] < RLocal.NEAR_CLIP)
                transformed[2] = RLocal.NEAR_CLIP;

            r_lzi1 = 1.0f / transformed[2];

            float scale = RShared.xscale * r_lzi1;
            r_u1 = RShared.xcenter + scale * transformed[0];
            if (r_u1 < RShared.r_refdef->FVrectxAdj)
                r_u1 = RShared.r_refdef->FVrectxAdj;
            if (r_u1 > RShared.r_refdef->FVrectRightAdj)
                r_u1 = RShared.r_refdef->FVrectRightAdj;

            scale = RShared.yscale * r_lzi1;
            r_v1 = RShared.ycenter - scale * transformed[1];
            if (r_v1 < RShared.r_refdef->FVrectyAdj)
                r_v1 = RShared.r_refdef->FVrectyAdj;
            if (r_v1 > RShared.r_refdef->FVrectBottomAdj)
                r_v1 = RShared.r_refdef->FVrectBottomAdj;
        }

        if (r_lzi1 > lzi0)
            lzi0 = r_lzi1;

        if (lzi0 > r_nearzi) // for mipmap finding
            r_nearzi = lzi0;

        // for right edges, all we want is the effect on 1/z
        if (r_nearzionly)
            return;

        r_emitted = 1;

        r_ceilv1 = (int)MathF.Ceiling(r_v1);

        // create the edge
        if (ceilv0 == r_ceilv1)
        {
            // we cache unclipped horizontal edges as fully clipped
            if (cacheoffset != 0x7FFFFFFF)
            {
                cacheoffset = FULLY_CLIPPED_CACHED | ((uint)DIface.r_framecount & FRAMECOUNT_MASK);
            }

            return; // horizontal edge
        }

        int side = ceilv0 > r_ceilv1 ? 1 : 0;

        Edge_t* edge = RShared.edge_p++;

        edge->Owner = r_pedge;
        edge->NearZi = lzi0;

        int v, v2;
        float u, u_step;

        if (side == 0)
        {
            // trailing edge (go from p1 to p2)
            v = ceilv0;
            v2 = r_ceilv1 - 1;

            edge->Surfs[0] = (ushort)(RShared.surface_p - RShared.surfaces);
            edge->Surfs[1] = 0;

            u_step = (r_u1 - u0) / (r_v1 - v0);
            u = u0 + (v - v0) * u_step;
        }
        else
        {
            // leading edge (go from p2 to p1)
            v2 = ceilv0 - 1;
            v = r_ceilv1;

            edge->Surfs[0] = 0;
            edge->Surfs[1] = (ushort)(RShared.surface_p - RShared.surfaces);

            u_step = (u0 - r_u1) / (v0 - r_v1);
            u = r_u1 + (v - r_v1) * u_step;
        }

        edge->UStep = (long)(u_step * 0x100000);
        edge->U = (long)(u * 0x100000) + 0xFFFFF;

        // we need to do this to avoid stepping off the edges if a very nearly
        // horizontal edge is less than epsilon above a scan, and numeric error causes
        // it to incorrectly extend to the scan, and the extension of the line goes off
        // the edge of the screen
        // FIXME (original comment): is this actually needed?
        if (edge->U < RShared.r_refdef->VrectXAdjShift20)
            edge->U = RShared.r_refdef->VrectXAdjShift20;
        if (edge->U > RShared.r_refdef->VrectRightAdjShift20)
            edge->U = RShared.r_refdef->VrectRightAdjShift20;

        // sort the edge in normally
        long u_check = edge->U;
        if (edge->Surfs[0] != 0)
            u_check++; // sort trailers after leaders

        if (RShared.newedges[v] == null || RShared.newedges[v]->U >= u_check)
        {
            edge->Next = RShared.newedges[v];
            RShared.newedges[v] = edge;
        }
        else
        {
            Edge_t* pcheck = RShared.newedges[v];
            while (pcheck->Next != null && pcheck->Next->U < u_check)
                pcheck = pcheck->Next;
            edge->Next = pcheck->Next;
            pcheck->Next = edge;
        }

        edge->NextRemove = RShared.removeedges[v2];
        RShared.removeedges[v2] = edge;
    }

    // R_ClipEdge
    public static void ClipEdge(MVertex_t* pv0, MVertex_t* pv1, ClipPlane_t* clip)
    {
        if (clip != null)
        {
            do
            {
                float d0 = Vector3.Dot(*pv0, clip->Normal) - clip->Dist;
                float d1 = Vector3.Dot(*pv1, clip->Normal) - clip->Dist;

                if (d0 >= 0)
                {
                    // point 0 is unclipped
                    if (d1 >= 0)
                    {
                        // both points are unclipped
                        continue;
                    }

                    // only point 1 is clipped

                    // we don't cache clipped edges
                    cacheoffset = 0x7FFFFFFF;

                    float f = d0 / (d0 - d1);
                    MVertex_t clipvert = *pv0 + f * (*pv1 - *pv0);

                    if (clip->LeftEdge != 0)
                    {
                        r_leftclipped = true;
                        *r_leftexit = clipvert;
                    }
                    else if (clip->RightEdge != 0)
                    {
                        r_rightclipped = true;
                        *r_rightexit = clipvert;
                    }

                    ClipEdge(pv0, &clipvert, clip->Next);
                    return;
                }
                else
                {
                    // point 0 is clipped
                    if (d1 < 0)
                    {
                        // both points are clipped
                        // we do cache fully clipped edges
                        if (!r_leftclipped)
                            cacheoffset = FULLY_CLIPPED_CACHED | ((uint)DIface.r_framecount & FRAMECOUNT_MASK);
                        return;
                    }

                    // only point 0 is clipped
                    r_lastvertvalid = false;

                    // we don't cache partially clipped edges
                    cacheoffset = 0x7FFFFFFF;

                    float f = d0 / (d0 - d1);
                    MVertex_t clipvert = *pv0 + f * (*pv1 - *pv0);

                    if (clip->LeftEdge != 0)
                    {
                        r_leftclipped = true;
                        *r_leftenter = clipvert;
                    }
                    else if (clip->RightEdge != 0)
                    {
                        r_rightclipped = true;
                        *r_rightenter = clipvert;
                    }

                    ClipEdge(&clipvert, pv1, clip->Next);
                    return;
                }
            } while ((clip = clip->Next) != null);
        }

        // add the edge
        EmitEdge(pv0, pv1);
    }

    // R_EmitCachedEdge
    public static void EmitCachedEdge()
    {
        Edge_t* pedge_t = (Edge_t*)((byte*)RShared.r_edges + r_pedge->CachedEdgeOffset);

        if (pedge_t->Surfs[0] == 0)
            pedge_t->Surfs[0] = (ushort)(RShared.surface_p - RShared.surfaces);
        else
            pedge_t->Surfs[1] = (ushort)(RShared.surface_p - RShared.surfaces);

        if (pedge_t->NearZi > r_nearzi) // for mipmap finding
            r_nearzi = pedge_t->NearZi;

        r_emitted = 1;
    }

    // R_RenderFace
    public static void RenderFace(MSurface_t* fa, int clipflags)
    {
        // skip out if no more surfs
        if (RShared.surface_p >= RShared.surf_max)
        {
            RMain.r_outofsurfaces++;
            return;
        }

        // ditto if not enough edges left, or switch to auxedges if possible
        if (RShared.edge_p + fa->NumEdges + 4 >= RShared.edge_max)
        {
            RMain.r_outofedges += fa->NumEdges;
            return;
        }

        RMain.c_faceclip++;

        // set up clip planes
        ClipPlane_t* pclip = null;

        uint mask = 0x08;
        {
            ClipPlane_t* view_clipplanes = RMain.view_clipplanes;
            for (int i = 3; i >= 0; i--, mask >>= 1)
            {
                if ((clipflags & mask) != 0)
                {
                    view_clipplanes[i].Next = pclip;
                    pclip = &view_clipplanes[i];
                }
            }

            // push the edges through
            r_emitted = 0;
            r_nearzi = 0;
            r_nearzionly = false;
            makeleftedge = makerightedge = false;
            MEdge_t* pedges = RShared.currententity->Model->Edges;
            r_lastvertvalid = false;

            for (int i = 0; i < fa->NumEdges; i++)
            {
                int lindex = RShared.currententity->Model->Surfedges[fa->FirstEdge + i];

                if (lindex > 0)
                {
                    r_pedge = &pedges[lindex];

                    // if the edge is cached, we can just reuse the edge
                    if (!RMain.insubmodel)
                    {
                        if ((r_pedge->CachedEdgeOffset & FULLY_CLIPPED_CACHED) != 0)
                        {
                            if ((r_pedge->CachedEdgeOffset & FRAMECOUNT_MASK) == (uint)DIface.r_framecount)
                            {
                                r_lastvertvalid = false;
                                continue;
                            }
                        }
                        else
                        {
                            if ((ulong)((byte*)RShared.edge_p - (byte*)RShared.r_edges) > r_pedge->CachedEdgeOffset &&
                                ((Edge_t*)((byte*)RShared.r_edges + r_pedge->CachedEdgeOffset))->Owner == r_pedge)
                            {
                                EmitCachedEdge();
                                r_lastvertvalid = false;
                                continue;
                            }
                        }
                    }

                    // assume it's cacheable
                    cacheoffset = (uint)((byte*)RShared.edge_p - (byte*)RShared.r_edges);
                    r_leftclipped = r_rightclipped = false;
                    ClipEdge(&RMain.r_pcurrentvertbase[r_pedge->V[0]], &RMain.r_pcurrentvertbase[r_pedge->V[1]], pclip);
                    r_pedge->CachedEdgeOffset = cacheoffset;

                    if (r_leftclipped)
                        makeleftedge = true;
                    if (r_rightclipped)
                        makerightedge = true;
                    r_lastvertvalid = true;
                }
                else
                {
                    lindex = -lindex;
                    r_pedge = &pedges[lindex];

                    // if the edge is cached, we can just reuse the edge
                    if (!RMain.insubmodel)
                    {
                        if ((r_pedge->CachedEdgeOffset & FULLY_CLIPPED_CACHED) != 0)
                        {
                            if ((r_pedge->CachedEdgeOffset & FRAMECOUNT_MASK) == (uint)DIface.r_framecount)
                            {
                                r_lastvertvalid = false;
                                continue;
                            }
                        }
                        else
                        {
                            // it's cached if the cached edge is valid and is owned
                            // by this medge_t
                            if ((ulong)((byte*)RShared.edge_p - (byte*)RShared.r_edges) > r_pedge->CachedEdgeOffset &&
                                ((Edge_t*)((byte*)RShared.r_edges + r_pedge->CachedEdgeOffset))->Owner == r_pedge)
                            {
                                EmitCachedEdge();
                                r_lastvertvalid = false;
                                continue;
                            }
                        }
                    }

                    // assume it's cacheable
                    cacheoffset = (uint)((byte*)RShared.edge_p - (byte*)RShared.r_edges);
                    r_leftclipped = r_rightclipped = false;
                    ClipEdge(&RMain.r_pcurrentvertbase[r_pedge->V[1]], &RMain.r_pcurrentvertbase[r_pedge->V[0]], pclip);
                    r_pedge->CachedEdgeOffset = cacheoffset;

                    if (r_leftclipped)
                        makeleftedge = true;
                    if (r_rightclipped)
                        makerightedge = true;
                    r_lastvertvalid = true;
                }
            }

            // if there was a clip off the left edge, add that edge too
            // FIXME (original comment): faster to do in screen space?
            // FIXME (original comment): share clipped edges?
            MEdge_t tedge = default;
            if (makeleftedge)
            {
                r_pedge = &tedge;
                r_lastvertvalid = false;
                ClipEdge(r_leftexit, r_leftenter, pclip->Next);
            }

            // if there was a clip off the right edge, get the right r_nearzi
            if (makerightedge)
            {
                r_pedge = &tedge;
                r_lastvertvalid = false;
                r_nearzionly = true;
                ClipEdge(r_rightexit, r_rightenter, view_clipplanes[1].Next);
            }
        }

        // if no edges made it out, return without posting the surface
        if (r_emitted == 0)
            return;

        RMain.r_polycount++;

        RShared.surface_p->Data = fa;
        RShared.surface_p->NearZi = r_nearzi;
        RShared.surface_p->Flags = fa->Flags;
        RShared.surface_p->InSubmodel = RMain.insubmodel;
        RShared.surface_p->SpanState = 0;
        RShared.surface_p->Entity = RShared.currententity;
        RShared.surface_p->Key = REdge.r_currentkey++;
        RShared.surface_p->Spans = null;

        MPlane_t* pplane = fa->Plane;
        // FIXME (original comment): cache this?
        RMisc.TransformVector(in pplane->Normal, out Vec3_t p_normal);
        // FIXME (original comment): cache this?
        float distinv = 1.0f / (pplane->Dist - Vector3.Dot(RShared.modelorg, pplane->Normal));

        RShared.surface_p->DZiStepU = p_normal[0] * RShared.xscaleinv * distinv;
        RShared.surface_p->DZiStepV = -p_normal[1] * RShared.yscaleinv * distinv;
        RShared.surface_p->DZiOrigin = p_normal[2] * distinv
            - RShared.xcenter * RShared.surface_p->DZiStepU
            - RShared.ycenter * RShared.surface_p->DZiStepV;

        RShared.surface_p++;
    }

    // R_RenderBmodelFace
    public static void RenderBmodelFace(BEdge_t* pedges, MSurface_t* psurf)
    {
        // skip out if no more surfs
        if (RShared.surface_p >= RShared.surf_max)
        {
            RMain.r_outofsurfaces++;
            return;
        }

        // ditto if not enough edges left, or switch to auxedges if possible
        if (RShared.edge_p + psurf->NumEdges + 4 >= RShared.edge_max)
        {
            RMain.r_outofedges += psurf->NumEdges;
            return;
        }

        RMain.c_faceclip++;

        // this is a dummy to give the caching mechanism someplace to write to
        MEdge_t tedge = default;
        r_pedge = &tedge;

        // set up clip planes
        ClipPlane_t* pclip = null;

        {
            ClipPlane_t* view_clipplanes = RMain.view_clipplanes;
            uint mask = 0x08;
            for (int i = 3; i >= 0; i--, mask >>= 1)
            {
                if ((RMain.r_clipflags & mask) != 0)
                {
                    view_clipplanes[i].Next = pclip;
                    pclip = &view_clipplanes[i];
                }
            }

            // push the edges through
            r_emitted = 0;
            r_nearzi = 0;
            r_nearzionly = false;
            makeleftedge = makerightedge = false;
            // FIXME (original comment): keep clipped bmodel edges in
            // clockwise order so last vertex caching can be used?
            r_lastvertvalid = false;

            for (; pedges != null; pedges = pedges->PNext)
            {
                r_leftclipped = r_rightclipped = false;
                ClipEdge(pedges->V0, pedges->V1, pclip);

                if (r_leftclipped)
                    makeleftedge = true;
                if (r_rightclipped)
                    makerightedge = true;
            }

            // if there was a clip off the left edge, add that edge too
            if (makeleftedge)
            {
                r_pedge = &tedge;
                ClipEdge(r_leftexit, r_leftenter, pclip->Next);
            }

            // if there was a clip off the right edge, get the right r_nearzi
            if (makerightedge)
            {
                r_pedge = &tedge;
                r_nearzionly = true;
                ClipEdge(r_rightexit, r_rightenter, view_clipplanes[1].Next);
            }
        }

        // if no edges made it out, return without posting the surface
        if (r_emitted == 0)
            return;

        RMain.r_polycount++;

        RShared.surface_p->Data = psurf;
        RShared.surface_p->NearZi = r_nearzi;
        RShared.surface_p->Flags = psurf->Flags;
        RShared.surface_p->InSubmodel = true;
        RShared.surface_p->SpanState = 0;
        RShared.surface_p->Entity = RShared.currententity;
        RShared.surface_p->Key = RBsp.r_currentbkey;
        RShared.surface_p->Spans = null;

        MPlane_t* pplane = psurf->Plane;
        // FIXME (original comment): cache this?
        RMisc.TransformVector(in pplane->Normal, out Vec3_t p_normal);
        // FIXME (original comment): cache this?
        float distinv = 1.0f / (pplane->Dist - Vector3.Dot(RShared.modelorg, pplane->Normal));

        RShared.surface_p->DZiStepU = p_normal[0] * RShared.xscaleinv * distinv;
        RShared.surface_p->DZiStepV = -p_normal[1] * RShared.yscaleinv * distinv;
        RShared.surface_p->DZiOrigin = p_normal[2] * distinv
            - RShared.xcenter * RShared.surface_p->DZiStepU
            - RShared.ycenter * RShared.surface_p->DZiStepV;

        RShared.surface_p++;
    }

    // R_RenderPoly: verts[2][100], pverts[100] -- the original's are stack
    // locals; allocated once here to avoid zero-initializing them per call
    // (and to keep r_polydesc.pverts valid after returning).
    internal static readonly MVertex_t* renderpoly_verts0 = NativeMemory.AllocArray<MVertex_t>(100); //FIXME (original comment): do real number
    internal static readonly MVertex_t* renderpoly_verts1 = NativeMemory.AllocArray<MVertex_t>(100);
    internal static readonly PolyVert_t* renderpoly_pverts = NativeMemory.AllocArray<PolyVert_t>(100); //FIXME (original comment): do real number, safely

    // R_RenderPoly
    public static void RenderPoly(MSurface_t* fa, int clipflags)
    {
        // FIXME (original comment): clean this up and make it faster
        // FIXME (original comment): guard against running out of vertices
        int s_axis = 0, t_axis = 0;

        // set up clip planes
        ClipPlane_t* pclip = null;

        {
            ClipPlane_t* view_clipplanes = RMain.view_clipplanes;
            uint mask = 0x08;
            for (int i = 3; i >= 0; i--, mask >>= 1)
            {
                if ((clipflags & mask) != 0)
                {
                    view_clipplanes[i].Next = pclip;
                    pclip = &view_clipplanes[i];
                }
            }
        }

        // reconstruct the polygon
        // FIXME (original comment): these should be precalculated and loaded off disk
        MEdge_t* pedges = RShared.currententity->Model->Edges;
        int lnumverts = fa->NumEdges;
        int vertpage = 0;

        MVertex_t** verts = stackalloc MVertex_t*[2] { renderpoly_verts0, renderpoly_verts1 };
        PolyVert_t* pverts = renderpoly_pverts;

        for (int i = 0; i < lnumverts; i++)
        {
            int lindex = RShared.currententity->Model->Surfedges[fa->FirstEdge + i];

            if (lindex > 0)
            {
                r_pedge = &pedges[lindex];
                verts[0][i] = RMain.r_pcurrentvertbase[r_pedge->V[0]];
            }
            else
            {
                r_pedge = &pedges[-lindex];
                verts[0][i] = RMain.r_pcurrentvertbase[r_pedge->V[1]];
            }
        }

        // clip the polygon, done if not visible
        while (pclip != null)
        {
            MVertex_t* src = verts[vertpage];
            MVertex_t* dst = verts[vertpage ^ 1];
            Vector3 normal = pclip->Normal;
            float pdist = pclip->Dist;

            int lastvert = lnumverts - 1;
            float lastdist = Vector3.Dot(src[lastvert], normal) - pdist;

            bool visible = false;
            int newverts = 0;

            for (int i = 0; i < lnumverts; i++)
            {
                Vector3 cur = src[i];
                float dist = Vector3.Dot(cur, normal) - pdist;

                if ((lastdist > 0) != (dist > 0))
                {
                    float frac = dist / (dist - lastdist);
                    dst[newverts++] = cur + (src[lastvert] - cur) * frac;
                }

                if (dist >= 0)
                {
                    dst[newverts++] = cur;
                    visible = true;
                }

                lastvert = i;
                lastdist = dist;
            }

            if (!visible || newverts < 3)
                return;

            lnumverts = newverts;
            vertpage ^= 1;
            pclip = pclip->Next;
        }

        // transform and project, remembering the z values at the vertices and
        // r_nearzi, and extract the s and t coordinates at the vertices
        MPlane_t* pplane = fa->Plane;
        switch ((PlaneType)pplane->Type)
        {
            case PlaneType.X:
            case PlaneType.AnyX:
                s_axis = 1;
                t_axis = 2;
                break;
            case PlaneType.Y:
            case PlaneType.AnyY:
                s_axis = 0;
                t_axis = 2;
                break;
            case PlaneType.Z:
            case PlaneType.AnyZ:
                s_axis = 0;
                t_axis = 1;
                break;
        }

        r_nearzi = 0;

        for (int i = 0; i < lnumverts; i++)
        {
            // transform and project
            Vec3_t local = verts[vertpage][i] - RShared.modelorg;
            RMisc.TransformVector(local, out Vec3_t transformed);

            if (transformed[2] < RLocal.NEAR_CLIP)
                transformed[2] = RLocal.NEAR_CLIP;

            float lzi = 1.0f / transformed[2];

            if (lzi > r_nearzi) // for mipmap finding
                r_nearzi = lzi;

            // FIXME (original comment): build x/yscale into transform?
            float scale = RShared.xscale * lzi;
            float u = RShared.xcenter + scale * transformed[0];
            if (u < RShared.r_refdef->FVrectxAdj)
                u = RShared.r_refdef->FVrectxAdj;
            if (u > RShared.r_refdef->FVrectRightAdj)
                u = RShared.r_refdef->FVrectRightAdj;

            scale = RShared.yscale * lzi;
            float v = RShared.ycenter - scale * transformed[1];
            if (v < RShared.r_refdef->FVrectyAdj)
                v = RShared.r_refdef->FVrectyAdj;
            if (v > RShared.r_refdef->FVrectBottomAdj)
                v = RShared.r_refdef->FVrectBottomAdj;

            pverts[i].U = u;
            pverts[i].V = v;
            pverts[i].Zi = lzi;
            pverts[i].S = verts[vertpage][i][s_axis];
            pverts[i].T = verts[vertpage][i][t_axis];
        }

        // build the polygon descriptor, including fa, r_nearzi, and u, v, s, t, and z
        // for each vertex
        DIface.r_polydesc.NumVerts = lnumverts;
        DIface.r_polydesc.NearZi = r_nearzi;
        DIface.r_polydesc.PCurrentFace = fa;
        DIface.r_polydesc.PVerts = pverts;

        // draw the polygon
        DEdge.DrawPoly();
    }

    // R_ZDrawSubmodelPolys
    public static void ZDrawSubmodelPolys(Model_t* pmodel)
    {
        MSurface_t* psurf = &pmodel->Surfaces[pmodel->FirstModelSurface];
        int numsurfaces = pmodel->NumModelSurfaces;

        for (int i = 0; i < numsurfaces; i++, psurf++)
        {
            // find which side of the node we are on
            MPlane_t* pplane = psurf->Plane;

            float dot = Vector3.Dot(RShared.modelorg, pplane->Normal) - pplane->Dist;

            // draw the polygon
            if (((psurf->Flags & Surf.PLANEBACK) != 0 && dot < -RLocal.BACKFACE_EPSILON) ||
                ((psurf->Flags & Surf.PLANEBACK) == 0 && dot > RLocal.BACKFACE_EPSILON))
            {
                // FIXME (original comment): use bounding-box-based frustum clipping info?
                RenderPoly(psurf, 15);
            }
        }
    }

}
