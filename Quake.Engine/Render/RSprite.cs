// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;

using System.Runtime.CompilerServices;

namespace Quake.Engine;

/// <summary>
/// r_sprite.c: builds and clips a sprite's screen-space quad, then hands off
/// to DSprite.cs's (d_sprite.c) rasterizer.
/// </summary>
public static unsafe class RSprite
{
    // internal (not private): RSpriteTests seeds clip_verts directly to
    // exercise ClipSpriteFace without going through the full
    // SetupAndDrawSprite pipeline.
    internal static int clip_current;

    // clip_verts[2][RShared.MAXWORKINGVERTS] of vec5_t (x,y,z,s,t) -- flat native
    // buffer (not a managed array) since R_ClipSpriteFace does raw pointer
    // arithmetic with a stride of 5 floats per vertex, same convention as
    // RAclip.cs's fv/av.
    internal static readonly float* clip_verts =
        (float*)NativeMemory.AllocZeroed((nuint)(2 * RShared.MAXWORKINGVERTS * 5 * sizeof(float)));

    internal static float* Row(int r) => clip_verts + r * RShared.MAXWORKINGVERTS * 5;

    // r_sprite.c's own sprite_width/sprite_height -- distinct globals from
    // DSprite.cs's (d_sprite.c) same-named statics; confirmed by reading
    // both C files directly (each is `static`, file-scoped, independently
    // set from r_spritedesc.pspriteframe->{width,height} in its own file).
    private static int sprite_width, sprite_height;

    // R_RotateSprite
    public static void RotateSprite(float beamlength)
    {
        if (beamlength == 0.0f)
            return;

        Vec3_t vec = DIface.r_spritedesc.Vpn * -beamlength;
        RMain.r_entorigin = RMain.r_entorigin + vec;
        RShared.modelorg = RShared.modelorg - vec;
    }

    // R_ClipSpriteFace
    // Clips the winding at clip_verts[clip_current] and changes clip_current.
    // Throws out the back side.
    [SkipLocalsInit]
    public static int ClipSpriteFace(int nump, ClipPlane_t* pclipplane)
    {
        float* dists = stackalloc float[RShared.MAXWORKINGVERTS + 1];
        float clipdist = pclipplane->Dist;
        Vec3_t pclipnormal = pclipplane->Normal;

        float* in_, outstep;

        // calc dists
        if (clip_current != 0)
        {
            in_ = Row(1);
            outstep = Row(0);
            clip_current = 0;
        }
        else
        {
            in_ = Row(0);
            outstep = Row(1);
            clip_current = 1;
        }

        float* instep = in_;
        for (int i = 0; i < nump; i++, instep += 5)
            dists[i] = instep[0] * pclipnormal[0] + instep[1] * pclipnormal[1] + instep[2] * pclipnormal[2] - clipdist;

        // handle wraparound case
        dists[nump] = dists[0];
        for (int k = 0; k < 5; k++)
            instep[k] = in_[k];

        // clip the winding
        instep = in_;
        int outcount = 0;

        for (int i = 0; i < nump; i++, instep += 5)
        {
            if (dists[i] >= 0)
            {
                for (int k = 0; k < 5; k++)
                    outstep[k] = instep[k];
                outstep += 5;
                outcount++;
            }

            if (dists[i] == 0 || dists[i + 1] == 0)
                continue;

            if ((dists[i] > 0) == (dists[i + 1] > 0))
                continue;

            // split it into a new vertex
            float frac = dists[i] / (dists[i] - dists[i + 1]);

            float* vert2 = instep + 5;

            for (int k = 0; k < 5; k++)
                outstep[k] = instep[k] + frac * (vert2[k] - instep[k]);

            outstep += 5;
            outcount++;
        }

        return outcount;
    }

