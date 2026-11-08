// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

// server_state_t (server.h)
public enum ServerState_t
{
    Loading,
    Active,
}

/// <summary>
/// server.h's server_static_t (svs) -- Net.cs already declares maxclients/
/// maxclientslimit (its earliest consumer); the rest of the struct is added
/// here. `clients` (client_t[maxclients], per-connection network state) can
/// really hold more than one connection (Host.FindMaxClients/
/// AllocateClients below) -- single-player (maxclients == 1, the default)
/// is just the common case, not a hard limit.
/// </summary>
public static unsafe partial class Svs
{
    // NUM_SPAWN_PARMS (server.h)
    public const int NUM_SPAWN_PARMS = 16;

    public static int serverflags;          // episode completion information
    public static bool changelevel_issued;  // cleared when at SV_SpawnServer

    // svs.clients -- a native pointer (like the original's Hunk-allocated
    // `client_t *clients`), not a managed array, so `host_client`-style
    // pointers into it (see `host_client` below) stay stable. Not readonly:
    // Host.FindMaxClients (host.c) now re-points this at a Hunk allocation
    // sized to the real maxclientslimit once -dedicated/-listen are parsed
    // (see AllocateClients below).
    // The static constructor's own NativeMemory allocation is a fallback
    // (sized to the default maxclientslimit=1) for callers that construct
    // Svs without going through Host.Init/FindMaxClients at all (most unit
    // tests), matching this port's "declared for its earliest consumer,
    // overridden later" convention elsewhere. Allocated in an explicit
    // static constructor rather than a field initializer: `maxclientslimit`
    // is declared in Net.cs's half of this partial class, and the C# spec
    // only guarantees a static constructor runs after every field
    // initializer across ALL partial declarations -- a field initializer
    // here could otherwise race Net.cs's `maxclientslimit = 1` depending on
    // file compile order and read the field's un-initialized default (0),
    // allocating zero bytes.
    public static Client_t* clients;

    static Svs()
    {
        clients = NativeMemory.AllocArrayZeroed<Client_t>(maxclientslimit);
    }

    // Host_FindMaxClients's `svs.clients = Hunk_AllocName(svs.maxclientslimit
    // *sizeof(client_t), "clients");` -- called once maxclientslimit is set
    // to its real (possibly >1) value. Hunk-allocated so it survives level
    // changes the same way the original's Hunk allocation does (Host.
    // ClearMemory only frees back to host_hunklevel, captured after
    // Host_Init, i.e. after this runs).
    public static void AllocateClients()
    {
        clients = (Client_t*)Hunk.AllocName(maxclientslimit * sizeof(Client_t), "clients"u8);
    }

    // host_client (sv_user.c/host.c file-scope global) -- "the client
    // currently being processed" by whichever per-client loop is running;
    // SV_DropClient/SV_SendClientMessages/SV_UpdateToReliableMessages all
    // read through this rather than taking a client_t* parameter, matching
    // the original exactly.
    public static Client_t* host_client;

    // EnsureLocalPlayerConnected -- SV_ConnectClient/SV_CheckForNewClients
    // (below) are ported and drive the real connect handshake once a client
    // actually connects over the loopback socket, but PF_checkclient/
    // PF_setspawnparms can run before that handshake completes (e.g. during
    // entity spawn) and need a real target rather than being permanently
    // inert. Since there's only ever one local player, lazily mark
    // clients[0] active and point it at edict 1 (the conventional player
    // edict slot) the first time either builtin needs a client, once a
    // player edict actually exists.
    public static void EnsureLocalPlayerConnected()
    {
        if (clients[0].Active || Sv.num_edicts <= 1)
            return;

        clients[0].Active = true;
        clients[0].Edict = Progs.EdictNum(1);
    }
}

/// <summary>
/// server.h's server_t (sv) and its supporting SV_Init/edict-management
/// pieces (pr_edict.c's ED_Alloc/ED_Free/EDICT_NUM/NUM_FOR_EDICT live in
/// Progs.cs, matching the original file split).
/// </summary>
public static unsafe partial class Sv
{

    // EF_* (server.h) -- entity effects bits.
    public const int EF_BRIGHTFIELD = 1;
    public const int EF_MUZZLEFLASH = 2;
    public const int EF_BRIGHTLIGHT = 4;
    public const int EF_DIMLIGHT = 8;

    public static bool paused;
    public static bool loadgame;

    public static double time;

    public static int lastcheck;
    public static double lastchecktime;

    // sv.name -- Cl.cs already declares this as `Sv.Name` (byte*), its
    // earliest consumer (R_ReadPointFile_f); not re-declared here.
    public static byte* modelname; // maps/<name>.bsp, for model_precache[0]

    public static Model_t* worldmodel;
    public static readonly byte** model_precache = NativeMemory.AllocPointerArray<byte>(QuakeDef.MAX_MODELS); // NULL terminated
    public static readonly Model_t** models = NativeMemory.AllocPointerArray<Model_t>(QuakeDef.MAX_MODELS);
    public static readonly byte** sound_precache = NativeMemory.AllocPointerArray<byte>(QuakeDef.MAX_SOUNDS); // NULL terminated
    public static readonly byte** lightstyles = NativeMemory.AllocPointerArray<byte>(QuakeDef.MAX_LIGHTSTYLES);

    // SV_ModelIndex -- linear scan of model_precache, used by PF_makestatic
    // (pr_cmds.c line 1582's prototype/PF_makestatic's own call site).
    public static int ModelIndex(Utf8String name_)
    {
        byte* name = name_;
        if (name == null || *name == 0)
            return 0;

        for (int i = 0; i < QuakeDef.MAX_MODELS && model_precache[i] != null; i++)
            if (Q.StrCmp(model_precache[i], name) == 0)
                return i;

        return 0;
    }

    public static int num_edicts;
    public static int max_edicts;

    // sv.edicts -- can NOT be array indexed (edict_t is logically variable
    // sized once a modified progs.dat adds extra fields; the stock id1
    // progs.dat never does -- see EDict_t's comment), but EDICT_NUM/
    // NEXT_EDICT/PROG_TO_EDICT below always go through pr_edict_size-based
    // byte arithmetic anyway, matching the original.
    public static EDict_t* edicts;

    public static ServerState_t state; // some actions are only valid during load

    // sv_player (sv_user.c) -- the edict SvUser.cs's physics functions act
    // on; set from host_client->edict by SvUser.cs's RunClients right before
    // ClientThink runs, matching the original exactly.
    public static EDict_t* sv_player;

