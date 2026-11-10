// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// view.c -- player eye positioning, palette flashes, and the top-level
/// V_RenderView entry point. GLQUAKE-only code (ramps/v_blend/V_CalcBlend)
/// is dropped, matching this port's software-only scope. host_basepal/
/// VID_ShiftPalette are RMisc.host_basepal/Vid.ShiftPalette.
/// </summary>
public static unsafe class View
{
    // client.h

    public static readonly Cvar_t* lcd_x = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* lcd_yaw = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* scr_ofsx = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* scr_ofsy = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* scr_ofsz = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_rollspeed = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_rollangle = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_bob = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_bobcycle = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_bobup = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_kicktime = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_kickroll = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_kickpitch = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_iyaw_cycle = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_iroll_cycle = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_ipitch_cycle = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* v_iyaw_level = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* v_iroll_level = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* v_ipitch_level = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_idlescale = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* crosshair = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_crossx = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* cl_crossy = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* gl_cshiftpercent = NativeMemory.AllocZeroed<Cvar_t>();

    // cl_input.c's own global (the original has exactly one); registered
    // here (View.Init, called first from Host.Init) since V_DriftPitch was
    // its first ported consumer, and ClInput.cs (open_tk.md) now reuses this
    // same field/registration for CL_BaseMove rather than declaring a second
    // Cvar_t for it -- see ClInput.cs's own comment.
    public static readonly Cvar_t* cl_forwardspeed = NativeMemory.AllocZeroed<Cvar_t>();

    public static float v_dmg_time, v_dmg_roll, v_dmg_pitch;

    // in_forward, in_back -- view.c only externs these (cl_input.c's real
    // kbutton_t globals, now ClInput.in_forward/in_back) but never actually
    // reads them; V_DriftPitch below uses Cl.cmd.ForwardMove instead. Nothing
    // to port here. in_forward2 is dead code in the original itself: view.c
    // externs it as a bare `int` (mismatched with cl_input.c's kbutton_t
    // type) and it's defined nowhere.

    /*
    ===============
    V_CalcRoll

    Used by view and sv_user
    ===============
    */
    public static Vec3_t forward, right, up;

    public static float CalcRoll(in Vec3_t angles, in Vec3_t velocity)
    {
        MathLib.AngleVectors(angles, out forward, out right, out up);
        float side = Vector3.Dot(velocity, right);
        float sign = side < 0 ? -1 : 1;
        side = MathF.Abs(side);

        float value = cl_rollangle->Value;

        if (side < cl_rollspeed->Value)
        {
            side = side * value / cl_rollspeed->Value;
        }
        else
        {
            side = value;
        }

        return side * sign;
    }

    /*
    ===============
    V_CalcBob
    ===============
    */
    public static float CalcBob()
    {
        float cycle = (float)(Cl.time - ((int)(Cl.time / cl_bobcycle->Value) * cl_bobcycle->Value));
        cycle /= cl_bobcycle->Value;
        if (cycle < cl_bobup->Value)
        {
            cycle = MathLib.M_PI * cycle / cl_bobup->Value;
        }
        else
        {
            cycle = MathLib.M_PI + (MathLib.M_PI * (cycle - cl_bobup->Value) / (1.0f - cl_bobup->Value));
        }

        // bob is proportional to velocity in the xy plane
        // (don't count Z, or jumping messes it up)
        float bob = new Vector2(Cl.velocity.X, Cl.velocity.Y).Length() * cl_bob->Value;
        bob = (bob * 0.3f) + (bob * 0.7f * MathF.Sin(cycle));
        if (bob > 4)
        {
            bob = 4;
        }
        else if (bob < -7)
        {
            bob = -7;
        }

        return bob;
    }

    //=============================================================================

    public static readonly Cvar_t* v_centermove = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly Cvar_t* v_centerspeed = NativeMemory.AllocZeroed<Cvar_t>();

