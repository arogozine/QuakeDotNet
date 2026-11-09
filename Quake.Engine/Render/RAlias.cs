// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// r_alias.c: routines for setting up to draw alias models -- transforms,
/// clips, and lights an entity's vertices, then hands off to DPolyse.cs's
/// (d_polyse.c) rasterizer.
///
/// R_AliasCheckBBox is textually defined in r_alias.c (confirmed by reading
/// the source directly); only R_AliasClipTriangle and its
/// R_Alias_clip_*/R_AliasClip helpers actually live in r_aclip.c.
/// </summary>
public static unsafe class RAlias
{
    private const int LIGHT_MIN = 5; // lowest light value we'll allow, to
                                     // avoid the need for inner-loop light clamping

    // ptriangles -- r_alias.c's own global; unused by this step's own code
    // (only ever read/written by r_aclip.c/d_polyse.c in the original), kept
    // for faithfulness to the source's global list.
    public static MTriangle_t* ptriangles;

    // r_affinetridesc, acolormap already live in DIface.cs -- reused.

    public static TriVertX_t* r_apverts;

    // aliaslerp_t (r_alias.c, Mark_V): a vertex blended between two poses. Only
    // filled while a model is mid-blend (r_alerping); otherwise r_apverts is used.
    public struct LerpVert_t
    {
        public Vec3_t V;            // blended (unscaled) vertex
        public byte LastLightNormal;
        public byte CurrLightNormal;
        public float Blend;
    }

    public static LerpVert_t* r_alerpverts = NativeMemory.AllocArray<LerpVert_t>(RLocal.MAXALIASVERTS);
    public static bool r_alerping;

    // TODO (original comment): these probably will go away with optimized rasterization
    public static Mdl_t* pmdl;
    public static Vec3_t r_plightvec;
    public static int r_ambientlight;
    public static float r_shadelight;
    public static AliasHdr_t* paliashdr;
    public static FinalVert_t* pfinalverts;
    public static AuxVert_t* pauxverts;
    private static float ziscale;
    private static Model_t* pmodel;

    private static Vec3_t alias_forward, alias_right, alias_up;

    private static MAliasSkinDesc_t* pskindesc;

    // r_amodels_drawn already lives in RMain.cs (r_main.c's own global,
    // step 2b) -- reused, not redeclared.
    public static int a_skinwidth;
    public static int r_anumverts;

    // aliastransform[3][4] -- flat row-major, same convention as RBsp.cs's
    // entity_rotation (persists across calls between SetUpTransform and the
    // per-vertex transform functions, so a native buffer rather than a
    // stackalloc local).
    public static readonly float* aliastransform = NativeMemory.AllocArrayZeroed<float>(3 * 4);

    private struct AEdge_t
    {
        public int Index0, Index1;
    }

    private static readonly AEdge_t[] aedges =
    {
        new() { Index0 = 0, Index1 = 1 }, new() { Index0 = 1, Index1 = 2 },
        new() { Index0 = 2, Index1 = 3 }, new() { Index0 = 3, Index1 = 0 },
        new() { Index0 = 4, Index1 = 5 }, new() { Index0 = 5, Index1 = 6 },
        new() { Index0 = 6, Index1 = 7 }, new() { Index0 = 7, Index1 = 4 },
        new() { Index0 = 0, Index1 = 5 }, new() { Index0 = 1, Index1 = 4 },
        new() { Index0 = 2, Index1 = 7 }, new() { Index0 = 3, Index1 = 6 },
    };

    private const int NUMVERTEXNORMALS = 162;

    // r_avertexnormals[NUMVERTEXNORMALS][3] -- from anorms.h, embedded verbatim.
    public static readonly Vector3* r_avertexnormals = AllocVertexNormals();

