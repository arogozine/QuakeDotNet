// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>quakedef.h - global compile-time constants shared by every module.</summary>
public static class QuakeDef
{
    public const float VERSION = 1.09f; // VERSION (quakedef.h)

    public const int MAX_NUM_ARGVS = 50;  // MAX_NUM_ARGVS (quakedef.h)

    public const int PITCH = 0; // PITCH (quakedef.h) -- up / down
    public const int YAW = 1;   // YAW (quakedef.h) -- left / right
    public const int ROLL = 2;  // ROLL (quakedef.h) -- fall over

    public const float ON_EPSILON = 0.1f; // ON_EPSILON (quakedef.h) -- point on plane side epsilon

    public const int MAX_QPATH = 64;      // MAX_QPATH (quakedef.h) -- max length of a quake game pathname
    public const int MAX_OSPATH = 128;    // MAX_OSPATH (quakedef.h) -- max length of a filesystem pathname

    public const int MAX_MSGLEN = 65536;  // MAX_MSGLEN (quakedef.h) -- Mark V MAX_MARK_V_MSGLEN (was 8000)
    public const int MAX_DATAGRAM = 1024; // MAX_DATAGRAM (quakedef.h) -- max length of unreliable message; Mark V MAX_WINQUAKE_DATAGRAM (protocol 15)
    public const int MAX_MARK_V_DATAGRAM = 65527; // Mark V: NETFLAG_DATA - 1 - NET_HEADERSIZE (0x10000 - 1 - 8)
    public const int MAX_FITZQUAKE_DATAGRAM_MTU = 1400; // Mark V: protocol 666 datagram size when networked
    public const int MAX_WINQUAKE_SIGNON = 8000 - 2;    // Mark V: protocol 15 signon size
    public const int MAX_MARK_V_SIGNON = MAX_MSGLEN - 2; // Mark V: protocol 666 signon size

    // per-level limits
    public const int MAX_EDICTS = 32000;    // MAX_EDICTS (quakedef.h) -- Mark V MAX_FITZQUAKE_MAX_EDICTS (was 600)
    public const int MAX_LIGHTSTYLES = 64;  // MAX_LIGHTSTYLES (quakedef.h)
    public const int MAX_MODELS = 2048;     // MAX_MODELS (quakedef.h) -- Mark V MAX_FITZQUAKE_MODELS (was 256); protocol 15 wire indices stay <= 255
    public const int MAX_SOUNDS = 2048;     // MAX_SOUNDS (quakedef.h) -- Mark V MAX_FITZQUAKE_SOUNDS (was 256); protocol 15 wire indices stay <= 255

    public const int SAVEGAME_COMMENT_LENGTH = 39; // SAVEGAME_COMMENT_LENGTH (quakedef.h)

    public const int MAX_STYLESTRING = 64;  // MAX_STYLESTRING (quakedef.h)

    // stats are integers communicated to the client by the server
    public const int MAX_CL_STATS = 32;     // MAX_CL_STATS (quakedef.h)

    // item flags (cl.items)
    public const int IT_SHOTGUN = 1;               // IT_SHOTGUN (quakedef.h)
    public const int IT_SUPER_SHOTGUN = 2;         // IT_SUPER_SHOTGUN (quakedef.h)
    public const int IT_NAILGUN = 4;               // IT_NAILGUN (quakedef.h)
    public const int IT_SUPER_NAILGUN = 8;         // IT_SUPER_NAILGUN (quakedef.h)
    public const int IT_GRENADE_LAUNCHER = 16;     // IT_GRENADE_LAUNCHER (quakedef.h)
    public const int IT_ROCKET_LAUNCHER = 32;      // IT_ROCKET_LAUNCHER (quakedef.h)
    public const int IT_LIGHTNING = 64;            // IT_LIGHTNING (quakedef.h)
    public const int IT_SUPER_LIGHTNING = 128;     // IT_SUPER_LIGHTNING (quakedef.h)
    public const int IT_SHELLS = 256;              // IT_SHELLS (quakedef.h)
    public const int IT_NAILS = 512;               // IT_NAILS (quakedef.h)
    public const int IT_ROCKETS = 1024;            // IT_ROCKETS (quakedef.h)
    public const int IT_CELLS = 2048;              // IT_CELLS (quakedef.h)
    public const int IT_AXE = 4096;                // IT_AXE (quakedef.h)
    public const int IT_ARMOR1 = 8192;             // IT_ARMOR1 (quakedef.h)
    public const int IT_ARMOR2 = 16384;            // IT_ARMOR2 (quakedef.h)
    public const int IT_ARMOR3 = 32768;            // IT_ARMOR3 (quakedef.h)
    public const int IT_SUPERHEALTH = 65536;       // IT_SUPERHEALTH (quakedef.h)
    public const int IT_KEY1 = 131072;             // IT_KEY1 (quakedef.h)
    public const int IT_KEY2 = 262144;             // IT_KEY2 (quakedef.h)
    public const int IT_INVISIBILITY = 524288;     // IT_INVISIBILITY (quakedef.h)
    public const int IT_INVULNERABILITY = 1048576; // IT_INVULNERABILITY (quakedef.h)
    public const int IT_SUIT = 2097152;            // IT_SUIT (quakedef.h)
    public const int IT_QUAD = 4194304;            // IT_QUAD (quakedef.h)
    public const int IT_SIGIL1 = 1 << 28;          // IT_SIGIL1 (quakedef.h)
    public const int IT_SIGIL2 = 1 << 29;          // IT_SIGIL2 (quakedef.h)
    public const int IT_SIGIL3 = 1 << 30;          // IT_SIGIL3 (quakedef.h)
    public const int IT_SIGIL4 = 1 << 31;          // IT_SIGIL4 (quakedef.h)