    // datagram/reliable_datagram/signon -- network message buffers.
    // reliable_datagram/signon are written by HostCmd.cs's signon-sequence
    // commands (Host_PreSpawn_f/Host_Spawn_f) and read by
    // SendClientMessages/UpdateToReliableMessages below. datagram is written
    // by StartParticle/SvPhys.StartSound and by PrCmds.cs's PF_Write*
    // builtins (MSG_BROADCAST via WriteDest), and is flushed to every client
    // each frame by SendClientDatagram.
    public static readonly SizeBuf_t* datagram = NativeMemory.AllocZeroed<SizeBuf_t>();
    public static readonly SizeBuf_t* reliable_datagram = NativeMemory.AllocZeroed<SizeBuf_t>();
    public static readonly SizeBuf_t* signon = NativeMemory.AllocZeroed<SizeBuf_t>();

    /*
    =================
    SV_Init
    =================
    */
    public static readonly Cvar_t* sv_maxvelocity = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_gravity = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_nostep = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_friction = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_edgefriction = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_stopspeed = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_maxspeed = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_accelerate = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_idealpitchscale = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sv_aim = NativeMemory.AllocZeroed<Cvar_t>();

    // sv_protocol (Mark V sv_main.c) -- protocol for the next map load
    public static readonly Cvar_t* sv_protocol = NativeMemory.AllocZeroed<Cvar_t>();

    // host_protocol_datagram_maxsize (Mark V sv_main.c) -- max datagram size in use: set per
    // protocol by SpawnServer; the client restores MAX_MARK_V_DATAGRAM in ClearState when not hosting
    public static int host_protocol_datagram_maxsize = QuakeDef.MAX_MARK_V_DATAGRAM;

    // sv.protocol -- protocol in use for the current map (NETQUAKE or FITZQUAKE)
    public static int current_protocol = Protocol.PROTOCOL_FITZQUAKE;

    // SV_Protocol_f (Mark V sv_main.c) -- only 15 and 666 are supported here
    public static void Protocol_f()
    {
        switch (Cmd.Argc())
        {
            case 1:
                Con.Printf($"\"sv_protocol\" is \"{(int)sv_protocol->Value}\"\n");
                break;
            case 2:
                int i = Q.Atoi(Cmd.Argv(1));
                if (i != Protocol.PROTOCOL_NETQUAKE && i != Protocol.PROTOCOL_FITZQUAKE)
                {
                    Con.Printf($"sv_protocol must be {Protocol.PROTOCOL_NETQUAKE} or {Protocol.PROTOCOL_FITZQUAKE}\n");
                }
                else
                {
                    Cvar.SetValue("sv_protocol"u8, i);
                    if (active)
                        Con.Printf("changes will not take effect until the next map load.\n"u8);
                }
                break;
            default:
                Con.Printf("usage: sv_protocol <protocol>\n"u8);
                break;
        }
    }

