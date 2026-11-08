// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;

using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// r_bsp.c: BSP tree traversal, frustum-clip, and bmodel edge clipping.
/// Only R_EntityRotate, R_RotateBmodel, R_RecursiveClipBPoly,
/// R_DrawSolidClippedSubmodelPolygons, R_DrawSubmodelPolygons,
/// R_RecursiveWorldNode, and R_RenderWorld actually live in this file --
/// R_RenderFace/R_RenderPoly/R_RenderBmodelFace live in r_draw.c (RDraw.cs).
///
/// insubmodel/currententity/r_entorigin/r_worldmodelorg/modelorg/
/// base_modelorg are this file's *real* C storage, but per the established
/// "declare in the earliest consumer" precedent (RShared.cs/RMain.cs, steps
/// 2a/2b) they're already declared there and reused as-is.
/// </summary>
public static unsafe class RBsp
{
    private const int MAX_BMODEL_VERTS = 1000; // r_bsp.c -- Mark V (was 500)
    private const int MAX_BMODEL_EDGES = 3000; // r_bsp.c -- Mark V (was 1000)

    // entity_rotation[3][3] -- flat row-major 3x3, matching MathLib's
    // R_ConcatRotations convention (float* with index i*3+j).
    public static readonly float* entity_rotation = NativeMemory.AllocArrayZeroed<float>(9);

    // r_currentbkey
    public static int r_currentbkey;

    // pbverts, pbedges, numbverts, numbedges -- scratch for
    // R_RecursiveClipBPoly, pointed at R_DrawSolidClippedSubmodelPolygons's
    // bverts/bedges.
    internal static MVertex_t* pbverts;

    // R_DrawSolidClippedSubmodelPolygons: bverts[], bedges[] -- the original's
    // are stack locals; allocated once here to avoid zero-initializing ~30 KB
    // per call (and to keep pbverts/pbedges valid).
    internal static readonly MVertex_t* bverts = NativeMemory.AllocArray<MVertex_t>(MAX_BMODEL_VERTS);
    internal static readonly BEdge_t* bedges = NativeMemory.AllocArray<BEdge_t>(MAX_BMODEL_EDGES);
    internal static BEdge_t* pbedges;
    internal static int numbverts, numbedges;

    private static MVertex_t* pfrontenter, pfrontexit;

    private static bool makeclippededge;

    // btofpolys[MAX_BTOFPOLYS] -- the original is a per-call C stack array;
    // kept as a lazily-allocated native buffer instead (too large to want
    // repeated per-frame stackalloc, and fully overwritten every frame).
    private static BToFPoly_t* btofpolysBuf;

    /*
    ================
    R_EntityRotate
    ================
    */
    public static void EntityRotate(ref Vec3_t vec)
    {
        Vec3_t tvec = vec;
        vec[0] = entity_rotation[0] * tvec[0] + entity_rotation[1] * tvec[1] + entity_rotation[2] * tvec[2];
        vec[1] = entity_rotation[3] * tvec[0] + entity_rotation[4] * tvec[1] + entity_rotation[5] * tvec[2];
        vec[2] = entity_rotation[6] * tvec[0] + entity_rotation[7] * tvec[1] + entity_rotation[8] * tvec[2];
    }