    private static Vector3* AllocVertexNormals()
    {
        ReadOnlySpan<Vector3> anorms =
        [
            new(-0.525731f, 0.000000f, 0.850651f), new(-0.442863f, 0.238856f, 0.864188f),
            new(-0.295242f, 0.000000f, 0.955423f), new(-0.309017f, 0.500000f, 0.809017f),
            new(-0.162460f, 0.262866f, 0.951056f), new(0.000000f, 0.000000f, 1.000000f),
            new(0.000000f, 0.850651f, 0.525731f), new(-0.147621f, 0.716567f, 0.681718f),
            new(0.147621f, 0.716567f, 0.681718f), new(0.000000f, 0.525731f, 0.850651f),
            new(0.309017f, 0.500000f, 0.809017f), new(0.525731f, 0.000000f, 0.850651f),
            new(0.295242f, 0.000000f, 0.955423f), new(0.442863f, 0.238856f, 0.864188f),
            new(0.162460f, 0.262866f, 0.951056f), new(-0.681718f, 0.147621f, 0.716567f),
            new(-0.809017f, 0.309017f, 0.500000f), new(-0.587785f, 0.425325f, 0.688191f),
            new(-0.850651f, 0.525731f, 0.000000f), new(-0.864188f, 0.442863f, 0.238856f),
            new(-0.716567f, 0.681718f, 0.147621f), new(-0.688191f, 0.587785f, 0.425325f),
            new(-0.500000f, 0.809017f, 0.309017f), new(-0.238856f, 0.864188f, 0.442863f),
            new(-0.425325f, 0.688191f, 0.587785f), new(-0.716567f, 0.681718f, -0.147621f),
            new(-0.500000f, 0.809017f, -0.309017f), new(-0.525731f, 0.850651f, 0.000000f),
            new(0.000000f, 0.850651f, -0.525731f), new(-0.238856f, 0.864188f, -0.442863f),
            new(0.000000f, 0.955423f, -0.295242f), new(-0.262866f, 0.951056f, -0.162460f),
            new(0.000000f, 1.000000f, 0.000000f), new(0.000000f, 0.955423f, 0.295242f),
            new(-0.262866f, 0.951056f, 0.162460f), new(0.238856f, 0.864188f, 0.442863f),
            new(0.262866f, 0.951056f, 0.162460f), new(0.500000f, 0.809017f, 0.309017f),
            new(0.238856f, 0.864188f, -0.442863f), new(0.262866f, 0.951056f, -0.162460f),
            new(0.500000f, 0.809017f, -0.309017f), new(0.850651f, 0.525731f, 0.000000f),
            new(0.716567f, 0.681718f, 0.147621f), new(0.716567f, 0.681718f, -0.147621f),
            new(0.525731f, 0.850651f, 0.000000f), new(0.425325f, 0.688191f, 0.587785f),
            new(0.864188f, 0.442863f, 0.238856f), new(0.688191f, 0.587785f, 0.425325f),
            new(0.809017f, 0.309017f, 0.500000f), new(0.681718f, 0.147621f, 0.716567f),
            new(0.587785f, 0.425325f, 0.688191f), new(0.955423f, 0.295242f, 0.000000f),
            new(1.000000f, 0.000000f, 0.000000f), new(0.951056f, 0.162460f, 0.262866f),
            new(0.850651f, -0.525731f, 0.000000f), new(0.955423f, -0.295242f, 0.000000f),
            new(0.864188f, -0.442863f, 0.238856f), new(0.951056f, -0.162460f, 0.262866f),
            new(0.809017f, -0.309017f, 0.500000f), new(0.681718f, -0.147621f, 0.716567f),
            new(0.850651f, 0.000000f, 0.525731f), new(0.864188f, 0.442863f, -0.238856f),
            new(0.809017f, 0.309017f, -0.500000f), new(0.951056f, 0.162460f, -0.262866f),
            new(0.525731f, 0.000000f, -0.850651f), new(0.681718f, 0.147621f, -0.716567f),
            new(0.681718f, -0.147621f, -0.716567f), new(0.850651f, 0.000000f, -0.525731f),
            new(0.809017f, -0.309017f, -0.500000f), new(0.864188f, -0.442863f, -0.238856f),
            new(0.951056f, -0.162460f, -0.262866f), new(0.147621f, 0.716567f, -0.681718f),
            new(0.309017f, 0.500000f, -0.809017f), new(0.425325f, 0.688191f, -0.587785f),
            new(0.442863f, 0.238856f, -0.864188f), new(0.587785f, 0.425325f, -0.688191f),
            new(0.688191f, 0.587785f, -0.425325f), new(-0.147621f, 0.716567f, -0.681718f),
            new(-0.309017f, 0.500000f, -0.809017f), new(0.000000f, 0.525731f, -0.850651f),
            new(-0.525731f, 0.000000f, -0.850651f), new(-0.442863f, 0.238856f, -0.864188f),
            new(-0.295242f, 0.000000f, -0.955423f), new(-0.162460f, 0.262866f, -0.951056f),
            new(0.000000f, 0.000000f, -1.000000f), new(0.295242f, 0.000000f, -0.955423f),
            new(0.162460f, 0.262866f, -0.951056f), new(-0.442863f, -0.238856f, -0.864188f),
            new(-0.309017f, -0.500000f, -0.809017f), new(-0.162460f, -0.262866f, -0.951056f),
            new(0.000000f, -0.850651f, -0.525731f), new(-0.147621f, -0.716567f, -0.681718f),
            new(0.147621f, -0.716567f, -0.681718f), new(0.000000f, -0.525731f, -0.850651f),
            new(0.309017f, -0.500000f, -0.809017f), new(0.442863f, -0.238856f, -0.864188f),
            new(0.162460f, -0.262866f, -0.951056f), new(0.238856f, -0.864188f, -0.442863f),
            new(0.500000f, -0.809017f, -0.309017f), new(0.425325f, -0.688191f, -0.587785f),
            new(0.716567f, -0.681718f, -0.147621f), new(0.688191f, -0.587785f, -0.425325f),
            new(0.587785f, -0.425325f, -0.688191f), new(0.000000f, -0.955423f, -0.295242f),
            new(0.000000f, -1.000000f, 0.000000f), new(0.262866f, -0.951056f, -0.162460f),
            new(0.000000f, -0.850651f, 0.525731f), new(0.000000f, -0.955423f, 0.295242f),
            new(0.238856f, -0.864188f, 0.442863f), new(0.262866f, -0.951056f, 0.162460f),
            new(0.500000f, -0.809017f, 0.309017f), new(0.716567f, -0.681718f, 0.147621f),
            new(0.525731f, -0.850651f, 0.000000f), new(-0.238856f, -0.864188f, -0.442863f),
            new(-0.500000f, -0.809017f, -0.309017f), new(-0.262866f, -0.951056f, -0.162460f),
            new(-0.850651f, -0.525731f, 0.000000f), new(-0.716567f, -0.681718f, -0.147621f),
            new(-0.716567f, -0.681718f, 0.147621f), new(-0.525731f, -0.850651f, 0.000000f),
            new(-0.500000f, -0.809017f, 0.309017f), new(-0.238856f, -0.864188f, 0.442863f),
            new(-0.262866f, -0.951056f, 0.162460f), new(-0.864188f, -0.442863f, 0.238856f),
            new(-0.809017f, -0.309017f, 0.500000f), new(-0.688191f, -0.587785f, 0.425325f),
            new(-0.681718f, -0.147621f, 0.716567f), new(-0.442863f, -0.238856f, 0.864188f),
            new(-0.587785f, -0.425325f, 0.688191f), new(-0.309017f, -0.500000f, 0.809017f),
            new(-0.147621f, -0.716567f, 0.681718f), new(-0.425325f, -0.688191f, 0.587785f),
            new(-0.162460f, -0.262866f, 0.951056f), new(0.442863f, -0.238856f, 0.864188f),
            new(0.162460f, -0.262866f, 0.951056f), new(0.309017f, -0.500000f, 0.809017f),
            new(0.147621f, -0.716567f, 0.681718f), new(0.000000f, -0.525731f, 0.850651f),
            new(0.425325f, -0.688191f, 0.587785f), new(0.587785f, -0.425325f, 0.688191f),
            new(0.688191f, -0.587785f, 0.425325f), new(-0.955423f, 0.295242f, 0.000000f),
            new(-0.951056f, 0.162460f, 0.262866f), new(-1.000000f, 0.000000f, 0.000000f),
            new(-0.850651f, 0.000000f, 0.525731f), new(-0.955423f, -0.295242f, 0.000000f),
            new(-0.951056f, -0.162460f, 0.262866f), new(-0.864188f, 0.442863f, -0.238856f),
            new(-0.951056f, 0.162460f, -0.262866f), new(-0.809017f, 0.309017f, -0.500000f),
            new(-0.864188f, -0.442863f, -0.238856f), new(-0.951056f, -0.162460f, -0.262866f),
            new(-0.809017f, -0.309017f, -0.500000f), new(-0.681718f, 0.147621f, -0.716567f),
            new(-0.681718f, -0.147621f, -0.716567f), new(-0.850651f, 0.000000f, -0.525731f),
            new(-0.688191f, 0.587785f, -0.425325f), new(-0.587785f, 0.425325f, -0.688191f),
            new(-0.425325f, 0.688191f, -0.587785f), new(-0.425325f, -0.688191f, -0.587785f),
            new(-0.587785f, -0.425325f, -0.688191f), new(-0.688191f, -0.587785f, -0.425325f),
        ];

        Vector3* normals = NativeMemory.AllocArray<Vector3>(NUMVERTEXNORMALS);
        anorms.CopyTo(new Span<Vector3>(normals, NUMVERTEXNORMALS));
        return normals;
    }

