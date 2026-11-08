// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

/// <summary>
/// cl_input.c -- builds an intended movement command to send to the server.
/// Raw hardware polling (reading the mouse device, centering the cursor)
/// stays out of scope -- that is Quake.Desktop's job.
/// </summary>
public static unsafe class ClInput
{
    // kbutton_t globals: one NativeMemory block each (pointer stability, no fixed)
    public static readonly KButton_t* in_mlook = Alloc();
    public static readonly KButton_t* in_klook = Alloc();
    public static readonly KButton_t* in_left = Alloc();
    public static readonly KButton_t* in_right = Alloc();
    public static readonly KButton_t* in_forward = Alloc();
    public static readonly KButton_t* in_back = Alloc();
    public static readonly KButton_t* in_lookup = Alloc();
    public static readonly KButton_t* in_lookdown = Alloc();
    public static readonly KButton_t* in_moveleft = Alloc();
    public static readonly KButton_t* in_moveright = Alloc();
    public static readonly KButton_t* in_strafe = Alloc();
    public static readonly KButton_t* in_speed = Alloc();
    public static readonly KButton_t* in_use = Alloc();
    public static readonly KButton_t* in_jump = Alloc();
    public static readonly KButton_t* in_attack = Alloc();
    public static readonly KButton_t* in_up = Alloc();
    public static readonly KButton_t* in_down = Alloc();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static KButton_t* Alloc() => NativeMemory.AllocZeroed<KButton_t>();

    public static int in_impulse;

    // KeyDown
    private static void KeyDown(KButton_t* b)
    {
        byte* c = Cmd.Argv(1);
        int k = c[0] != 0 ? Q.Atoi(c) : -1; // typed manually at the console for continuous down

        if (k == b->Down[0] || k == b->Down[1])
            return; // repeating key

        if (b->Down[0] == 0)
            b->Down[0] = k;
        else if (b->Down[1] == 0)
            b->Down[1] = k;
        else
        {
            Con.Printf("Three keys down for a button!\n"u8);
            return;
        }

        if ((b->State & 1) != 0)
            return; // still down
        b->State |= 1 + 2; // down + impulse down
    }

    // KeyUp
    private static void KeyUp(KButton_t* b)
    {
        byte* c = Cmd.Argv(1);
        if (c[0] != 0)
        {
            int k = Q.Atoi(c);
            if (b->Down[0] == k)
                b->Down[0] = 0;
            else if (b->Down[1] == k)
                b->Down[1] = 0;
            else
                return; // key up without corresponding down (menu pass through)
            if (b->Down[0] != 0 || b->Down[1] != 0)
                return; // some other key is still holding it down
        }
        else
        {
            // typed manually at the console, assume for unsticking, so clear all
            b->Down[0] = b->Down[1] = 0;
            b->State = 4; // impulse up
            return;
        }

        if ((b->State & 1) == 0)
            return; // still up (this should not happen)
        b->State &= ~1; // now up
        b->State |= 4;  // impulse up
    }

    private static void KLookDownCmd() => KeyDown(in_klook);
    private static void KLookUpCmd() => KeyUp(in_klook);
    private static void MLookDownCmd() => KeyDown(in_mlook);

    private static void MLookUpCmd()
    {
        KeyUp(in_mlook);
        if ((in_mlook->State & 1) == 0 && lookspring->Value != 0)
            View.StartPitchDrift();
    }

