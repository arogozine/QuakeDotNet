// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;
using Quake.Engine.Memory;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// client.h's client_state_t (cl). Fields live here as plain statics rather
/// than inside one struct; cl_main.c's ClMain.cs and cl_parse.c's ClParse.cs
/// are the real per-frame writers of most of them (see each field's own
/// comment below for its writer), while r_main.c/r_misc.c's RMain.cs/RMisc.cs
/// are consumers.
/// </summary>
public static unsafe class Cl
{
    // cl.worldmodel -- R_RenderView_ requires this non-null; callers
    // (including tests) must assign a loaded Model_t* first.
    public static Model_t* worldmodel;

    public static int intermission;    // cl.intermission (R_SetVrect)
    public static double time;         // cl.time
    public static double oldtime;      // cl.oldtime (R_DrawParticles' frametime)
    public static int viewentity;      // cl.viewentity

    // STAT_* (quakedef.h) -- cl.stats indices.

    public static readonly int* stats = NativeMemory.AllocArrayZeroed<int>(MAX_CL_STATS); // cl.stats
    public static int items;           // cl.items
    public static readonly Entity_t* viewent = NativeMemory.AllocZeroed<Entity_t>();    // cl.viewent
    public static int maxclients;      // cl.maxclients (R_SetupFrame cheat gate)
    public static int protocol;        // cl.protocol (Mark V) -- set from svc_serverinfo; 15 or 666

    // svc_fog (PROTOCOL_FITZQUAKE) -- parsed and stored; not rendered yet
    public static float fog_density;
    public static float fog_red;
    public static float fog_green;
    public static float fog_blue;
    public static float fog_time;

    // cl.free_efrags -- head of the efrag_t free list; the backing
    // ClEntities.cl_efrags[MAX_EFRAGS] array is allocated there, and
    // ClMain.ClearState links it into this free list (cl_main.c's
    // CL_ClearState), same as the original.
    public static EFrag_t* free_efrags;

    public const int NUM_CSHIFTS = 4; // client.h
    // cl.cshifts -- consumed by View.UpdatePalette/CalcBlend; the
    // (int)CShiftType.Contents slot is written each frame by RMain.EdgeDrawing's
    // View.SetContentsColor(r_viewleaf->Contents) call.
    public static readonly CShift_t* cshifts = NativeMemory.AllocArrayZeroed<CShift_t>(NUM_CSHIFTS);
    // cl.prev_cshifts -- view.c's V_UpdatePalette change-detection copy.
    public static readonly CShift_t* prev_cshifts = NativeMemory.AllocArrayZeroed<CShift_t>(NUM_CSHIFTS);

    // view.c's player-eye/movement-prediction fields. ClMain.cs/ClParse.cs
    // are the real writers of most of these every frame (ClMain.ClearState/
    // LerpPoint, ClParse.ParseUpdate/ParseServerMessage).
    public static Vec3_t viewangles;   // cl.viewangles
    public static Vec3_t velocity;     // cl.velocity
    public static float viewheight;    // cl.viewheight
    public static Vec3_t punchangle;   // cl.punchangle
    public static readonly Vec3_t* mviewangles = NativeMemory.AllocArrayZeroed<Vec3_t>(2); // cl.mviewangles -- [0] is the just-read demo frame, [1] the previous one

    public static float idealpitch;    // cl.idealpitch
    public static float pitchvel;      // cl.pitchvel
    public static bool nodrift;        // cl.nodrift
    public static float driftmove;     // cl.driftmove
    public static double laststop;     // cl.laststop

    public static bool onground;       // cl.onground
    public static bool paused;         // cl.paused
    public static double faceanimtime; // cl.faceanimtime

    // cl.cmd (usercmd_t) -- ClInput.cs (cl_input.c) builds the real usercmd_t
    // each frame; ClMain.SendMove copies it here (Cl.cmd = *cmd).
    public static UserCmd_t cmd;

    // cl.model_precache[MAX_MODELS] -- populated by ClParse.ParseServerInfo.
    public static readonly Model_t** model_precache = NativeMemory.AllocPointerArray<Model_t>(MAX_MODELS);

    // noclip_anglehack -- cl_main.c global, set by the "noclip" impulse
    // handler (HostCmd.Noclip_f) and read by ClInput.cs (see BaseMove).
    public static bool noclip_anglehack;

    // sbar.c's HUD-flash/scoreboard/intermission fields; ClParse.cs
    // (cl_parse.c) is the real writer of most of these every frame.
    public static readonly float* item_gettime = NativeMemory.AllocArrayZeroed<float>(32); // cl.item_gettime
    public static int gametype;            // cl.gametype
    public const int LEVELNAME_LENGTH = 40; // client.h
    public static readonly byte* levelname = NativeMemory.AllocArrayZeroed<byte>(LEVELNAME_LENGTH); // cl.levelname
    public static int completed_time;      // cl.completed_time