    // R_SetupAndDrawSprite
    public static void SetupAndDrawSprite()
    {
        float dot = Vector3.Dot(DIface.r_spritedesc.Vpn, RShared.modelorg);

        // backface cull
        if (dot >= 0)
            return;

        // build the sprite poster in worldspace
        Vec3_t right = DIface.r_spritedesc.Vright * DIface.r_spritedesc.PSpriteFrame->Right;
        Vec3_t up = DIface.r_spritedesc.Vup * DIface.r_spritedesc.PSpriteFrame->Up;
        Vec3_t left = DIface.r_spritedesc.Vright * DIface.r_spritedesc.PSpriteFrame->Left;
        Vec3_t down = DIface.r_spritedesc.Vup * DIface.r_spritedesc.PSpriteFrame->Down;

        float* pverts = Row(0);

        pverts[0 * 5 + 0] = RMain.r_entorigin[0] + up[0] + left[0];
        pverts[0 * 5 + 1] = RMain.r_entorigin[1] + up[1] + left[1];
        pverts[0 * 5 + 2] = RMain.r_entorigin[2] + up[2] + left[2];
        pverts[0 * 5 + 3] = 0;
        pverts[0 * 5 + 4] = 0;

        pverts[1 * 5 + 0] = RMain.r_entorigin[0] + up[0] + right[0];
        pverts[1 * 5 + 1] = RMain.r_entorigin[1] + up[1] + right[1];
        pverts[1 * 5 + 2] = RMain.r_entorigin[2] + up[2] + right[2];
        pverts[1 * 5 + 3] = sprite_width;
        pverts[1 * 5 + 4] = 0;

        pverts[2 * 5 + 0] = RMain.r_entorigin[0] + down[0] + right[0];
        pverts[2 * 5 + 1] = RMain.r_entorigin[1] + down[1] + right[1];
        pverts[2 * 5 + 2] = RMain.r_entorigin[2] + down[2] + right[2];
        pverts[2 * 5 + 3] = sprite_width;
        pverts[2 * 5 + 4] = sprite_height;

        pverts[3 * 5 + 0] = RMain.r_entorigin[0] + down[0] + left[0];
        pverts[3 * 5 + 1] = RMain.r_entorigin[1] + down[1] + left[1];
        pverts[3 * 5 + 2] = RMain.r_entorigin[2] + down[2] + left[2];
        pverts[3 * 5 + 3] = 0;
        pverts[3 * 5 + 4] = sprite_height;

        // clip to the frustum in worldspace
        int nump = 4;
        clip_current = 0;

        for (int i = 0; i < 4; i++)
        {
            nump = ClipSpriteFace(nump, &RMain.view_clipplanes[i]);
            if (nump < 3)
                return;
            if (nump >= RShared.MAXWORKINGVERTS)
                Sys.Error("R_SetupAndDrawSprite: too many points"u8);
        }

        // transform vertices into viewspace and project
        float* pv = Row(clip_current);
        DIface.r_spritedesc.NearZi = -999999;

        EmitPoint_t* outverts = stackalloc EmitPoint_t[RShared.MAXWORKINGVERTS + 1];

        for (int i = 0; i < nump; i++)
        {
            Vec3_t pvpos = *(Vec3_t*)pv;

            Vec3_t local = pvpos - RShared.r_origin;
            RMisc.TransformVector(local, out Vec3_t transformed);

            if (transformed[2] < RLocal.NEAR_CLIP)
                transformed[2] = RLocal.NEAR_CLIP;

            EmitPoint_t* pout = &outverts[i];
            pout->Zi = 1.0f / transformed[2];
            if (pout->Zi > DIface.r_spritedesc.NearZi)
                DIface.r_spritedesc.NearZi = pout->Zi;

            pout->S = pv[3];
            pout->T = pv[4];

            float scale = RShared.xscale * pout->Zi;
            pout->U = RShared.xcenter + scale * transformed[0];

            scale = RShared.yscale * pout->Zi;
            pout->V = RShared.ycenter - scale * transformed[1];

            pv += 5;
        }

        // draw it
        DIface.r_spritedesc.Nump = nump;
        DIface.r_spritedesc.PVerts = outverts;
        DSprite.DrawSprite();
    }

