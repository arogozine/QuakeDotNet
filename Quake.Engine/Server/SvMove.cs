// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// sv_move.c -- monster movement: SV_CheckBottom's dangling-corner check,
/// SV_movestep's step-up/step-down probe, and the chase-direction picker
/// QuakeC's monster AI calls into via the walkmove/movetogoal builtins.
/// SV_StepDirection calls PF_changeyaw (pr_cmds.c) directly in the original
/// (the engine calling one of its own QuakeC-exposed builtins by C function
/// call, not through PR_ExecuteProgram) -- its body lives here as ChangeYaw,
/// and PrCmds.PF_changeyaw (builtin #49) forwards to it. SV_MoveToGoal is
/// itself a builtin body (reads/writes OFS_PARM0/OFS_RETURN directly),
/// reached from QuakeC through Progs.pr_builtins.
/// </summary>
public static unsafe class SvMove
{
    private const int DI_NODIR = -1;

    // c_yes, c_no (sv_move.c file-scope debug counters)
    public static int c_yes, c_no;

    // SV_CheckBottom -- returns false if any part of the bottom of the
    // entity is off an edge that is not a staircase.
    public static bool CheckBottom(EDict_t* ent)
    {
        Vec3_t mins = ent->V.Origin + ent->V.Mins, maxs = ent->V.Origin + ent->V.Maxs, start = default, stop = default;

        // if all of the points under the corners are solid world, don't bother
        // with the tougher checks
        // the corners must be within 16 of the midpoint
        start[2] = mins[2] - 1;
        for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            {
                start[0] = x != 0 ? maxs[0] : mins[0];
                start[1] = y != 0 ? maxs[1] : mins[1];
                if (World.PointContents(start) != (int)Contents.Solid)
                    goto realcheck;
            }

        c_yes++;
        return true; // we got out easy

    realcheck:
        c_no++;

        // check it for real...
        start[2] = mins[2];

        // the midpoint must be within 16 of the bottom
        start[0] = stop[0] = (mins[0] + maxs[0]) * 0.5f;
        start[1] = stop[1] = (mins[1] + maxs[1]) * 0.5f;
        stop[2] = start[2] - 2 * SvPhys.STEPSIZE;
        Trace_t trace = World.Move(start, MathLib.vec3_origin, MathLib.vec3_origin, stop, (int)ClipMoveType.NoMonsters, ent);

        if (trace.Fraction == 1)
            return false;
        float mid = trace.EndPos[2];
        float bottom = mid;

        // the corners must be within 16 of the midpoint
        for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            {
                start[0] = stop[0] = x != 0 ? maxs[0] : mins[0];
                start[1] = stop[1] = y != 0 ? maxs[1] : mins[1];

                trace = World.Move(start, MathLib.vec3_origin, MathLib.vec3_origin, stop, (int)ClipMoveType.NoMonsters, ent);

                if (trace.Fraction != 1 && trace.EndPos[2] > bottom)
                    bottom = trace.EndPos[2];
                if (trace.Fraction == 1 || mid - trace.EndPos[2] > SvPhys.STEPSIZE)
                    return false;
            }

        c_yes++;
        return true;
    }

    // SV_movestep -- called by monster program code. The move will be
    // adjusted for slopes and stairs, but if the move isn't possible, no
    // move is done and false is returned.
    public static bool MoveStep(EDict_t* ent, in Vec3_t move, bool relink)
    {
        Vec3_t oldorg = ent->V.Origin;
        Vec3_t neworg = ent->V.Origin + move;

        // flying monsters don't step up
        if (((int)ent->V.Flags & (SvPhys.FL_SWIM | SvPhys.FL_FLY)) != 0)
        {
            // try one move with vertical motion, then one without
            for (int i = 0; i < 2; i++)
            {
                neworg = ent->V.Origin + move;
                EDict_t* enemy = Progs.ProgToEdict(ent->V.Enemy);
                if (i == 0 && enemy != Sv.edicts)
                {
                    float dz = ent->V.Origin[2] - Progs.ProgToEdict(ent->V.Enemy)->V.Origin[2];
                    if (dz > 40)
                        neworg[2] -= 8;
                    if (dz < 30)
                        neworg[2] += 8;
                }
                Trace_t trace = World.Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, neworg, (int)ClipMoveType.Normal, ent);

                if (trace.Fraction == 1)
                {
                    if (((int)ent->V.Flags & SvPhys.FL_SWIM) != 0 && World.PointContents(trace.EndPos) == (int)Contents.Empty)
                        return false; // swim monster left water

                    ent->V.Origin = trace.EndPos;
                    if (relink)
                        World.LinkEdict(ent, true);
                    return true;
                }

                if (enemy == Sv.edicts)
                    break;
            }

            return false;
        }