    // rogue changed and added defines
    public const int RIT_SHELLS = 128;                    // RIT_SHELLS (quakedef.h)
    public const int RIT_NAILS = 256;                     // RIT_NAILS (quakedef.h)
    public const int RIT_ROCKETS = 512;                   // RIT_ROCKETS (quakedef.h)
    public const int RIT_CELLS = 1024;                    // RIT_CELLS (quakedef.h)
    public const int RIT_AXE = 2048;                      // RIT_AXE (quakedef.h)
    public const int RIT_LAVA_NAILGUN = 4096;             // RIT_LAVA_NAILGUN (quakedef.h)
    public const int RIT_LAVA_SUPER_NAILGUN = 8192;       // RIT_LAVA_SUPER_NAILGUN (quakedef.h)
    public const int RIT_MULTI_GRENADE = 16384;           // RIT_MULTI_GRENADE (quakedef.h)
    public const int RIT_MULTI_ROCKET = 32768;            // RIT_MULTI_ROCKET (quakedef.h)
    public const int RIT_PLASMA_GUN = 65536;              // RIT_PLASMA_GUN (quakedef.h)
    public const int RIT_ARMOR1 = 8388608;                // RIT_ARMOR1 (quakedef.h)
    public const int RIT_ARMOR2 = 16777216;               // RIT_ARMOR2 (quakedef.h)
    public const int RIT_ARMOR3 = 33554432;               // RIT_ARMOR3 (quakedef.h)
    public const int RIT_LAVA_NAILS = 67108864;           // RIT_LAVA_NAILS (quakedef.h)
    public const int RIT_PLASMA_AMMO = 134217728;         // RIT_PLASMA_AMMO (quakedef.h)
    public const int RIT_MULTI_ROCKETS = 268435456;       // RIT_MULTI_ROCKETS (quakedef.h)
    public const int RIT_SHIELD = 536870912;              // RIT_SHIELD (quakedef.h)
    public const int RIT_ANTIGRAV = 1073741824;           // RIT_ANTIGRAV (quakedef.h)
    public const int RIT_SUPERHEALTH = unchecked((int)2147483648); // RIT_SUPERHEALTH (quakedef.h)

    // hipnotic added defines
    public const int HIT_PROXIMITY_GUN_BIT = 16;                  // HIT_PROXIMITY_GUN_BIT (quakedef.h)
    public const int HIT_MJOLNIR_BIT = 7;                         // HIT_MJOLNIR_BIT (quakedef.h)
    public const int HIT_LASER_CANNON_BIT = 23;                   // HIT_LASER_CANNON_BIT (quakedef.h)
    public const int HIT_PROXIMITY_GUN = 1 << HIT_PROXIMITY_GUN_BIT; // HIT_PROXIMITY_GUN (quakedef.h)
    public const int HIT_MJOLNIR = 1 << HIT_MJOLNIR_BIT;          // HIT_MJOLNIR (quakedef.h)
    public const int HIT_LASER_CANNON = 1 << HIT_LASER_CANNON_BIT; // HIT_LASER_CANNON (quakedef.h)
    public const int HIT_WETSUIT = 1 << (23 + 2);                 // HIT_WETSUIT (quakedef.h)
    public const int HIT_EMPATHY_SHIELDS = 1 << (23 + 3);         // HIT_EMPATHY_SHIELDS (quakedef.h)

    public const int MAX_SCOREBOARD = 16;      // MAX_SCOREBOARD (quakedef.h)
    public const int MAX_SCOREBOARDNAME = 32;  // MAX_SCOREBOARDNAME (quakedef.h)
}