    // R_AliasCheckBBox
    public static bool AliasCheckBBox()
    {
        Entity_t* currententity = RShared.currententity;
        currententity->TrivialAccept = 0;
        pmodel = currententity->Model;
        AliasHdr_t* pahdr = (AliasHdr_t*)Model.Extradata(pmodel);
        pmdl = (Mdl_t*)((byte*)pahdr + pahdr->Model);

        SetUpTransform(0);

        // construct the base bounding box for this frame
        int frame = currententity->Frame;
        // TODO (original comment): don't repeat this check when drawing?
        if (frame >= pmdl->NumFrames || frame < 0)
        {
            Con.DPrintf($"No such frame {frame} {pmodel->Name}\n");
            frame = 0;
        }

        MAliasFrameDesc_t* pframedesc = &pahdr->Frames + frame;

        float* basepts = stackalloc float[8 * 3];

        // x worldspace coordinates
        basepts[0 * 3 + 0] = basepts[1 * 3 + 0] = basepts[2 * 3 + 0] = basepts[3 * 3 + 0] = pframedesc->BboxMin.V[0];
        basepts[4 * 3 + 0] = basepts[5 * 3 + 0] = basepts[6 * 3 + 0] = basepts[7 * 3 + 0] = pframedesc->BboxMax.V[0];

        // y worldspace coordinates
        basepts[0 * 3 + 1] = basepts[3 * 3 + 1] = basepts[5 * 3 + 1] = basepts[6 * 3 + 1] = pframedesc->BboxMin.V[1];
        basepts[1 * 3 + 1] = basepts[2 * 3 + 1] = basepts[4 * 3 + 1] = basepts[7 * 3 + 1] = pframedesc->BboxMax.V[1];

        // z worldspace coordinates
        basepts[0 * 3 + 2] = basepts[1 * 3 + 2] = basepts[4 * 3 + 2] = basepts[5 * 3 + 2] = pframedesc->BboxMin.V[2];
        basepts[2 * 3 + 2] = basepts[3 * 3 + 2] = basepts[6 * 3 + 2] = basepts[7 * 3 + 2] = pframedesc->BboxMax.V[2];

        bool zclipped = false;
        bool zfullyclipped = true;

        int minz = 9999;

        FinalVert_t* viewpts = stackalloc FinalVert_t[16];
        AuxVert_t* viewaux = stackalloc AuxVert_t[16];

        for (int i = 0; i < 8; i++)
        {
            TransformVector(&basepts[i * 3], &viewaux[i]);

            float vz = viewaux[i].Z;

            if (vz < RLocal.ALIAS_Z_CLIP_PLANE)
            {
                // we must clip points that are closer than the near clip plane
                viewpts[i].Flags = DIface.ALIAS_Z_CLIP;
                zclipped = true;
            }
            else
            {
                if (vz < minz)
                    minz = (int)vz;
                viewpts[i].Flags = 0;
                zfullyclipped = false;
            }
        }

        if (zfullyclipped)
            return false; // everything was near-z-clipped

        int numv = 8;

        if (zclipped)
        {
            // organize points by edges, use edges to get new points (possible trivial reject)
            for (int i = 0; i < 12; i++)
            {
                // edge endpoints
                FinalVert_t* pv0 = &viewpts[aedges[i].Index0];
                FinalVert_t* pv1 = &viewpts[aedges[i].Index1];
                AuxVert_t* pa0 = &viewaux[aedges[i].Index0];
                AuxVert_t* pa1 = &viewaux[aedges[i].Index1];

                // if one end is clipped and the other isn't, make a new point
                if ((pv0->Flags ^ pv1->Flags) != 0)
                {
                    float frac = (RLocal.ALIAS_Z_CLIP_PLANE - pa0->Z) / (pa1->Z - pa0->Z);
                    Vector3 interp = *pa0 + (*pa1 - *pa0) * frac;
                    interp.Z = RLocal.ALIAS_Z_CLIP_PLANE;
                    viewaux[numv] = interp;
                    viewpts[numv].Flags = 0;
                    numv++;
                }
            }
        }

        // project the vertices that remain after clipping
        int anyclip = 0;
        int allclip = DIface.ALIAS_XY_CLIP_MASK;

        // TODO (original comment): probably should do this loop in ASM, especially if we use floats
        for (int i = 0; i < numv; i++)
        {
            // we don't need to bother with vertices that were z-clipped
            if ((viewpts[i].Flags & DIface.ALIAS_Z_CLIP) != 0)
                continue;

            Vector3 va = viewaux[i];
            float zi = 1.0f / va.Z;

            // FIXME (original comment): do with chop mode in ASM, or convert to float
            float v0 = va.X * RMain.aliasxscale * zi + RMain.aliasxcenter;
            float v1 = va.Y * RMain.aliasyscale * zi + RMain.aliasycenter;

            int flags = 0;

            if (v0 < RShared.r_refdef->FVrectx)
                flags |= DIface.ALIAS_LEFT_CLIP;
            if (v1 < RShared.r_refdef->FVrecty)
                flags |= DIface.ALIAS_TOP_CLIP;
            if (v0 > RShared.r_refdef->FVrectRight)
                flags |= DIface.ALIAS_RIGHT_CLIP;
            if (v1 > RShared.r_refdef->FVrectBottom)
                flags |= DIface.ALIAS_BOTTOM_CLIP;

            anyclip |= flags;
            allclip &= flags;
        }

        if (allclip != 0)
            return false; // trivial reject off one side

        currententity->TrivialAccept = (anyclip == 0 ? 1 : 0) & (zclipped ? 0 : 1);

        if (currententity->TrivialAccept != 0)
        {
            if (minz > RMain.r_aliastransition + pmdl->Size * RMain.r_resfudge)
                currententity->TrivialAccept |= 2;
        }

        return true;
    }