    // R_GetSpriteframe
    public static MSpriteFrame_t* GetSpriteframe(MSprite_t* psprite)
    {
        Entity_t* currententity = RShared.currententity;

        int frame = currententity->Frame;

        if (frame >= psprite->NumFrames || frame < 0)
        {
            Con.Printf($"R_DrawSprite: no such frame {frame}\n");
            frame = 0;
        }

        MSpriteFrameDesc_t* frames = &psprite->Frames;

        MSpriteFrame_t* pspriteframe;

        if (frames[frame].Type == SpriteFrameType_t.Single)
        {
            pspriteframe = frames[frame].FramePtr;
        }
        else
        {
            MSpriteGroup_t* pspritegroup = (MSpriteGroup_t*)frames[frame].FramePtr;
            float* pintervals = pspritegroup->Intervals;
            int numframes = pspritegroup->NumFrames;
            float fullinterval = pintervals[numframes - 1];

            double time = Cl.time + currententity->SyncBase;

            // when loading in Mod_LoadSpriteGroup, we guaranteed all interval values
            // are positive, so we don't have to worry about division by 0
            float targettime = (float)(time - (int)(time / fullinterval) * fullinterval);

            int i;
            for (i = 0; i < numframes - 1; i++)
            {
                if (pintervals[i] > targettime)
                    break;
            }

            MSpriteFrame_t** groupframes = &pspritegroup->Frames;
            pspriteframe = groupframes[i];
        }

        return pspriteframe;
    }

