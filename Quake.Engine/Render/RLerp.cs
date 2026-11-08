// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// entity_t lerpflags (render.h, johnfitz -- for lerping; Mark_V)
public static class LerpFlags
{
    public const byte MOVESTEP = 1 << 0;   // this is a MOVETYPE_STEP entity, enable movement lerp
    public const byte RESETANIM = 1 << 1;  // disable anim lerping until next anim frame
    public const byte RESETANIM2 = 1 << 2; // set this and previous flag to disable anim lerping for two anim frames
    public const byte RESETMOVE = 1 << 3;  // disable movement lerping until next origin/angles change
    public const byte FINISH = 1 << 4;     // use lerpfinish time from server update instead of assuming interval of 0.1 (U_LERPFINISH); not yet consumed by the renderer
}

// lerpdata_t (model.h, Mark_V). Pose1/Pose2 are byte offsets of vertex data from
// the aliashdr_t instead of Fitz's pose indices (this port keeps the original
// single/group frame layout rather than flattening into poses).
public struct LerpData_t
{
    public int Pose1;
    public int Pose2;
    public float Blend;
}

/// <summary>
/// r_common.c (Mark_V, WINQUAKE_RENDERER_SUPPORT): R_SetupAliasFrame and
/// R_SetupEntityTransform -- animation and movement interpolation.
/// </summary>
public static unsafe class RLerp
{
    // R_SetupAliasFrame
    // frame: entity frame (already range-checked); returns pose offsets and blend.
    public static void SetupAliasFrame(AliasHdr_t* paliashdr, int frame, Entity_t* e, out LerpData_t lerpdata)
    {
        MAliasFrameDesc_t* frames = &paliashdr->Frames;
        int posenum;
        float lerptime;

        if (frames[frame].Type == AliasFrameType_t.Single)
        {
            posenum = frames[frame].Frame;
            lerptime = 0.1f;
        }
        else
        {
            // same group pose selection as R_AliasSetupFrame
            MAliasGroup_t* paliasgroup = (MAliasGroup_t*)((byte*)paliashdr + frames[frame].Frame);
            float* pintervals = (float*)((byte*)paliashdr + paliasgroup->Intervals);
            int numframes = paliasgroup->NumFrames;
            float fullinterval = pintervals[numframes - 1];

            double time = Cl.time + e->SyncBase;
            float targettime = (float)(time - (int)(time / fullinterval) * fullinterval);

            int j;
            for (j = 0; j < numframes - 1; j++)
            {
                if (pintervals[j] > targettime)
                    break;
            }

            MAliasGroupFrameDesc_t* groupframes = &paliasgroup->Frames;
            posenum = groupframes[j].Frame;
            lerptime = pintervals[j] - (j > 0 ? pintervals[j - 1] : 0); // Fitz: the group's (uniform) interval
        }
        e->LerpTime = lerptime;

        // Offsets only mean something for the model they were taken from; a
        // never-lerped entity (LerpModel null) or a model swap must not blend.
        if (e->LerpModel != e->Model)
        {
            e->LerpModel = e->Model;
            e->LerpFlags |= LerpFlags.RESETANIM;
        }

        if ((e->LerpFlags & LerpFlags.RESETANIM) != 0) // kill any lerp in progress
        {
            e->LerpStart = 0;
            e->PreviousPose = posenum;
            e->CurrentPose = posenum;
            e->LerpFlags &= unchecked((byte)~LerpFlags.RESETANIM);
        }
        else if (e->CurrentPose != posenum) // pose changed, start new lerp
        {
            if ((e->LerpFlags & LerpFlags.RESETANIM2) != 0) // defer lerping one more time
            {
                e->LerpStart = 0;
                e->PreviousPose = posenum;
                e->CurrentPose = posenum;
                e->LerpFlags &= unchecked((byte)~LerpFlags.RESETANIM2);
            }
            else
            {
                e->LerpStart = (float)Cl.time;
                e->PreviousPose = e->CurrentPose;
                e->CurrentPose = posenum;
            }
        }

        // set up values
        if (RMain.r_lerpmodels->Value != 0 &&
            !((e->Model->Flags & ModelFlags.MOD_NOLERP) != 0 && RMain.r_lerpmodels->Value != 2))
        {
            lerpdata.Blend = Math.Clamp(((float)Cl.time - e->LerpStart) / e->LerpTime, 0, 1);
            lerpdata.Pose1 = e->PreviousPose;
            lerpdata.Pose2 = e->CurrentPose;
        }
        else // don't lerp
        {
            lerpdata.Blend = 1;
            lerpdata.Pose1 = posenum;
            lerpdata.Pose2 = posenum;
        }
    }

    // R_SetupEntityTransform
    // Writes the interpolated origin/angles back into the entity, which is what
    // the WINQUAKE_RENDERER_SUPPORT variant does: the software renderer reads
    // e->origin/e->angles in too many places.
    public static void SetupEntityTransform(Entity_t* e)
    {
        // if LERP_RESETMOVE, kill any lerps in progress
        if ((e->LerpFlags & LerpFlags.RESETMOVE) != 0)
        {
            e->MoveLerpStart = 0;
            e->PreviousOrigin = e->Origin;
            e->CurrentOrigin = e->Origin;
            e->PreviousAngles = e->Angles;
            e->CurrentAngles = e->Angles;
            e->LerpFlags &= unchecked((byte)~LerpFlags.RESETMOVE);
        }
        else if (e->Origin != e->CurrentOrigin || e->Angles != e->CurrentAngles) // origin/angles changed, start new lerp
        {
            e->MoveLerpStart = (float)Cl.time;
            e->PreviousOrigin = e->CurrentOrigin;
            e->CurrentOrigin = e->Origin;
            e->PreviousAngles = e->CurrentAngles;
            e->CurrentAngles = e->Angles;
        }

        // set up values
        Entity_t* viewent = Cl.viewent;
        if (RMain.r_lerpmove->Value != 0 && e != viewent && (e->LerpFlags & LerpFlags.MOVESTEP) != 0)
        {
            float blend = Math.Clamp(((float)Cl.time - e->MoveLerpStart) / 0.1f, 0, 1);

            // translation
            e->Origin = e->PreviousOrigin + (e->CurrentOrigin - e->PreviousOrigin) * blend;

            // rotation
            Vec3_t d = e->CurrentAngles - e->PreviousAngles;
            for (int i = 0; i < 3; i++)
            {
                if (d[i] > 180) d[i] -= 360;
                if (d[i] < -180) d[i] += 360;
            }
            e->Angles = e->PreviousAngles + d * blend;
        }
        // else: don't lerp -- origin/angles already hold the unlerped values
    }
}