    // R_AliasTransformVector
    public static void TransformVector(float* in_, AuxVert_t* out_)
    {
        float* aliastransform = RAlias.aliastransform;
        float i0 = in_[0], i1 = in_[1], i2 = in_[2];

        *out_ = new Vector3(
            i0 * aliastransform[0] + i1 * aliastransform[1] + i2 * aliastransform[2] + aliastransform[3],
            i0 * aliastransform[4] + i1 * aliastransform[5] + i2 * aliastransform[6] + aliastransform[7],
            i0 * aliastransform[8] + i1 * aliastransform[9] + i2 * aliastransform[10] + aliastransform[11]);
    }

    // R_AliasPreparePoints
    // General clipped case
    public static void PreparePoints()
    {
        StVert_t* pstverts = (StVert_t*)((byte*)paliashdr + paliashdr->StVerts);
        r_anumverts = pmdl->NumVerts;
        FinalVert_t* fv = pfinalverts;
        AuxVert_t* av = pauxverts;

        LerpVert_t* lv = r_alerpverts;
        for (int i = 0; i < r_anumverts; i++, fv++, av++, r_apverts++, lv++, pstverts++)
        {
            if (r_alerping)
                TransformFinalVertLerp(fv, av, lv, pstverts);
            else
                TransformFinalVert(fv, av, r_apverts, pstverts);
            if (av->Z < RLocal.ALIAS_Z_CLIP_PLANE)
                fv->Flags |= DIface.ALIAS_Z_CLIP;
            else
            {
                ProjectFinalVert(fv, av);

                if (fv->V[0] < RShared.r_refdef->AliasVrect.X)
                    fv->Flags |= DIface.ALIAS_LEFT_CLIP;
                if (fv->V[1] < RShared.r_refdef->AliasVrect.Y)
                    fv->Flags |= DIface.ALIAS_TOP_CLIP;
                if (fv->V[0] > RShared.r_refdef->AliasVrectRight)
                    fv->Flags |= DIface.ALIAS_RIGHT_CLIP;
                if (fv->V[1] > RShared.r_refdef->AliasVrectBottom)
                    fv->Flags |= DIface.ALIAS_BOTTOM_CLIP;
            }
        }

        // clip and draw all triangles
        DIface.r_affinetridesc.NumTriangles = 1;

        MTriangle_t* ptri = (MTriangle_t*)((byte*)paliashdr + paliashdr->Triangles);
        for (int i = 0; i < pmdl->NumTris; i++, ptri++)
        {
            FinalVert_t* pfv0 = &pfinalverts[ptri->VertIndex[0]];
            FinalVert_t* pfv1 = &pfinalverts[ptri->VertIndex[1]];
            FinalVert_t* pfv2 = &pfinalverts[ptri->VertIndex[2]];

            if ((pfv0->Flags & pfv1->Flags & pfv2->Flags & (DIface.ALIAS_XY_CLIP_MASK | DIface.ALIAS_Z_CLIP)) != 0)
                continue; // completely clipped

            if (((pfv0->Flags | pfv1->Flags | pfv2->Flags) & (DIface.ALIAS_XY_CLIP_MASK | DIface.ALIAS_Z_CLIP)) == 0)
            {
                // totally unclipped
                DIface.r_affinetridesc.PFinalVerts = pfinalverts;
                DIface.r_affinetridesc.PTriangles = ptri;
                DPolyse.PolysetDraw();
            }
            else
            {
                // partially clipped
                RAclip.ClipTriangle(ptri);
            }
        }
    }