    /*
    ================
    R_RotateBmodel
    ================
    */
    [SkipLocalsInit]
    public static void RotateBmodel()
    {
        // TODO (original comment): should use a look-up table
        // TODO (original comment): should really be stored with the entity instead of being reconstructed
        // TODO (original comment): could cache lazily, stored in the entity
        // TODO (original comment): share work with R_SetUpAliasTransform

        float* temp1 = stackalloc float[9];
        float* temp2 = stackalloc float[9];
        float* temp3 = stackalloc float[9];

        // yaw
        float angle = RShared.currententity->Angles[QuakeDef.YAW];
        angle = angle * MathLib.M_PI * 2 / 360;
        (float s, float c) = MathF.SinCos(angle);

        temp1[0 * 3 + 0] = c;
        temp1[0 * 3 + 1] = s;
        temp1[0 * 3 + 2] = 0;
        temp1[1 * 3 + 0] = -s;
        temp1[1 * 3 + 1] = c;
        temp1[1 * 3 + 2] = 0;
        temp1[2 * 3 + 0] = 0;
        temp1[2 * 3 + 1] = 0;
        temp1[2 * 3 + 2] = 1;

        // pitch
        angle = RShared.currententity->Angles[QuakeDef.PITCH];
        angle = angle * MathLib.M_PI * 2 / 360;
        (s, c) = MathF.SinCos(angle);

        temp2[0 * 3 + 0] = c;
        temp2[0 * 3 + 1] = 0;
        temp2[0 * 3 + 2] = -s;
        temp2[1 * 3 + 0] = 0;
        temp2[1 * 3 + 1] = 1;
        temp2[1 * 3 + 2] = 0;
        temp2[2 * 3 + 0] = s;
        temp2[2 * 3 + 1] = 0;
        temp2[2 * 3 + 2] = c;

        MathLib.R_ConcatRotations(temp2, temp1, temp3);

        // roll
        angle = RShared.currententity->Angles[QuakeDef.ROLL];
        angle = angle * MathLib.M_PI * 2 / 360;
        (s, c) = MathF.SinCos(angle);

        temp1[0 * 3 + 0] = 1;
        temp1[0 * 3 + 1] = 0;
        temp1[0 * 3 + 2] = 0;
        temp1[1 * 3 + 0] = 0;
        temp1[1 * 3 + 1] = c;
        temp1[1 * 3 + 2] = s;
        temp1[2 * 3 + 0] = 0;
        temp1[2 * 3 + 1] = -s;
        temp1[2 * 3 + 2] = c;

        MathLib.R_ConcatRotations(temp1, temp3, entity_rotation);

        // rotate modelorg and the transformation matrix
        EntityRotate(ref RShared.modelorg);
        EntityRotate(ref RShared.vpn);
        EntityRotate(ref RShared.vright);
        EntityRotate(ref RShared.vup);

        RMisc.TransformFrustum();
    }

