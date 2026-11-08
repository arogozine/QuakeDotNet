// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.


using System.Numerics;

namespace Quake.Engine;

/// <summary>
/// r_light.c: dynamic light marking and lightmap sampling.
/// </summary>
public static unsafe class RLight
{
    // r_dlightframecount already lives in RMain.cs (r_main.c) -- not redeclared here.

    // R_AnimateLight
    public static void AnimateLight()
    {
        // light animations
        // 'm' is normal light, 'a' is no light, 'z' is double bright
        int i = (int)(Cl.time * 10);
        for (int j = 0; j < QuakeDef.MAX_LIGHTSTYLES; j++)
        {
            if (ClLightStyle.cl_lightstyle[j].Length == 0)
            {
                RShared.d_lightstylevalue[j] = 256;
                continue;
            }
            int k = i % ClLightStyle.cl_lightstyle[j].Length;
            k = ClLightStyle.cl_lightstyle[j].Map[k] - 'a';
            k *= 22;
            RShared.d_lightstylevalue[j] = k;
        }
    }

    // R_MarkLights
    public static void MarkLights(DLight_t* light, int bit, MNode_t* node)
    {
        if (node->Contents < 0)
            return;

        MPlane_t* splitplane = node->Plane;
        float dist = Vector3.Dot(light->Origin, splitplane->Normal) - splitplane->Dist;

        if (dist > light->Radius)
        {
            MarkLights(light, bit, node->Children[0]);
            return;
        }
        if (dist < -light->Radius)
        {
            MarkLights(light, bit, node->Children[1]);
            return;
        }

        // mark the polygons
        MSurface_t* surf = Cl.worldmodel->Surfaces + node->FirstSurface;
        for (int i = 0; i < node->NumSurfaces; i++, surf++)
        {
            if (surf->DLightFrame != RMain.r_dlightframecount)
            {
                surf->DLightBits = 0;
                surf->DLightFrame = RMain.r_dlightframecount;
            }
            surf->DLightBits |= bit;
        }

        MarkLights(light, bit, node->Children[0]);
        MarkLights(light, bit, node->Children[1]);
    }

    // R_PushDlights
    // The original's only caller is view.c's V_RenderView -- now ported
    // (View.cs) and calling this directly (View.RenderView).
    public static void PushDlights()
    {
        RMain.r_dlightframecount = DIface.r_framecount + 1; // because the count hasn't
                                                              //  advanced yet for this frame

        for (int i = 0; i < ClEntities.MAX_DLIGHTS; i++)
        {
            DLight_t* l = &ClEntities.cl_dlights[i];
            if (l->Die < Cl.time || l->Radius == 0)
                continue;
            MarkLights(l, 1 << i, Cl.worldmodel->Nodes);
        }
    }

    // RecursiveLightPoint
    public static int RecursiveLightPoint(MNode_t* node, Vec3_t start, Vec3_t end)
    {
        if (node->Contents < 0)
            return -1; // didn't hit anything

        // calculate mid point

        // FIXME: optimize for axial
        MPlane_t* plane = node->Plane;
        float front = Vector3.Dot(start, plane->Normal) - plane->Dist;
        float back = Vector3.Dot(end, plane->Normal) - plane->Dist;
        int side = front < 0 ? 1 : 0;

        if ((back < 0 ? 1 : 0) == side)
            return RecursiveLightPoint(node->Children[side], start, end);

        float frac = front / (front - back);
        Vec3_t mid = start + (end - start) * frac;

        // go down front side
        int r = RecursiveLightPoint(node->Children[side], start, mid);
        if (r >= 0)
            return r; // hit something

        if ((back < 0 ? 1 : 0) == side)
            return -1; // didn't hit anything

        // check for impact on this node
        MSurface_t* surf = Cl.worldmodel->Surfaces + node->FirstSurface;
        for (int i = 0; i < node->NumSurfaces; i++, surf++)
        {
            if ((surf->Flags & Surf.DRAWTILED) != 0)
                continue; // no lightmaps

            MTexInfo_t* tex = surf->TexInfo;

            int s = (int)(mid[0] * tex->Vecs[0] + mid[1] * tex->Vecs[1] + mid[2] * tex->Vecs[2] + tex->Vecs[3]);
            int t = (int)(mid[0] * tex->Vecs[4] + mid[1] * tex->Vecs[5] + mid[2] * tex->Vecs[6] + tex->Vecs[7]);

            if (s < surf->TextureMins[0] || t < surf->TextureMins[1])
                continue;

            int ds = s - surf->TextureMins[0];
            int dt = t - surf->TextureMins[1];

            if (ds > surf->Extents[0] || dt > surf->Extents[1])
                continue;

            if (surf->Samples == null)
                return 0;

            ds >>= 4;
            dt >>= 4;

            byte* lightmap = surf->Samples;
            r = 0;

            lightmap += dt * ((surf->Extents[0] >> 4) + 1) + ds;

            for (int maps = 0; maps < BspFile.MAXLIGHTMAPS && surf->Styles[maps] != 255; maps++)
            {
                uint scale = (uint)RShared.d_lightstylevalue[surf->Styles[maps]];
                r += *lightmap * (int)scale;
                lightmap += ((surf->Extents[0] >> 4) + 1) * ((surf->Extents[1] >> 4) + 1);
            }

            r >>= 8;

            return r;
        }

        // go down back side
        return RecursiveLightPoint(node->Children[side == 0 ? 1 : 0], mid, end);
    }

    // R_LightPoint
    public static int LightPoint(Vec3_t* p)
    {
        if (Cl.worldmodel->LightData == null)
            return 255;

        Vec3_t end = *p;
        end[2] = (*p)[2] - 8192;  //johnfitz -- was 2048

        int r = RecursiveLightPoint(Cl.worldmodel->Nodes, *p, end);

        if (r == -1)
            r = 0;

        if (r < RShared.r_refdef->AmbientLight)
            r = RShared.r_refdef->AmbientLight;

        return r;
    }

}
