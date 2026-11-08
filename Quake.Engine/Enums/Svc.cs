// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: protocol.h svc_* (Protocol.cs).
// Each member's original constant name is given in its trailing comment.
public enum Svc
{
    Bad = 0, // svc_bad
    Nop = 1, // svc_nop
    Disconnect = 2, // svc_disconnect
    UpdateStat = 3, // svc_updatestat
    Version = 4, // svc_version
    SetView = 5, // svc_setview
    Sound = 6, // svc_sound
    Time = 7, // svc_time
    Print = 8, // svc_print
    StuffText = 9, // svc_stufftext
    SetAngle = 10, // svc_setangle
    ServerInfo = 11, // svc_serverinfo
    LightStyle = 12, // svc_lightstyle
    UpdateName = 13, // svc_updatename
    UpdateFrags = 14, // svc_updatefrags
    ClientData = 15, // svc_clientdata
    StopSound = 16, // svc_stopsound
    UpdateColors = 17, // svc_updatecolors
    Particle = 18, // svc_particle
    Damage = 19, // svc_damage
    SpawnStatic = 20, // svc_spawnstatic
    SpawnBaseline = 22, // svc_spawnbaseline
    TempEntity = 23, // svc_temp_entity
    SetPause = 24, // svc_setpause
    SignOnNum = 25, // svc_signonnum
    CenterPrint = 26, // svc_centerprint
    KilledMonster = 27, // svc_killedmonster
    FoundSecret = 28, // svc_foundsecret
    SpawnStaticSound = 29, // svc_spawnstaticsound
    Intermission = 30, // svc_intermission
    Finale = 31, // svc_finale
    CdTrack = 32, // svc_cdtrack
    SellScreen = 33, // svc_sellscreen
    CutScene = 34, // svc_cutscene
    Skybox = 37, // svc_skybox (PROTOCOL_FITZQUAKE)
    Bf = 40, // svc_bf
    Fog = 41, // svc_fog
    SpawnBaseline2 = 42, // svc_spawnbaseline2
    SpawnStatic2 = 43, // svc_spawnstatic2
    SpawnStaticSound2 = 44, // svc_spawnstaticsound2
}