    /*
    ================
    R_RecursiveClipBPoly
    ================
    */
    [SkipLocalsInit]
    public static void RecursiveClipBPoly(BEdge_t* pedges, MNode_t* pnode, MSurface_t* psurf)
    {
        BEdge_t** psideedges = stackalloc BEdge_t*[2];
        psideedges[0] = psideedges[1] = null;

        makeclippededge = false;

        // transform the BSP plane into model space
        // FIXME: cache these?
        MPlane_t* splitplane = pnode->Plane;
        float entDotNormal = Vector3.Dot(RMain.r_entorigin, splitplane->Normal);
        MPlane_t tplane = default;
        tplane.Dist = splitplane->Dist - entDotNormal;
        tplane.Normal[0] = entity_rotation[0] * splitplane->Normal[0] + entity_rotation[1] * splitplane->Normal[1] + entity_rotation[2] * splitplane->Normal[2];
        tplane.Normal[1] = entity_rotation[3] * splitplane->Normal[0] + entity_rotation[4] * splitplane->Normal[1] + entity_rotation[5] * splitplane->Normal[2];
        tplane.Normal[2] = entity_rotation[6] * splitplane->Normal[0] + entity_rotation[7] * splitplane->Normal[1] + entity_rotation[8] * splitplane->Normal[2];

        // clip edges to BSP plane
        while (pedges != null)
        {
            BEdge_t* pnextedge = pedges->PNext;

            // set the status for the last point as the previous point
            // FIXME: cache this stuff somehow?
            MVertex_t* plastvert = pedges->V0;
            float lastdist = Vector3.Dot(*plastvert, tplane.Normal) - tplane.Dist;

            int lastside = lastdist > 0 ? 0 : 1;

            MVertex_t* pvert = pedges->V1;
            float dist = Vector3.Dot(*pvert, tplane.Normal) - tplane.Dist;

            int side = dist > 0 ? 0 : 1;

            if (side != lastside)
            {
                // clipped
                if (numbverts >= MAX_BMODEL_VERTS)
                    return;

                // generate the clipped vertex
                float frac = lastdist / (lastdist - dist);
                MVertex_t* ptvert = &pbverts[numbverts++];

                *ptvert = *plastvert + frac * (*pvert - *plastvert);

                // split into two edges, one on each side, and remember
                // entering and exiting points
                // FIXME: share the clip edge by having a winding direction flag?
                if (numbedges >= MAX_BMODEL_EDGES - 1)
                {
                    Con.Printf("Out of edges for bmodel\n"u8);
                    return;
                }

                BEdge_t* ptedge = &pbedges[numbedges];
                ptedge->PNext = psideedges[lastside];
                psideedges[lastside] = ptedge;
                ptedge->V0 = plastvert;
                ptedge->V1 = ptvert;

                ptedge = &pbedges[numbedges + 1];
                ptedge->PNext = psideedges[side];
                psideedges[side] = ptedge;
                ptedge->V0 = ptvert;
                ptedge->V1 = pvert;

                numbedges += 2;

                if (side == 0)
                {
                    // entering for front, exiting for back
                    pfrontenter = ptvert;
                    makeclippededge = true;
                }
                else
                {
                    pfrontexit = ptvert;
                    makeclippededge = true;
                }
            }
            else
            {
                // add the edge to the appropriate side
                pedges->PNext = psideedges[side];
                psideedges[side] = pedges;
            }

            pedges = pnextedge;
        }

        // if anything was clipped, reconstitute and add the edges along the
        // clip plane to both sides (but in opposite directions)
        if (makeclippededge)
        {
            if (numbedges >= MAX_BMODEL_EDGES - 2)
            {
                Con.Printf("Out of edges for bmodel\n"u8);
                return;
            }

            BEdge_t* ptedge = &pbedges[numbedges];
            ptedge->PNext = psideedges[0];
            psideedges[0] = ptedge;
            ptedge->V0 = pfrontexit;
            ptedge->V1 = pfrontenter;

            ptedge = &pbedges[numbedges + 1];
            ptedge->PNext = psideedges[1];
            psideedges[1] = ptedge;
            ptedge->V0 = pfrontenter;
            ptedge->V1 = pfrontexit;

            numbedges += 2;
        }

        // draw or recurse further
        for (int i = 0; i < 2; i++)
        {
            if (psideedges[i] != null)
            {
                // draw if we've reached a non-solid leaf, done if all that's
                // left is a solid leaf, and continue down the tree if it's
                // not a leaf
                MNode_t* pn = pnode->Children[i];

                // we're done with this branch if the node or leaf isn't in the PVS
                if (pn->VisFrame == RMain.r_visframecount)
                {
                    if (pn->Contents < 0)
                    {
                        if (pn->Contents != (int)Contents.Solid)
                        {
                            r_currentbkey = ((MLeaf_t*)pn)->Key;
                            RDraw.RenderBmodelFace(psideedges[i], psurf);
                        }
                    }
                    else
                    {
                        RecursiveClipBPoly(psideedges[i], pnode->Children[i], psurf);
                    }
                }
            }
        }
    }

    /*
    ================
    R_DrawSolidClippedSubmodelPolygons
    ================
    */
    public static void DrawSolidClippedSubmodelPolygons(Model_t* pmodel)
    {
        // FIXME: use bounding-box-based frustum clipping info?

        MSurface_t* psurf = &pmodel->Surfaces[pmodel->FirstModelSurface];
        int numsurfaces = pmodel->NumModelSurfaces;
        MEdge_t* pedges = pmodel->Edges;

        for (int i = 0; i < numsurfaces; i++, psurf++)
        {
            // find which side of the node we are on
            MPlane_t* pplane = psurf->Plane;

            float dot = Vector3.Dot(RShared.modelorg, pplane->Normal) - pplane->Dist;

            // draw the polygon
            if ((psurf->Flags & Surf.PLANEBACK) != 0 && dot < -RLocal.BACKFACE_EPSILON ||
                (psurf->Flags & Surf.PLANEBACK) == 0 && dot > RLocal.BACKFACE_EPSILON)
            {
                // FIXME: use bounding-box-based frustum clipping info?

                // copy the edges to bedges, flipping if necessary so always
                // clockwise winding
                // FIXME: if edges and vertices get caches, these assignments
                // must move outside the loop, and overflow checking must be
                // done here
                pbverts = bverts;
                pbedges = bedges;
                numbverts = numbedges = 0;

                if (psurf->NumEdges > 0)
                {
                    BEdge_t* pbedge = &pbedges[numbedges];
                    numbedges += psurf->NumEdges;

                    int j;
                    for (j = 0; j < psurf->NumEdges; j++)
                    {
                        int lindex = pmodel->Surfedges[psurf->FirstEdge + j];

                        MEdge_t* pedge;
                        if (lindex > 0)
                        {
                            pedge = &pedges[lindex];
                            pbedge[j].V0 = &RMain.r_pcurrentvertbase[pedge->V[0]];
                            pbedge[j].V1 = &RMain.r_pcurrentvertbase[pedge->V[1]];
                        }
                        else
                        {
                            lindex = -lindex;
                            pedge = &pedges[lindex];
                            pbedge[j].V0 = &RMain.r_pcurrentvertbase[pedge->V[1]];
                            pbedge[j].V1 = &RMain.r_pcurrentvertbase[pedge->V[0]];
                        }

                        pbedge[j].PNext = &pbedge[j + 1];
                    }

                    pbedge[j - 1].PNext = null; // mark end of edges

                    RecursiveClipBPoly(pbedge, RShared.currententity->TopNode, psurf);
                }
                else
                {
                    Sys.Error("no edges in bmodel"u8);
                }
            }
        }
    }

