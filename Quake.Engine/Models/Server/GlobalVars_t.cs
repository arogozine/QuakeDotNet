// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// globalvars_t (progdefs.q1). The 28-int `pad` at the start is preserved as
// a fixed buffer purely to keep field offsets identical to the original.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GlobalVars_t
{
    public fixed int Pad[28];
    public int Self;
    public int Other;
    public int World;
    public float Time;
    public float FrameTime;
    public float ForceRetouch;
    public int MapName;    // string_t
    public float DeathMatch;
    public float Coop;
    public float TeamPlay;
    public float ServerFlags;
    public float TotalSecrets;
    public float TotalMonsters;
    public float FoundSecrets;
    public float KilledMonsters;
    public float Parm1, Parm2, Parm3, Parm4, Parm5, Parm6, Parm7, Parm8;
    public float Parm9, Parm10, Parm11, Parm12, Parm13, Parm14, Parm15, Parm16;
    public Vec3_t VForward;
    public Vec3_t VUp;
    public Vec3_t VRight;
    public float TraceAllSolid;
    public float TraceStartSolid;
    public float TraceFraction;
    public Vec3_t TraceEndPos;
    public Vec3_t TracePlaneNormal;
    public float TracePlaneDist;
    public int TraceEnt;
    public float TraceInOpen;
    public float TraceInWater;
    public int MsgEntity;
    public int Main;               // func_t
    public int StartFrame;         // func_t
    public int PlayerPreThink;     // func_t
    public int PlayerPostThink;    // func_t
    public int ClientKill;         // func_t
    public int ClientConnect;      // func_t
    public int PutClientInServer;  // func_t
    public int ClientDisconnect;   // func_t
    public int SetNewParms;        // func_t
    public int SetChangeParms;     // func_t
}
