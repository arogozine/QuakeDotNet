// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// cl_main.c -- client main loop: connecting to (the loopback-only) server,
/// pumping incoming messages each frame, and sending the local player's
/// movement back.
/// </summary>
public static unsafe class ClMain
{
    /*
    =====================
    CL_ClearState
    =====================
    */
    public static void ClearState()
    {
        if (!Sv.active)
            Host.ClearMemory();

        ResetClientState();

        // Mark V CL_ClearState -- if not running a server, restore datagram cap for the client
        if (!Sv.active)
            Sv.host_protocol_datagram_maxsize = QuakeDef.MAX_MARK_V_DATAGRAM;

        SizeBuf_t* msg = Cls.message;
        Sz.Clear(msg);

        // clear other arrays
        NativeMemory.Clear(ClEntities.cl_efrags, ClEntities.MAX_EFRAGS);
        NativeMemory.Clear(ClEntities.cl_entities, QuakeDef.MAX_EDICTS);
        NativeMemory.Clear(ClEntities.cl_static_entities, ClEntities.MAX_STATIC_ENTITIES);
        NativeMemory.Clear(ClEntities.cl_dlights, ClEntities.MAX_DLIGHTS);
        NativeMemory.Clear(ClLightStyle.cl_lightstyle, QuakeDef.MAX_LIGHTSTYLES);
        NativeMemory.Clear(ClTEnt.cl_temp_entities, ClTEnt.MAX_TEMP_ENTITIES);
        ClTEnt.num_temp_entities = 0;
        NativeMemory.Clear(ClTEnt.cl_beams, ClTEnt.MAX_BEAMS);

        // allocate the efrags and chain together into a free list
        Cl.free_efrags = &ClEntities.cl_efrags[0];
        for (int i = 0; i < ClEntities.MAX_EFRAGS - 1; i++)
            ClEntities.cl_efrags[i].EntNext = &ClEntities.cl_efrags[i + 1];
        ClEntities.cl_efrags[ClEntities.MAX_EFRAGS - 1].EntNext = null;
    }

    // "wipe the entire cl structure" -- memset(&cl, 0, sizeof(cl)) in the
    // original, done both here (CL_ClearState) and inline in
    // Host_ClearMemory. cl (client_state_t) is a set of discrete static
    // fields in this port (Cl.cs), not a single struct, so each is reset
    // individually rather than via a literal memset; shared with
    // Host.ClearMemory so a level change through SV_SpawnServer (which
    // calls Host_ClearMemory directly, without going through
    // CL_ClearState) also nulls out Cl.worldmodel and friends instead of
    // leaving them dangling into memory the Hunk just freed.
    public static void ResetClientState()
    {
        Cl.intermission = 0;
        Cl.time = 0;
        Cl.oldtime = 0;
        Cl.viewentity = 0;
        NativeMemory.Clear(Cl.stats, QuakeDef.MAX_CL_STATS);
        Cl.items = 0;
        *Cl.viewent = default;
        Cl.maxclients = 0;
        Cl.protocol = Protocol.PROTOCOL_NETQUAKE;
        NativeMemory.Clear(Cl.item_gettime, 32);
        Cl.faceanimtime = 0;
        NativeMemory.Clear(Cl.cshifts, Cl.NUM_CSHIFTS);
        NativeMemory.Clear(Cl.prev_cshifts, Cl.NUM_CSHIFTS);
        Cl.viewangles = default;
        Cl.velocity = default;
        Cl.punchangle = default;
        Cl.idealpitch = 0;
        Cl.pitchvel = 0;
        Cl.nodrift = false;
        Cl.driftmove = 0;
        Cl.laststop = 0;
        Cl.onground = false;
        Cl.paused = false;
        Cl.inwater = false;
        Cl.cmd = default;
        NativeMemory.ClearPointerArray<Model_t>(Cl.model_precache, QuakeDef.MAX_MODELS);
        NativeMemory.ClearPointerArray<Sfx_t>(Cl.sound_precache, QuakeDef.MAX_SOUNDS);
        NativeMemory.Clear(Cl.levelname, Cl.LEVELNAME_LENGTH);
        Cl.gametype = 0;
        Cl.completed_time = 0;
        NativeMemory.Clear(Cl.scores, QuakeDef.MAX_SCOREBOARD);
        Cl.num_entities = 0;
        Cl.num_statics = 0;
        Cl.mtime[0] = 0;
        Cl.mtime[1] = 0;
        Cl.mvelocity[0] = default;
        Cl.mvelocity[1] = default;
        Cl.cdtrack = 0;
        Cl.looptrack = 0;
        Cl.movemessages = 0;
        Cl.worldmodel = null;
        Cl.noclip_anglehack = false;
    }