    private static void UpDownCmd() => KeyDown(in_up);
    private static void UpUpCmd() => KeyUp(in_up);
    private static void DownDownCmd() => KeyDown(in_down);
    private static void DownUpCmd() => KeyUp(in_down);
    private static void LeftDownCmd() => KeyDown(in_left);
    private static void LeftUpCmd() => KeyUp(in_left);
    private static void RightDownCmd() => KeyDown(in_right);
    private static void RightUpCmd() => KeyUp(in_right);
    private static void ForwardDownCmd() => KeyDown(in_forward);
    private static void ForwardUpCmd() => KeyUp(in_forward);
    private static void BackDownCmd() => KeyDown(in_back);
    private static void BackUpCmd() => KeyUp(in_back);
    private static void LookupDownCmd() => KeyDown(in_lookup);
    private static void LookupUpCmd() => KeyUp(in_lookup);
    private static void LookdownDownCmd() => KeyDown(in_lookdown);
    private static void LookdownUpCmd() => KeyUp(in_lookdown);
    private static void MoveleftDownCmd() => KeyDown(in_moveleft);
    private static void MoveleftUpCmd() => KeyUp(in_moveleft);
    private static void MoverightDownCmd() => KeyDown(in_moveright);
    private static void MoverightUpCmd() => KeyUp(in_moveright);
    private static void SpeedDownCmd() => KeyDown(in_speed);
    private static void SpeedUpCmd() => KeyUp(in_speed);
    private static void StrafeDownCmd() => KeyDown(in_strafe);
    private static void StrafeUpCmd() => KeyUp(in_strafe);
    private static void AttackDownCmd() => KeyDown(in_attack);
    private static void AttackUpCmd() => KeyUp(in_attack);
    private static void UseDownCmd() => KeyDown(in_use);
    private static void UseUpCmd() => KeyUp(in_use);
    private static void JumpDownCmd() => KeyDown(in_jump);
    private static void JumpUpCmd() => KeyUp(in_jump);

    private static void ImpulseCmd() => in_impulse = Q.Atoi(Cmd.Argv(1));

    /*
    ===============
    CL_KeyState

    Returns 0.25 if a key was pressed and released during the frame,
    0.5 if it was pressed and held
    0 if held then released, and
    1.0 if held for the entire time
    ===============
    */
    private static float KeyState(KButton_t* key)
    {
        bool impulsedown = (key->State & 2) != 0;
        bool impulseup = (key->State & 4) != 0;
        bool down = (key->State & 1) != 0;
        float val = 0;

        if (impulsedown && !impulseup)
            val = down ? 0.5f : 0f;
        if (impulseup && !impulsedown)
            val = 0f; // released this frame
        if (!impulsedown && !impulseup)
            val = down ? 1.0f : 0f;
        if (impulsedown && impulseup)
            val = down ? 0.75f : 0.25f;

        key->State &= 1; // clear impulses

        return val;
    }

    //==========================================================================

    // cl_forwardspeed is NOT declared here: it already exists as
    // View.cl_forwardspeed (View.cs), added ahead of this file for
    // V_DriftPitch's use with a doc comment flagging it as "cl_input.c
    // (unported) global" -- now that cl_input.c is ported, this class reuses
    // that same field/registration rather than creating a second Cvar_t for
    // one original global (Cvar_t itself has no aliasing mechanism, so
    // duplicating it would silently desync BaseMove's speed from console
    // changes View.cs's copy sees, and vice versa).
    public static readonly Cvar_t* cl_upspeed = Cvar.Alloc("cl_upspeed"u8, "200"u8);
    public static readonly Cvar_t* cl_backspeed = Cvar.Alloc("cl_backspeed"u8, "200"u8, archive: true);
    public static readonly Cvar_t* cl_sidespeed = Cvar.Alloc("cl_sidespeed"u8, "350"u8);

    public static readonly Cvar_t* cl_movespeedkey = Cvar.Alloc("cl_movespeedkey"u8, "2.0"u8);

    public static readonly Cvar_t* cl_yawspeed = Cvar.Alloc("cl_yawspeed"u8, "140"u8);
    public static readonly Cvar_t* cl_pitchspeed = Cvar.Alloc("cl_pitchspeed"u8, "150"u8);

    public static readonly Cvar_t* cl_anglespeedkey = Cvar.Alloc("cl_anglespeedkey"u8, "1.5"u8);

