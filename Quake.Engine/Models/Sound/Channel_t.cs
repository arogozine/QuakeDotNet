// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// channel_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Channel_t
{
    public Sfx_t* Sfx;        // sfx number
    public int LeftVol;       // 0-255 volume
    public int RightVol;      // 0-255 volume
    public int End;           // end time in global paintsamples
    public int Pos;           // sample position in sfx
    public int Looping;       // where to loop, -1 = no looping
    public int EntNum;        // to allow overriding a specific sound
    public int EntChannel;
    public Vec3_t Origin;     // origin of sound effect
    public float DistMult;    // distance multiplier (attenuation/clipK)
    public int MasterVol;     // 0-255 master volume
}