    // localmodels[MAX_MODELS] -- sv_main.c's own global (a *N inline-brush-
    // model name cache), built once here exactly like the original's
    // `for (i=0 ; i<MAX_MODELS ; i++) sprintf(localmodels[i], "*%i", i);`.
    // Hunk-allocated once during SV_Init, i.e. before Host.host_hunklevel is
    // captured, so it survives every later Host.ClearMemory() the same way
    // the original's plain global array survives every level change.
    public static readonly byte*[] localmodels = new byte*[QuakeDef.MAX_MODELS];

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        Cvar.RegisterVariable(cvar);
    }

    public static void Init()
    {
        RegisterCvar(sv_maxvelocity, "sv_maxvelocity"u8, "2000"u8);
        RegisterCvar(sv_gravity, "sv_gravity"u8, "800"u8, true);
        RegisterCvar(sv_friction, "sv_friction"u8, "4"u8, true);
        RegisterCvar(sv_edgefriction, "edgefriction"u8, "2"u8);
        RegisterCvar(sv_stopspeed, "sv_stopspeed"u8, "100"u8);
        RegisterCvar(sv_maxspeed, "sv_maxspeed"u8, "320"u8, true);
        RegisterCvar(sv_accelerate, "sv_accelerate"u8, "10"u8);
        RegisterCvar(sv_idealpitchscale, "sv_idealpitchscale"u8, "0.8"u8);
        RegisterCvar(sv_aim, "sv_aim"u8, "0.93"u8);
        RegisterCvar(sv_nostep, "sv_nostep"u8, "0"u8);
        RegisterCvar(sv_protocol, "sv_protocol"u8, "666"u8);
        Cmd.AddCommand("sv_protocol"u8, &Protocol_f);

        // for (i=0 ; i<MAX_MODELS ; i++) sprintf(localmodels[i], "*%i", i);
        for (int i = 0; i < QuakeDef.MAX_MODELS; i++)
        {
            byte* buf = (byte*)Hunk.AllocName(8, "lclmdl"u8); // char[8]: "*" + up to 4 digits (MAX_MODELS = 2048) + NUL
            WriteStarNum(buf, i);
            localmodels[i] = buf;
        }
    }

    // sprintf(dest, "*%i", n) -- the one dynamic-string composition
    // SV_Init/SV_SpawnServer need; Q.cs has no sprintf equivalent, so this is
    // a narrow, purpose-built helper rather than a general one.
    private static void WriteStarNum(byte* dest, int n)
    {
        CStr.Write(dest, $"*{n}");
    }

    /*
    ==================
    SV_ClearDatagram
    ==================
    */
    public static void ClearDatagram()
    {
        SizeBuf_t* dg = datagram;
        Sz.Clear(dg);
    }

    /*
    ================
    SV_SendServerinfo

    Sends the first message from the server to a connected client.
    This will be sent on the initial connection and upon each server load.
    ================
    */
    public static void SendServerinfo(Client_t* client)
    {
        Msg.WriteByte(&client->Message, (int)Svc.Print);
        // sprintf(message, "%c\nVERSION %4.2f SERVER (%i CRC)", 2, VERSION, pr_crc) --
        // the original then feeds that composed string as the FORMAT string
        // to a second, argument-less sprintf a few lines below (see
        // SendServerinfo's other sprintf, over sv.edicts->v.message) purely
        // to copy it; there's no format-string reinterpretation of THIS
        // string, so composing it directly with string interpolation is a
        // faithful, safe equivalent.
        Msg.WriteString(&client->Message, $"\x02\nVERSION {VERSION:F2} SERVER ({Progs.pr_crc} CRC)");

        Msg.WriteByte(&client->Message, (int)Svc.ServerInfo);
        Msg.WriteLong(&client->Message, current_protocol);
        Msg.WriteByte(&client->Message, Svs.maxclients);

        if (Host.coop->Value == 0 && Host.deathmatch->Value != 0)
            Msg.WriteByte(&client->Message, (int)GameType.Deathmatch);
        else
            Msg.WriteByte(&client->Message, (int)GameType.Coop);

        // sprintf(message, pr_strings+sv.edicts->v.message) -- the original
        // uses the world entity's "message" key (the level title) AS a
        // printf format string with no variadic args, a latent bug if a
        // level's title ever contains a '%'. Copying it as plain text
        // reproduces every observable behavior for every real id1 map
        // (none of which have a '%' in their title) without carrying the
        // format-string risk into this port (CLAUDE.md's OWASP guidance).
        Msg.WriteString(&client->Message, Progs.pr_strings + edicts->V.Message);

        // Mark V SV_SendServerinfo -- protocol 15 only has room for 256 of each
        int maxModels = current_protocol == Protocol.PROTOCOL_NETQUAKE ? Protocol.MAX_P15_MODELS : QuakeDef.MAX_MODELS;
        int maxSounds = current_protocol == Protocol.PROTOCOL_NETQUAKE ? Protocol.MAX_P15_SOUNDS : QuakeDef.MAX_SOUNDS;

        for (int i = 1; i < maxModels && model_precache[i] != null; i++)
            Msg.WriteString(&client->Message, model_precache[i]);
        Msg.WriteByte(&client->Message, 0);

        for (int i = 1; i < maxSounds && sound_precache[i] != null; i++)
            Msg.WriteString(&client->Message, sound_precache[i]);
        Msg.WriteByte(&client->Message, 0);

        // send music
        Msg.WriteByte(&client->Message, (int)Svc.CdTrack);
        Msg.WriteByte(&client->Message, (int)edicts->V.Sounds);
        Msg.WriteByte(&client->Message, (int)edicts->V.Sounds);

        // set view
        Msg.WriteByte(&client->Message, (int)Svc.SetView);
        Msg.WriteShort(&client->Message, Progs.NumForEdict(client->Edict));

        Msg.WriteByte(&client->Message, (int)Svc.SignOnNum);
        Msg.WriteByte(&client->Message, 1);

        client->SendSignon = true;
        client->Spawned = false; // need prespawn, spawn, etc
    }

    /*
    ================
    SV_ConnectClient

    Initializes a client_t for a new net connection. This will only be
    called once for a player each game, not once for each level change.
    ================
    */
    public static void ConnectClient(int clientnum)
    {
        Client_t* client = &Svs.clients[clientnum];

        Con.DPrintf($"Client {client->NetConnection->Address} connected\n");

        int edictnum = clientnum + 1;
        EDict_t* ent = Progs.EdictNum(edictnum);

        // set up the client_t
        QSocket_t* netconnection = client->NetConnection;

        float* savedSpawnParms = stackalloc float[Svs.NUM_SPAWN_PARMS];
        if (loadgame)
            for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
                savedSpawnParms[i] = client->spawn_parms[i];

        *client = default;
        client->NetConnection = netconnection;

        Q.StrCpy(client->Name, "unconnected"u8);
        client->Active = true;
        client->Spawned = false;
        client->Edict = ent;
        client->Message.Data = client->MsgBuf;
        client->Message.MaxSize = QuakeDef.MAX_MSGLEN;
        client->Message.AllowOverflow = true; // we can catch it

        // privileged (IDGODS-only in the original, dead code even there) --
        // left false (the zeroed default), see Client_t's own comment.

        if (loadgame)
        {
            for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
                client->spawn_parms[i] = savedSpawnParms[i];
        }
        else
        {
            // call the progs to get default spawn parms for the new client
            Progs.ExecuteProgram(Progs.pr_global_struct->SetNewParms);
            float* parms = &Progs.pr_global_struct->Parm1;
            for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
                client->spawn_parms[i] = parms[i];
        }

        SendServerinfo(client);
    }

    /*
    ===================
    SV_CheckForNewClients
    ===================
    */
    public static void CheckForNewClients()
    {
        while (true)
        {
            QSocket_t* ret = Net.CheckNewConnections();
            if (ret == null)
                break;

            int i;
            for (i = 0; i < Svs.maxclients; i++)
                if (!Svs.clients[i].Active)
                    break;
            if (i == Svs.maxclients)
                Sys.Error("Host_CheckForNewClients: no free clients"u8);

            Svs.clients[i].NetConnection = ret;
            ConnectClient(i);

            Net.net_activeconnections++;
        }
    }


    /*
    ==================
    SV_StartParticle

    Make sure the event gets sent to all clients
    ==================
    */
    public static void StartParticle(Vec3_t org, Vec3_t dir, int color, int count)
    {
        if (datagram->CurSize > host_protocol_datagram_maxsize - 16)
            return;

        SizeBuf_t* dg = datagram;
        Msg.WriteByte(dg, (int)Svc.Particle);
        Msg.WriteCoord(dg, org[0]);
        Msg.WriteCoord(dg, org[1]);
        Msg.WriteCoord(dg, org[2]);
        for (int i = 0; i < 3; i++)
        {
            int v = (int)(dir[i] * 16);
            if (v > 127)
                v = 127;
            else if (v < -128)
                v = -128;
            Msg.WriteChar(dg, v);
        }
        Msg.WriteByte(dg, count);
        Msg.WriteByte(dg, color);
    }

    /*
    =============================================================================

    The PVS must include a small area around the client to allow head bobbing
    or other small motion on the client side. Otherwise, a bob might cause an
    entity that should be visible to not show up, especially when the bob
    crosses a waterline.

    =============================================================================
    */

    private static int fatbytes;
    internal static readonly byte* fatpvs = (byte*)NativeMemory.AllocZeroed(BspFile.MAX_MAP_LEAFS / 8);

    // SV_AddToFatPVS
    public static void AddToFatPVS(Vec3_t org, MNode_t* node)
    {
        while (true)
        {
            // if this is a leaf, accumulate the pvs bits
            if (node->Contents < 0)
            {
                if (node->Contents != (int)Contents.Solid)
                {
                    byte* pvs = Model.LeafPVS((MLeaf_t*)node, worldmodel);
                    DrawSimd.OrBytes(fatpvs, pvs, fatbytes);
                }
                return;
            }

            MPlane_t* plane = node->Plane;
            float d = org[0] * plane->Normal[0] + org[1] * plane->Normal[1] + org[2] * plane->Normal[2] - plane->Dist;
            if (d > 8)
                node = node->Children[0];
            else if (d < -8)
                node = node->Children[1];
            else
            { // go down both
                AddToFatPVS(org, node->Children[0]);
                node = node->Children[1];
            }
        }
    }

    /*
    =============
    SV_FatPVS

    Calculates a PVS that is the inclusive or of all leafs within 8 pixels of the
    given point.
    =============
    */
    public static byte* FatPVS(Vec3_t org)
    {
        fatbytes = (worldmodel->NumLeafs + 31) >> 3;
        Q.MemSet(fatpvs, 0, fatbytes);
        AddToFatPVS(org, worldmodel->Nodes);
        return fatpvs;
    }

    // ofs of the progs' "alpha" field: -2 = not looked up yet, -1 = progs has none
    // (Mark V pr_alpha_supported / eval_alpha, pr_edict.c)
    private static int alpha_field_ofs = -2;

    // refreshes and returns ent->alpha from the progs' .alpha field (ENTALPHA_ENCODE'd);
    // Mark V does this inline in SV_WriteEntitiesToClient
    internal static byte UpdateEntAlpha(EDict_t* ent)
    {
        if (alpha_field_ofs == -2)
        {
            if (Progs.progs == null)
                return ent->Alpha;
            Utf8String alphaName = "alpha"u8;
            DDef_t* def = EdictSerialization.FindField(alphaName);
            alpha_field_ofs = def == null ? -1 : def->Ofs;
        }

        if (alpha_field_ofs >= 0)
            ent->Alpha = Protocol.EntAlphaEncode(*(float*)((byte*)&ent->V + alpha_field_ofs * 4));
        return ent->Alpha;
    }

    /*
    =============
    SV_WriteEntitiesToClient
    =============
    */
    public static void WriteEntitiesToClient(EDict_t* clent, SizeBuf_t* msg)
    {
        // find the client's PVS
        Vec3_t org = clent->V.Origin + clent->V.ViewOfs;
        byte* pvs = FatPVS(org);

        // send over all entities (except the client) that touch the pvs
        EDict_t* ent = Progs.NextEdict(edicts);
        for (int e = 1; e < num_edicts; e++, ent = Progs.NextEdict(ent))
        {
            // ignore if not touching a PV leaf
            if (ent != clent) // clent is ALLWAYS sent
            {
                // ignore ents without visible models
                if (ent->V.ModelIndex == 0 || Progs.pr_strings[ent->V.Model] == 0)
                    continue;

                // johnfitz -- don't send model>255 entities if protocol is 15
                if (current_protocol == Protocol.PROTOCOL_NETQUAKE && ((int)ent->V.ModelIndex & 0xFF00) != 0)
                    continue;

                int i;
                for (i = 0; i < ent->NumLeafs; i++)
                    if ((pvs[ent->LeafNums[i] >> 3] & (1 << (ent->LeafNums[i] & 7))) != 0)
                        break;

                if (i == ent->NumLeafs)
                    continue; // not visible
            }

            if (msg->MaxSize - msg->CurSize < 24) // johnfitz -- max size for protocol 85 is 24 bytes
            {
                Con.Printf("packet overflow\n"u8);
                return;
            }

            // send an update
            int bits = 0;

            for (int i = 0; i < 3; i++)
            {
                float miss = ent->V.Origin[i] - ent->Baseline.Origin[i];
                if (miss < -0.1f || miss > 0.1f)
                    bits |= Protocol.U_ORIGIN1 << i;
            }

            if (ent->V.Angles[0] != ent->Baseline.Angles[0])
                bits |= Protocol.U_ANGLE1;

            if (ent->V.Angles[1] != ent->Baseline.Angles[1])
                bits |= Protocol.U_ANGLE2;

            if (ent->V.Angles[2] != ent->Baseline.Angles[2])
                bits |= Protocol.U_ANGLE3;

            if ((int)ent->V.MoveType == (int)MoveType.Step)
                bits |= Protocol.U_NOLERP; // don't mess up the step animation

            if (ent->Baseline.ColorMap != (int)ent->V.ColorMap)
                bits |= Protocol.U_COLORMAP;

            if (ent->Baseline.Skin != (int)ent->V.Skin)
                bits |= Protocol.U_SKIN;

            if (ent->Baseline.Frame != (int)ent->V.Frame)
                bits |= Protocol.U_FRAME;

            if (ent->Baseline.Effects != (int)ent->V.Effects)
                bits |= Protocol.U_EFFECTS;

            if (ent->Baseline.ModelIndex != (int)ent->V.ModelIndex)
                bits |= Protocol.U_MODEL;

            // johnfitz -- alpha
            byte entAlpha = UpdateEntAlpha(ent);

            // don't send invisible entities unless they have effects
            if (entAlpha == Protocol.ENTALPHA_ZERO && (int)ent->V.Effects == 0)
                continue;

            // johnfitz -- PROTOCOL_FITZQUAKE
            if (current_protocol != Protocol.PROTOCOL_NETQUAKE)
            {
                if (ent->Baseline.Alpha != entAlpha)
                    bits |= Protocol.U_ALPHA;
                if ((bits & Protocol.U_FRAME) != 0 && ((int)ent->V.Frame & 0xFF00) != 0)
                    bits |= Protocol.U_FRAME2;
                if ((bits & Protocol.U_MODEL) != 0 && ((int)ent->V.ModelIndex & 0xFF00) != 0)
                    bits |= Protocol.U_MODEL2;
                if (ent->SendInterval)
                    bits |= Protocol.U_LERPFINISH;
                if (bits >= 65536)
                    bits |= Protocol.U_EXTEND1;
                if (bits >= 16777216)
                    bits |= Protocol.U_EXTEND2;
            }

            if (e >= 256)
                bits |= Protocol.U_LONGENTITY;

            if (bits >= 256)
                bits |= Protocol.U_MOREBITS;

            // write the message
            Msg.WriteByte(msg, bits | Protocol.U_SIGNAL);

            if ((bits & Protocol.U_MOREBITS) != 0)
                Msg.WriteByte(msg, bits >> 8);
            if ((bits & Protocol.U_EXTEND1) != 0)
                Msg.WriteByte(msg, bits >> 16);
            if ((bits & Protocol.U_EXTEND2) != 0)
                Msg.WriteByte(msg, bits >> 24);
            if ((bits & Protocol.U_LONGENTITY) != 0)
                Msg.WriteShort(msg, e);
            else
                Msg.WriteByte(msg, e);

            if ((bits & Protocol.U_MODEL) != 0)
                Msg.WriteByte(msg, (int)ent->V.ModelIndex);
            if ((bits & Protocol.U_FRAME) != 0)
                Msg.WriteByte(msg, (int)ent->V.Frame);
            if ((bits & Protocol.U_COLORMAP) != 0)
                Msg.WriteByte(msg, (int)ent->V.ColorMap);
            if ((bits & Protocol.U_SKIN) != 0)
                Msg.WriteByte(msg, (int)ent->V.Skin);
            if ((bits & Protocol.U_EFFECTS) != 0)
                Msg.WriteByte(msg, (int)ent->V.Effects);
            if ((bits & Protocol.U_ORIGIN1) != 0)
                Msg.WriteCoord(msg, ent->V.Origin[0]);
            if ((bits & Protocol.U_ANGLE1) != 0)
                Msg.WriteAngle(msg, ent->V.Angles[0]);
            if ((bits & Protocol.U_ORIGIN2) != 0)
                Msg.WriteCoord(msg, ent->V.Origin[1]);
            if ((bits & Protocol.U_ANGLE2) != 0)
                Msg.WriteAngle(msg, ent->V.Angles[1]);
            if ((bits & Protocol.U_ORIGIN3) != 0)
                Msg.WriteCoord(msg, ent->V.Origin[2]);
            if ((bits & Protocol.U_ANGLE3) != 0)
                Msg.WriteAngle(msg, ent->V.Angles[2]);

            // johnfitz -- PROTOCOL_FITZQUAKE
            if ((bits & Protocol.U_ALPHA) != 0)
                Msg.WriteByte(msg, entAlpha);
            if ((bits & Protocol.U_FRAME2) != 0)
                Msg.WriteByte(msg, (int)ent->V.Frame >> 8);
            if ((bits & Protocol.U_MODEL2) != 0)
                Msg.WriteByte(msg, (int)ent->V.ModelIndex >> 8);
            if ((bits & Protocol.U_LERPFINISH) != 0)
                Msg.WriteByte(msg, (byte)MathF.Round((ent->V.NextThink - (float)time) * 255, MidpointRounding.ToEven));
        }
    }

    /*
    =============
    SV_CleanupEnts
    =============
    */
    public static void CleanupEnts()
    {
        EDict_t* ent = Progs.NextEdict(edicts);
        for (int e = 1; e < num_edicts; e++, ent = Progs.NextEdict(ent))
            ent->V.Effects = (int)ent->V.Effects & ~EF_MUZZLEFLASH;
    }

    /*
    ==================
    SV_WriteClientdataToMessage
    ==================
    */
    public static void WriteClientdataToMessage(EDict_t* ent, SizeBuf_t* msg)
    {
        // send a damage message
        if (ent->V.DmgTake != 0 || ent->V.DmgSave != 0)
        {
            EDict_t* other = Progs.ProgToEdict(ent->V.DmgInflictor);
            Msg.WriteByte(msg, (int)Svc.Damage);
            Msg.WriteByte(msg, (int)ent->V.DmgSave);
            Msg.WriteByte(msg, (int)ent->V.DmgTake);
            for (int i = 0; i < 3; i++)
                Msg.WriteCoord(msg, other->V.Origin[i] + 0.5f * (other->V.Mins[i] + other->V.Maxs[i]));

            ent->V.DmgTake = 0;
            ent->V.DmgSave = 0;
        }

        // send the current viewpos offset from the view entity
        SvUser.SetIdealPitch(); // how much to look up / down ideally

        // a fixangle might get lost in a dropped packet. Oh well.
        if (ent->V.FixAngle != 0)
        {
            Msg.WriteByte(msg, (int)Svc.SetAngle);
            for (int i = 0; i < 3; i++)
                Msg.WriteAngle(msg, ent->V.Angles[i]);
            ent->V.FixAngle = 0;
        }

        int bits = 0;

        if (ent->V.ViewOfs[2] != Protocol.DEFAULT_VIEWHEIGHT)
            bits |= Protocol.SU_VIEWHEIGHT;

        if (ent->V.IdealPitch != 0)
            bits |= Protocol.SU_IDEALPITCH;

        // stuff the sigil bits into the high bits of items for sbar, or else
        // mix in items2
        int items;
        float* val = EdictSerialization.GetEdictFieldValue(ent, "items2"u8);
        if (val != null)
            items = (int)ent->V.Items | ((int)*val << 23);
        else
            items = (int)ent->V.Items | ((int)Progs.pr_global_struct->ServerFlags << 28);

        bits |= Protocol.SU_ITEMS;

        if (((int)ent->V.Flags & SvPhys.FL_ONGROUND) != 0)
            bits |= Protocol.SU_ONGROUND;

        if (ent->V.WaterLevel >= 2)
            bits |= Protocol.SU_INWATER;

        for (int i = 0; i < 3; i++)
        {
            if (ent->V.PunchAngle[i] != 0)
                bits |= Protocol.SU_PUNCH1 << i;
            if (ent->V.Velocity[i] != 0)
                bits |= Protocol.SU_VELOCITY1 << i;
        }

        if (ent->V.WeaponFrame != 0)
            bits |= Protocol.SU_WEAPONFRAME;

        if (ent->V.ArmorValue != 0)
            bits |= Protocol.SU_ARMOR;

        // if (ent->v.weapon)
        bits |= Protocol.SU_WEAPON;

        // johnfitz -- PROTOCOL_FITZQUAKE
        byte entAlpha = UpdateEntAlpha(ent);
        int weaponModel = ModelIndex(Progs.pr_strings + ent->V.WeaponModel);
        if (current_protocol != Protocol.PROTOCOL_NETQUAKE)
        {
            if ((bits & Protocol.SU_WEAPON) != 0 && (weaponModel & 0xFF00) != 0)
                bits |= Protocol.SU_WEAPON2;
            if (((int)ent->V.ArmorValue & 0xFF00) != 0)
                bits |= Protocol.SU_ARMOR2;
            if (((int)ent->V.CurrentAmmo & 0xFF00) != 0)
                bits |= Protocol.SU_AMMO2;
            if (((int)ent->V.AmmoShells & 0xFF00) != 0)
                bits |= Protocol.SU_SHELLS2;
            if (((int)ent->V.AmmoNails & 0xFF00) != 0)
                bits |= Protocol.SU_NAILS2;
            if (((int)ent->V.AmmoRockets & 0xFF00) != 0)
                bits |= Protocol.SU_ROCKETS2;
            if (((int)ent->V.AmmoCells & 0xFF00) != 0)
                bits |= Protocol.SU_CELLS2;
            if ((bits & Protocol.SU_WEAPONFRAME) != 0 && ((int)ent->V.WeaponFrame & 0xFF00) != 0)
                bits |= Protocol.SU_WEAPONFRAME2;
            if ((bits & Protocol.SU_WEAPON) != 0 && entAlpha != Protocol.ENTALPHA_DEFAULT)
                bits |= Protocol.SU_WEAPONALPHA; // for now, weaponalpha = client entity alpha
            if (bits >= 65536)
                bits |= Protocol.SU_EXTEND1;
            if (bits >= 16777216)
                bits |= Protocol.SU_EXTEND2;
        }

        // send the data

        Msg.WriteByte(msg, (int)Svc.ClientData);
        Msg.WriteShort(msg, bits);

        if ((bits & Protocol.SU_EXTEND1) != 0)
            Msg.WriteByte(msg, bits >> 16);
        if ((bits & Protocol.SU_EXTEND2) != 0)
            Msg.WriteByte(msg, bits >> 24);

        if ((bits & Protocol.SU_VIEWHEIGHT) != 0)
            Msg.WriteChar(msg, (int)ent->V.ViewOfs[2]);

        if ((bits & Protocol.SU_IDEALPITCH) != 0)
            Msg.WriteChar(msg, (int)ent->V.IdealPitch);

        for (int i = 0; i < 3; i++)
        {
            if ((bits & (Protocol.SU_PUNCH1 << i)) != 0)
                Msg.WriteChar(msg, (int)ent->V.PunchAngle[i]);
            if ((bits & (Protocol.SU_VELOCITY1 << i)) != 0)
                Msg.WriteChar(msg, (int)(ent->V.Velocity[i] / 16));
        }

        // [always sent] if (bits & SU_ITEMS)
        Msg.WriteLong(msg, items);

        if ((bits & Protocol.SU_WEAPONFRAME) != 0)
            Msg.WriteByte(msg, (int)ent->V.WeaponFrame);
        if ((bits & Protocol.SU_ARMOR) != 0)
            Msg.WriteByte(msg, (int)ent->V.ArmorValue);
        if ((bits & Protocol.SU_WEAPON) != 0)
            Msg.WriteByte(msg, weaponModel);

        Msg.WriteShort(msg, (int)ent->V.Health);
        Msg.WriteByte(msg, (int)ent->V.CurrentAmmo);
        Msg.WriteByte(msg, (int)ent->V.AmmoShells);
        Msg.WriteByte(msg, (int)ent->V.AmmoNails);
        Msg.WriteByte(msg, (int)ent->V.AmmoRockets);
        Msg.WriteByte(msg, (int)ent->V.AmmoCells);

        // Com.standard_quake is set false by "-rogue"/"-hipnotic" (Common.cs's
        // InitArgv), so this branch is live, reachable code, not dead code --
        // only the rogue/hipnotic mission-pack *content* itself is out of
        // scope.
        if (Com.standard_quake)
        {
            Msg.WriteByte(msg, (int)ent->V.Weapon);
        }
        else
        {
            for (int i = 0; i < 32; i++)
            {
                if (((int)ent->V.Weapon & (1 << i)) != 0)
                {
                    Msg.WriteByte(msg, i);
                    break;
                }
            }
        }

        // johnfitz -- PROTOCOL_FITZQUAKE
        if ((bits & Protocol.SU_WEAPON2) != 0)
            Msg.WriteByte(msg, weaponModel >> 8);
        if ((bits & Protocol.SU_ARMOR2) != 0)
            Msg.WriteByte(msg, (int)ent->V.ArmorValue >> 8);
        if ((bits & Protocol.SU_AMMO2) != 0)
            Msg.WriteByte(msg, (int)ent->V.CurrentAmmo >> 8);
        if ((bits & Protocol.SU_SHELLS2) != 0)
            Msg.WriteByte(msg, (int)ent->V.AmmoShells >> 8);
        if ((bits & Protocol.SU_NAILS2) != 0)
            Msg.WriteByte(msg, (int)ent->V.AmmoNails >> 8);
        if ((bits & Protocol.SU_ROCKETS2) != 0)
            Msg.WriteByte(msg, (int)ent->V.AmmoRockets >> 8);
        if ((bits & Protocol.SU_CELLS2) != 0)
            Msg.WriteByte(msg, (int)ent->V.AmmoCells >> 8);
        if ((bits & Protocol.SU_WEAPONFRAME2) != 0)
            Msg.WriteByte(msg, (int)ent->V.WeaponFrame >> 8);
        if ((bits & Protocol.SU_WEAPONALPHA) != 0)
            Msg.WriteByte(msg, entAlpha); // for now, weaponalpha = client entity alpha
    }

    /*
    =======================
    SV_SendClientDatagram
    =======================
    */
    public static bool SendClientDatagram(Client_t* client)
    {
        byte* buf = stackalloc byte[QuakeDef.MAX_MARK_V_DATAGRAM];
        SizeBuf_t msg = default;
        msg.Data = buf;
        msg.MaxSize = host_protocol_datagram_maxsize;
        msg.CurSize = 0;

        Msg.WriteByte(&msg, (int)Svc.Time);
        Msg.WriteFloat(&msg, (float)time);

        // add the client specific data to the datagram
        WriteClientdataToMessage(client->Edict, &msg);

        WriteEntitiesToClient(client->Edict, &msg);

        // copy the server datagram if there is space
        SizeBuf_t* dg = datagram;
        if (msg.CurSize + dg->CurSize < msg.MaxSize)
            Sz.Write(&msg, dg->Data, dg->CurSize);

        // send the datagram
        if (Net.SendUnreliableMessage(client->NetConnection, &msg) == -1)
        {
            Host.DropClient(true); // if the message couldn't send, kick off
            return false;
        }

        return true;
    }

    /*
    =======================
    SV_UpdateToReliableMessages
    =======================
    */
    public static void UpdateToReliableMessages()
    {
        // check for changes to be sent over the reliable streams
        for (int i = 0; i < Svs.maxclients; i++)
        {
            Svs.host_client = &Svs.clients[i];
            if (Svs.host_client->OldFrags != (int)Svs.host_client->Edict->V.Frags)
            {
                for (int j = 0; j < Svs.maxclients; j++)
                {
                    Client_t* client = &Svs.clients[j];
                    if (!client->Active)
                        continue;
                    Msg.WriteByte(&client->Message, (int)Svc.UpdateFrags);
                    Msg.WriteByte(&client->Message, i);
                    Msg.WriteShort(&client->Message, (int)Svs.host_client->Edict->V.Frags);
                }

                Svs.host_client->OldFrags = (int)Svs.host_client->Edict->V.Frags;
            }
        }

        for (int j = 0; j < Svs.maxclients; j++)
        {
            Client_t* client = &Svs.clients[j];
            if (!client->Active)
                continue;
            Sz.Write(&client->Message, reliable_datagram->Data, reliable_datagram->CurSize);
        }

        Sz.Clear(reliable_datagram);
    }

    /*
    =======================
    SV_SendNop

    Send a nop message without trashing or sending the accumulated client
    message buffer
    =======================
    */
    public static void SendNop(Client_t* client)
    {
        byte* buf = stackalloc byte[4];
        SizeBuf_t msg = default;
        msg.Data = buf;
        msg.MaxSize = 4;
        msg.CurSize = 0;

        Msg.WriteChar(&msg, (int)Svc.Nop);

        if (Net.SendUnreliableMessage(client->NetConnection, &msg) == -1)
            Host.DropClient(true); // if the message couldn't send, kick off
        client->LastMessage = Host.realtime;
    }

    /*
    =======================
    SV_SendClientMessages
    =======================
    */
    public static void SendClientMessages()
    {
        // update frags, names, etc
        UpdateToReliableMessages();

        // build individual updates
        for (int i = 0; i < Svs.maxclients; i++)
        {
            Svs.host_client = &Svs.clients[i];

            if (!Svs.host_client->Active)
                continue;

            if (Svs.host_client->Spawned)
            {
                if (!SendClientDatagram(Svs.host_client))
                    continue;
            }
            else
            {
                // the player isn't totally in the game yet
                // send small keepalive messages if too much time has passed
                // send a full message when the next signon stage has been requested
                // some other message data (name changes, etc) may accumulate
                // between signon stages
                if (!Svs.host_client->SendSignon)
                {
                    if (Host.realtime - Svs.host_client->LastMessage > 5)
                        SendNop(Svs.host_client);
                    continue; // don't send out non-signon messages
                }
            }

            // check for an overflowed message. Should only happen
            // on a very messed up connection that backs up a lot, then
            // changes level
            if (Svs.host_client->Message.Overflowed)
            {
                Host.DropClient(true);
                Svs.host_client->Message.Overflowed = false;
                continue;
            }

            if (Svs.host_client->Message.CurSize != 0 || Svs.host_client->DropAsap)
            {
                if (!Net.CanSendMessage(Svs.host_client->NetConnection))
                    continue;

                if (Svs.host_client->DropAsap)
                {
                    Host.DropClient(false); // went to another level
                }
                else
                {
                    if (Net.SendMessage(Svs.host_client->NetConnection, &Svs.host_client->Message) == -1)
                        Host.DropClient(true); // if the message couldn't send, kick off
                    Sz.Clear(&Svs.host_client->Message);
                    Svs.host_client->LastMessage = Host.realtime;
                    Svs.host_client->SendSignon = false;
                }
            }
        }

        // clear muzzle flashes
        CleanupEnts();
    }

    /*
    ================
    SV_CreateBaseline
    ================
    */
    public static void CreateBaseline()
    {
        for (int entnum = 0; entnum < num_edicts; entnum++)
        {
            // get the current server version
            EDict_t* svent = Progs.EdictNum(entnum);
            if (svent->Free != 0)
                continue;
            if (entnum > Svs.maxclients && svent->V.ModelIndex == 0)
                continue;

            // create entity baseline
            svent->Baseline.Origin = svent->V.Origin;
            svent->Baseline.Angles = svent->V.Angles;
            svent->Baseline.Frame = (int)svent->V.Frame;
            svent->Baseline.Skin = (int)svent->V.Skin;
            if (entnum > 0 && entnum <= Svs.maxclients)
            {
                svent->Baseline.ColorMap = entnum;
                svent->Baseline.ModelIndex = ModelIndex("progs/player.mdl"u8);
                svent->Baseline.Alpha = Protocol.ENTALPHA_DEFAULT; // johnfitz -- alpha support
            }
            else
            {
                svent->Baseline.ColorMap = 0;
                svent->Baseline.ModelIndex = ModelIndex(Progs.pr_strings + svent->V.Model);
                svent->Baseline.Alpha = UpdateEntAlpha(svent); // johnfitz -- alpha support
            }

            // johnfitz -- PROTOCOL_FITZQUAKE
            int bits = 0;

            if (current_protocol == Protocol.PROTOCOL_NETQUAKE) // still want to send baseline in PROTOCOL_NETQUAKE, so reset these values
            {
                if ((svent->Baseline.ModelIndex & 0xFF00) != 0)
                    svent->Baseline.ModelIndex = 0;
                if ((svent->Baseline.Frame & 0xFF00) != 0)
                    svent->Baseline.Frame = 0;
                svent->Baseline.Alpha = Protocol.ENTALPHA_DEFAULT;
            }
            else // decide which extra data needs to be sent
            {
                if ((svent->Baseline.ModelIndex & 0xFF00) != 0)
                    bits |= Protocol.B_LARGEMODEL;
                if ((svent->Baseline.Frame & 0xFF00) != 0)
                    bits |= Protocol.B_LARGEFRAME;
                if (svent->Baseline.Alpha != Protocol.ENTALPHA_DEFAULT)
                    bits |= Protocol.B_ALPHA;
            }

            // add to the message
            SizeBuf_t* sig = signon;
            Msg.WriteByte(sig, bits != 0 ? (int)Svc.SpawnBaseline2 : (int)Svc.SpawnBaseline);
            Msg.WriteShort(sig, entnum);

            if (bits != 0)
                Msg.WriteByte(sig, bits);

            if ((bits & Protocol.B_LARGEMODEL) != 0)
                Msg.WriteShort(sig, svent->Baseline.ModelIndex);
            else
                Msg.WriteByte(sig, svent->Baseline.ModelIndex);

            if ((bits & Protocol.B_LARGEFRAME) != 0)
                Msg.WriteShort(sig, svent->Baseline.Frame);
            else
                Msg.WriteByte(sig, svent->Baseline.Frame);

            Msg.WriteByte(sig, svent->Baseline.ColorMap);
            Msg.WriteByte(sig, svent->Baseline.Skin);
            for (int i = 0; i < 3; i++)
            {
                Msg.WriteCoord(sig, svent->Baseline.Origin[i]);
                Msg.WriteAngle(sig, svent->Baseline.Angles[i]);
            }

            if ((bits & Protocol.B_ALPHA) != 0)
                Msg.WriteByte(sig, svent->Baseline.Alpha);
        }
    }

    // resets every field this file's SpawnServer touches, standing in for
    // the original's `memset (&sv, 0, sizeof(sv));` (there's no single `sv`
    // struct in this port to memset -- see this file's own type-level
    // comment). Internal (not private): Host.ShutdownServer's own
    // `memset (&sv, 0, sizeof(sv));` reuses this same method.
    internal static void ResetState()
    {
        paused = false;
        loadgame = false;
        current_protocol = Protocol.PROTOCOL_FITZQUAKE;
        alpha_field_ofs = -2;
        time = 0;
        lastcheck = 0;
        lastchecktime = 0;
        Name = null;
        modelname = null;
        worldmodel = null;
        NativeMemory.ClearPointerArray<byte>(model_precache, QuakeDef.MAX_MODELS);
        NativeMemory.ClearPointerArray<Model_t>(models, QuakeDef.MAX_MODELS);
        NativeMemory.ClearPointerArray<byte>(sound_precache, QuakeDef.MAX_SOUNDS);
        NativeMemory.ClearPointerArray<byte>(lightstyles, QuakeDef.MAX_LIGHTSTYLES);
        num_edicts = 0;
        max_edicts = 0;
        edicts = null;
        sv_player = null;
        state = ServerState_t.Loading;
        active = false;
        *datagram = default;
        *reliable_datagram = default;
        *signon = default;
    }

    /*
    ================
    SV_SendReconnect

    Tell all the clients that the server is changing levels
    ================
    */
    public static void SendReconnect()
    {
        byte* dataBuf = stackalloc byte[128];
        SizeBuf_t msg = default;
        msg.Data = dataBuf;
        msg.CurSize = 0;
        msg.MaxSize = 128;

        Msg.WriteChar(&msg, (int)Svc.StuffText);
        Msg.WriteString(&msg, "reconnect\n"u8);
        Net.SendToAll(&msg, 5);

        if (Cls.state != CActive.Dedicated)
            Cmd.ExecuteString("reconnect\n"u8, CmdSource_t.SrcCommand);
    }

    /*
    ================
    SV_SaveSpawnparms

    Grabs the current state of each client for saving across the
    transition to another level
    ================
    */
    public static void SaveSpawnparms()
    {
        Svs.serverflags = (int)Progs.pr_global_struct->ServerFlags;

        for (int i = 0; i < Svs.maxclients; i++)
        {
            Svs.host_client = &Svs.clients[i];
            if (!Svs.host_client->Active)
                continue;

            // call the progs to get default spawn parms for the new client
            Progs.pr_global_struct->Self = Progs.EdictToProg(Svs.host_client->Edict);
            Progs.ExecuteProgram(Progs.pr_global_struct->SetChangeParms);
            float* parms = &Progs.pr_global_struct->Parm1;
            for (int j = 0; j < Svs.NUM_SPAWN_PARMS; j++)
                Svs.host_client->spawn_parms[j] = parms[j];
        }
    }

    /*
    ================
    SV_SpawnServer

    This is called at the start of each level
    ================
    */
    public static void SpawnServer(byte* server)
    {
        // let's not have any servers with no name
        if (Net.hostname->String.AsSpan()[0] == 0)
            Cvar.Set("hostname"u8, "UNNAMED"u8);
        Screen.scr_centertime_off = 0;

        Con.DPrintf($"SpawnServer: {server}\n");
        Svs.changelevel_issued = false; // now safe to issue another

        // tell all connected clients that we are going to a new level
        if (active)
            SendReconnect();

        // make cvars consistant
        if (Host.coop->Value != 0)
            Cvar.SetValue("deathmatch"u8, 0);
        SaveGame.current_skill = (int)(Host.skill->Value + 0.5f);
        if (SaveGame.current_skill < 0)
            SaveGame.current_skill = 0;
        if (SaveGame.current_skill > 3)
            SaveGame.current_skill = 3;
        Cvar.SetValue("skill"u8, SaveGame.current_skill);

        // set up the new server
        Host.ClearMemory();

        ResetState();

        // sv.protocol = sv_protocol; (Mark V SV_SpawnServer) -- anything but 15 is 666
        current_protocol = (int)sv_protocol->Value == Protocol.PROTOCOL_NETQUAKE
            ? Protocol.PROTOCOL_NETQUAKE
            : Protocol.PROTOCOL_FITZQUAKE;

        byte* nameBuf = (byte*)Hunk.AllocName(MAX_QPATH, "sv.name"u8);
        Q.StrCpy(nameBuf, server);
        Name = nameBuf;

        // load progs to get entity field count
        Progs.LoadProgs();

        // allocate server memory
        max_edicts = QuakeDef.MAX_EDICTS;
        edicts = (EDict_t*)Hunk.AllocName(max_edicts * Progs.pr_edict_size, "edicts"u8);

        // Mark V SV_SpawnServer -- datagram and signon sizes depend on the protocol
        int signonSize;
        if (current_protocol == Protocol.PROTOCOL_NETQUAKE)
        {
            host_protocol_datagram_maxsize = QuakeDef.MAX_DATAGRAM;      // MAX_WINQUAKE_DATAGRAM 1024
            signonSize = QuakeDef.MAX_WINQUAKE_SIGNON;                   // 7998
        }
        else
        {
            // single player (maxclients == 1, not dedicated) can use the biggest datagram; networked games stay within the MTU
            host_protocol_datagram_maxsize = Svs.maxclients == 1 && Cls.state != CActive.Dedicated
                ? QuakeDef.MAX_MARK_V_DATAGRAM                           // 65527
                : QuakeDef.MAX_FITZQUAKE_DATAGRAM_MTU;                   // 1400
            signonSize = QuakeDef.MAX_MARK_V_SIGNON;                     // 65534
        }

        SizeBuf_t* dg = datagram;
        Sz.Alloc(dg, host_protocol_datagram_maxsize);
        SizeBuf_t* rdg = reliable_datagram;
        Sz.Alloc(rdg, host_protocol_datagram_maxsize);
        SizeBuf_t* sig = signon;
        Sz.Alloc(sig, signonSize);

        // leave slots at start for clients only
        num_edicts = Svs.maxclients + 1;
        for (int i = 0; i < Svs.maxclients; i++)
        {
            EDict_t* ent = Progs.EdictNum(i + 1);
            Svs.clients[i].Edict = ent;
        }

        state = ServerState_t.Loading;
        paused = false;

        time = 1.0;

        byte* modelnameBuf = (byte*)Hunk.AllocName(MAX_QPATH, "sv.mdl"u8);
        Q.StrCpy(modelnameBuf, "maps/"u8);
        Q.StrCat(modelnameBuf, server);
        Q.StrCat(modelnameBuf, ".bsp"u8);
        modelname = modelnameBuf;

        worldmodel = Model.ForName(modelname, false);
        if (worldmodel == null)
        {
            Con.Printf($"Couldn't spawn server {modelname}\n");
            active = false;
            return;
        }
        models[1] = worldmodel;

        // clear world interaction links
        World.ClearWorld();

        sound_precache[0] = Progs.pr_strings;

        model_precache[0] = Progs.pr_strings;
        model_precache[1] = modelname;
        for (int i = 1; i < worldmodel->NumSubmodels; i++)
        {
            model_precache[1 + i] = localmodels[i];
            models[i + 1] = Model.ForName(localmodels[i], false);
        }

        // load the rest of the entities
        EDict_t* ent0 = Progs.EdictNum(0);
        Q.MemSet(&ent0->V, 0, Progs.progs->EntityFields * 4);
        ent0->Free = 0;
        // ent->v.model = sv.worldmodel->name - pr_strings in the original.
        // Model.mod_known is a NativeMemory block outside the Hunk (the
        // original's is a static array, only reachable from pr_strings on
        // 32-bit), so copy the name into the Hunk with ED_NewString instead.
        ent0->V.Model = EdictSerialization.NewString(worldmodel->Name);
        ent0->V.ModelIndex = 1; // world model
        ent0->V.Solid = (int)SolidType.Bsp;
        ent0->V.MoveType = (int)MoveType.Push;

        if (Host.coop->Value != 0)
            Progs.pr_global_struct->Coop = Host.coop->Value;
        else
            Progs.pr_global_struct->DeathMatch = Host.deathmatch->Value;

        // pr_global_struct->mapname = sv.name - pr_strings (both in the Hunk)
        Progs.pr_global_struct->MapName = Progs.PR_SetString(Name);

        // serverflags are for cross level information (sigils)
        Progs.pr_global_struct->ServerFlags = Svs.serverflags;

        EdictSerialization.LoadFromFile(worldmodel->Entities);

        active = true;

        // all setup is completed, any further precache statements are errors
        state = ServerState_t.Active;

        // run two frames to allow everything to settle
        Host.host_frametime = 0.1f;
        SvPhys.Physics();
        SvPhys.Physics();

        // create a baseline for more efficient communications
        CreateBaseline();

        // send serverinfo to all connected clients
        for (int i = 0; i < Svs.maxclients; i++)
            if (Svs.clients[i].Active)
                SendServerinfo(&Svs.clients[i]);

        Con.DPrintf("Server spawned.\n"u8);
    }
}