    public static void StartPitchDrift()
    {
        if (Cl.laststop == Cl.time)
        {
            return; // something else is keeping it from drifting
        }

        if (Cl.nodrift || Cl.pitchvel == 0)
        {
            Cl.pitchvel = v_centerspeed->Value;
            Cl.nodrift = false;
            Cl.driftmove = 0;
        }
    }

    public static void StopPitchDrift()
    {
        Cl.laststop = Cl.time;
        Cl.nodrift = true;
        Cl.pitchvel = 0;
    }

    /*
    ===============
    V_DriftPitch

    Moves the client pitch angle towards cl.idealpitch sent by the server.
    ===============
    */
    public static void DriftPitch()
    {
        if (Cl.noclip_anglehack || !Cl.onground || ClDemo.demoplayback)
        {
            Cl.driftmove = 0;
            Cl.pitchvel = 0;
            return;
        }

        // don't count small mouse motion
        if (Cl.nodrift)
        {
            if (MathF.Abs(Cl.cmd.ForwardMove) < cl_forwardspeed->Value)
            {
                Cl.driftmove = 0;
            }
            else
            {
                Cl.driftmove += Host.host_frametime;
            }

            if (Cl.driftmove > v_centermove->Value)
            {
                StartPitchDrift();
            }

            return;
        }

        float delta = Cl.idealpitch - Cl.viewangles[QuakeDef.PITCH];

        if (delta == 0)
        {
            Cl.pitchvel = 0;
            return;
        }

        float move = Host.host_frametime * Cl.pitchvel;
        Cl.pitchvel += Host.host_frametime * v_centerspeed->Value;

        if (delta > 0)
        {
            if (move > delta)
            {
                Cl.pitchvel = 0;
                move = delta;
            }
            Cl.viewangles[QuakeDef.PITCH] += move;
        }
        else if (delta < 0)
        {
            if (move > -delta)
            {
                Cl.pitchvel = 0;
                move = -delta;
            }
            Cl.viewangles[QuakeDef.PITCH] -= move;
        }
    }

    /*
    ==============================================================================
                            PALETTE FLASHES
    ==============================================================================
    */

    public static CShift_t cshift_empty = MakeCShift(130, 80, 50, 0);
    public static CShift_t cshift_water = MakeCShift(130, 80, 50, 128);
    public static CShift_t cshift_slime = MakeCShift(0, 25, 5, 150);
    public static CShift_t cshift_lava = MakeCShift(255, 80, 0, 150);

    private static CShift_t MakeCShift(int r, int g, int b, int percent)
    {
        CShift_t c = default;
        c.Destcolor[0] = r;
        c.Destcolor[1] = g;
        c.Destcolor[2] = b;
        c.Percent = percent;
        return c;
    }

    public static readonly Cvar_t* v_gamma = NativeMemory.AllocZeroed<Cvar_t>();

    public static readonly byte* gammatable = NativeMemory.AllocArrayZeroed<byte>(256);

    public static void BuildGammaTable(float g)
    {
        if (g == 1.0f)
        {
            for (int i = 0; i < 256; i++)
            {
                gammatable[i] = (byte)i;
            }

            return;
        }

        for (int i = 0; i < 256; i++)
        {
            int inf = (int)((255 * MathF.Pow((i + 0.5f) / 255.5f, g)) + 0.5f);
            if (inf < 0)
            {
                inf = 0;
            }

            if (inf > 255)
            {
                inf = 255;
            }

            gammatable[i] = (byte)inf;
        }
    }

    /*
    =================
    V_CheckGamma
    =================
    */
    private static float oldgammavalue;

    public static bool CheckGamma()
    {
        if (v_gamma->Value == oldgammavalue)
        {
            return false;
        }

        oldgammavalue = v_gamma->Value;

        BuildGammaTable(v_gamma->Value);
        Vid.vid.RecalcRefdef = 1; // force a surface cache flush

        return true;
    }

