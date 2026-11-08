// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

/// <summary>
/// sv_phys.c -- per-frame entity physics simulation (pushmove, toss/bounce,
/// stepping monsters, and player movement). SV_Physics is the real per-frame
/// entry point, called once per server frame by Host.Frame (host.c's
/// Host_ServerFrame). QUAKE2-only branches
/// (SV_PushRotate, (int)MoveType.Follow/BOUNCEMISSILE, ent->v.basevelocity/
/// gravity) are dropped -- WinQuake never defines QUAKE2.
/// </summary>
public static unsafe class SvPhys
{
    // SV_PushMove's moved_edict[MAX_EDICTS]/moved_from[MAX_EDICTS] locals; static
    // NativeMemory since MAX_EDICTS is now 32000 (too large to allocate per call).
    internal static readonly EDict_t** pushMovedEdict = NativeMemory.AllocPointerArray<EDict_t>(QuakeDef.MAX_EDICTS);
    internal static readonly Vec3_t* pushMovedFrom = NativeMemory.AllocArrayZeroed<Vec3_t>(QuakeDef.MAX_EDICTS);

    // edict->movetype values (server.h) not already declared by World.cs
    // (which owns (int)MoveType.Push, its earliest consumer).

    // edict->flags (server.h)
    public const int FL_FLY = 1;            // FL_FLY (server.h)
    public const int FL_SWIM = 2;           // FL_SWIM (server.h)
    public const int FL_CONVEYOR = 4;       // FL_CONVEYOR (server.h)
    public const int FL_CLIENT = 8;         // FL_CLIENT (server.h)
    public const int FL_INWATER = 16;       // FL_INWATER (server.h)
    public const int FL_MONSTER = 32;       // FL_MONSTER (server.h)
    public const int FL_GODMODE = 64;       // FL_GODMODE (server.h)
    public const int FL_NOTARGET = 128;     // FL_NOTARGET (server.h)
    public const int FL_ITEM = 256;         // FL_ITEM (server.h)
    public const int FL_ONGROUND = 512;     // FL_ONGROUND (server.h)
    public const int FL_PARTIALGROUND = 1024; // FL_PARTIALGROUND (server.h) -- not all corners are valid
    public const int FL_WATERJUMP = 2048;   // FL_WATERJUMP (server.h) -- player jumping out of water
    public const int FL_JUMPRELEASED = 4096; // FL_JUMPRELEASED (server.h)

    private const float STOP_EPSILON = 0.1f;
    private const int MAX_CLIP_PLANES = 5;
    public const int STEPSIZE = 18;

    /*
    ================
    SV_CheckAllEnts

    Dead code in the original too (its only call site in SV_Physics is
    commented out there); ported anyway since it's a real, self-contained
    function.
    ================
    */

    // SV_CheckVelocity
    public static void CheckVelocity(EDict_t* ent)
    {
        for (int i = 0; i < 3; i++)
        {
            if (MathLib.IS_NAN(ent->V.Velocity[i]))
            {
                Con.Printf($"Got a NaN velocity on {ClassName(ent)}\n");
                ent->V.Velocity[i] = 0;
            }
            if (MathLib.IS_NAN(ent->V.Origin[i]))
            {
                Con.Printf($"Got a NaN origin on {ClassName(ent)}\n");
                ent->V.Origin[i] = 0;
            }
            if (ent->V.Velocity[i] > Sv.sv_maxvelocity->Value)
                ent->V.Velocity[i] = Sv.sv_maxvelocity->Value;
            else if (ent->V.Velocity[i] < -Sv.sv_maxvelocity->Value)
                ent->V.Velocity[i] = -Sv.sv_maxvelocity->Value;
        }
    }

    private static string ClassName(EDict_t* ent) =>
        CStr.ToString(Progs.pr_strings + ent->V.ClassName);