    /*
    ================
    R_DrawSubmodelPolygons
    ================
    */
    public static void DrawSubmodelPolygons(Model_t* pmodel, int clipflags)
    {
        // FIXME: use bounding-box-based frustum clipping info?

        MSurface_t* psurf = &pmodel->Surfaces[pmodel->FirstModelSurface];
        int numsurfaces = pmodel->NumModelSurfaces;

        for (int i = 0; i < numsurfaces; i++, psurf++)
        {
            // find which side of the node we are on
            MPlane_t* pplane = psurf->Plane;

            float dot = Vector3.Dot(RShared.modelorg, pplane->Normal) - pplane->Dist;

            // draw the polygon
            if ((psurf->Flags & Surf.PLANEBACK) != 0 && dot < -RLocal.BACKFACE_EPSILON ||
                (psurf->Flags & Surf.PLANEBACK) == 0 && dot > RLocal.BACKFACE_EPSILON)
            {
                REdge.r_currentkey = ((MLeaf_t*)RShared.currententity->TopNode)->Key;

                // FIXME: use bounding-box-based frustum clipping info?
                RDraw.RenderFace(psurf, clipflags);
            }
        }
    }

    /*
    ================
    R_RecursiveWorldNode
    ================
    */
    public static void RecursiveWorldNode(MNode_t* node, int clipflags)
    {
        if (node->Contents == (int)Contents.Solid)
            return; // solid

        if (node->VisFrame != RMain.r_visframecount)
            return;

        // cull the clipping planes if not trivial accept
        if (clipflags != 0)
        {
            for (int i = 0; i < 4; i++)
            {
                if ((clipflags & (1 << i)) == 0)
                    continue; // don't need to clip against it

                // generate accept and reject points
                // FIXME: do with fast look-ups or integer tests based on the
                // sign bit of the floating point values
                int* pindex = RMain.pfrustum_indexes[i];

                Unsafe.SkipInit(out Vec3_t rejectpt);
                rejectpt[0] = node->MinMaxs[pindex[0]];
                rejectpt[1] = node->MinMaxs[pindex[1]];
                rejectpt[2] = node->MinMaxs[pindex[2]];

                double d = Vector3.Dot(rejectpt, RMain.view_clipplanes[i].Normal);
                d -= RMain.view_clipplanes[i].Dist;

                if (d <= 0)
                    return;

                Unsafe.SkipInit(out Vec3_t acceptpt);
                acceptpt[0] = node->MinMaxs[pindex[3 + 0]];
                acceptpt[1] = node->MinMaxs[pindex[3 + 1]];
                acceptpt[2] = node->MinMaxs[pindex[3 + 2]];

                d = Vector3.Dot(acceptpt, RMain.view_clipplanes[i].Normal);
                d -= RMain.view_clipplanes[i].Dist;

                if (d >= 0)
                    clipflags &= ~(1 << i); // node is entirely on screen
            }
        }

        // if a leaf node, draw stuff
        if (node->Contents < 0)
        {
            MLeaf_t* pleaf = (MLeaf_t*)node;

            MSurface_t** mark = pleaf->FirstMarkSurface;
            int c = pleaf->NumMarkSurfaces;

            if (c != 0)
            {
                do
                {
                    (*mark)->VisFrame = DIface.r_framecount;
                    mark++;
                } while (--c != 0);
            }

            // deal with model fragments in this leaf
            if (pleaf->EFrags != null)
            {
                REfrag.StoreEfrags(&pleaf->EFrags);
            }

            pleaf->Key = REdge.r_currentkey;
            REdge.r_currentkey++; // all bmodels in a leaf share the same key
        }
        else
        {
            // node is just a decision point, so go down the apropriate sides

            // find which side of the node we are on
            MPlane_t* plane = node->Plane;

            double dot;
            switch ((PlaneType)plane->Type)
            {
                case PlaneType.X:
                    dot = RShared.modelorg[0] - plane->Dist;
                    break;
                case PlaneType.Y:
                    dot = RShared.modelorg[1] - plane->Dist;
                    break;
                case PlaneType.Z:
                    dot = RShared.modelorg[2] - plane->Dist;
                    break;
                default:
                    dot = Vector3.Dot(RShared.modelorg, plane->Normal) - plane->Dist;
                    break;
            }

            int side = dot >= 0 ? 0 : 1;

            // recurse down the children, front side first
            RecursiveWorldNode(node->Children[side], clipflags);

            // draw stuff
            int c = node->NumSurfaces;

            if (c != 0)
            {
                MSurface_t* surf = Cl.worldmodel->Surfaces + node->FirstSurface;

                if (dot < -RLocal.BACKFACE_EPSILON)
                {
                    do
                    {
                        if ((surf->Flags & Surf.PLANEBACK) != 0 && surf->VisFrame == DIface.r_framecount)
                            DrawOrQueueSurface(surf, clipflags);

                        surf++;
                    } while (--c != 0);
                }
                else if (dot > RLocal.BACKFACE_EPSILON)
                {
                    do
                    {
                        if ((surf->Flags & Surf.PLANEBACK) == 0 && surf->VisFrame == DIface.r_framecount)
                            DrawOrQueueSurface(surf, clipflags);

                        surf++;
                    } while (--c != 0);
                }

                // all surfaces on the same node share the same sequence number
                REdge.r_currentkey++;
            }

            // recurse down the back side
            RecursiveWorldNode(node->Children[side == 0 ? 1 : 0], clipflags);
        }
    }