    /*
    ===============
    V_ParseDamage
    ===============
    */
    public static void ParseDamage()
    {
        int armor = Msg.ReadByte();
        int blood = Msg.ReadByte();
        Vec3_t from = default;
        for (int i = 0; i < 3; i++)
        {
            from[i] = Msg.ReadCoord();
        }

        float count = (blood * 0.5f) + (armor * 0.5f);
        if (count < 10)
        {
            count = 10;
        }

        Cl.faceanimtime = Cl.time + 0.2; // but sbar face into pain frame

        Cl.cshifts[(int)CShiftType.Damage].Percent += (int)(3 * count);
        if (Cl.cshifts[(int)CShiftType.Damage].Percent < 0)
        {
            Cl.cshifts[(int)CShiftType.Damage].Percent = 0;
        }

        if (Cl.cshifts[(int)CShiftType.Damage].Percent > 150)
        {
            Cl.cshifts[(int)CShiftType.Damage].Percent = 150;
        }

        if (armor > blood)
        {
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[0] = 200;
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[1] = 100;
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[2] = 100;
        }
        else if (armor != 0)
        {
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[0] = 220;
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[1] = 50;
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[2] = 50;
        }
        else
        {
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[0] = 255;
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[1] = 0;
            Cl.cshifts[(int)CShiftType.Damage].Destcolor[2] = 0;
        }

        // calculate view angle kicks
        Entity_t* ent = &ClEntities.cl_entities[Cl.viewentity];

        from = from - ent->Origin;
        MathLib.VectorNormalize(&from);

        MathLib.AngleVectors(ent->Angles, out Vec3_t fwd, out Vec3_t rgt, out Vec3_t upv);

        float side = Vector3.Dot(from, rgt);
        v_dmg_roll = count * side * v_kickroll->Value;

        side = Vector3.Dot(from, fwd);
        v_dmg_pitch = count * side * v_kickpitch->Value;

        v_dmg_time = v_kicktime->Value;
    }

    /*
    ==================
    V_cshift_f
    ==================
    */
    public static void Cshift_f()
    {
        cshift_empty.Destcolor[0] = Q.Atoi(Cmd.Argv(1));
        cshift_empty.Destcolor[1] = Q.Atoi(Cmd.Argv(2));
        cshift_empty.Destcolor[2] = Q.Atoi(Cmd.Argv(3));
        cshift_empty.Percent = Q.Atoi(Cmd.Argv(4));
    }

    /*
    ==================
    V_BonusFlash_f

    When you run over an item, the server sends this command
    ==================
    */
    public static void BonusFlash_f()
    {
        Cl.cshifts[(int)CShiftType.Bonus].Destcolor[0] = 215;
        Cl.cshifts[(int)CShiftType.Bonus].Destcolor[1] = 186;
        Cl.cshifts[(int)CShiftType.Bonus].Destcolor[2] = 69;
        Cl.cshifts[(int)CShiftType.Bonus].Percent = 50;
    }

    /*
    =============
    V_SetContentsColor

    Underwater, lava, etc each has a color shift
    =============
    */
    public static void SetContentsColor(int contents)
    {
        switch ((Contents)contents)
        {
            case Contents.Empty:
            case Contents.Solid:
                Cl.cshifts[(int)CShiftType.Contents] = cshift_empty;
                break;
            case Contents.Lava:
                Cl.cshifts[(int)CShiftType.Contents] = cshift_lava;
                break;
            case Contents.Slime:
                Cl.cshifts[(int)CShiftType.Contents] = cshift_slime;
                break;
            default:
                Cl.cshifts[(int)CShiftType.Contents] = cshift_water;
                break;
        }
    }