    // cl.scores[MAX_SCOREBOARD] -- deathmatch/coop scoreboard entries;
    // ClParse.cs's ParseUpdate/(int)Svc.UpdateName populate this each frame for
    // every connected player. Starts zero-initialized (every name[0] == 0),
    // matching Sbar.cs's loops that already skip entries with an empty name.
    public static readonly ScoreBoard_t* scores = NativeMemory.AllocArrayZeroed<ScoreBoard_t>(MAX_SCOREBOARD);

    // cl_parse.c's ClParse.cs is the real writer of everything below --
    // added here (rather than there) since Cl is where the rest of
    // client_state_t already lives.

    // cl.sound_precache[MAX_SOUNDS] -- populated by ClParse.ParseServerInfo.
    public static readonly Sfx_t** sound_precache = NativeMemory.AllocPointerArray<Sfx_t>(MAX_SOUNDS);

    public static int num_entities;    // cl.num_entities -- held in ClEntities.cl_entities
    public static int num_statics;     // cl.num_statics -- held in ClEntities.cl_static_entities

    public static readonly double* mtime = NativeMemory.AllocArrayZeroed<double>(2); // cl.mtime -- timestamp of last two messages
    public static readonly Vec3_t* mvelocity = NativeMemory.AllocArrayZeroed<Vec3_t>(2); // cl.mvelocity -- update by server, used for lean+bob
    public static bool inwater;        // cl.inwater

    public static int cdtrack;         // cl.cdtrack
    public static int looptrack;       // cl.looptrack

    // cl.movemessages -- ClMain.SendMove's "dump the first two messages"
    // counter (since connecting to this server).
    public static int movemessages;

    // cl_name/cl_color -- ClMain.Init (CL_Init) is their real
    // Cvar_RegisterVariable call site. Declared here with the same default
    // string/value the original assigns at compile time, so ClParse.SignonReply's
    // stage-2 reply matches the original before "name"/"color" is ever run,
    // even before ClMain.Init() has registered the live Cvar.cvar_vars entry.
    public static readonly Cvar_t* cl_name = Cvar.Alloc("_cl_name"u8, "player"u8);
    public static readonly Cvar_t* cl_color = Cvar.Alloc("_cl_color"u8, "0"u8);

    // cl_shownet -- debug-only SHOWNET() print gate in ClParse.ParseServerMessage;
    // same ClMain.Init() registration site as cl_name/cl_color above.
    public static readonly Cvar_t* cl_shownet = Cvar.Alloc("cl_shownet"u8, "0"u8);

    // cl_nolerp -- ClMain.LerpPoint's interpolation disable switch; same
    // ClMain.Init() registration site as cl_name/cl_color above.
    public static readonly Cvar_t* cl_nolerp = Cvar.Alloc("cl_nolerp"u8, "0"u8);
}

public static unsafe class ClLightStyle
{

    // cl_lightstyle[MAX_LIGHTSTYLES]
    public static readonly LightStyle_t* cl_lightstyle = NativeMemory.AllocArrayZeroed<LightStyle_t>(MAX_LIGHTSTYLES);
}

/// <summary>
/// client.h's cl_entities/cl_static_entities/cl_efrags/cl_dlights/cl_visedicts
/// arrays. cl_entities is populated by ClParse.cs's ParseUpdate/
/// ParseBaseline/ParseStatic (cl_parse.c); cl_numvisedicts is set each frame
/// by ClMain.RelinkEntities (cl_main.c's CL_RelinkEntities, called from
/// ClMain's per-frame update), so R_DrawEntitiesOnList/R_DrawBEntitiesOnList's
/// visedict loops now have real data to walk. ClMain.AllocDlight (CL_AllocDlight)
/// is likewise ported and hands out pointers into cl_dlights.
/// </summary>
public static unsafe class ClEntities
{
    // Mark V quakedef.h engine_limits
    public const int MAX_DLIGHTS = 128;   // client.h -- MAX_FITZQUAKE_DLIGHTS (was 32)
    public const int MAX_VISEDICTS = 4096; // client.h -- MAX_MARK_V_VISEDICTS (was 256)
    public const int MAX_EFRAGS = 8192;    // client.h -- MAX_MARK_V_EFRAGS (was 640)
    public const int MAX_STATIC_ENTITIES = 512; // client.h -- MAX_FITZQUAKE_STATIC_ENTITIES (was 128)

    // cl_entities[MAX_EDICTS]
    public static readonly Entity_t* cl_entities = NativeMemory.AllocArrayZeroed<Entity_t>(QuakeDef.MAX_EDICTS);