    /*
    =====================
    CL_Disconnect

    Sends a disconnect message to the server
    This is also called on Host_Error, so it shouldn't cause any errors
    =====================
    */
    public static void Disconnect()
    {
        SndDma.StopAllSounds(true);

        if (ClDemo.demoplayback)
            ClDemo.StopPlayback();
        else if (Cls.state == CActive.Connected)
        {
            if (ClDemo.demorecording)
                ClDemo.Stop_f();

            Con.DPrintf("Sending (int)Clc.Disconnect\n"u8);
            SizeBuf_t* msg = Cls.message;
            Sz.Clear(msg);
            Msg.WriteByte(msg, (int)Clc.Disconnect);
            Net.SendUnreliableMessage(Cls.netcon, msg);
            Sz.Clear(msg);
            Net.Close(Cls.netcon);

            Cls.state = CActive.Disconnected;

            // if running a local server, shut it down. Host.ShutdownServer
            // itself calls back into Disconnect(), but only when
            // Cls.state == CActive.Connected -- already false by this point, so
            // no recursion. Without this, a local server left running (e.g.
            // Host_Loadgame_f disconnecting and re-spawning while already
            // in-game) leaks the previous game's server-side loopback
            // qsocket (NetLoop.loop_server is never freed back to
            // Net.net_freeSockets), which starves the fixed-size qsocket
            // pool and surfaces as "Loop_Connect: no qsocket available" ->
            // Host_Error on the very next reconnect.
            if (Sv.active)
                Host.ShutdownServer(false);
        }

        ClDemo.demoplayback = false;
        Cls.timedemo = false;
        Cls.signon = 0;
    }

    // Host_Disconnect_f / "disconnect" console command wrapper.
    public static void Disconnect_f() => Disconnect();

    /*
    =====================
    CL_EstablishConnection

    Host should be either "local" or a net address to be passed on
    =====================
    */
    public static void EstablishConnection(Utf8String host)
    {
        if (Cls.state == CActive.Dedicated)
            return;
        if (ClDemo.demoplayback)
            return;

        Disconnect();

        Cls.netcon = Net.Connect(host);
        if (Cls.netcon == null)
            Host.Error("CL_Connect: connect failed\n"u8);
        Con.DPrintf($"CL_EstablishConnection: connected to {host}\n");

        ClDemo.demonum = -1; // not in the demo loop now
        Cls.state = CActive.Connected;
        Cls.signon = 0; // need all the signon messages before playing
    }

    /*
    ===============
    CL_AllocDlight
    ===============
    */
    public static DLight_t* AllocDlight(int key)
    {
        DLight_t* dl;

        // first look for an exact key match
        if (key != 0)
        {
            for (int i = 0; i < ClEntities.MAX_DLIGHTS; i++)
            {
                dl = &ClEntities.cl_dlights[i];
                if (dl->Key == key)
                {
                    *dl = default;
                    dl->Key = key;
                    return dl;
                }
            }
        }

        // then look for anything else
        for (int i = 0; i < ClEntities.MAX_DLIGHTS; i++)
        {
            dl = &ClEntities.cl_dlights[i];
            if (dl->Die < Cl.time)
            {
                *dl = default;
                dl->Key = key;
                return dl;
            }
        }

        dl = &ClEntities.cl_dlights[0];
        *dl = default;
        dl->Key = key;
        return dl;
    }

    /*
    ===============
    CL_DecayLights
    ===============
    */
    public static void DecayLights()
    {
        float time = (float)Math.Abs(Cl.time - Cl.oldtime);

        for (int i = 0; i < ClEntities.MAX_DLIGHTS; i++)
        {
            DLight_t* dl = &ClEntities.cl_dlights[i];
            if (dl->Die < Cl.time || dl->Radius == 0)
                continue;

            dl->Radius -= time * dl->Decay;
            if (dl->Radius < 0)
                dl->Radius = 0;
        }
    }

    /*
    ===============
    CL_LerpPoint

    Determines the fraction between the last two messages that the objects
    should be put at.
    ===============
    */
    public static float LerpPoint()
    {
        float f = (float)(Cl.mtime[0] - Cl.mtime[1]);

        if (f == 0 || Cl.cl_nolerp->Value != 0 || Cls.timedemo || Sv.active)
        {
            Cl.time = Cl.mtime[0];
            return 1;
        }

        if (f > 0.1f)
        {
            // dropped packet, or start of demo
            Cl.mtime[1] = Cl.mtime[0] - 0.1;
            f = 0.1f;
        }

        float frac = (float)((Cl.time - Cl.mtime[1]) / f);
        // SetPal(1)/SetPal(2)/SetPal(0) -- cl_main.c's own debugging tool,
        // already an #if 0'd no-op body in the original; not ported (there's
        // no observable behavior to match).
        if (frac < 0)
        {
            if (frac < -0.01f)
                Cl.time = Cl.mtime[1];
            frac = 0;
        }
        else if (frac > 1)
        {
            if (frac > 1.01f)
                Cl.time = Cl.mtime[0];
            frac = 1;
        }

        return frac;
    }