    /*
    =============
    V_CalcPowerupCshift
    =============
    */
    public static void CalcPowerupCshift()
    {
        // quakedef.h

        if ((Cl.items & IT_QUAD) != 0)
        {
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[0] = 0;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[1] = 0;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[2] = 255;
            Cl.cshifts[(int)CShiftType.Powerup].Percent = 30;
        }
        else if ((Cl.items & IT_SUIT) != 0)
        {
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[0] = 0;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[1] = 255;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[2] = 0;
            Cl.cshifts[(int)CShiftType.Powerup].Percent = 20;
        }
        else if ((Cl.items & IT_INVISIBILITY) != 0)
        {
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[0] = 100;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[1] = 100;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[2] = 100;
            Cl.cshifts[(int)CShiftType.Powerup].Percent = 100;
        }
        else if ((Cl.items & IT_INVULNERABILITY) != 0)
        {
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[0] = 255;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[1] = 255;
            Cl.cshifts[(int)CShiftType.Powerup].Destcolor[2] = 0;
            Cl.cshifts[(int)CShiftType.Powerup].Percent = 30;
        }
        else
        {
            Cl.cshifts[(int)CShiftType.Powerup].Percent = 0;
        }
    }

    /*
    =============
    V_UpdatePalette

    Non-GLQUAKE variant only (GLQUAKE's ramps/v_blend/V_CalcBlend path is
    dropped -- software-only port).
    =============
    */
    [SkipLocalsInit]
    public static void UpdatePalette()
    {
        CalcPowerupCshift();

        bool isNew = false;

        for (int i = 0; i < Cl.NUM_CSHIFTS; i++)
        {
            if (Cl.cshifts[i].Percent != Cl.prev_cshifts[i].Percent)
            {
                isNew = true;
                Cl.prev_cshifts[i].Percent = Cl.cshifts[i].Percent;
            }
            for (int j = 0; j < 3; j++)
            {
                if (Cl.cshifts[i].Destcolor[j] != Cl.prev_cshifts[i].Destcolor[j])
                {
                    isNew = true;
                    Cl.prev_cshifts[i].Destcolor[j] = Cl.cshifts[i].Destcolor[j];
                }
            }
        }

        // drop the damage value
        Cl.cshifts[(int)CShiftType.Damage].Percent -= (int)(Host.host_frametime * 150);
        if (Cl.cshifts[(int)CShiftType.Damage].Percent <= 0)
        {
            Cl.cshifts[(int)CShiftType.Damage].Percent = 0;
        }

        // drop the bonus value
        Cl.cshifts[(int)CShiftType.Bonus].Percent -= (int)(Host.host_frametime * 100);
        if (Cl.cshifts[(int)CShiftType.Bonus].Percent <= 0)
        {
            Cl.cshifts[(int)CShiftType.Bonus].Percent = 0;
        }

        bool force = CheckGamma();
        if (!isNew && !force)
        {
            return;
        }

        byte* basepal = RMisc.host_basepal;
        byte* pal = stackalloc byte[768];
        byte* newpal = pal;

        for (int i = 0; i < 256; i++)
        {
            int r = basepal[0];
            int g = basepal[1];
            int b = basepal[2];
            basepal += 3;

            for (int j = 0; j < Cl.NUM_CSHIFTS; j++)
            {
                r += (Cl.cshifts[j].Percent * (Cl.cshifts[j].Destcolor[0] - r)) >> 8;
                g += (Cl.cshifts[j].Percent * (Cl.cshifts[j].Destcolor[1] - g)) >> 8;
                b += (Cl.cshifts[j].Percent * (Cl.cshifts[j].Destcolor[2] - b)) >> 8;
            }

            newpal[0] = gammatable[r];
            newpal[1] = gammatable[g];
            newpal[2] = gammatable[b];
            newpal += 3;
        }

        Vid.ShiftPalette(pal);
    }

    /*
    ==============================================================================
                            VIEW RENDERING
    ==============================================================================
    */

    public static float AngleDelta(float a)
    {
        a = MathLib.anglemod(a);
        if (a > 180)
        {
            a -= 360;
        }

        return a;
    }