    // R_AliasSetUpTransform
    public static void SetUpTransform(int trivial_accept)
    {
        Entity_t* currententity = RShared.currententity;

        // TODO (original comment): should really be stored with the entity instead of being reconstructed
        // TODO (original comment): should use a look-up table
        // TODO (original comment): could cache lazily, stored in the entity
        Vec3_t angles = default;
        angles[QuakeDef.ROLL] = currententity->Angles[QuakeDef.ROLL];
        angles[QuakeDef.PITCH] = -currententity->Angles[QuakeDef.PITCH];
        angles[QuakeDef.YAW] = currententity->Angles[QuakeDef.YAW];
        MathLib.AngleVectors(angles, out alias_forward, out alias_right, out alias_up);

        // tmatrix[3][4], viewmatrix[3][4] -- static in the original (values
        // fully overwritten each call for the entries actually used; the
        // rest are always 0), matched here by a zero-initialized stackalloc
        // rather than a persistent field, since C# stackalloc zero-inits by
        // default and every entry either gets set below or stays 0 in both
        // versions.
        float* tmatrix = stackalloc float[3 * 4];
        float* viewmatrix = stackalloc float[3 * 4];
        float* rotationmatrix = stackalloc float[3 * 4];
        float* t2matrix = stackalloc float[3 * 4];

        tmatrix[0 * 4 + 0] = pmdl->Scale[0];
        tmatrix[1 * 4 + 1] = pmdl->Scale[1];
        tmatrix[2 * 4 + 2] = pmdl->Scale[2];

        tmatrix[0 * 4 + 3] = pmdl->ScaleOrigin[0];
        tmatrix[1 * 4 + 3] = pmdl->ScaleOrigin[1];
        tmatrix[2 * 4 + 3] = pmdl->ScaleOrigin[2];

        // TODO (original comment): can do this with simple matrix rearrangement
        for (int i = 0; i < 3; i++)
        {
            t2matrix[i * 4 + 0] = alias_forward[i];
            t2matrix[i * 4 + 1] = -alias_right[i];
            t2matrix[i * 4 + 2] = alias_up[i];
        }

        t2matrix[0 * 4 + 3] = -RShared.modelorg[0];
        t2matrix[1 * 4 + 3] = -RShared.modelorg[1];
        t2matrix[2 * 4 + 3] = -RShared.modelorg[2];

        // FIXME (original comment): can do more efficiently than full concatenation
        MathLib.R_ConcatTransforms(t2matrix, tmatrix, rotationmatrix);

        // TODO (original comment): should be global, set when vright, etc., set
        Unsafe3(viewmatrix, 0) = RShared.vright;
        Unsafe3(viewmatrix, 4) = RShared.vup;
        Unsafe3(viewmatrix, 4) = -Unsafe3(viewmatrix, 4);
        Unsafe3(viewmatrix, 8) = RShared.vpn;

        // viewmatrix[0][3] = 0; viewmatrix[1][3] = 0; viewmatrix[2][3] = 0;
        // (already 0 from the zero-initialized stackalloc)

        MathLib.R_ConcatTransforms(viewmatrix, rotationmatrix, aliastransform);

        // do the scaling up of x and y to screen coordinates as part of the transform
        // for the unclipped case (it would mess up clipping in the clipped case).
        // Also scale down z, so 1/z is scaled 31 bits for free, and scale down x and y
        // correspondingly so the projected x and y come out right
        // FIXME (original comment): make this work for clipped case too?
        if (trivial_accept != 0)
        {
            for (int i = 0; i < 4; i++)
            {
                aliastransform[0 * 4 + i] *= RMain.aliasxscale * (1.0f / ((float)0x8000 * 0x10000));
                aliastransform[1 * 4 + i] *= RMain.aliasyscale * (1.0f / ((float)0x8000 * 0x10000));
                aliastransform[2 * 4 + i] *= 1.0f / ((float)0x8000 * 0x10000);
            }
        }
    }