    /*
    ===============
    CL_RelinkEntities
    ===============
    */
    public static void RelinkEntities()
    {
        float frac = LerpPoint();

        ClEntities.cl_numvisedicts = 0;

        // interpolate player info
        Cl.velocity = Cl.mvelocity[1] + frac * (Cl.mvelocity[0] - Cl.mvelocity[1]);

        if (ClDemo.demoplayback)
        {
            // interpolate the angles
            for (int j = 0; j < 3; j++)
            {
                float d = Cl.mviewangles[0][j] - Cl.mviewangles[1][j];
                if (d > 180) d -= 360;
                else if (d < -180) d += 360;
                Cl.viewangles[j] = Cl.mviewangles[1][j] + frac * d;
            }
        }

        float bobjrotate = MathLib.anglemod(100 * (float)Cl.time);

        // start on the entity after the world
        for (int i = 1; i < Cl.num_entities; i++)
        {
            Entity_t* ent = &ClEntities.cl_entities[i];

            if (ent->Model == null)
            {
                // empty slot
                if (ent->ForceLink)
                    REfrag.RemoveEfrags(ent); // just became empty
                continue;
            }

            // if the object wasn't included in the last packet, remove it
            if (ent->MsgTime != Cl.mtime[0])
            {
                ent->Model = null;
                ent->LerpFlags |= LerpFlags.RESETMOVE | LerpFlags.RESETANIM; // johnfitz -- next time this entity slot is reused, the lerp will need to be reset
                continue;
            }

            Vec3_t oldorg = ent->Origin;

            if (ent->ForceLink)
            {
                // the entity was not updated in the last message, so move
                // to the final spot
                ent->Origin = ent->MsgOrigins[0];
                ent->Angles = ent->MsgAngles[0];
            }
            else
            {
                // if the delta is large, assume a teleport and don't lerp
                float f = frac;
                Unsafe.SkipInit(out Vec3_t delta);
                for (int j = 0; j < 3; j++)
                {
                    delta[j] = ent->MsgOrigins[0][j] - ent->MsgOrigins[1][j];
                    if (delta[j] > 100 || delta[j] < -100)
                    {
                        f = 1; // assume a teleportation, not a motion
                        ent->LerpFlags |= LerpFlags.RESETMOVE; // johnfitz -- don't lerp teleports
                    }
                }

                // johnfitz -- don't cl_lerp entities that will be r_lerped
                if (RMain.r_lerpmove->Value != 0 && (ent->LerpFlags & LerpFlags.MOVESTEP) != 0)
                    f = 1;

                // interpolate the origin and angles
                for (int j = 0; j < 3; j++)
                {
                    ent->Origin[j] = ent->MsgOrigins[1][j] + f * delta[j];

                    float d = ent->MsgAngles[0][j] - ent->MsgAngles[1][j];
                    if (d > 180) d -= 360;
                    else if (d < -180) d += 360;
                    ent->Angles[j] = ent->MsgAngles[1][j] + f * d;
                }
            }

            // rotate binary objects locally
            if ((ent->Model->Flags & ModelFlags.EF_ROTATE) != 0)
                ent->Angles[1] = bobjrotate;

            if ((ent->Effects & Sv.EF_BRIGHTFIELD) != 0)
                RPart.EntityParticles(ent);

            if ((ent->Effects & Sv.EF_MUZZLEFLASH) != 0)
            {
                // johnfitz -- assume muzzle flash accompanied by muzzle flare, which looks bad when lerped
                if (RMain.r_lerpmodels->Value != 2)
                {
                    if (i == Cl.viewentity)
                        Cl.viewent->LerpFlags |= LerpFlags.RESETANIM | LerpFlags.RESETANIM2; // no lerping for two frames
                    else
                        ent->LerpFlags |= LerpFlags.RESETANIM | LerpFlags.RESETANIM2; // no lerping for two frames
                }

                DLight_t* muzzleDl = AllocDlight(i);
                muzzleDl->Origin = ent->Origin;
                muzzleDl->Origin[2] += 16;
                MathLib.AngleVectors(ent->Angles, out Vec3_t fv, out Vec3_t rv, out Vec3_t uv);

                muzzleDl->Origin += 18 * fv;
                muzzleDl->Radius = 200 + (Q.Rand() & 31);
                muzzleDl->MinLight = 32;
                muzzleDl->Die = (float)Cl.time + 0.1f;
            }
            if ((ent->Effects & Sv.EF_BRIGHTLIGHT) != 0)
            {
                DLight_t* dl = AllocDlight(i);
                dl->Origin = ent->Origin;
                dl->Origin[2] += 16;
                dl->Radius = 400 + (Q.Rand() & 31);
                dl->Die = (float)Cl.time + 0.001f;
            }
            if ((ent->Effects & Sv.EF_DIMLIGHT) != 0)
            {
                DLight_t* dl = AllocDlight(i);
                dl->Origin = ent->Origin;
                dl->Radius = 200 + (Q.Rand() & 31);
                dl->Die = (float)Cl.time + 0.001f;
            }

            if ((ent->Model->Flags & ModelFlags.EF_GIB) != 0)
                RPart.RocketTrail(oldorg, ent->Origin, 2);
            else if ((ent->Model->Flags & ModelFlags.EF_ZOMGIB) != 0)
                RPart.RocketTrail(oldorg, ent->Origin, 4);
            else if ((ent->Model->Flags & ModelFlags.EF_TRACER) != 0)
                RPart.RocketTrail(oldorg, ent->Origin, 3);
            else if ((ent->Model->Flags & ModelFlags.EF_TRACER2) != 0)
                RPart.RocketTrail(oldorg, ent->Origin, 5);
            else if ((ent->Model->Flags & ModelFlags.EF_ROCKET) != 0)
            {
                RPart.RocketTrail(oldorg, ent->Origin, 0);
                DLight_t* dl = AllocDlight(i);
                dl->Origin = ent->Origin;
                dl->Radius = 200;
                dl->Die = (float)Cl.time + 0.01f;
            }
            else if ((ent->Model->Flags & ModelFlags.EF_GRENADE) != 0)
                RPart.RocketTrail(oldorg, ent->Origin, 1);
            else if ((ent->Model->Flags & ModelFlags.EF_TRACER3) != 0)
                RPart.RocketTrail(oldorg, ent->Origin, 6);

            ent->ForceLink = false;

            if (i == Cl.viewentity && Chase.chase_active->Value == 0)
                continue;

            if (ClEntities.cl_numvisedicts < ClEntities.MAX_VISEDICTS)
            {
                ClEntities.cl_visedicts[ClEntities.cl_numvisedicts] = ent;
                ClEntities.cl_numvisedicts++;
            }
        }
    }