    /*
    ==================
    CalcGunAngle
    ==================
    */
    private static float oldyaw = 0;
    private static float oldpitch = 0;

    public static void CalcGunAngle()
    {
        float yaw = RShared.r_refdef->ViewAngles[QuakeDef.YAW];
        float pitch = -RShared.r_refdef->ViewAngles[QuakeDef.PITCH];

        yaw = AngleDelta(yaw - RShared.r_refdef->ViewAngles[QuakeDef.YAW]) * 0.4f;
        if (yaw > 10)
        {
            yaw = 10;
        }

        if (yaw < -10)
        {
            yaw = -10;
        }

        pitch = AngleDelta(-pitch - RShared.r_refdef->ViewAngles[QuakeDef.PITCH]) * 0.4f;
        if (pitch > 10)
        {
            pitch = 10;
        }

        if (pitch < -10)
        {
            pitch = -10;
        }

        float move = Host.host_frametime * 20;
        if (yaw > oldyaw)
        {
            if (oldyaw + move < yaw)
            {
                yaw = oldyaw + move;
            }
        }
        else
        {
            if (oldyaw - move > yaw)
            {
                yaw = oldyaw - move;
            }
        }

        if (pitch > oldpitch)
        {
            if (oldpitch + move < pitch)
            {
                pitch = oldpitch + move;
            }
        }
        else
        {
            if (oldpitch - move > pitch)
            {
                pitch = oldpitch - move;
            }
        }

        oldyaw = yaw;
        oldpitch = pitch;

        Cl.viewent->Angles[QuakeDef.YAW] = RShared.r_refdef->ViewAngles[QuakeDef.YAW] + yaw;
        Cl.viewent->Angles[QuakeDef.PITCH] = -(RShared.r_refdef->ViewAngles[QuakeDef.PITCH] + pitch);

        Cl.viewent->Angles[QuakeDef.ROLL] -= v_idlescale->Value * MathF.Sin((float)Cl.time * v_iroll_cycle->Value) * v_iroll_level->Value;
        Cl.viewent->Angles[QuakeDef.PITCH] -= v_idlescale->Value * MathF.Sin((float)Cl.time * v_ipitch_cycle->Value) * v_ipitch_level->Value;
        Cl.viewent->Angles[QuakeDef.YAW] -= v_idlescale->Value * MathF.Sin((float)Cl.time * v_iyaw_cycle->Value) * v_iyaw_level->Value;
    }

    /*
    ==============
    V_BoundOffsets
    ==============
    */
    public static void BoundOffsets()
    {
        Entity_t* ent = &ClEntities.cl_entities[Cl.viewentity];

        // absolutely bound refresh reletive to entity clipping hull
        // so the view can never be inside a solid wall

        RShared.r_refdef->Vieworg = Vector3.Clamp(
            RShared.r_refdef->Vieworg,
            ent->Origin + new Vec3_t(-14, -14, -22),
            ent->Origin + new Vec3_t(14, 14, 30));
    }

    /*
    ==============
    V_AddIdle

    Idle swaying
    ==============
    */
    public static void AddIdle()
    {
        RShared.r_refdef->ViewAngles[QuakeDef.ROLL] += v_idlescale->Value * MathF.Sin((float)Cl.time * v_iroll_cycle->Value) * v_iroll_level->Value;
        RShared.r_refdef->ViewAngles[QuakeDef.PITCH] += v_idlescale->Value * MathF.Sin((float)Cl.time * v_ipitch_cycle->Value) * v_ipitch_level->Value;
        RShared.r_refdef->ViewAngles[QuakeDef.YAW] += v_idlescale->Value * MathF.Sin((float)Cl.time * v_iyaw_cycle->Value) * v_iyaw_level->Value;
    }

