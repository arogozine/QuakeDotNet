// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// chase.c -- chase camera code.
/// </summary>
public static unsafe class Chase
{
    public static readonly Cvar_t* chase_back = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* chase_up = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* chase_right = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* chase_active = NativeMemory.AllocZeroed<Cvar_t>();

    public static Vec3_t chase_dest;

    // TraceLine -- SV_RecursiveHullCheck against cl.worldmodel's hull 0.
    private static void TraceLine(in Vec3_t start, in Vec3_t end, ref Vec3_t impact)
    {
        Trace_t trace = default;
        World.RecursiveHullCheck(&Cl.worldmodel->Hulls[0], 0, 0, 1, start, end, ref trace);
        impact = trace.EndPos;
    }

    public static void Init()
    {
        RegisterCvar(chase_back, "chase_back"u8, "100"u8);
        RegisterCvar(chase_up, "chase_up"u8, "16"u8);
        RegisterCvar(chase_right, "chase_right"u8, "0"u8);
        RegisterCvar(chase_active, "chase_active"u8, "0"u8);
    }

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value)
    {
        cvar->Name = name;
        cvar->String = value;
        Cvar.RegisterVariable(cvar);
    }

    /*
    ===============
    Chase_Update
    ===============
    */
    public static void Update()
    {
        // if can't see player, reset
        MathLib.AngleVectors(Cl.viewangles, out Vec3_t forward, out Vec3_t right, out Vec3_t up);

        // calc exact destination
        for (int i = 0; i < 3; i++)
            chase_dest[i] = RShared.r_refdef->Vieworg[i]
                - forward[i] * chase_back->Value
                - right[i] * chase_right->Value;
        chase_dest[2] = RShared.r_refdef->Vieworg[2] + chase_up->Value;

        // find the spot the player is looking at
        Vec3_t stop = default;
        Vec3_t dest = RShared.r_refdef->Vieworg + 4096 * forward;
        TraceLine(RShared.r_refdef->Vieworg, dest, ref stop);

        // calculate pitch to look at the same spot from camera
        stop = stop - RShared.r_refdef->Vieworg;
        float dist = Vector3.Dot(stop, forward);
        if (dist < 1)
            dist = 1;
        RShared.r_refdef->ViewAngles[QuakeDef.PITCH] = -MathF.Atan(stop[2] / dist) / MathLib.M_PI * 180;

        // move towards destination
        RShared.r_refdef->Vieworg = chase_dest;
    }
}