    private static void DrawOrQueueSurface(MSurface_t* surf, int clipflags)
    {
        if (DIface.r_drawpolys)
        {
            if (DIface.r_worldpolysbacktofront)
            {
                if (RMain.numbtofpolys < RLocal.MAX_BTOFPOLYS)
                {
                    RMain.pbtofpolys[RMain.numbtofpolys].ClipFlags = clipflags;
                    RMain.pbtofpolys[RMain.numbtofpolys].PSurf = surf;
                    RMain.numbtofpolys++;
                }
            }
            else
            {
                RDraw.RenderPoly(surf, clipflags);
            }
        }
        else
        {
            RDraw.RenderFace(surf, clipflags);
        }
    }

    /*
    ================
    R_RenderWorld
    ================
    */
    public static void RenderWorld()
    {
        if (btofpolysBuf == null)
            btofpolysBuf = NativeMemory.AllocArrayZeroed<BToFPoly_t>(RLocal.MAX_BTOFPOLYS);
        RMain.pbtofpolys = btofpolysBuf;

        RShared.currententity = ClEntities.cl_entities; // &cl_entities[0]
        RShared.modelorg = RShared.r_origin;
        Model_t* clmodel = RShared.currententity->Model;
        RMain.r_pcurrentvertbase = clmodel->Vertexes;

        RecursiveWorldNode(clmodel->Nodes, 15);

        // if the driver wants the polygons back to front, play the visible
        // ones back in that order
        if (DIface.r_worldpolysbacktofront)
        {
            for (int i = RMain.numbtofpolys - 1; i >= 0; i--)
            {
                RDraw.RenderPoly(RMain.pbtofpolys[i].PSurf, RMain.pbtofpolys[i].ClipFlags);
            }
        }
    }

}