    // lookspring/lookstrafe/sensitivity/m_pitch/m_yaw/m_forward/m_side/
    // m_filter -- in_win.c's own cvars (mouse look/sensitivity config), not
    // cl_input.c's; declared here rather than a separate InWin.cs since
    // ClInput.Move (this class's IN_Move/IN_MouseMove port) is their only
    // reader and Quake.Desktop is the only caller.
    public static readonly Cvar_t* lookspring = Cvar.Alloc("lookspring"u8, "0"u8, archive: true);
    public static readonly Cvar_t* lookstrafe = Cvar.Alloc("lookstrafe"u8, "0"u8, archive: true);
    public static readonly Cvar_t* sensitivity = Cvar.Alloc("sensitivity"u8, "3"u8, archive: true);
    public static readonly Cvar_t* m_pitch = Cvar.Alloc("m_pitch"u8, "0.022"u8, archive: true);
    public static readonly Cvar_t* m_yaw = Cvar.Alloc("m_yaw"u8, "0.022"u8, archive: true);
    public static readonly Cvar_t* m_forward = Cvar.Alloc("m_forward"u8, "1"u8, archive: true);
    public static readonly Cvar_t* m_side = Cvar.Alloc("m_side"u8, "0.8"u8, archive: true);
    public static readonly Cvar_t* m_filter = Cvar.Alloc("m_filter"u8, "0"u8, archive: true);

    /*
    ================
    CL_AdjustAngles

    Moves the local angle positions
    ================
    */
    private static void AdjustAngles()
    {
        float speed = (in_speed->State & 1) != 0
            ? Host.host_frametime * cl_anglespeedkey->Value
            : Host.host_frametime;

        if ((in_strafe->State & 1) == 0)
        {
            Cl.viewangles[QuakeDef.YAW] -= speed * cl_yawspeed->Value * KeyState(in_right);
            Cl.viewangles[QuakeDef.YAW] += speed * cl_yawspeed->Value * KeyState(in_left);
            Cl.viewangles[QuakeDef.YAW] = MathLib.anglemod(Cl.viewangles[QuakeDef.YAW]);
        }

        if ((in_klook->State & 1) != 0)
        {
            View.StopPitchDrift();
            Cl.viewangles[QuakeDef.PITCH] -= speed * cl_pitchspeed->Value * KeyState(in_forward);
            Cl.viewangles[QuakeDef.PITCH] += speed * cl_pitchspeed->Value * KeyState(in_back);
        }

        float up = KeyState(in_lookup);
        float down = KeyState(in_lookdown);

        Cl.viewangles[QuakeDef.PITCH] -= speed * cl_pitchspeed->Value * up;
        Cl.viewangles[QuakeDef.PITCH] += speed * cl_pitchspeed->Value * down;

        if (up != 0 || down != 0)
            View.StopPitchDrift();

        if (Cl.viewangles[QuakeDef.PITCH] > 80)
            Cl.viewangles[QuakeDef.PITCH] = 80;
        if (Cl.viewangles[QuakeDef.PITCH] < -70)
            Cl.viewangles[QuakeDef.PITCH] = -70;

        if (Cl.viewangles[QuakeDef.ROLL] > 50)
            Cl.viewangles[QuakeDef.ROLL] = 50;
        if (Cl.viewangles[QuakeDef.ROLL] < -50)
            Cl.viewangles[QuakeDef.ROLL] = -50;
    }

    /*
    ================
    CL_BaseMove

    Send the intended movement message to the server
    ================
    */
    public static void BaseMove(UserCmd_t* cmd)
    {
        if (Cls.signon != Cls.SIGNONS)
            return;

        AdjustAngles();

        *cmd = default;

        if ((in_strafe->State & 1) != 0)
        {
            cmd->SideMove += cl_sidespeed->Value * KeyState(in_right);
            cmd->SideMove -= cl_sidespeed->Value * KeyState(in_left);
        }

        cmd->SideMove += cl_sidespeed->Value * KeyState(in_moveright);
        cmd->SideMove -= cl_sidespeed->Value * KeyState(in_moveleft);

        cmd->UpMove += cl_upspeed->Value * KeyState(in_up);
        cmd->UpMove -= cl_upspeed->Value * KeyState(in_down);

        if ((in_klook->State & 1) == 0)
        {
            cmd->ForwardMove += View.cl_forwardspeed->Value * KeyState(in_forward);
            cmd->ForwardMove -= cl_backspeed->Value * KeyState(in_back);
        }

        if ((in_speed->State & 1) != 0)
        {
            cmd->ForwardMove *= cl_movespeedkey->Value;
            cmd->SideMove *= cl_movespeedkey->Value;
            cmd->UpMove *= cl_movespeedkey->Value;
        }
    }