    // Helper: reinterprets a 3-float slice of a flat row-major matrix buffer
    // as a Vec3_t for MathLib's VectorCopy/VectorInverse (which take
    // ref Vec3_t) -- a thin view, not a copy, since Vec3_t is layout-
    // compatible with three consecutive floats.
    private static ref Vec3_t Unsafe3(float* buf, int offset) =>
        ref Unsafe.AsRef<Vec3_t>(buf + offset);

    // R_AliasTransformFinalVert
    public static void TransformFinalVert(FinalVert_t* fv, AuxVert_t* av, TriVertX_t* pverts, StVert_t* pstverts)
    {
        float* aliastransform = RAlias.aliastransform;

        // locals rather than a per-vertex stackalloc (which zero-inits and
        // adds a stack-guard check on every call)
        float v0 = pverts->V[0], v1 = pverts->V[1], v2 = pverts->V[2];

        *av = new Vector3(
            v0 * aliastransform[0] + v1 * aliastransform[1] + v2 * aliastransform[2] + aliastransform[3],
            v0 * aliastransform[4] + v1 * aliastransform[5] + v2 * aliastransform[6] + aliastransform[7],
            v0 * aliastransform[8] + v1 * aliastransform[9] + v2 * aliastransform[10] + aliastransform[11]);

        fv->V[2] = pstverts->S;
        fv->V[3] = pstverts->T;

        fv->Flags = pstverts->OnSeam;

        // lighting
        float lightcos = Vector3.Dot(r_avertexnormals[pverts->LightNormalIndex], r_plightvec);
        int temp = r_ambientlight;

        if (lightcos < 0)
        {
            temp += (int)(r_shadelight * lightcos);

            // clamp; because we limited the minimum ambient and shading light, we
            // don't have to clamp low light, just bright
            if (temp < 0)
                temp = 0;
        }

        fv->V[4] = temp;
    }

    // R_AliasTransformFinalVertMH (r_alias.c, Mark_V): TransformFinalVert for a blended vertex.
    // Lighting blends the two poses' light normals, like Mark_V's R_AliasLightVert.
    public static void TransformFinalVertLerp(FinalVert_t* fv, AuxVert_t* av, LerpVert_t* pverts, StVert_t* pstverts)
    {
        float* aliastransform = RAlias.aliastransform;
        float v0 = pverts->V.X, v1 = pverts->V.Y, v2 = pverts->V.Z;

        *av = new Vector3(
            v0 * aliastransform[0] + v1 * aliastransform[1] + v2 * aliastransform[2] + aliastransform[3],
            v0 * aliastransform[4] + v1 * aliastransform[5] + v2 * aliastransform[6] + aliastransform[7],
            v0 * aliastransform[8] + v1 * aliastransform[9] + v2 * aliastransform[10] + aliastransform[11]);

        fv->V[2] = pstverts->S;
        fv->V[3] = pstverts->T;
        fv->Flags = pstverts->OnSeam;

        fv->V[4] = LightLerp(pverts);
    }

    private static int LightLerp(LerpVert_t* pverts)
    {
        // R_AliasLightVert (r_alias.c, Mark_V): each pose's shading is applied and
        // clamped separately
        int temp = r_ambientlight;

        float lightcos = Vector3.Dot(r_avertexnormals[pverts->CurrLightNormal], r_plightvec) * pverts->Blend;
        if (lightcos < 0)
        {
            temp += (int)(r_shadelight * lightcos);
            if (temp < 0)
                temp = 0;
        }

        lightcos = Vector3.Dot(r_avertexnormals[pverts->LastLightNormal], r_plightvec) * (1.0f - pverts->Blend);
        if (lightcos < 0)
        {
            temp += (int)(r_shadelight * lightcos);
            if (temp < 0)
                temp = 0;
        }

        return temp;
    }

    // R_AliasTransformAndProjectFinalVerts
    public static void TransformAndProjectFinalVerts(FinalVert_t* fv, StVert_t* pstverts)
    {
        TriVertX_t* pverts = r_apverts;
        LerpVert_t* lverts = r_alerpverts;
        bool lerping = r_alerping;

        // aliastransform's columns, gathered once per model so each vertex is
        // transformed as v0 * col0 + v1 * col1 + v2 * col2 + col3 -- per lane
        // that's the original's left-to-right v0*a + v1*b + v2*c + d, so the
        // result is bit-identical (no FMA contraction) at a third of the
        // multiplies/adds
        Vector3 col0 = new(aliastransform[0], aliastransform[4], aliastransform[8]);
        Vector3 col1 = new(aliastransform[1], aliastransform[5], aliastransform[9]);
        Vector3 col2 = new(aliastransform[2], aliastransform[6], aliastransform[10]);
        Vector3 col3 = new(aliastransform[3], aliastransform[7], aliastransform[11]);
        float xcenter = RMain.aliasxcenter;
        float ycenter = RMain.aliasycenter;

        for (int i = 0; i < r_anumverts; i++, fv++, pverts++, lverts++, pstverts++)
        {
            // transform and project
            Vector3 t = lerping
                ? lverts->V.X * col0 + lverts->V.Y * col1 + lverts->V.Z * col2 + col3
                : pverts->V[0] * col0 + pverts->V[1] * col1 + pverts->V[2] * col2 + col3;
            float zi = 1.0f / t.Z;

            // x, y, and z are scaled down by 1/2**31 in the transform, so 1/z is
            // scaled up by 1/2**31, and the scaling cancels out for x and y in the
            // projection
            fv->V[5] = (int)zi;

            fv->V[0] = (int)((t.X * zi) + xcenter);
            fv->V[1] = (int)((t.Y * zi) + ycenter);

            fv->V[2] = pstverts->S;
            fv->V[3] = pstverts->T;
            fv->Flags = pstverts->OnSeam;

            // lighting
            if (lerping)
            {
                fv->V[4] = LightLerp(lverts);
                continue;
            }

            float lightcos = Vector3.Dot(r_avertexnormals[pverts->LightNormalIndex], r_plightvec);
            int temp = r_ambientlight;

            if (lightcos < 0)
            {
                temp += (int)(r_shadelight * lightcos);

                if (temp < 0)
                    temp = 0;
            }

            fv->V[4] = temp;
        }
    }