        // push down from a step height above the wished position
        neworg[2] += SvPhys.STEPSIZE;
        Vec3_t end = neworg;
        end[2] -= SvPhys.STEPSIZE * 2;

        Trace_t downtrace = World.Move(neworg, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.Normal, ent);

        if (downtrace.AllSolid)
            return false;

        if (downtrace.StartSolid)
        {
            neworg[2] -= SvPhys.STEPSIZE;
            downtrace = World.Move(neworg, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.Normal, ent);
            if (downtrace.AllSolid || downtrace.StartSolid)
                return false;
        }
        if (downtrace.Fraction == 1)
        {
            // if monster had the ground pulled out, go ahead and fall
            if (((int)ent->V.Flags & SvPhys.FL_PARTIALGROUND) != 0)
            {
                ent->V.Origin = ent->V.Origin + move;
                if (relink)
                    World.LinkEdict(ent, true);
                ent->V.Flags = (int)ent->V.Flags & ~SvPhys.FL_ONGROUND;
                return true;
            }

            return false; // walked off an edge
        }

        // check point traces down for dangling corners
        ent->V.Origin = downtrace.EndPos;

        if (!CheckBottom(ent))
        {
            if (((int)ent->V.Flags & SvPhys.FL_PARTIALGROUND) != 0)
            { // entity had floor mostly pulled out from underneath it
                // and is trying to correct
                if (relink)
                    World.LinkEdict(ent, true);
                return true;
            }
            ent->V.Origin = oldorg;
            return false;
        }

        if (((int)ent->V.Flags & SvPhys.FL_PARTIALGROUND) != 0)
            ent->V.Flags = (int)ent->V.Flags & ~SvPhys.FL_PARTIALGROUND;
        ent->V.GroundEntity = Progs.EdictToProg(downtrace.Ent);

