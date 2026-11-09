// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// sv_user.c -- server-side player movement and the network message pump.
/// SV_ReadClientMove/SV_ReadClientMessage (NET_GetMessage on each client's
/// connection, MSG_Read*, and the clc_*/(int)Clc.StringCmd dispatch switch) are
/// fully ported below, using Net.GetMessage/Msg.Read*/Protocol.cs's clc_*
/// constants/Cmd.ExecuteString -- the same primitives ClParse.cs already
/// uses client-side. SV_RunClients drives every active client through
/// ReadClientMessage first (dropping it on a bad read, matching the
/// original), then gates ClientThink on Client_t.Spawned exactly like
/// sv_user.c's host_client->spawned check. Everything that's pure per-frame
/// movement math given a usercmd_t and sv_player -- SV_AirMove/SV_WaterMove/
/// SV_Accelerate/SV_UserFriction/SV_SetIdealPitch/SV_ClientThink -- is fully
/// ported and callable today.
/// </summary>
public static unsafe class SvUser
{
    private const int MAX_FORWARD = 6;

    private static Vec3_t forward, right, up;
    private static Vec3_t wishdir;
    private static float wishspeed;

    private static bool onground;
    private static UserCmd_t cmd; // cmd (sv_user.c) -- assigned in SV_ClientThink

    // origin/velocity (sv_user.c's file-scope float* aliases into
    // sv_player->v.origin/velocity) -- set at the top of ClientThink.
    private static Vec3_t* originPtr;
    private static Vec3_t* velocityPtr;

    // SV_SetIdealPitch
    public static void SetIdealPitch()
    {
        if (((int)Sv.sv_player->V.Flags & SvPhys.FL_ONGROUND) == 0)
            return;

        float angleval = Sv.sv_player->V.Angles[QuakeDef.YAW] * MathLib.M_PI * 2 / 360;
        float sinval = MathF.Sin(angleval);
        float cosval = MathF.Cos(angleval);

        float* z = stackalloc float[MAX_FORWARD];
        Vec3_t top = default, bottom = default;

        int i;
        for (i = 0; i < MAX_FORWARD; i++)
        {
            top[0] = Sv.sv_player->V.Origin[0] + cosval * (i + 3) * 12;
            top[1] = Sv.sv_player->V.Origin[1] + sinval * (i + 3) * 12;
            top[2] = Sv.sv_player->V.Origin[2] + Sv.sv_player->V.ViewOfs[2];

            bottom[0] = top[0];
            bottom[1] = top[1];
            bottom[2] = top[2] - 160;

            Trace_t tr = World.Move(top, MathLib.vec3_origin, MathLib.vec3_origin, bottom, (int)ClipMoveType.NoMonsters, Sv.sv_player);
            if (tr.AllSolid)
                return; // looking at a wall, leave ideal the way it was

            if (tr.Fraction == 1)
                return; // near a dropoff

            z[i] = top[2] + tr.Fraction * (bottom[2] - top[2]);
        }

        int dir = 0;
        int steps = 0;
        for (int j = 1; j < i; j++)
        {
            // step/dir are `int` in the original too, truncating a float
            // difference -- preserved faithfully, quirks included.
            int step = (int)(z[j] - z[j - 1]);
            if (step > -ON_EPSILON && step < ON_EPSILON)
                continue;

            if (dir != 0 && (step - dir > ON_EPSILON || step - dir < -ON_EPSILON))
                return; // mixed changes

            steps++;
            dir = step;
        }

        if (dir == 0)
        {
            Sv.sv_player->V.IdealPitch = 0;
            return;
        }

        if (steps < 2)
            return;
        Sv.sv_player->V.IdealPitch = -dir * Sv.sv_idealpitchscale->Value;
    }