    // mouse_dx/mouse_dy -- the accumulated pointer delta since the last
    // Move() call, in the original's terms `current_pos.x - window_center_x
    // + mx_accum` (a relative-to-recentered-cursor read); Quake.Desktop's
    // window layer computes this delta however OpenTK reports it (there is
    // no Win32 SetCursorPos-to-center step to replicate) and calls
    // AccumulateMouse each time new input arrives, only while the window is
    // focused and unminimized (this class's stand-in for the original's
    // `ActiveApp && !Minimized` gate in IN_Move, which the window owns).
    private static float mouse_dx, mouse_dy;
    private static float old_mouse_x, old_mouse_y;

    public static void AccumulateMouse(float dx, float dy)
    {
        mouse_dx += dx;
        mouse_dy += dy;
    }

    /*
    ===========
    IN_MouseMove / IN_Move

    IN_JoyMove has no joystick to read in this port (no controller support
    ever ported) and is dropped; IN_Move's ActiveApp/Minimized gate is
    Quake.Desktop's job (see AccumulateMouse's own comment).
    ===========
    */
    public static void Move(UserCmd_t* cmd)
    {
        float mx = mouse_dx, my = mouse_dy;
        mouse_dx = 0;
        mouse_dy = 0;

        float mouse_x, mouse_y;
        if (m_filter->Value != 0)
        {
            mouse_x = (mx + old_mouse_x) * 0.5f;
            mouse_y = (my + old_mouse_y) * 0.5f;
        }
        else
        {
            mouse_x = mx;
            mouse_y = my;
        }

        old_mouse_x = mx;
        old_mouse_y = my;

        mouse_x *= sensitivity->Value;
        mouse_y *= sensitivity->Value;

        // add mouse X/Y movement to cmd
        if ((in_strafe->State & 1) != 0 || (lookstrafe->Value != 0 && (in_mlook->State & 1) != 0))
            cmd->SideMove += m_side->Value * mouse_x;
        else
            Cl.viewangles[QuakeDef.YAW] -= m_yaw->Value * mouse_x;

        if ((in_mlook->State & 1) != 0)
            View.StopPitchDrift();

        if ((in_mlook->State & 1) != 0 && (in_strafe->State & 1) == 0)
        {
            Cl.viewangles[QuakeDef.PITCH] += m_pitch->Value * mouse_y;
            if (Cl.viewangles[QuakeDef.PITCH] > 80)
                Cl.viewangles[QuakeDef.PITCH] = 80;
            if (Cl.viewangles[QuakeDef.PITCH] < -70)
                Cl.viewangles[QuakeDef.PITCH] = -70;
        }
        else
        {
            if ((in_strafe->State & 1) != 0 && Cl.noclip_anglehack)
                cmd->UpMove -= m_forward->Value * mouse_y;
            else
                cmd->ForwardMove -= m_forward->Value * mouse_y;
        }
    }

    /*
    ============
    CL_InitInput
    ============
    */
    // Resets cvar->String to its declared default before registering --
    // Cvar.RegisterVariable repoints .String at a fresh Zone.Malloc'd copy
    // every call, so calling RegisterVariable directly on these static
    // fields (as this method used to) would, on a second call in the same
    // process (e.g. a later unit test class), read the *previous* call's
    // already-freed Zone copy as its "old value" -- a use-after-free. See
    // Common.Init's own comment on the same hazard.
    private static void RegisterCvar(Cvar_t* cvar, Utf8String value)
    {
        cvar->String = value;
        Cvar.RegisterVariable(cvar);
    }

