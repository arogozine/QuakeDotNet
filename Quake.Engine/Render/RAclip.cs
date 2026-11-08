// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Quake.Engine;

/// <summary>
/// r_aclip.c: clip routines for drawing Alias models directly to the screen.
/// </summary>
public static unsafe class RAclip
{
    // fv[2][8], av[8]
    internal static readonly FinalVert_t* fv = NativeMemory.AllocArrayZeroed<FinalVert_t>(2 * 8);
    internal static readonly AuxVert_t* av = NativeMemory.AllocArrayZeroed<AuxVert_t>(8);

    // R_Alias_clip_z
    // pfv0 is the unclipped vertex, pfv1 is the z-clipped vertex
    public static void ClipZ(FinalVert_t* pfv0, FinalVert_t* pfv1, FinalVert_t* @out)
    {
        AuxVert_t* pav0 = &av[pfv0 - fv];
        AuxVert_t* pav1 = &av[pfv1 - fv];
        AuxVert_t avout = default;

        float scale;
        if (pfv0->V[1] >= pfv1->V[1])
        {
            scale = (RLocal.ALIAS_Z_CLIP_PLANE - pav0->Z) / (pav1->Z - pav0->Z);

            avout = *pav0 + (*pav1 - *pav0) * scale;
            avout.Z = RLocal.ALIAS_Z_CLIP_PLANE;

            @out->V[2] = float.ConvertToIntegerNative<int>(pfv0->V[2] + (pfv1->V[2] - pfv0->V[2]) * scale);
            @out->V[3] = float.ConvertToIntegerNative<int>(pfv0->V[3] + (pfv1->V[3] - pfv0->V[3]) * scale);
            @out->V[4] = float.ConvertToIntegerNative<int>(pfv0->V[4] + (pfv1->V[4] - pfv0->V[4]) * scale);
        }
        else
        {
            scale = (RLocal.ALIAS_Z_CLIP_PLANE - pav1->Z) / (pav0->Z - pav1->Z);

            avout = *pav1 + (*pav0 - *pav1) * scale;
            avout.Z = RLocal.ALIAS_Z_CLIP_PLANE;

            @out->V[2] = float.ConvertToIntegerNative<int>(pfv1->V[2] + (pfv0->V[2] - pfv1->V[2]) * scale);
            @out->V[3] = float.ConvertToIntegerNative<int>(pfv1->V[3] + (pfv0->V[3] - pfv1->V[3]) * scale);
            @out->V[4] = float.ConvertToIntegerNative<int>(pfv1->V[4] + (pfv0->V[4] - pfv1->V[4]) * scale);
        }

        RAlias.ProjectFinalVert(@out, &avout);

        if (@out->V[0] < RShared.r_refdef->AliasVrect.X)
            @out->Flags |= DIface.ALIAS_LEFT_CLIP;
        if (@out->V[1] < RShared.r_refdef->AliasVrect.Y)
            @out->Flags |= DIface.ALIAS_TOP_CLIP;
        if (@out->V[0] > RShared.r_refdef->AliasVrectRight)
            @out->Flags |= DIface.ALIAS_RIGHT_CLIP;
        if (@out->V[1] > RShared.r_refdef->AliasVrectBottom)
            @out->Flags |= DIface.ALIAS_BOTTOM_CLIP;
    }

    // R_Alias_clip_left
    public static void ClipLeft(FinalVert_t* pfv0, FinalVert_t* pfv1, FinalVert_t* @out)
    {
        float scale;
        if (pfv0->V[1] >= pfv1->V[1])
        {
            scale = (RShared.r_refdef->AliasVrect.X - pfv0->V[0]) / (float)(pfv1->V[0] - pfv0->V[0]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv0->V[i] + (pfv1->V[i] - pfv0->V[i]) * scale + 0.5);
        }
        else
        {
            scale = (RShared.r_refdef->AliasVrect.X - pfv1->V[0]) / (float)(pfv0->V[0] - pfv1->V[0]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv1->V[i] + (pfv0->V[i] - pfv1->V[i]) * scale + 0.5);
        }
    }