    // SV_UserFriction
    public static void UserFriction()
    {
        Vec3_t vel = *velocityPtr;
        float speed = MathF.Sqrt(vel[0] * vel[0] + vel[1] * vel[1]);
        if (speed == 0)
            return;

        // if the leading edge is over a dropoff, increase friction
        Vec3_t start = default, stop = default;
        start[0] = stop[0] = (*originPtr)[0] + vel[0] / speed * 16;
        start[1] = stop[1] = (*originPtr)[1] + vel[1] / speed * 16;
        start[2] = (*originPtr)[2] + Sv.sv_player->V.Mins[2];
        stop[2] = start[2] - 34;

        Trace_t trace = World.Move(start, MathLib.vec3_origin, MathLib.vec3_origin, stop, (int)ClipMoveType.NoMonsters, Sv.sv_player);

        float friction = trace.Fraction == 1.0f
            ? Sv.sv_friction->Value * Sv.sv_edgefriction->Value
            : Sv.sv_friction->Value;

        // apply friction
        float control = speed < Sv.sv_stopspeed->Value ? Sv.sv_stopspeed->Value : speed;
        float newspeed = speed - Host.host_frametime * control * friction;

        if (newspeed < 0)
            newspeed = 0;
        newspeed /= speed;

        *velocityPtr *= newspeed;
    }

    // SV_Accelerate
    public static void Accelerate()
    {
        float currentspeed = Vector3.Dot(*velocityPtr, wishdir);
        float addspeed = wishspeed - currentspeed;
        if (addspeed <= 0)
            return;
        float accelspeed = Sv.sv_accelerate->Value * Host.host_frametime * wishspeed;
        if (accelspeed > addspeed)
            accelspeed = addspeed;

        *velocityPtr += accelspeed * wishdir;
    }

    // SV_AirAccelerate
    public static void AirAccelerate(ref Vec3_t wishveloc)
    {
        float wishspd = MathLib.VectorNormalize((Vec3_t*)Unsafe.AsPointer(ref wishveloc));
        if (wishspd > 30)
            wishspd = 30;
        float currentspeed = Vector3.Dot(*velocityPtr, wishveloc);
        float addspeed = wishspd - currentspeed;
        if (addspeed <= 0)
            return;
        float accelspeed = Sv.sv_accelerate->Value * wishspeed * Host.host_frametime;
        if (accelspeed > addspeed)
            accelspeed = addspeed;

        *velocityPtr += accelspeed * wishveloc;
    }

    // DropPunchAngle
    public static void DropPunchAngle()
    {
        float len = MathLib.VectorNormalize(&Sv.sv_player->V.PunchAngle);

        len -= 10 * Host.host_frametime;
        if (len < 0)
            len = 0;
        Sv.sv_player->V.PunchAngle *= len;
    }

    // SV_WaterMove
    public static void WaterMove()
    {
        Vec3_t wishvel = default;

        MathLib.AngleVectors(Sv.sv_player->V.VAngle, out forward, out right, out up);

        wishvel = forward * cmd.ForwardMove + right * cmd.SideMove;

        if (cmd.ForwardMove == 0 && cmd.SideMove == 0 && cmd.UpMove == 0)
            wishvel[2] -= 60; // drift towards bottom
        else
            wishvel[2] += cmd.UpMove;

        float wishspd = wishvel.Length();
        if (wishspd > Sv.sv_maxspeed->Value)
        {
            wishvel *= Sv.sv_maxspeed->Value / wishspd;
            wishspd = Sv.sv_maxspeed->Value;
        }
        wishspd *= 0.7f;

        // water friction
        float speed = velocityPtr->Length();
        float newspeed;
        if (speed != 0)
        {
            newspeed = speed - Host.host_frametime * speed * Sv.sv_friction->Value;
            if (newspeed < 0)
                newspeed = 0;
            *velocityPtr *= newspeed / speed;
        }
        else
        {
            newspeed = 0;
        }

        // water acceleration
        if (wishspd == 0)
            return;

        float addspeed = wishspd - newspeed;
        if (addspeed <= 0)
            return;

        MathLib.VectorNormalize(&wishvel);
        float accelspeed = Sv.sv_accelerate->Value * wishspd * Host.host_frametime;
        if (accelspeed > addspeed)
            accelspeed = addspeed;

        *velocityPtr += accelspeed * wishvel;
    }