    /*
    ==============
    V_CalcViewRoll

    Roll is induced by movement and damage
    ==============
    */
    public static void CalcViewRoll()
    {
        float side = CalcRoll(ClEntities.cl_entities[Cl.viewentity].Angles, Cl.velocity);
        RShared.r_refdef->ViewAngles[QuakeDef.ROLL] += side;

        if (v_dmg_time > 0)
        {
            RShared.r_refdef->ViewAngles[QuakeDef.ROLL] += v_dmg_time / v_kicktime->Value * v_dmg_roll;
            RShared.r_refdef->ViewAngles[QuakeDef.PITCH] += v_dmg_time / v_kicktime->Value * v_dmg_pitch;
            v_dmg_time -= Host.host_frametime;
        }

        if (Cl.stats[(int)Stat.Health] <= 0)
        {
            RShared.r_refdef->ViewAngles[QuakeDef.ROLL] = 80; // dead view angle
        }
    }

    /*
    ==================
    V_CalcIntermissionRefdef
    ==================
    */
    public static void CalcIntermissionRefdef()
    {
        Entity_t* ent = &ClEntities.cl_entities[Cl.viewentity]; // player model
        Entity_t* view = Cl.viewent; // weapon model
        RShared.r_refdef->Vieworg = ent->Origin;
        RShared.r_refdef->ViewAngles = ent->Angles;
        view->Model = null;

        // allways idle in intermission
        float old = v_idlescale->Value;
        v_idlescale->Value = 1;
        AddIdle();
        v_idlescale->Value = old;
    }

    /*
    ==================
    V_CalcRefdef
    ==================
    */
    private static float oldz = 0;

    public static void CalcRefdef()
    {
        DriftPitch();

        Entity_t* ent = &ClEntities.cl_entities[Cl.viewentity]; // player model
        Entity_t* view = Cl.viewent; // weapon model
        // transform the view offset by the model's matrix to get the offset from
        // model origin for the view
        ent->Angles[QuakeDef.YAW] = Cl.viewangles[QuakeDef.YAW];
        ent->Angles[QuakeDef.PITCH] = -Cl.viewangles[QuakeDef.PITCH];

        float bob = CalcBob();

        // refresh position
        RShared.r_refdef->Vieworg = ent->Origin;
        RShared.r_refdef->Vieworg[2] += Cl.viewheight + bob;

        // never let it sit exactly on a node line
        RShared.r_refdef->Vieworg[0] += 1.0f / 32;
        RShared.r_refdef->Vieworg[1] += 1.0f / 32;
        RShared.r_refdef->Vieworg[2] += 1.0f / 32;

        RShared.r_refdef->ViewAngles = Cl.viewangles;
        CalcViewRoll();
        AddIdle();

        // offsets
        Vec3_t angles = default;
        angles[QuakeDef.PITCH] = -ent->Angles[QuakeDef.PITCH]; // entity pitches are backward
        angles[QuakeDef.YAW] = ent->Angles[QuakeDef.YAW];
        angles[QuakeDef.ROLL] = ent->Angles[QuakeDef.ROLL];

        MathLib.AngleVectors(angles, out forward, out right, out up);

        for (int i = 0; i < 3; i++)
        {
            RShared.r_refdef->Vieworg[i] += (scr_ofsx->Value * forward[i])
                + (scr_ofsy->Value * right[i])
                + (scr_ofsz->Value * up[i]);
        }

        BoundOffsets();

        // set up gun position
        view->Angles = Cl.viewangles;

        CalcGunAngle();

        view->Origin = ent->Origin;
        view->Origin[2] += Cl.viewheight;

        for (int i = 0; i < 3; i++)
        {
            view->Origin[i] += forward[i] * bob * 0.4f;
        }

        view->Origin[2] += bob;

        // fudge position around to keep amount of weapon visible
        // roughly equal with different FOV
        if (Screen.scr_viewsize->Value == 110)
        {
            view->Origin[2] += 1;
        }
        else if (Screen.scr_viewsize->Value == 100)
        {
            view->Origin[2] += 2;
        }
        else if (Screen.scr_viewsize->Value == 90)
        {
            view->Origin[2] += 1;
        }
        else if (Screen.scr_viewsize->Value == 80)
        {
            view->Origin[2] += 0.5f;
        }

        view->Model = Cl.model_precache[Cl.stats[(int)Stat.Weapon]];
        view->Frame = Cl.stats[(int)Stat.WeaponFrame];
        view->ColorMap = Vid.vid.Colormap;

        // set up the refresh position
        RShared.r_refdef->ViewAngles = RShared.r_refdef->ViewAngles + Cl.punchangle;

        // smooth out stair step ups
        if (Cl.onground && ent->Origin[2] - oldz > 0)
        {
            float steptime = (float)(Cl.time - Cl.oldtime);
            if (steptime < 0)
            {
                steptime = 0;
            }

            oldz += steptime * 80;
            if (oldz > ent->Origin[2])
            {
                oldz = ent->Origin[2];
            }

            if (ent->Origin[2] - oldz > 12)
            {
                oldz = ent->Origin[2] - 12;
            }

            RShared.r_refdef->Vieworg[2] += oldz - ent->Origin[2];
            view->Origin[2] += oldz - ent->Origin[2];
        }
        else
        {
            oldz = ent->Origin[2];
        }

        if (Chase.chase_active->Value != 0)
        {
            Chase.Update();
        }
    }