    // SV_RunThink
    public static bool RunThink(EDict_t* ent)
    {
        float thinktime = ent->V.NextThink;
        if (thinktime <= 0 || thinktime > Sv.time + Host.host_frametime)
            return true;

        if (thinktime < Sv.time)
            thinktime = (float)Sv.time;

        // cache the frame from before the think function was run
        float oldframe = ent->V.Frame; // johnfitz

        ent->V.NextThink = 0;
        Progs.pr_global_struct->Time = thinktime;
        Progs.pr_global_struct->Self = Progs.EdictToProg(ent);
        Progs.pr_global_struct->Other = Progs.EdictToProg(Sv.edicts);
        Progs.ExecuteProgram(ent->V.Think);

        // johnfitz -- PROTOCOL_FITZQUAKE
        // capture interval to nextthink here and send it to client for better
        // lerp timing, but only if interval is not 0.1 (which client assumes)

        // default is not to do it
        ent->SendInterval = false;

        // check if we need to send the interval
        if (ent->Free == 0 && ent->V.NextThink != 0 && ((int)ent->V.MoveType == (int)MoveType.Step || ent->V.Frame != oldframe))
        {
            int i = (int)MathF.Round((ent->V.NextThink - thinktime) * 255, MidpointRounding.ToEven);

            // 25 and 26 are close enough to 0.1 to not send
            if (i is >= 0 and < 256 && i != 25 && i != 26)
                ent->SendInterval = true;
        }

        return ent->Free == 0;
    }

    // SV_Impact
    public static void Impact(EDict_t* e1, EDict_t* e2)
    {
        int old_self = Progs.pr_global_struct->Self;
        int old_other = Progs.pr_global_struct->Other;

        Progs.pr_global_struct->Time = (float)Sv.time;
        if (e1->V.Touch != 0 && (int)e1->V.Solid != (int)SolidType.Not)
        {
            Progs.pr_global_struct->Self = Progs.EdictToProg(e1);
            Progs.pr_global_struct->Other = Progs.EdictToProg(e2);
            Progs.ExecuteProgram(e1->V.Touch);
        }

        if (e2->V.Touch != 0 && (int)e2->V.Solid != (int)SolidType.Not)
        {
            Progs.pr_global_struct->Self = Progs.EdictToProg(e2);
            Progs.pr_global_struct->Other = Progs.EdictToProg(e1);
            Progs.ExecuteProgram(e2->V.Touch);
        }

        Progs.pr_global_struct->Self = old_self;
        Progs.pr_global_struct->Other = old_other;
    }

    // ClipVelocity
    public static int ClipVelocity(in Vec3_t @in, in Vec3_t normal, ref Vec3_t @out, float overbounce)
    {
        int blocked = 0;
        if (normal[2] > 0)
            blocked |= 1; // floor
        if (normal[2] == 0)
            blocked |= 2; // step

        float backoff = Vector3.Dot(@in, normal) * overbounce;

        // per lane: change = normal[i] * backoff; out[i] = in[i] - change;
        // then zero any lane inside (-STOP_EPSILON, STOP_EPSILON)
        Vector3 result = @in - normal * backoff;
        Vector3 nearZero = Vector3.GreaterThan(result, new Vector3(-STOP_EPSILON))
                         & Vector3.LessThan(result, new Vector3(STOP_EPSILON));
        @out = Vector3.ConditionalSelect(nearZero, Vector3.Zero, result);

        return blocked;
    }

    // SV_FlyMove
    public static int FlyMove(EDict_t* ent, float time, Trace_t* steptrace)
    {
        const int numbumps = 4;
        int blocked = 0;
        Vec3_t original_velocity = ent->V.Velocity;
        Vec3_t primal_velocity = ent->V.Velocity;
        int numplanes = 0;
        Vec3_t* planes = stackalloc Vec3_t[MAX_CLIP_PLANES];
        float time_left = time;

        for (int bumpcount = 0; bumpcount < numbumps; bumpcount++)
        {
            if (ent->V.Velocity == Vec3_t.Zero)
                break;

            Vec3_t end = ent->V.Origin + time_left * ent->V.Velocity;

            Trace_t trace = World.Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.Normal, ent);

            if (trace.AllSolid)
            { // entity is trapped in another solid
                ent->V.Velocity = default;
                return 3;
            }

            if (trace.Fraction > 0)
            { // actually covered some distance
                ent->V.Origin = trace.EndPos;
                original_velocity = ent->V.Velocity;
                numplanes = 0;
            }

            if (trace.Fraction == 1)
                break; // moved the entire distance

            if (trace.Ent == null)
                Sys.Error("SV_FlyMove: !trace.ent"u8);

            if (trace.Plane.Normal[2] > 0.7f)
            {
                blocked |= 1; // floor
                if ((int)trace.Ent->V.Solid == (int)SolidType.Bsp)
                {
                    ent->V.Flags = (int)ent->V.Flags | FL_ONGROUND;
                    ent->V.GroundEntity = Progs.EdictToProg(trace.Ent);
                }
            }
            if (trace.Plane.Normal[2] == 0)
            {
                blocked |= 2; // step
                if (steptrace != null)
                    *steptrace = trace; // save for player extrafriction
            }

            // run the impact function
            Impact(ent, trace.Ent);
            if (ent->Free != 0)
                break; // removed by the impact function

            time_left -= time_left * trace.Fraction;

            // clipped to another plane
            if (numplanes >= MAX_CLIP_PLANES)
            { // this shouldn't really happen
                ent->V.Velocity = default;
                return 3;
            }

            planes[numplanes] = trace.Plane.Normal;
            numplanes++;

            // modify original_velocity so it parallels all of the clip planes
            Vec3_t new_velocity = default;
            int i2;
            for (i2 = 0; i2 < numplanes; i2++)
            {
                ClipVelocity(original_velocity, planes[i2], ref new_velocity, 1);
                int j;
                for (j = 0; j < numplanes; j++)
                    if (j != i2 && Vector3.Dot(new_velocity, planes[j]) < 0)
                        break; // not ok
                if (j == numplanes)
                    break;
            }

            if (i2 != numplanes)
            { // go along this plane
                ent->V.Velocity = new_velocity;
            }
            else
            { // go along the crease
                if (numplanes != 2)
                {
                    ent->V.Velocity = default;
                    return 7;
                }
                Vec3_t dir = Vector3.Cross(planes[0], planes[1]);
                float d = Vector3.Dot(dir, ent->V.Velocity);
                ent->V.Velocity = dir * d;
            }

            // if original velocity is against the original velocity, stop dead
            // to avoid tiny oscillations in sloping corners
            if (Vector3.Dot(ent->V.Velocity, primal_velocity) <= 0)
            {
                ent->V.Velocity = default;
                return blocked;
            }
        }