    // R_AliasProjectFinalVert
    public static void ProjectFinalVert(FinalVert_t* fv, AuxVert_t* av)
    {
        // project points
        float zi = 1.0f / av->Z;

        fv->V[5] = float.ConvertToIntegerNative<int>(zi * ziscale);

        fv->V[0] = float.ConvertToIntegerNative<int>(av->X * RMain.aliasxscale * zi + RMain.aliasxcenter);
        fv->V[1] = float.ConvertToIntegerNative<int>(av->Y * RMain.aliasyscale * zi + RMain.aliasycenter);
    }

    // R_AliasPrepareUnclippedPoints
    public static void PrepareUnclippedPoints()
    {
        StVert_t* pstverts = (StVert_t*)((byte*)paliashdr + paliashdr->StVerts);
        r_anumverts = pmdl->NumVerts;
        // FIXME (original comment): just use pfinalverts directly?
        FinalVert_t* fv = pfinalverts;

        TransformAndProjectFinalVerts(fv, pstverts);

        if (DIface.r_affinetridesc.DrawType != 0)
            DPolyse.PolysetDrawFinalVerts(fv, r_anumverts);

        DIface.r_affinetridesc.PFinalVerts = pfinalverts;
        DIface.r_affinetridesc.PTriangles = (MTriangle_t*)((byte*)paliashdr + paliashdr->Triangles);
        DIface.r_affinetridesc.NumTriangles = pmdl->NumTris;

        DPolyse.PolysetDraw();
    }

    // R_AliasSetupSkin
    public static void SetupSkin()
    {
        Entity_t* currententity = RShared.currententity;

        int skinnum = currententity->SkinNum;
        if (skinnum >= pmdl->NumSkins || skinnum < 0)
        {
            Con.DPrintf($"R_AliasSetupSkin: no such skin # {skinnum}\n");
            skinnum = 0;
        }

        pskindesc = (MAliasSkinDesc_t*)((byte*)paliashdr + paliashdr->SkinDesc) + skinnum;
        a_skinwidth = pmdl->SkinWidth;

        if (pskindesc->Type == AliasSkinType_t.Group)
        {
            MAliasSkinGroup_t* paliasskingroup = (MAliasSkinGroup_t*)((byte*)paliashdr + pskindesc->Skin);
            float* pskinintervals = (float*)((byte*)paliashdr + paliasskingroup->Intervals);
            int numskins = paliasskingroup->NumSkins;
            float fullskininterval = pskinintervals[numskins - 1];

            double skintime = Cl.time + currententity->SyncBase;

            // when loading in Mod_LoadAliasSkinGroup, we guaranteed all interval
            // values are positive, so we don't have to worry about division by 0
            float skintargettime = (float)(skintime - (int)(skintime / fullskininterval) * fullskininterval);

            int i;
            for (i = 0; i < numskins - 1; i++)
            {
                if (pskinintervals[i] > skintargettime)
                    break;
            }

            MAliasSkinDesc_t* skindescs = &paliasskingroup->SkinDescs;
            pskindesc = &skindescs[i];
        }

        DIface.r_affinetridesc.PSkinDesc = pskindesc;
        DIface.r_affinetridesc.PSkin = (byte*)paliashdr + pskindesc->Skin;
        DIface.r_affinetridesc.SkinWidth = a_skinwidth;
        DIface.r_affinetridesc.SeamFixupX16 = (a_skinwidth >> 1) << 16;
        DIface.r_affinetridesc.SkinHeight = pmdl->SkinHeight;
    }

    // R_AliasSetupLighting
    public static void SetupLighting(ALight_t* plighting)
    {
        // guarantee that no vertex will ever be lit below LIGHT_MIN, so we don't have
        // to clamp off the bottom
        r_ambientlight = plighting->AmbientLight;

        if (r_ambientlight < LIGHT_MIN)
            r_ambientlight = LIGHT_MIN;

        r_ambientlight = (255 - r_ambientlight) << Vid.VID_CBITS;

        if (r_ambientlight < LIGHT_MIN)
            r_ambientlight = LIGHT_MIN;

        r_shadelight = plighting->ShadeLight;

        if (r_shadelight < 0)
            r_shadelight = 0;

        r_shadelight *= Vid.VID_GRADES;

        // rotate the lighting vector into the model's frame of reference
        //float* pv = plighting->PLightVec;
        Vec3_t pv = plighting->PLightVec;
        r_plightvec[0] = Vector3.Dot(pv, alias_forward);
        r_plightvec[1] = -Vector3.Dot(pv, alias_right);
        r_plightvec[2] = Vector3.Dot(pv, alias_up);
    }

