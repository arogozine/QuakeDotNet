// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// entvars_t (progdefs.q1 -- QUAKE2 is never defined for WinQuake). Field
// order/types match qcc's generated header exactly; string_t/func_t/entity
// fields are plain ints (progs-relative string offset / function index /
// EDICT_TO_PROG-style byte offset), matching the original union layout.
[StructLayout(LayoutKind.Sequential)]
public struct EntVars_t
{
    public float ModelIndex;
    public Vec3_t AbsMin;
    public Vec3_t AbsMax;
    public float LTime;
    public float MoveType;
    public float Solid;
    public Vec3_t Origin;
    public Vec3_t OldOrigin;
    public Vec3_t Velocity;
    public Vec3_t Angles;
    public Vec3_t AVelocity;
    public Vec3_t PunchAngle;
    public int ClassName;   // string_t
    public int Model;       // string_t
    public float Frame;
    public float Skin;
    public float Effects;
    public Vec3_t Mins;
    public Vec3_t Maxs;
    public Vec3_t Size;
    public int Touch;       // func_t
    public int Use;         // func_t
    public int Think;       // func_t
    public int Blocked;     // func_t
    public float NextThink;
    public int GroundEntity;
    public float Health;
    public float Frags;
    public float Weapon;
    public int WeaponModel; // string_t
    public float WeaponFrame;
    public float CurrentAmmo;
    public float AmmoShells;
    public float AmmoNails;
    public float AmmoRockets;
    public float AmmoCells;
    public float Items;
    public float TakeDamage;
    public int Chain;
    public float DeadFlag;
    public Vec3_t ViewOfs;
    public float Button0;
    public float Button1;
    public float Button2;
    public float Impulse;
    public float FixAngle;
    public Vec3_t VAngle;
    public float IdealPitch;
    public int NetName;     // string_t
    public int Enemy;
    public float Flags;
    public float ColorMap;
    public float Team;
    public float MaxHealth;
    public float TeleportTime;
    public float ArmorType;
    public float ArmorValue;
    public float WaterLevel;
    public float WaterType;
    public float IdealYaw;
    public float YawSpeed;
    public int AiEnt;
    public int GoalEntity;
    public float SpawnFlags;
    public int Target;      // string_t
    public int TargetName;  // string_t
    public float DmgTake;
    public float DmgSave;
    public int DmgInflictor;
    public int Owner;
    public Vec3_t MoveDir;
    public int Message;     // string_t
    public float Sounds;
    public int Noise;       // string_t
    public int Noise1;      // string_t
    public int Noise2;      // string_t
    public int Noise3;      // string_t
}