        return blocked;
    }

    // SV_AddGravity -- GetEdictFieldValue(ent,"gravity") (a QUAKE2-only
    // per-entity override) always misses against the stock id1 progs.dat,
    // which never declares that field, so this always takes the original's
    // else branch (ent_gravity = 1.0).
    public static void AddGravity(EDict_t* ent)
    {
        const float ent_gravity = 1.0f;
        ent->V.Velocity[2] -= ent_gravity * Sv.sv_gravity->Value * Host.host_frametime;
    }

    /*
    ===============================================================================
    PUSHMOVE
    ===============================================================================
    */

    // SV_PushEntity -- does not change the entity's velocity at all
    public static Trace_t PushEntity(EDict_t* ent, in Vec3_t push)
    {
        Vec3_t end = ent->V.Origin + push;

        Trace_t trace;
        if ((int)ent->V.MoveType == (int)MoveType.FlyMissile)
            trace = World.Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.Missile, ent);
        else if ((int)ent->V.Solid == (int)SolidType.Trigger || (int)ent->V.Solid == (int)SolidType.Not)
            // only clip against bmodels
            trace = World.Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.NoMonsters, ent);
        else
            trace = World.Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.Normal, ent);

        ent->V.Origin = trace.EndPos;
        World.LinkEdict(ent, true);

        if (trace.Ent != null)
            Impact(ent, trace.Ent);

        return trace;
    }

    // SV_PushMove
    public static void PushMove(EDict_t* pusher, float movetime)
    {
        if (pusher->V.Velocity == Vec3_t.Zero)
        {
            pusher->V.LTime += movetime;
            return;
        }

        Vec3_t move = pusher->V.Velocity * movetime;
        Vec3_t mins = pusher->V.AbsMin + move;
        Vec3_t maxs = pusher->V.AbsMax + move;

        Vec3_t pushorig = pusher->V.Origin;

        // move the pusher to its final position
        Vec3_t newOrigin = pusher->V.Origin + move;
        pusher->V.Origin = newOrigin;
        pusher->V.LTime += movetime;
        World.LinkEdict(pusher, false);

        // see if any solid entities are inside the final position
        int num_moved = 0;
        EDict_t** moved_edict = pushMovedEdict;
        Vec3_t* moved_from = pushMovedFrom;

        EDict_t* check = Progs.NextEdict(Sv.edicts);
        for (int e = 1; e < Sv.num_edicts; e++, check = Progs.NextEdict(check))
        {
            if (check->Free != 0)
                continue;
            int checkMoveType = (int)check->V.MoveType;
            if (checkMoveType == (int)MoveType.Push || checkMoveType == (int)MoveType.None || checkMoveType == (int)MoveType.NoClip)
                continue;

            // if the entity is standing on the pusher, it will definitely be moved
            bool standingOnPusher = ((int)check->V.Flags & FL_ONGROUND) != 0
                && Progs.ProgToEdict(check->V.GroundEntity) == pusher;

            if (!standingOnPusher)
            {
                if (check->V.AbsMin[0] >= maxs[0]
                    || check->V.AbsMin[1] >= maxs[1]
                    || check->V.AbsMin[2] >= maxs[2]
                    || check->V.AbsMax[0] <= mins[0]
                    || check->V.AbsMax[1] <= mins[1]
                    || check->V.AbsMax[2] <= mins[2])
                    continue;

                // see if the ent's bbox is inside the pusher's final position
                if (World.TestEntityPosition(check) == null)
                    continue;
            }

            // remove the onground flag for non-players
            if ((int)check->V.MoveType != (int)MoveType.Walk)
                check->V.Flags = (int)check->V.Flags & ~FL_ONGROUND;

            Vec3_t entorig = check->V.Origin;
            moved_from[num_moved] = check->V.Origin;
            moved_edict[num_moved] = check;
            num_moved++;

            // try moving the contacted entity
            pusher->V.Solid = (int)SolidType.Not;
            PushEntity(check, move);
            pusher->V.Solid = (int)SolidType.Bsp;

            // if it is still inside the pusher, block
            EDict_t* block = World.TestEntityPosition(check);
            if (block != null)
            { // fail the move
                if (check->V.Mins[0] == check->V.Maxs[0])
                    continue;
                if ((int)check->V.Solid == (int)SolidType.Not || (int)check->V.Solid == (int)SolidType.Trigger)
                { // corpse
                    check->V.Mins[0] = check->V.Mins[1] = 0;
                    check->V.Maxs = check->V.Mins;
                    continue;
                }

                check->V.Origin = entorig;
                World.LinkEdict(check, true);

                pusher->V.Origin = pushorig;
                World.LinkEdict(pusher, false);
                pusher->V.LTime -= movetime;

                // if the pusher has a "blocked" function, call it
                // otherwise, just stay in place until the obstacle is gone
                if (pusher->V.Blocked != 0)
                {
                    Progs.pr_global_struct->Self = Progs.EdictToProg(pusher);
                    Progs.pr_global_struct->Other = Progs.EdictToProg(check);
                    Progs.ExecuteProgram(pusher->V.Blocked);
                }

                // move back any entities we already moved
                for (int i = 0; i < num_moved; i++)
                {
                    moved_edict[i]->V.Origin = moved_from[i];
                    World.LinkEdict(moved_edict[i], false);
                }
                return;
            }
        }
    }

    // SV_Physics_Pusher
    public static void Physics_Pusher(EDict_t* ent)
    {
        float oldltime = ent->V.LTime;
        float thinktime = ent->V.NextThink;
        float movetime;

        if (thinktime < ent->V.LTime + Host.host_frametime)
        {
            movetime = thinktime - ent->V.LTime;
            if (movetime < 0)
                movetime = 0;
        }
        else
        {
            movetime = Host.host_frametime;
        }

        if (movetime != 0)
            PushMove(ent, movetime); // advances ent->v.ltime if not blocked

        if (thinktime > oldltime && thinktime <= ent->V.LTime)
        {
            ent->V.NextThink = 0;
            Progs.pr_global_struct->Time = (float)Sv.time;
            Progs.pr_global_struct->Self = Progs.EdictToProg(ent);
            Progs.pr_global_struct->Other = Progs.EdictToProg(Sv.edicts);
            Progs.ExecuteProgram(ent->V.Think);
            if (ent->Free != 0)
                return;
        }
    }

    /*
    ===============================================================================
    CLIENT MOVEMENT
    ===============================================================================
    */

    // SV_CheckStuck -- a big hack to try and fix the rare case of getting
    // stuck in the world clipping hull.
    public static void CheckStuck(EDict_t* ent)
    {
        if (World.TestEntityPosition(ent) == null)
        {
            ent->V.OldOrigin = ent->V.Origin;
            return;
        }

        Vec3_t org = ent->V.Origin;
        ent->V.Origin = ent->V.OldOrigin;
        if (World.TestEntityPosition(ent) == null)
        {
            Con.DPrintf("Unstuck.\n"u8);
            World.LinkEdict(ent, true);
            return;
        }

        for (int z = 0; z < 18; z++)
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    ent->V.Origin[0] = org[0] + i;
                    ent->V.Origin[1] = org[1] + j;
                    ent->V.Origin[2] = org[2] + z;
                    if (World.TestEntityPosition(ent) == null)
                    {
                        Con.DPrintf("Unstuck.\n"u8);
                        World.LinkEdict(ent, true);
                        return;
                    }
                }

        ent->V.Origin = org;
        Con.DPrintf("player is stuck.\n"u8);
    }

    // SV_CheckWater
    public static bool CheckWater(EDict_t* ent)
    {
        Vec3_t point = default;
        point[0] = ent->V.Origin[0];
        point[1] = ent->V.Origin[1];
        point[2] = ent->V.Origin[2] + ent->V.Mins[2] + 1;

        ent->V.WaterLevel = 0;
        ent->V.WaterType = (int)Contents.Empty;
        int cont = World.PointContents(point);
        if (cont <= (int)Contents.Water)
        {
            ent->V.WaterType = cont;
            ent->V.WaterLevel = 1;
            point[2] = ent->V.Origin[2] + (ent->V.Mins[2] + ent->V.Maxs[2]) * 0.5f;
            cont = World.PointContents(point);
            if (cont <= (int)Contents.Water)
            {
                ent->V.WaterLevel = 2;
                point[2] = ent->V.Origin[2] + ent->V.ViewOfs[2];
                cont = World.PointContents(point);
                if (cont <= (int)Contents.Water)
                    ent->V.WaterLevel = 3;
            }
        }

        return ent->V.WaterLevel > 1;
    }

    // SV_WallFriction
    public static void WallFriction(EDict_t* ent, in Trace_t trace)
    {
        MathLib.AngleVectors(ent->V.VAngle, out Vec3_t forward, out Vec3_t right, out Vec3_t up);
        float d = Vector3.Dot(trace.Plane.Normal, forward);

        d += 0.5f;
        if (d >= 0)
            return;

        // cut the tangential velocity
        float i = Vector3.Dot(trace.Plane.Normal, ent->V.Velocity);
        Vec3_t into = trace.Plane.Normal * i;
        Vec3_t side = ent->V.Velocity - into;

        ent->V.Velocity[0] = side[0] * (1 + d);
        ent->V.Velocity[1] = side[1] * (1 + d);
    }

    // SV_TryUnstick -- player has come to a dead stop, possibly due to the
    // problem with limited float precision at some angle joins in the BSP
    // hull. Try fixing by pushing one pixel in each direction.
    public static int TryUnstick(EDict_t* ent, in Vec3_t oldvel)
    {
        Vec3_t oldorg = ent->V.Origin;

        for (int i = 0; i < 8; i++)
        {
            Vec3_t dir = default;
            switch (i)
            {
                case 0: dir[0] = 2; dir[1] = 0; break;
                case 1: dir[0] = 0; dir[1] = 2; break;
                case 2: dir[0] = -2; dir[1] = 0; break;
                case 3: dir[0] = 0; dir[1] = -2; break;
                case 4: dir[0] = 2; dir[1] = 2; break;
                case 5: dir[0] = -2; dir[1] = 2; break;
                case 6: dir[0] = 2; dir[1] = -2; break;
                case 7: dir[0] = -2; dir[1] = -2; break;
            }

            PushEntity(ent, dir);

            // retry the original move
            ent->V.Velocity[0] = oldvel[0];
            ent->V.Velocity[1] = oldvel[1];
            ent->V.Velocity[2] = 0;
            Trace_t steptrace = default;
            int clip = FlyMove(ent, 0.1f, &steptrace);

            if (MathF.Abs(oldorg[1] - ent->V.Origin[1]) > 4
                || MathF.Abs(oldorg[0] - ent->V.Origin[0]) > 4)
                return clip;

            // go back to the original pos and try again
            ent->V.Origin = oldorg;
        }

        ent->V.Velocity = default;
        return 7; // still not moving
    }

    // SV_WalkMove -- only used by players
    public static void WalkMove(EDict_t* ent)
    {
        int oldonground = (int)ent->V.Flags & FL_ONGROUND;
        ent->V.Flags = (int)ent->V.Flags & ~FL_ONGROUND;

        Vec3_t oldorg = ent->V.Origin;
        Vec3_t oldvel = ent->V.Velocity;

        Trace_t steptrace = default;
        int clip = FlyMove(ent, Host.host_frametime, &steptrace);

        if ((clip & 2) == 0)
            return; // move didn't block on a step

        if (oldonground == 0 && ent->V.WaterLevel == 0)
            return; // don't stair up while jumping

        if ((int)ent->V.MoveType != (int)MoveType.Walk)
            return; // gibbed by a trigger

        if (Sv.sv_nostep->Value != 0)
            return;

        if (((int)Sv.sv_player->V.Flags & FL_WATERJUMP) != 0)
            return;

        Vec3_t nosteporg = ent->V.Origin;
        Vec3_t nostepvel = ent->V.Velocity;

        // try moving up and forward to go up a step
        ent->V.Origin = oldorg; // back to start pos

        Vec3_t upmove = default, downmove = default;
        upmove[2] = STEPSIZE;
        downmove[2] = -STEPSIZE + oldvel[2] * Host.host_frametime;

        // move up
        PushEntity(ent, upmove); // FIXME: don't link?

        // move forward
        ent->V.Velocity[0] = oldvel[0];
        ent->V.Velocity[1] = oldvel[1];
        ent->V.Velocity[2] = 0;
        clip = FlyMove(ent, Host.host_frametime, &steptrace);

        // check for stuckness, possibly due to the limited precision of floats
        // in the clipping hulls
        if (clip != 0)
        {
            if (MathF.Abs(oldorg[1] - ent->V.Origin[1]) < 0.03125f
                && MathF.Abs(oldorg[0] - ent->V.Origin[0]) < 0.03125f)
            { // stepping up didn't make any progress
                clip = TryUnstick(ent, oldvel);
            }
        }

        // extra friction based on view angle
        if ((clip & 2) != 0)
            WallFriction(ent, steptrace);

        // move down
        Trace_t downtrace = PushEntity(ent, downmove); // FIXME: don't link?

        if (downtrace.Plane.Normal[2] > 0.7f)
        {
            if ((int)ent->V.Solid == (int)SolidType.Bsp)
            {
                ent->V.Flags = (int)ent->V.Flags | FL_ONGROUND;
                ent->V.GroundEntity = Progs.EdictToProg(downtrace.Ent);
            }
        }
        else
        {
            // if the push down didn't end up on good ground, use the move without
            // the step up. This happens near wall / slope combinations, and can
            // cause the player to hop up higher on a slope too steep to climb
            ent->V.Origin = nosteporg;
            ent->V.Velocity = nostepvel;
        }
    }

    // SV_Physics_Client -- player character actions
    public static void Physics_Client(EDict_t* ent, int num)
    {
        if (!Svs.clients[num - 1].Active)
            return; // unconnected slot

        // call standard client pre-think
        Progs.pr_global_struct->Time = (float)Sv.time;
        Progs.pr_global_struct->Self = Progs.EdictToProg(ent);
        Progs.ExecuteProgram(Progs.pr_global_struct->PlayerPreThink);

        // do a move
        CheckVelocity(ent);

        // decide which move function to call
        switch ((int)ent->V.MoveType)
        {
            case (int)MoveType.None:
                if (!RunThink(ent))
                    return;
                break;

            case (int)MoveType.Walk:
                if (!RunThink(ent))
                    return;
                if (!CheckWater(ent) && ((int)ent->V.Flags & FL_WATERJUMP) == 0)
                    AddGravity(ent);
                CheckStuck(ent);
                WalkMove(ent);
                break;

            case (int)MoveType.Toss:
            case (int)MoveType.Bounce:
                Physics_Toss(ent);
                break;

            case (int)MoveType.Fly:
                if (!RunThink(ent))
                    return;
                FlyMove(ent, Host.host_frametime, null);
                break;

            case (int)MoveType.NoClip:
                if (!RunThink(ent))
                    return;
                ent->V.Origin += Host.host_frametime * ent->V.Velocity;
                break;

            default:
                Sys.Error($"SV_Physics_client: bad movetype {(int)ent->V.MoveType}");
                break;
        }

        // call standard player post-think
        World.LinkEdict(ent, true);

        Progs.pr_global_struct->Time = (float)Sv.time;
        Progs.pr_global_struct->Self = Progs.EdictToProg(ent);
        Progs.ExecuteProgram(Progs.pr_global_struct->PlayerPostThink);
    }

    // SV_Physics_None -- non-moving objects can only think
    public static void Physics_None(EDict_t* ent) => RunThink(ent);

    // SV_Physics_Noclip -- a moving object that doesn't obey physics
    public static void Physics_Noclip(EDict_t* ent)
    {
        if (!RunThink(ent))
            return;

        ent->V.Angles += Host.host_frametime * ent->V.AVelocity;
        ent->V.Origin += Host.host_frametime * ent->V.Velocity;

        World.LinkEdict(ent, false);
    }

    /*
    ==============================================================================
    TOSS / BOUNCE
    ==============================================================================
    */

    // SV_CheckWaterTransition
    public static void CheckWaterTransition(EDict_t* ent)
    {
        int cont = World.PointContents(ent->V.Origin);

        if ((int)ent->V.WaterType == 0)
        { // just spawned here
            ent->V.WaterType = cont;
            ent->V.WaterLevel = 1;
            return;
        }

        if (cont <= (int)Contents.Water)
        {
            if ((int)ent->V.WaterType == (int)Contents.Empty)
                StartSound(ent, 0, "misc/h2ohit1.wav"u8, 255, 1);
            ent->V.WaterType = cont;
            ent->V.WaterLevel = 1;
        }
        else
        {
            if ((int)ent->V.WaterType != (int)Contents.Empty)
                StartSound(ent, 0, "misc/h2ohit1.wav"u8, 255, 1);
            ent->V.WaterType = (int)Contents.Empty;
            ent->V.WaterLevel = cont;
        }
    }

    /*
    ==================
    SV_StartSound

    Each entity can have eight independant sound sources, like voice,
    weapon, feet, etc.

    Channel 0 is an auto-allocate channel, the others override anything
    allready running on that entity/channel pair.

    An attenuation of 0 will play full volume everywhere in the level.
    Larger attenuations will drop off. (max 4 attenuation)
    ==================
    */
    public static void StartSound(EDict_t* entity, int channel, Utf8String sample, int volume, float attenuation)
    {
        if (Sv.datagram->CurSize > Sv.host_protocol_datagram_maxsize - 16)
            return;

        // find precache number for sound
        int sound_num;
        for (sound_num = 1; sound_num < QuakeDef.MAX_SOUNDS && Sv.sound_precache[sound_num] != null; sound_num++)
            if (Q.StrCmp(sample, Sv.sound_precache[sound_num]) == 0)
                break;

        if (sound_num == QuakeDef.MAX_SOUNDS || Sv.sound_precache[sound_num] == null)
        {
            Con.Printf($"SV_StartSound: {sample} not precacheed\n");
            return;
        }

        int ent = Progs.NumForEdict(entity);

        int field_mask = 0;
        if (volume != 255) // DEFAULT_SOUND_PACKET_VOLUME
            field_mask |= Protocol.SND_VOLUME;
        if (attenuation != 1.0f) // DEFAULT_SOUND_PACKET_ATTENUATION
            field_mask |= Protocol.SND_ATTENUATION;

        // johnfitz -- PROTOCOL_FITZQUAKE
        if (ent >= Protocol.MAX_P15_SOUND_ENTITIES)
        {
            // protocol 15: (ent << 3) | channel is sent as a short, so the sound is dropped
            if (Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE)
                return;
            field_mask |= Protocol.SND_LARGEENTITY;
        }

        if (sound_num >= Protocol.MAX_P15_SOUNDS || channel >= 8)
        {
            if (Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE)
                return;
            field_mask |= Protocol.SND_LARGESOUND;
        }

        // johnfitz -- the entity/channel are only packed together when small
        if ((field_mask & Protocol.SND_LARGEENTITY) == 0)
            channel = (ent << 3) | channel;

        // directed messages go only to the entity the are targeted on
        SizeBuf_t* dg = Sv.datagram;
        Msg.WriteByte(dg, (int)Svc.Sound);
        Msg.WriteByte(dg, field_mask);
        if ((field_mask & Protocol.SND_VOLUME) != 0)
            Msg.WriteByte(dg, volume);
        if ((field_mask & Protocol.SND_ATTENUATION) != 0)
            Msg.WriteByte(dg, (int)(attenuation * 64));
        if ((field_mask & Protocol.SND_LARGEENTITY) != 0)
        {
            Msg.WriteShort(dg, ent);
            Msg.WriteByte(dg, channel);
        }
        else
            Msg.WriteShort(dg, channel);
        if ((field_mask & Protocol.SND_LARGESOUND) != 0)
            Msg.WriteShort(dg, sound_num);
        else
            Msg.WriteByte(dg, sound_num);
        for (int i = 0; i < 3; i++)
            Msg.WriteCoord(dg, entity->V.Origin[i] + 0.5f * (entity->V.Mins[i] + entity->V.Maxs[i]));
    }

    // SV_Physics_Toss -- toss, bounce, and fly movement. When onground, do
    // nothing.
    public static void Physics_Toss(EDict_t* ent)
    {
        // regular thinking
        if (!RunThink(ent))
            return;

        // if onground, return without moving
        if (((int)ent->V.Flags & FL_ONGROUND) != 0)
            return;

        CheckVelocity(ent);

        // add gravity
        if ((int)ent->V.MoveType != (int)MoveType.Fly && (int)ent->V.MoveType != (int)MoveType.FlyMissile)
            AddGravity(ent);

        // move angles
        ent->V.Angles += Host.host_frametime * ent->V.AVelocity;

        // move origin
        Vec3_t move = ent->V.Velocity * Host.host_frametime;
        Trace_t trace = PushEntity(ent, move);
        if (trace.Fraction == 1)
            return;
        if (ent->Free != 0)
            return;

        float backoff = (int)ent->V.MoveType == (int)MoveType.Bounce ? 1.5f : 1f;

        ClipVelocity(ent->V.Velocity, trace.Plane.Normal, ref ent->V.Velocity, backoff);

        // stop if on ground
        if (trace.Plane.Normal[2] > 0.7f)
        {
            if (ent->V.Velocity[2] < 60 || (int)ent->V.MoveType != (int)MoveType.Bounce)
            {
                ent->V.Flags = (int)ent->V.Flags | FL_ONGROUND;
                ent->V.GroundEntity = Progs.EdictToProg(trace.Ent);
                ent->V.Velocity = default;
                ent->V.AVelocity = default;
            }
        }

        // check for in water
        CheckWaterTransition(ent);
    }

    /*
    ===============================================================================
    STEPPING MOVEMENT
    ===============================================================================
    */

    // SV_Physics_Step -- monsters freefall when they don't have a ground
    // entity, otherwise all movement is done with discrete steps. Also used
    // for objects that have become still on the ground, but will fall if
    // the floor is pulled out from under them.
    public static void Physics_Step(EDict_t* ent)
    {
        // freefall if not onground
        if (((int)ent->V.Flags & (FL_ONGROUND | FL_FLY | FL_SWIM)) == 0)
        {
            bool hitsound = ent->V.Velocity[2] < Sv.sv_gravity->Value * -0.1f;

            AddGravity(ent);
            CheckVelocity(ent);
            FlyMove(ent, Host.host_frametime, null);
            World.LinkEdict(ent, true);

            if (((int)ent->V.Flags & FL_ONGROUND) != 0) // just hit ground
            {
                if (hitsound)
                    StartSound(ent, 0, "demon/dland2.wav"u8, 255, 1);
            }
        }

        // regular thinking
        RunThink(ent);

        CheckWaterTransition(ent);
    }

    // SV_Physics
    public static void Physics()
    {
        // let the progs know that a new frame has started
        Progs.pr_global_struct->Self = Progs.EdictToProg(Sv.edicts);
        Progs.pr_global_struct->Other = Progs.EdictToProg(Sv.edicts);
        Progs.pr_global_struct->Time = (float)Sv.time;
        Progs.ExecuteProgram(Progs.pr_global_struct->StartFrame);

        // treat each object in turn
        EDict_t* ent = Sv.edicts;
        for (int i = 0; i < Sv.num_edicts; i++, ent = Progs.NextEdict(ent))
        {
            if (ent->Free != 0)
                continue;

            if (Progs.pr_global_struct->ForceRetouch != 0)
                World.LinkEdict(ent, true); // force retouch even for stationary

            if (i > 0 && i <= Svs.maxclients)
                Physics_Client(ent, i);
            else if ((int)ent->V.MoveType == (int)MoveType.Push)
                Physics_Pusher(ent);
            else if ((int)ent->V.MoveType == (int)MoveType.None)
                Physics_None(ent);
            else if ((int)ent->V.MoveType == (int)MoveType.NoClip)
                Physics_Noclip(ent);
            else if ((int)ent->V.MoveType == (int)MoveType.Step)
                Physics_Step(ent);
            else if ((int)ent->V.MoveType == (int)MoveType.Toss
                     || (int)ent->V.MoveType == (int)MoveType.Bounce
                     || (int)ent->V.MoveType == (int)MoveType.Fly
                     || (int)ent->V.MoveType == (int)MoveType.FlyMissile)
                Physics_Toss(ent);
            else
                Sys.Error($"SV_Physics: bad movetype {(int)ent->V.MoveType}");
        }

        if (Progs.pr_global_struct->ForceRetouch != 0)
            Progs.pr_global_struct->ForceRetouch--;

        Sv.time += Host.host_frametime;
    }
}