    // R_AliasSetupFrame / R_AliasSetupFrameMH (r_alias.c, Mark_V)
    // set r_apverts, or fill r_alerpverts and set r_alerping while two poses are being blended
    public static void SetupFrame()
    {
        Entity_t* currententity = RShared.currententity;

        int frame = currententity->Frame;
        if (frame >= pmdl->NumFrames || frame < 0)
        {
            Con.DPrintf($"R_AliasSetupFrame: no such frame {frame}\n");
            frame = 0;
        }

        RLerp.SetupAliasFrame(paliashdr, frame, currententity, out LerpData_t lerpdata);

        r_alerping = false;

        // blend 0 shows the previous pose, 1 the current one; only in between do we need to mix
        if (lerpdata.Pose1 == lerpdata.Pose2 || lerpdata.Blend <= 0)
        {
            r_apverts = (TriVertX_t*)((byte*)paliashdr + lerpdata.Pose1);
            return;
        }

        if (lerpdata.Blend >= 1)
        {
            r_apverts = (TriVertX_t*)((byte*)paliashdr + lerpdata.Pose2);
            return;
        }

        TriVertX_t* currverts = (TriVertX_t*)((byte*)paliashdr + lerpdata.Pose2);
        TriVertX_t* lastverts = (TriVertX_t*)((byte*)paliashdr + lerpdata.Pose1);
        float blend = lerpdata.Blend;
        LerpVert_t* lv = r_alerpverts;

        // perform the lerp
        for (int i = 0; i < pmdl->NumVerts; i++, currverts++, lastverts++, lv++)
        {
            lv->V.X = currverts->V[0] * blend + lastverts->V[0] * (1.0f - blend);
            lv->V.Y = currverts->V[1] * blend + lastverts->V[1] * (1.0f - blend);
            lv->V.Z = currverts->V[2] * blend + lastverts->V[2] * (1.0f - blend);
            lv->CurrLightNormal = currverts->LightNormalIndex;
            lv->LastLightNormal = lastverts->LightNormalIndex;
            lv->Blend = blend;
        }

        r_alerping = true;
        r_apverts = (TriVertX_t*)((byte*)paliashdr + lerpdata.Pose2); // keeps r_apverts valid for callers
    }

    // R_AliasDrawModel: finalverts[], auxverts[] -- the original's are stack
    // locals; allocated once here to avoid zero-initializing ~88 KB per call
    // (and to keep pfinalverts/pauxverts valid). Every vertex used is fully
    // written (Flags included) before it is read. The cache-align padding
    // (RLocal.MAXALIASVERTS + ((CACHE_SIZE-1)/sizeof(finalvert_t)) + 1) is dropped.
    internal static readonly FinalVert_t* finalverts = NativeMemory.AllocArray<FinalVert_t>(RLocal.MAXALIASVERTS + 1);
    internal static readonly AuxVert_t* auxverts = NativeMemory.AllocArray<AuxVert_t>(RLocal.MAXALIASVERTS);

    // R_AliasDrawModel
    public static void DrawModel(ALight_t* plighting)
    {
        Entity_t* currententity = RShared.currententity;

        RMain.r_amodels_drawn++;

        pfinalverts = finalverts;
        pauxverts = auxverts;

        paliashdr = (AliasHdr_t*)Model.Extradata(currententity->Model);
        pmdl = (Mdl_t*)((byte*)paliashdr + paliashdr->Model);

        SetupSkin();
        SetupFrame();

        // R_AliasDrawModelMH (r_alias.c, Mark_V) never trivially accepts alias models:
        // AliasCheckBBox only bounds currententity->Frame, but the pose drawn can be the
        // previous/current pose (or a blend), which may poke off-screen. Unclipped
        // drawing would then write outside the view/z buffers.
        currententity->TrivialAccept = 0;

        SetUpTransform(currententity->TrivialAccept);
        SetupLighting(plighting);

        if (currententity->ColorMap == null)
            Sys.Error("R_AliasDrawModel: !currententity->colormap"u8);

        DIface.r_affinetridesc.DrawType = currententity->TrivialAccept == 3 && DIface.r_recursiveaffinetriangles ? 1 : 0;

        if (DIface.r_affinetridesc.DrawType != 0)
        {
            DPolyse.PolysetUpdateTables(); // FIXME (original comment): precalc...
        }
        // else: D_Aff8Patch (currententity->colormap) -- id386 ASM-only path
        // (#if id386), dropped (no x86 asm in this port).

        DIface.acolormap = currententity->ColorMap;

        Entity_t* viewent = Cl.viewent;
        if (currententity != viewent)
            ziscale = (float)0x8000 * (float)0x10000;
        else
            ziscale = (float)0x8000 * (float)0x10000 * 3.0f;

        if (currententity->TrivialAccept != 0)
            PrepareUnclippedPoints();
        else
            PreparePoints();
    }

}