    // SV_WaterJump
    public static void WaterJump()
    {
        if (Sv.time > Sv.sv_player->V.TeleportTime || Sv.sv_player->V.WaterLevel == 0)
        {
            Sv.sv_player->V.Flags = (int)Sv.sv_player->V.Flags & ~SvPhys.FL_WATERJUMP;
            Sv.sv_player->V.TeleportTime = 0;
        }
        Sv.sv_player->V.Velocity[0] = Sv.sv_player->V.MoveDir[0];
        Sv.sv_player->V.Velocity[1] = Sv.sv_player->V.MoveDir[1];
    }

    // SV_AirMove
    public static void AirMove()
    {
        Vec3_t wishvel;

        MathLib.AngleVectors(Sv.sv_player->V.Angles, out forward, out right, out up);

        float fmove = cmd.ForwardMove;
        float smove = cmd.SideMove;

        // hack to not let you back into teleporter
        if (Sv.time < Sv.sv_player->V.TeleportTime && fmove < 0)
            fmove = 0;

        wishvel = forward * fmove + right * smove;

        if ((int)Sv.sv_player->V.MoveType != (int)MoveType.Walk)
            wishvel[2] = cmd.UpMove;
        else
            wishvel[2] = 0;

        wishdir = wishvel;
        wishspeed = MathLib.VectorNormalize((Vec3_t*)Unsafe.AsPointer(ref wishdir));
        if (wishspeed > Sv.sv_maxspeed->Value)
        {
            wishvel *= Sv.sv_maxspeed->Value / wishspeed;
            wishspeed = Sv.sv_maxspeed->Value;
        }

        if ((int)Sv.sv_player->V.MoveType == (int)MoveType.NoClip)
        { // noclip
            *velocityPtr = wishvel;
        }
        else if (onground)
        {
            UserFriction();
            Accelerate();
        }
        else
        { // not on ground, so little effect on velocity
            AirAccelerate(ref wishvel);
        }
    }

    /*
    ===================
    SV_ClientThink

    the move fields specify an intended velocity in pix/sec
    the angle fields specify an exact angular motion in degrees
    ===================
    */
    public static void ClientThink()
    {
        if ((int)Sv.sv_player->V.MoveType == (int)MoveType.None)
            return;

        onground = ((int)Sv.sv_player->V.Flags & SvPhys.FL_ONGROUND) != 0;

        originPtr = &Sv.sv_player->V.Origin;
        velocityPtr = &Sv.sv_player->V.Velocity;

        DropPunchAngle();

        // if dead, behave differently
        if (Sv.sv_player->V.Health <= 0)
            return;

        // angles
        // show 1/3 the pitch angle and all the roll angle
        cmd = Svs.host_client->Cmd; // cmd = host_client->cmd (SV_ClientThink)

        Vec3_t v_angle = Sv.sv_player->V.VAngle + Sv.sv_player->V.PunchAngle;
        Sv.sv_player->V.Angles[QuakeDef.ROLL] = View.CalcRoll(Sv.sv_player->V.Angles, Sv.sv_player->V.Velocity) * 4;
        if (Sv.sv_player->V.FixAngle == 0)
        {
            Sv.sv_player->V.Angles[QuakeDef.PITCH] = -v_angle[QuakeDef.PITCH] / 3;
            Sv.sv_player->V.Angles[QuakeDef.YAW] = v_angle[QuakeDef.YAW];
        }

        if (((int)Sv.sv_player->V.Flags & SvPhys.FL_WATERJUMP) != 0)
        {
            WaterJump();
            return;
        }

        // walk
        if (Sv.sv_player->V.WaterLevel >= 2 && (int)Sv.sv_player->V.MoveType != (int)MoveType.NoClip)
        {
            WaterMove();
            return;
        }

        AirMove();
    }