    /*
    =================
    CL_SendMove
    =================
    */
    public static void SendMove(UserCmd_t* cmd)
    {
        Cl.cmd = *cmd;

        byte* data = stackalloc byte[128];
        SizeBuf_t buf = new() { Data = data, MaxSize = 128, CurSize = 0 };

        Msg.WriteByte(&buf, (int)Clc.Move);

        Msg.WriteFloat(&buf, (float)Cl.mtime[0]); // so server can get ping times

        for (int i = 0; i < 3; i++)
        {
            // Mark V CL_SendMove -- 16-bit angles unless PROTOCOL_NETQUAKE
            if (Cl.protocol == Protocol.PROTOCOL_NETQUAKE)
                Msg.WriteAngle(&buf, Cl.viewangles[i]);
            else
                Msg.WriteAngle16(&buf, Cl.viewangles[i]);
        }

        Msg.WriteShort(&buf, (int)cmd->ForwardMove);
        Msg.WriteShort(&buf, (int)cmd->SideMove);
        Msg.WriteShort(&buf, (int)cmd->UpMove);

        // send button bits
        int bits = 0;
        if ((ClInput.in_attack->State & 3) != 0)
            bits |= 1;
        ClInput.in_attack->State &= ~2;

        if ((ClInput.in_jump->State & 3) != 0)
            bits |= 2;
        ClInput.in_jump->State &= ~2;

        Msg.WriteByte(&buf, bits);

        Msg.WriteByte(&buf, ClInput.in_impulse);
        ClInput.in_impulse = 0;

        // deliver the message
        if (ClDemo.demoplayback)
            return;

        // always dump the first two messages, because it may contain
        // leftover inputs from the last level
        Cl.movemessages++;
        if (Cl.movemessages <= 2)
            return;

        if (Net.SendUnreliableMessage(Cls.netcon, &buf) == -1)
        {
            Con.Printf("CL_SendMove: lost server connection\n"u8);
            Disconnect();
        }
    }