    // R_Alias_clip_right
    public static void ClipRight(FinalVert_t* pfv0, FinalVert_t* pfv1, FinalVert_t* @out)
    {
        float scale;
        if (pfv0->V[1] >= pfv1->V[1])
        {
            scale = (RShared.r_refdef->AliasVrectRight - pfv0->V[0]) / (float)(pfv1->V[0] - pfv0->V[0]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv0->V[i] + (pfv1->V[i] - pfv0->V[i]) * scale + 0.5);
        }
        else
        {
            scale = (RShared.r_refdef->AliasVrectRight - pfv1->V[0]) / (float)(pfv0->V[0] - pfv1->V[0]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv1->V[i] + (pfv0->V[i] - pfv1->V[i]) * scale + 0.5);
        }
    }

    // R_Alias_clip_top
    public static void ClipTop(FinalVert_t* pfv0, FinalVert_t* pfv1, FinalVert_t* @out)
    {
        float scale;
        if (pfv0->V[1] >= pfv1->V[1])
        {
            scale = (RShared.r_refdef->AliasVrect.Y - pfv0->V[1]) / (float)(pfv1->V[1] - pfv0->V[1]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv0->V[i] + (pfv1->V[i] - pfv0->V[i]) * scale + 0.5);
        }
        else
        {
            scale = (RShared.r_refdef->AliasVrect.Y - pfv1->V[1]) / (float)(pfv0->V[1] - pfv1->V[1]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv1->V[i] + (pfv0->V[i] - pfv1->V[i]) * scale + 0.5);
        }
    }