    public static void Init()
    {
        RegisterCvar(cl_upspeed, "200"u8);
        // cl_forwardspeed -- already registered by View.Init() (see this
        // class's field-declaration comment); not re-registered here.
        RegisterCvar(cl_backspeed, "200"u8);
        RegisterCvar(cl_sidespeed, "350"u8);
        RegisterCvar(cl_movespeedkey, "2.0"u8);
        RegisterCvar(cl_yawspeed, "140"u8);
        RegisterCvar(cl_pitchspeed, "150"u8);
        RegisterCvar(cl_anglespeedkey, "1.5"u8);
        RegisterCvar(lookspring, "0"u8);
        RegisterCvar(lookstrafe, "0"u8);
        RegisterCvar(sensitivity, "3"u8);
        RegisterCvar(m_pitch, "0.022"u8);
        RegisterCvar(m_yaw, "0.022"u8);
        RegisterCvar(m_forward, "1"u8);
        RegisterCvar(m_side, "0.8"u8);
        RegisterCvar(m_filter, "0"u8);

        Cmd.AddCommand("+moveup"u8, &UpDownCmd);
        Cmd.AddCommand("-moveup"u8, &UpUpCmd);
        Cmd.AddCommand("+movedown"u8, &DownDownCmd);
        Cmd.AddCommand("-movedown"u8, &DownUpCmd);
        Cmd.AddCommand("+left"u8, &LeftDownCmd);
        Cmd.AddCommand("-left"u8, &LeftUpCmd);
        Cmd.AddCommand("+right"u8, &RightDownCmd);
        Cmd.AddCommand("-right"u8, &RightUpCmd);
        Cmd.AddCommand("+forward"u8, &ForwardDownCmd);
        Cmd.AddCommand("-forward"u8, &ForwardUpCmd);
        Cmd.AddCommand("+back"u8, &BackDownCmd);
        Cmd.AddCommand("-back"u8, &BackUpCmd);
        Cmd.AddCommand("+lookup"u8, &LookupDownCmd);
        Cmd.AddCommand("-lookup"u8, &LookupUpCmd);
        Cmd.AddCommand("+lookdown"u8, &LookdownDownCmd);
        Cmd.AddCommand("-lookdown"u8, &LookdownUpCmd);
        Cmd.AddCommand("+strafe"u8, &StrafeDownCmd);
        Cmd.AddCommand("-strafe"u8, &StrafeUpCmd);
        Cmd.AddCommand("+moveleft"u8, &MoveleftDownCmd);
        Cmd.AddCommand("-moveleft"u8, &MoveleftUpCmd);
        Cmd.AddCommand("+moveright"u8, &MoverightDownCmd);
        Cmd.AddCommand("-moveright"u8, &MoverightUpCmd);
        Cmd.AddCommand("+speed"u8, &SpeedDownCmd);
        Cmd.AddCommand("-speed"u8, &SpeedUpCmd);
        Cmd.AddCommand("+attack"u8, &AttackDownCmd);
        Cmd.AddCommand("-attack"u8, &AttackUpCmd);
        Cmd.AddCommand("+use"u8, &UseDownCmd);
        Cmd.AddCommand("-use"u8, &UseUpCmd);
        Cmd.AddCommand("+jump"u8, &JumpDownCmd);
        Cmd.AddCommand("-jump"u8, &JumpUpCmd);
        Cmd.AddCommand("impulse"u8, &ImpulseCmd);
        Cmd.AddCommand("+klook"u8, &KLookDownCmd);
        Cmd.AddCommand("-klook"u8, &KLookUpCmd);
        Cmd.AddCommand("+mlook"u8, &MLookDownCmd);
        Cmd.AddCommand("-mlook"u8, &MLookUpCmd);
    }
}