    // (int)Stat.Weapon, (int)Stat.WeaponFrame (quakedef.h)

    /*
    ==================
    V_RenderView

    The player's clipping box goes from (-16 -16 -24) to (16 16 32) from
    the entity origin, so any view position inside that will be valid
    ==================
    */
    public static void RenderView()
    {
        if (Con.con_forcedup)
        {
            return;
        }

        // don't allow cheats in multiplayer
        if (Cl.maxclients > 1)
        {
            Cvar.Set("scr_ofsx"u8, "0"u8);
            Cvar.Set("scr_ofsy"u8, "0"u8);
            Cvar.Set("scr_ofsz"u8, "0"u8);
        }

        if (Cl.intermission != 0)
        {
            CalcIntermissionRefdef();
        }
        else
        {
            if (!Cl.paused)
            {
                CalcRefdef();
            }
        }

        RLight.PushDlights();

        if (lcd_x->Value != 0)
        {
            Vid.vid.RowBytes <<= 1;
            Vid.vid.Aspect *= 0.5f;

            RShared.r_refdef->ViewAngles[QuakeDef.YAW] -= lcd_yaw->Value;
            for (int i = 0; i < 3; i++)
            {
                RShared.r_refdef->Vieworg[i] -= right[i] * lcd_x->Value;
            }

            RMain.RenderView();

            Vid.vid.Buffer += Vid.vid.RowBytes >> 1;

            RLight.PushDlights();

            RShared.r_refdef->ViewAngles[QuakeDef.YAW] += lcd_yaw->Value * 2;
            for (int i = 0; i < 3; i++)
            {
                RShared.r_refdef->Vieworg[i] += 2 * right[i] * lcd_x->Value;
            }

            RMain.RenderView();

            Vid.vid.Buffer -= Vid.vid.RowBytes >> 1;

            RShared.r_refdef->Vrect.Height <<= 1;

            Vid.vid.RowBytes >>= 1;
            Vid.vid.Aspect *= 2;
        }
        else
        {
            RMain.RenderView();
        }

        if (crosshair->Value != 0)
        {
            // scr_crosshairscale (Mark V CANVAS_CROSSHAIR): keep the glyph's
            // centre where the unscaled 8x8 '+' would be, scaling about it.
            // The physical position is split into a virtual coordinate plus a
            // sub-scale origin offset so it lands on the exact pixel.
            Canvas.Set(CanvasType.Crosshair);
            int s = Canvas.Scale;
            int px = (int)(Screen.scr_vrect->X + (Screen.scr_vrect->Width / 2) + cl_crossx->Value) + 4 - (4 * s);
            int py = (int)(Screen.scr_vrect->Y + (Screen.scr_vrect->Height / 2) + cl_crossy->Value) + 4 - (4 * s);
            int vx = (int)MathF.Floor((float)px / s);
            int vy = (int)MathF.Floor((float)py / s);
            Canvas.OriginX = px - (vx * s);
            Canvas.OriginY = py - (vy * s);
            Draw.Character(vx, vy, '+');
            Canvas.Set(CanvasType.Default);
        }
    }