    // R_Alias_clip_bottom
    public static void ClipBottom(FinalVert_t* pfv0, FinalVert_t* pfv1, FinalVert_t* @out)
    {
        float scale;
        if (pfv0->V[1] >= pfv1->V[1])
        {
            scale = (RShared.r_refdef->AliasVrectBottom - pfv0->V[1]) / (float)(pfv1->V[1] - pfv0->V[1]);
            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv0->V[i] + (pfv1->V[i] - pfv0->V[i]) * scale + 0.5);
        }
        else
        {
            scale = (RShared.r_refdef->AliasVrectBottom - pfv1->V[1]) / (float)(pfv0->V[1] - pfv1->V[1]);

            for (int i = 0; i < 6; i++)
                @out->V[i] = double.ConvertToIntegerNative<int>(pfv1->V[i] + (pfv0->V[i] - pfv1->V[i]) * scale + 0.5);
        }
    }

    // R_AliasClip
    public static int AliasClip(FinalVert_t* in_, FinalVert_t* out_, int flag, int count,
        delegate* managed<FinalVert_t*, FinalVert_t*, FinalVert_t*, void> clip)
    {
        int j = count - 1;
        int k = 0;
        for (int i = 0; i < count; j = i, i++)
        {
            int oldflags = in_[j].Flags & flag;
            int flags = in_[i].Flags & flag;

            if (flags != 0 && oldflags != 0)
                continue;
            if ((oldflags ^ flags) != 0)
            {
                clip(&in_[j], &in_[i], &out_[k]);
                out_[k].Flags = 0;
                if (out_[k].V[0] < RShared.r_refdef->AliasVrect.X)
                    out_[k].Flags |= DIface.ALIAS_LEFT_CLIP;
                if (out_[k].V[1] < RShared.r_refdef->AliasVrect.Y)
                    out_[k].Flags |= DIface.ALIAS_TOP_CLIP;
                if (out_[k].V[0] > RShared.r_refdef->AliasVrectRight)
                    out_[k].Flags |= DIface.ALIAS_RIGHT_CLIP;
                if (out_[k].V[1] > RShared.r_refdef->AliasVrectBottom)
                    out_[k].Flags |= DIface.ALIAS_BOTTOM_CLIP;
                k++;
            }
            if (flags == 0)
            {
                out_[k] = in_[i];
                k++;
            }
        }

        return k;
    }

    // R_AliasClipTriangle
    public static void ClipTriangle(MTriangle_t* ptri)
    {
        FinalVert_t* fv0 = fv;      // fv[0]
        FinalVert_t* fv1 = fv + 8;  // fv[1]

        // copy vertexes and fix seam texture coordinates
        if (ptri->FacesFront != 0)
        {
            fv0[0] = RAlias.pfinalverts[ptri->VertIndex[0]];
            fv0[1] = RAlias.pfinalverts[ptri->VertIndex[1]];
            fv0[2] = RAlias.pfinalverts[ptri->VertIndex[2]];
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                fv0[i] = RAlias.pfinalverts[ptri->VertIndex[i]];

                if (ptri->FacesFront == 0 && (fv0[i].Flags & DIface.ALIAS_ONSEAM) != 0)
                    fv0[i].V[2] += DIface.r_affinetridesc.SeamFixupX16;
            }
        }

        // clip
        int clipflags = fv0[0].Flags | fv0[1].Flags | fv0[2].Flags;

        int pingpong, k;

        if ((clipflags & DIface.ALIAS_Z_CLIP) != 0)
        {
            for (int i = 0; i < 3; i++)
                av[i] = RAlias.pauxverts[ptri->VertIndex[i]];

            k = AliasClip(fv0, fv1, DIface.ALIAS_Z_CLIP, 3, &ClipZ);
            if (k == 0)
                return;

            pingpong = 1;
            clipflags = fv1[0].Flags | fv1[1].Flags | fv1[2].Flags;
        }
        else
        {
            pingpong = 0;
            k = 3;
        }

        if ((clipflags & DIface.ALIAS_LEFT_CLIP) != 0)
        {
            k = AliasClip(fv + pingpong * 8, fv + (pingpong ^ 1) * 8, DIface.ALIAS_LEFT_CLIP, k, &ClipLeft);
            if (k == 0)
                return;
            pingpong ^= 1;
        }

        if ((clipflags & DIface.ALIAS_RIGHT_CLIP) != 0)
        {
            k = AliasClip(fv + pingpong * 8, fv + (pingpong ^ 1) * 8, DIface.ALIAS_RIGHT_CLIP, k, &ClipRight);
            if (k == 0)
                return;
            pingpong ^= 1;
        }

        if ((clipflags & DIface.ALIAS_BOTTOM_CLIP) != 0)
        {
            k = AliasClip(fv + pingpong * 8, fv + (pingpong ^ 1) * 8, DIface.ALIAS_BOTTOM_CLIP, k, &ClipBottom);
            if (k == 0)
                return;
            pingpong ^= 1;
        }

        if ((clipflags & DIface.ALIAS_TOP_CLIP) != 0)
        {
            k = AliasClip(fv + pingpong * 8, fv + (pingpong ^ 1) * 8, DIface.ALIAS_TOP_CLIP, k, &ClipTop);
            if (k == 0)
                return;
            pingpong ^= 1;
        }

        FinalVert_t* fvPing = fv + pingpong * 8;

        for (int i = 0; i < k; i++)
        {
            if (fvPing[i].V[0] < RShared.r_refdef->AliasVrect.X)
                fvPing[i].V[0] = RShared.r_refdef->AliasVrect.X;
            else if (fvPing[i].V[0] > RShared.r_refdef->AliasVrectRight)
                fvPing[i].V[0] = RShared.r_refdef->AliasVrectRight;

            if (fvPing[i].V[1] < RShared.r_refdef->AliasVrect.Y)
                fvPing[i].V[1] = RShared.r_refdef->AliasVrect.Y;
            else if (fvPing[i].V[1] > RShared.r_refdef->AliasVrectBottom)
                fvPing[i].V[1] = RShared.r_refdef->AliasVrectBottom;

            fvPing[i].Flags = 0;
        }

        // draw triangles
        MTriangle_t mtri = default;
        mtri.FacesFront = ptri->FacesFront;
        DIface.r_affinetridesc.PTriangles = &mtri;
        DIface.r_affinetridesc.PFinalVerts = fvPing;

        // FIXME (original comment): do all at once as trifan?
        mtri.VertIndex[0] = 0;
        for (int i = 1; i < k - 1; i++)
        {
            mtri.VertIndex[1] = i;
            mtri.VertIndex[2] = i + 1;
            DPolyse.PolysetDraw();
        }
    }

}