    // R_DrawSprite
    public static void DrawSprite()
    {
        Entity_t* currententity = RShared.currententity;

        MSprite_t* psprite = (MSprite_t*)currententity->Model->Cache.Data;

        DIface.r_spritedesc.PSpriteFrame = GetSpriteframe(psprite);

        sprite_width = DIface.r_spritedesc.PSpriteFrame->Width;
        sprite_height = DIface.r_spritedesc.PSpriteFrame->Height;

        // TODO (original comment): make this caller-selectable
        if (psprite->Type == SpriteType.FacingUpright)
        {
            // generate the sprite's axes, with vup straight up in worldspace, and
            // r_spritedesc.vright perpendicular to modelorg.
            // This will not work if the view direction is very close to straight up or
            // down, because the cross product will be between two nearly parallel
            // vectors and starts to approach an undefined state, so we don't draw if
            // the two vectors are less than 1 degree apart
            Vec3_t tvec = -RShared.modelorg;
            MathLib.VectorNormalize(&tvec);
            float dot = tvec[2]; // same as DotProduct (tvec, r_spritedesc.vup) because
                                  //  r_spritedesc.vup is 0, 0, 1
            if (dot > 0.999848f || dot < -0.999848f) // cos(1 degree) = 0.999848
                return;
            DIface.r_spritedesc.Vup[0] = 0;
            DIface.r_spritedesc.Vup[1] = 0;
            DIface.r_spritedesc.Vup[2] = 1;
            DIface.r_spritedesc.Vright[0] = tvec[1];
                                    // CrossProduct(r_spritedesc.vup, -modelorg,
            DIface.r_spritedesc.Vright[1] = -tvec[0];
                                    //              r_spritedesc.vright)
            DIface.r_spritedesc.Vright[2] = 0;
            MathLib.VectorNormalize((Vec3_t*)Unsafe.AsPointer(ref DIface.r_spritedesc.Vright));
            DIface.r_spritedesc.Vpn[0] = -DIface.r_spritedesc.Vright[1];
            DIface.r_spritedesc.Vpn[1] = DIface.r_spritedesc.Vright[0];
            DIface.r_spritedesc.Vpn[2] = 0;
                        // CrossProduct (r_spritedesc.vright, r_spritedesc.vup,
                        //  r_spritedesc.vpn)
        }
        else if (psprite->Type == SpriteType.VpParallel)
        {
            // generate the sprite's axes, completely parallel to the viewplane. There
            // are no problem situations, because the sprite is always in the same
            // position relative to the viewer
            DIface.r_spritedesc.Vup = RShared.vup;
            DIface.r_spritedesc.Vright = RShared.vright;
            DIface.r_spritedesc.Vpn = RShared.vpn;
            /*
            for (int i = 0; i < 3; i++)
            {
                DIface.r_spritedesc.Vup[i] = RShared.vup[i];
                DIface.r_spritedesc.Vright[i] = RShared.vright[i];
                DIface.r_spritedesc.Vpn[i] = RShared.vpn[i];
            }
            */
        }
        else if (psprite->Type == SpriteType.VpParallelUpright)
        {
            // generate the sprite's axes, with vup straight up in worldspace, and
            // r_spritedesc.vright parallel to the viewplane.
            // This will not work if the view direction is very close to straight up or
            // down, because the cross product will be between two nearly parallel
            // vectors and starts to approach an undefined state, so we don't draw if
            // the two vectors are less than 1 degree apart
            float dot = RShared.vpn[2]; // same as DotProduct (vpn, r_spritedesc.vup) because
                                         //  r_spritedesc.vup is 0, 0, 1
            if (dot > 0.999848f || dot < -0.999848f) // cos(1 degree) = 0.999848
                return;
            DIface.r_spritedesc.Vup[0] = 0;
            DIface.r_spritedesc.Vup[1] = 0;
            DIface.r_spritedesc.Vup[2] = 1;
            DIface.r_spritedesc.Vright[0] = RShared.vpn[1];
                                            // CrossProduct (r_spritedesc.vup, vpn,
            DIface.r_spritedesc.Vright[1] = -RShared.vpn[0]; //  r_spritedesc.vright)
            DIface.r_spritedesc.Vright[2] = 0;
            MathLib.VectorNormalize((Vec3_t*)Unsafe.AsPointer(ref DIface.r_spritedesc.Vright));
            DIface.r_spritedesc.Vpn[0] = -DIface.r_spritedesc.Vright[1];
            DIface.r_spritedesc.Vpn[1] = DIface.r_spritedesc.Vright[0];
            DIface.r_spritedesc.Vpn[2] = 0;
                        // CrossProduct (r_spritedesc.vright, r_spritedesc.vup,
                        //  r_spritedesc.vpn)
        }
        else if (psprite->Type == SpriteType.Oriented)
        {
            // generate the sprite's axes, according to the sprite's world orientation
            MathLib.AngleVectors(currententity->Angles, out DIface.r_spritedesc.Vpn,
                out DIface.r_spritedesc.Vright, out DIface.r_spritedesc.Vup);
        }
        else if (psprite->Type == SpriteType.VpParallelOriented)
        {
            // generate the sprite's axes, parallel to the viewplane, but rotated in
            // that plane around the center according to the sprite entity's roll
            // angle. So vpn stays the same, but vright and vup rotate
            float angle = currententity->Angles[QuakeDef.ROLL] * (MathLib.M_PI * 2 / 360);
            (float sr, float cr) = MathF.SinCos(angle);

            DIface.r_spritedesc.Vpn = RShared.vpn;
            DIface.r_spritedesc.Vright = RShared.vright * cr + RShared.vup * sr;
            DIface.r_spritedesc.Vup = RShared.vright * -sr + RShared.vup * cr;
            /*
            for (int i = 0; i < 3; i++)
            {
                DIface.r_spritedesc.Vpn[i] = RShared.vpn[i];
                DIface.r_spritedesc.Vright[i] = RShared.vright[i] * cr + RShared.vup[i] * sr;
                DIface.r_spritedesc.Vup[i] = RShared.vright[i] * -sr + RShared.vup[i] * cr;
            }
            */
        }
        else
        {
            Sys.Error($"R_DrawSprite: Bad sprite type {(int)psprite->Type}");
        }

        RotateSprite(psprite->BeamLength);

        SetupAndDrawSprite();
    }

}