    // cl_static_entities[MAX_STATIC_ENTITIES] -- populated by ClParse.ParseStatic
    public static readonly Entity_t* cl_static_entities = NativeMemory.AllocArrayZeroed<Entity_t>(MAX_STATIC_ENTITIES);

    // cl_efrags[MAX_EFRAGS] -- the efrag_t pool R_AddEfrags/R_SplitEntityOnNode
    // (REfrag.cs) draw from; ClMain.ClearState links it into Cl.free_efrags's
    // free list, same as the original's CL_ClearState.
    public static readonly EFrag_t* cl_efrags = NativeMemory.AllocArrayZeroed<EFrag_t>(MAX_EFRAGS);

    // cl_dlights[MAX_DLIGHTS]
    public static readonly DLight_t* cl_dlights = NativeMemory.AllocArrayZeroed<DLight_t>(MAX_DLIGHTS);

    // cl_visedicts[MAX_VISEDICTS], cl_numvisedicts
    public static readonly Entity_t** cl_visedicts = NativeMemory.AllocPointerArray<Entity_t>(MAX_VISEDICTS);
    public static int cl_numvisedicts;
}

/// <summary>
/// client.h's client_static_t (cls) -- like ClDemo.cs's demofile/
/// demoplayback/demorecording/forcetrack (which are this same struct's
/// fields, declared there since cl_demo.c was their earliest consumer),
/// this holds only the fields screen.c's SCR_UpdateScreen/
/// SCR_BeginLoadingPlaque/SCR_ScreenShot_f read: the connection-state
/// gates and the last-received-message timestamp SCR_DrawNet checks.
/// cactive_t (client.h) is the CActive enum (Enums/CActive.cs).
/// </summary>
public static unsafe class Cls
{

    public const int SIGNONS = 4; // quakedef.h

    // cls.state -- defaults to ca_disconnected, matching Host_Init's real
    // initial value before any connection attempt.
    public static CActive state = CActive.Disconnected;

    public static int signon;                  // cls.signon (0 to SIGNONS)
    public static double last_received_message; // cls.last_received_message

    // cls.message -- reliable buffer ClParse.SignonReply writes (int)Clc.StringCmd
    // replies into. Normally SZ_Alloc'd by CL_Init (ClMain.Init); allocated
    // here directly instead (same 1024-byte size CL_Init uses) so SignonReply
    // works even before ClMain.Init() runs.
    internal const int MESSAGE_BUFSIZE = 1024;
    internal static readonly byte* messageBuffer = (byte*)NativeMemory.AllocZeroed(MESSAGE_BUFSIZE);
    public static readonly SizeBuf_t* message = Sz.Wrap(messageBuffer, MESSAGE_BUFSIZE);

    // cls.spawnparms -- personalization string sent back on signon stage 2
    // ("spawn <spawnparms>"); left empty (matching its post-CL_ClearState
    // zeroed state) since nothing populates it -- Host_Map_f's spawnparms
    // save/restore (host_cmd.c) isn't part of this port's "map" command.
    public static readonly byte* spawnparms = (byte*)NativeMemory.AllocZeroed(1);

    // cls.mapstring -- char mapstring[MAX_QPATH] (client.h); the full "map"
    // command line, rebuilt in HostCmd.Map_f. Currently inert since nothing
    // reads it back yet.
    public static readonly byte* mapstring = (byte*)NativeMemory.AllocZeroed(QuakeDef.MAX_QPATH);

    // cls.netcon -- the client's end of the loopback socket pair, set by
    // ClMain.EstablishConnection ("connect local", wired into HostCmd.Map_f).
    public static QSocket_t* netcon;

    // cls.timedemo -- demo-benchmark playback mode, set by ClDemo.TimeDemo_f.
    // Host_FilterTime's earliest consumer.
    public static bool timedemo;

    // cls.td_lastframe/td_startframe/td_starttime -- timedemo bookkeeping,
    // set by ClDemo.TimeDemo_f/GetMessage and read by ClDemo.FinishTimeDemo.
    public static int td_lastframe;
    public static int td_startframe;
    public static float td_starttime; // client.h: float td_starttime
}

/// <summary>
/// server.h's server_t (sv). The rest of the fields (edicts/num_edicts/
/// worldmodel/state/time/...) are in Sv.cs alongside SV_Init; these two are
/// declared here since r_misc.c's R_SetupFrame (RMisc.cs) was their earliest
/// consumer.
/// </summary>
public static unsafe partial class Sv
{
    public static bool active;

    // sv.name -- current map name (server.h); R_ReadPointFile_f builds
    // "maps/<name>.pts" from it. Populated by Sv.SpawnServer (Sv.cs).
    public static byte* Name;
}