    //============================================================================

    private static void CshiftCmd() => Cshift_f();
    private static void BonusFlashCmd() => BonusFlash_f();
    private static void StartPitchDriftCmd() => StartPitchDrift();

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        Cvar.RegisterVariable(cvar);
    }

    /*
    =============
    V_Init
    =============
    */
    public static void Init()
    {
        Cmd.AddCommand("v_cshift"u8, &CshiftCmd);
        Cmd.AddCommand("bf"u8, &BonusFlashCmd);
        Cmd.AddCommand("centerview"u8, &StartPitchDriftCmd);

        RegisterCvar(lcd_x, "lcd_x"u8, "0"u8);
        RegisterCvar(lcd_yaw, "lcd_yaw"u8, "0"u8);

        RegisterCvar(v_centermove, "v_centermove"u8, "0.15"u8);
        RegisterCvar(v_centerspeed, "v_centerspeed"u8, "500"u8);

        RegisterCvar(v_iyaw_cycle, "v_iyaw_cycle"u8, "2"u8);
        RegisterCvar(v_iroll_cycle, "v_iroll_cycle"u8, "0.5"u8);
        RegisterCvar(v_ipitch_cycle, "v_ipitch_cycle"u8, "1"u8);
        RegisterCvar(v_iyaw_level, "v_iyaw_level"u8, "0.3"u8);
        RegisterCvar(v_iroll_level, "v_iroll_level"u8, "0.1"u8);
        RegisterCvar(v_ipitch_level, "v_ipitch_level"u8, "0.3"u8);

        RegisterCvar(v_idlescale, "v_idlescale"u8, "0"u8);
        RegisterCvar(crosshair, "crosshair"u8, "0"u8, true);
        RegisterCvar(cl_crossx, "cl_crossx"u8, "0"u8);
        RegisterCvar(cl_crossy, "cl_crossy"u8, "0"u8);
        RegisterCvar(gl_cshiftpercent, "gl_cshiftpercent"u8, "100"u8);

        RegisterCvar(scr_ofsx, "scr_ofsx"u8, "0"u8);
        RegisterCvar(scr_ofsy, "scr_ofsy"u8, "0"u8);
        RegisterCvar(scr_ofsz, "scr_ofsz"u8, "0"u8);
        RegisterCvar(cl_rollspeed, "cl_rollspeed"u8, "200"u8);
        RegisterCvar(cl_rollangle, "cl_rollangle"u8, "2.0"u8);
        RegisterCvar(cl_bob, "cl_bob"u8, "0.02"u8);
        RegisterCvar(cl_bobcycle, "cl_bobcycle"u8, "0.6"u8);
        RegisterCvar(cl_bobup, "cl_bobup"u8, "0.5"u8);

        RegisterCvar(v_kicktime, "v_kicktime"u8, "0.5"u8);
        RegisterCvar(v_kickroll, "v_kickroll"u8, "0.6"u8);
        RegisterCvar(v_kickpitch, "v_kickpitch"u8, "0.6"u8);

        RegisterCvar(cl_forwardspeed, "cl_forwardspeed"u8, "200"u8);

        BuildGammaTable(1.0f); // no gamma yet
        RegisterCvar(v_gamma, "gamma"u8, "1"u8, true);
    }
}