        // the move is ok
        if (relink)
            World.LinkEdict(ent, true);
        return true;
    }

    // PF_changeyaw's core logic (pr_cmds.c) -- see this class's type-level
    // comment for why it lives here rather than in PrCmds.cs.
    public static void ChangeYaw()
    {
        EDict_t* ent = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        float current = MathLib.anglemod(ent->V.Angles[1]);
        float ideal = ent->V.IdealYaw;
        float speed = ent->V.YawSpeed;

        if (current == ideal)
            return;
        float move = ideal - current;
        if (ideal > current)
        {
            if (move >= 180)
                move -= 360;
        }
        else
        {
            if (move <= -180)
                move += 360;
        }
        if (move > 0)
        {
            if (move > speed)
                move = speed;
        }
        else
        {
            if (move < -speed)
                move = -speed;
        }

        ent->V.Angles[1] = MathLib.anglemod(current + move);
    }

    // SV_StepDirection -- turns to the movement direction, and walks the
    // current distance if facing it.
    public static bool StepDirection(EDict_t* ent, float yaw, float dist)
    {
        ent->V.IdealYaw = yaw;
        ChangeYaw();

        yaw = yaw * MathLib.M_PI * 2 / 360;
        Vec3_t move = default;
        move[0] = MathF.Cos(yaw) * dist;
        move[1] = MathF.Sin(yaw) * dist;
        move[2] = 0;

        Vec3_t oldorigin = ent->V.Origin;
        if (MoveStep(ent, move, false))
        {
            float delta = ent->V.Angles[QuakeDef.YAW] - ent->V.IdealYaw;
            if (delta > 45 && delta < 315)
            { // not turned far enough, so don't take the step
                ent->V.Origin = oldorigin;
            }
            World.LinkEdict(ent, true);
            return true;
        }
        World.LinkEdict(ent, true);

        return false;
    }

    // SV_FixCheckBottom
    public static void FixCheckBottom(EDict_t* ent)
    {
        ent->V.Flags = (int)ent->V.Flags | SvPhys.FL_PARTIALGROUND;
    }

    // SV_NewChaseDir
    public static void NewChaseDir(EDict_t* actor, EDict_t* enemy, float dist)
    {
        float olddir = MathLib.anglemod((int)(actor->V.IdealYaw / 45) * 45);
        float turnaround = MathLib.anglemod(olddir - 180);

        float deltax = enemy->V.Origin[0] - actor->V.Origin[0];
        float deltay = enemy->V.Origin[1] - actor->V.Origin[1];

        float d1, d2;
        if (deltax > 10)
            d1 = 0;
        else if (deltax < -10)
            d1 = 180;
        else
            d1 = DI_NODIR;

        if (deltay < -10)
            d2 = 270;
        else if (deltay > 10)
            d2 = 90;
        else
            d2 = DI_NODIR;

        // try direct route
        if (d1 != DI_NODIR && d2 != DI_NODIR)
        {
            float tdir = d1 == 0 ? (d2 == 90 ? 45 : 315) : (d2 == 90 ? 135 : 215);

            if (tdir != turnaround && StepDirection(actor, tdir, dist))
                return;
        }

        // try other directions
        if (((Q.Rand() & 3) & 1) != 0 || MathF.Abs(deltay) > MathF.Abs(deltax))
        {
            float tmp = d1;
            d1 = d2;
            d2 = tmp;
        }

        if (d1 != DI_NODIR && d1 != turnaround && StepDirection(actor, d1, dist))
            return;

        if (d2 != DI_NODIR && d2 != turnaround && StepDirection(actor, d2, dist))
            return;

        // there is no direct path to the player, so pick another direction
        if (olddir != DI_NODIR && StepDirection(actor, olddir, dist))
            return;

        if ((Q.Rand() & 1) != 0) // randomly determine direction of search
        {
            for (float tdir = 0; tdir <= 315; tdir += 45)
                if (tdir != turnaround && StepDirection(actor, tdir, dist))
                    return;
        }
        else
        {
            for (float tdir = 315; tdir >= 0; tdir -= 45)
                if (tdir != turnaround && StepDirection(actor, tdir, dist))
                    return;
        }

        if (turnaround != DI_NODIR && StepDirection(actor, turnaround, dist))
            return;

        actor->V.IdealYaw = olddir; // can't move

        // if a bridge was pulled out from underneath a monster, it may not
        // have a valid standing position at all
        if (!CheckBottom(actor))
            FixCheckBottom(actor);
    }

    // SV_CloseEnough
    public static bool CloseEnough(EDict_t* ent, EDict_t* goal, float dist)
    {
        for (int i = 0; i < 3; i++)
        {
            if (goal->V.AbsMin[i] > ent->V.AbsMax[i] + dist)
                return false;
            if (goal->V.AbsMax[i] < ent->V.AbsMin[i] - dist)
                return false;
        }
        return true;
    }

    // SV_MoveToGoal
    public static void MoveToGoal()
    {
        EDict_t* ent = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        EDict_t* goal = Progs.ProgToEdict(ent->V.GoalEntity);
        float dist = Progs.pr_globals[Progs.OFS_PARM0];

        if (((int)ent->V.Flags & (SvPhys.FL_ONGROUND | SvPhys.FL_FLY | SvPhys.FL_SWIM)) == 0)
        {
            Progs.pr_globals[Progs.OFS_RETURN] = 0;
            return;
        }

        // if the next step hits the enemy, return immediately
        if (Progs.ProgToEdict(ent->V.Enemy) != Sv.edicts && CloseEnough(ent, goal, dist))
            return;

        // bump around...
        if ((Q.Rand() & 3) == 1 || !StepDirection(ent, ent->V.IdealYaw, dist))
        {
            NewChaseDir(ent, goal, dist);
        }
    }
}