    /*
    =================
    CL_SendCmd
    =================
    */
    public static void SendCmd()
    {
        if (Cls.state != CActive.Connected)
            return;

        if (Cls.signon == Cls.SIGNONS)
        {
            UserCmd_t cmd;
            ClInput.BaseMove(&cmd);

            // allow mice or other external controllers to add to the move
            ClInput.Move(&cmd);

            SendMove(&cmd);
        }

        SizeBuf_t* msg = Cls.message;
        if (ClDemo.demoplayback)
        {
            Sz.Clear(msg);
            return;
        }

        // send the reliable message
        if (msg->CurSize == 0)
            return; // no message at all

        if (!Net.CanSendMessage(Cls.netcon))
        {
            Con.DPrintf("CL_WriteToServer: can't send\n"u8);
            return;
        }

        if (Net.SendMessage(Cls.netcon, msg) == -1)
            Host.Error("CL_WriteToServer: lost server connection"u8);

        Sz.Clear(msg);
    }

    /*
    ===============
    CL_ReadFromServer

    Read all incoming data from the server
    ===============
    */
    public static int ReadFromServer()
    {
        Cl.oldtime = Cl.time;
        Cl.time += Host.host_frametime;

        int ret;
        do
        {
            // CL_GetMessage (ClDemo.cs) instead of a direct Net.GetMessage
            // call -- routes through demo playback/recording the same way
            // the original does.
            ret = ClDemo.GetMessage();
            if (ret == -1)
                Host.Error("CL_ReadFromServer: lost server connection"u8);
            if (ret == 0)
                break;

            Cls.last_received_message = Host.realtime;
            ClParse.ParseServerMessage();
        } while (ret != 0 && Cls.state == CActive.Connected);

        if (Cl.cl_shownet->Value != 0)
            Con.Printf("\n"u8);

        RelinkEntities();
        ClTEnt.UpdateTEnts();

        return 0;
    }

    /*
    =================
    CL_Init
    =================
    */
    public static void Init()
    {
        // SZ_Alloc(&cls.message, 1024) -- Cls.message is already allocated
        // at its 1024-byte size as a field initializer (Cl.cs), so nothing
        // to do here.

        // CL_InitInput -> ClInput.Init() (open_tk.md's input work; registers
        // cl_input.c's kbutton_t command bindings and their cvars).
        ClInput.Init();
        ClTEnt.InitTEnts();

        // Reset each cvar's .String to its declared default before
        // registering -- Cvar.RegisterVariable repoints .String at a fresh
        // Zone.Malloc'd copy every call, so registering these process-
        // lifetime statics directly (as this used to) would, on a second
        // call in the same process (e.g. a later unit test class), read
        // the *previous* call's already-freed Zone copy as its "old value"
        // -- a use-after-free. See Common.Init's own comment on the same
        // hazard.
        Cl.cl_name->String = "player"u8;
        Cvar.RegisterVariable(Cl.cl_name);
        Cl.cl_color->String = "0"u8;
        Cvar.RegisterVariable(Cl.cl_color);
        Cl.cl_shownet->String = "0"u8;
        Cvar.RegisterVariable(Cl.cl_shownet);
        Cl.cl_nolerp->String = "0"u8;
        Cvar.RegisterVariable(Cl.cl_nolerp);

        Cmd.AddCommand("disconnect"u8, &Disconnect_f);
        Cmd.AddCommand("record"u8, &ClDemo.Record_f);
        Cmd.AddCommand("stop"u8, &ClDemo.Stop_f);
        Cmd.AddCommand("playdemo"u8, &ClDemo.PlayDemo_f);
        Cmd.AddCommand("timedemo"u8, &ClDemo.TimeDemo_f);
        Cmd.AddCommand("entities"u8, &PrintEntities_f);
    }

    /*
    ==============
    CL_PrintEntities_f
    ==============
    */
    public static void PrintEntities_f()
    {
        for (int i = 0; i < Cl.num_entities; i++)
        {
            Entity_t* ent = ClEntities.cl_entities + i;
            Con.Printf($"{i,3}:");
            if (ent->Model == null)
            {
                Con.Printf("EMPTY\n"u8);
                continue;
            }
            Con.Printf($"{ent->Model->Name}:{ent->Frame,2}  ({ent->Origin[0],5:F1},{ent->Origin[1],5:F1},{ent->Origin[2],5:F1}) [{ent->Angles[0],5:F1} {ent->Angles[1],5:F1} {ent->Angles[2],5:F1}]\n");
        }
    }
}
