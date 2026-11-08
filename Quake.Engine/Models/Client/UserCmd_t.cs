// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// usercmd_t (client.h) -- the QUAKE2 lightlevel field is not built. The server's
// SV_ReadClientMove (SvUser.cs) writes angles/buttons/impulse straight into the edict, as
// in sv_user.c, so ViewAngles is declared for fidelity but unused, as in C.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct UserCmd_t
{
    public Vec3_t ViewAngles;

    // intended velocities
    public float ForwardMove;
    public float SideMove;
    public float UpMove;
}