    /*
    ===================
    SV_ReadClientMove
    ===================
    */
    public static void ReadClientMove(UserCmd_t* move)
    {
        // read ping time
        Svs.host_client->PingTimes[Svs.host_client->NumPings % Client_t.NUM_PING_TIMES] =
            (float)(Sv.time - Msg.ReadFloat());
        Svs.host_client->NumPings++;

        // read current angles
        Vec3_t angle = default;
        for (int i = 0; i < 3; i++)
            angle[i] = Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE ? Msg.ReadAngle() : Msg.ReadAngle16(); // Mark V SV_ReadClientMove

        Svs.host_client->Edict->V.VAngle = angle;

        // read movement
        move->ForwardMove = Msg.ReadShort();
        move->SideMove = Msg.ReadShort();
        move->UpMove = Msg.ReadShort();

        // read buttons
        int bits = Msg.ReadByte();
        Svs.host_client->Edict->V.Button0 = bits & 1;
        Svs.host_client->Edict->V.Button2 = (bits & 2) >> 1;

        int impulse = Msg.ReadByte();
        if (impulse != 0)
            Svs.host_client->Edict->V.Impulse = impulse;
    }

    // Q_strncasecmp(s, lit, n) == 0 shorthand for SV_ReadClientMessage's
    // (int)Clc.StringCmd whitelist below.
    private static bool Is(byte* s, Utf8String lit) => Q.StrNCaseCmp(s, lit, Q.StrLen(lit)) == 0;

    /*
    ===================
    SV_ReadClientMessage

    Returns false if the client should be killed
    ===================
    */
    public static bool ReadClientMessage()
    {
        while (true)
        {
            int ret = Net.GetMessage(Svs.host_client->NetConnection);
            if (ret == -1)
            {
                Sys.Printf("SV_ReadClientMessage: NET_GetMessage failed\n"u8);
                return false;
            }
            if (ret == 0)
                return true;

            Msg.BeginReading();

            bool nextMessage = false;
            while (!nextMessage)
            {
                if (!Svs.host_client->Active)
                    return false; // a command caused an error

                if (Msg.msg_badread)
                {
                    Sys.Printf("SV_ReadClientMessage: badread\n"u8);
                    return false;
                }

                int cmd = Msg.ReadChar();

                switch (cmd)
                {
                    case -1:
                        nextMessage = true; // end of message
                        break;

                    default:
                        Sys.Printf("SV_ReadClientMessage: unknown command char\n"u8);
                        return false;

                    case (int)Clc.Nop:
                        break;

                    case (int)Clc.StringCmd:
                        {
                            byte* s = Msg.ReadString();

                            int handled = Svs.host_client->Privileged ? 2 : 0;
                            if (Is(s, "status"u8) || Is(s, "god"u8) || Is(s, "notarget"u8) || Is(s, "fly"u8)
                                || Is(s, "name"u8) || Is(s, "noclip"u8) || Is(s, "say"u8) || Is(s, "say_team"u8)
                                || Is(s, "tell"u8) || Is(s, "color"u8) || Is(s, "kill"u8) || Is(s, "pause"u8)
                                || Is(s, "spawn"u8) || Is(s, "begin"u8) || Is(s, "prespawn"u8) || Is(s, "kick"u8)
                                || Is(s, "ping"u8) || Is(s, "give"u8) || Is(s, "ban"u8))
                                handled = 1;

                            if (handled == 2)
                                Cmd.InsertText(s);
                            else if (handled == 1)
                                Cmd.ExecuteString(s, CmdSource_t.SrcClient);
                            else
                                Con.DPrintf($"{Svs.host_client->Name} tried to {s}\n");
                            break;
                        }

                    case (int)Clc.Disconnect:
                        return false;

                    case (int)Clc.Move:
                        ReadClientMove(&Svs.host_client->Cmd);
                        break;
                }
            }
        }
    }

    /*
    ==================
    SV_RunClients
    ==================
    */
    public static void RunClients()
    {
        for (int i = 0; i < Svs.maxclients; i++)
        {
            Svs.host_client = &Svs.clients[i];

            if (!Svs.host_client->Active)
                continue;

            Sv.sv_player = Svs.host_client->Edict;

            if (!ReadClientMessage())
            {
                Host.DropClient(false); // client misbehaved...
                continue;
            }

            if (!Svs.host_client->Spawned)
            {
                // clear client movement until a new packet is received
                Svs.host_client->Cmd = default;
                continue;
            }

            // always pause in single player if in console or menus
            if (!Sv.paused && (Svs.maxclients > 1 || Keys.key_dest == KeyDest.Game))
                ClientThink();
        }
    }
}
