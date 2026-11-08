// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// cl_parse.c -- parses a message received from the server.
/// </summary>
public static unsafe class ClParse
{
    // CL_ParseServerInfo's model/sound name tables (char[MAX_MODELS][MAX_QPATH]
    // locals in the original), static since MAX_MODELS/MAX_SOUNDS are now 2048.
    internal static readonly byte* precacheModelNames = NativeMemory.AllocArrayZeroed<byte>(QuakeDef.MAX_MODELS * QuakeDef.MAX_QPATH);
    internal static readonly byte* precacheSoundNames = NativeMemory.AllocArrayZeroed<byte>(QuakeDef.MAX_SOUNDS * QuakeDef.MAX_QPATH);

    // svc_strings -- only consumed by the debug-only SHOWNET(2) print below;
    // cl_shownet defaults to 0 (see Cl.cl_shownet), matching the original's
    // own default-quiet behavior.
    private static readonly Utf8String[] svc_strings =
    [
        "svc_bad"u8, "svc_nop"u8, "svc_disconnect"u8, "svc_updatestat"u8, "svc_version"u8,
        "svc_setview"u8, "svc_sound"u8, "svc_time"u8, "svc_print"u8, "svc_stufftext"u8,
        "svc_setangle"u8, "svc_serverinfo"u8, "svc_lightstyle"u8, "svc_updatename"u8,
        "svc_updatefrags"u8, "svc_clientdata"u8, "svc_stopsound"u8, "svc_updatecolors"u8,
        "svc_particle"u8, "svc_damage"u8, "svc_spawnstatic"u8, "OBSOLETE svc_spawnbinary"u8,
        "svc_spawnbaseline"u8, "svc_temp_entity"u8, "svc_setpause"u8, "svc_signonnum"u8,
        "svc_centerprint"u8, "svc_killedmonster"u8, "svc_foundsecret"u8,
        "svc_spawnstaticsound"u8, "svc_intermission"u8, "svc_finale"u8, "svc_cdtrack"u8,
        "svc_sellscreen"u8, "svc_cutscene"u8, "svc_showlmp"u8, "svc_hidelmp"u8, "svc_skybox"u8,
        "svc_botchat"u8, "svc_unused39"u8, "svc_bf"u8, "svc_fog"u8, "svc_spawnbaseline2"u8,
        "svc_spawnstatic2"u8, "svc_spawnstaticsound2"u8,
    ];

    private static void ShowNet(Utf8String x)
    {
        if (Cl.cl_shownet->Value == 2)
            Con.Printf($"{Msg.msg_readcount - 1}:{x}\n");
    }


    /*
    ===============
    CL_EntityNum

    This error checks and tracks the total number of entities
    ===============
    */
    public static Entity_t* EntityNum(int num)
    {
        if (num >= Cl.num_entities)
        {
            if (num >= QuakeDef.MAX_EDICTS)
                Host.Error($"CL_EntityNum: {num} is an invalid number");
            while (Cl.num_entities <= num)
            {
                ClEntities.cl_entities[Cl.num_entities].ColorMap = Vid.vid.Colormap;
                ClEntities.cl_entities[Cl.num_entities].LerpFlags |= LerpFlags.RESETMOVE | LerpFlags.RESETANIM; // johnfitz
                Cl.num_entities++;
            }
        }

        return &ClEntities.cl_entities[num];
    }

    /*
    ==================
    CL_ParseStartSoundPacket
    ==================
    */
    public static void ParseStartSoundPacket()
    {
        int field_mask = Msg.ReadByte();

        int volume = (field_mask & Protocol.SND_VOLUME) != 0 ? Msg.ReadByte() : 255; // DEFAULT_SOUND_PACKET_VOLUME
        float attenuation = (field_mask & Protocol.SND_ATTENUATION) != 0 ? Msg.ReadByte() / 64.0f : 1.0f; // DEFAULT_SOUND_PACKET_ATTENUATION

        // johnfitz -- PROTOCOL_FITZQUAKE
        int ent, channel;
        if ((field_mask & Protocol.SND_LARGEENTITY) != 0)
        {
            ent = Msg.ReadShort();
            channel = Msg.ReadByte();
        }
        else
        {
            channel = Msg.ReadShort();
            ent = channel >> 3;
            channel &= 7;
        }

        int sound_num = (field_mask & Protocol.SND_LARGESOUND) != 0 ? Msg.ReadShort() : Msg.ReadByte();

        if ((uint)sound_num >= QuakeDef.MAX_SOUNDS)
            Host.Error($"CL_ParseStartSoundPacket: {sound_num} > MAX_SOUNDS");

        if (ent > QuakeDef.MAX_EDICTS)
            Host.Error($"CL_ParseStartSoundPacket: ent = {ent}");

        Vec3_t pos = default;
        for (int i = 0; i < 3; i++)
            pos[i] = Msg.ReadCoord();

        SndDma.StartSound(ent, channel, Cl.sound_precache[sound_num], pos, volume / 255.0f, attenuation);
    }

    /*
    ==================
    CL_KeepaliveMessage

    When the client is taking a long time to load stuff, send keepalive
    messages so the server doesn't disconnect. In this port sv.active is
    always true by the time a local client parses a serverinfo message
    (SV_SpawnServer runs before any client can connect over the loopback
    socket), so this always takes the early-out branch -- the live
    CL_GetMessage/NET_SendMessage path below it is faithfully ported but
    never actually exercised in singleplayer.
    ==================
    */
    public static void KeepaliveMessage()
    {
        if (Sv.active)
            return; // no need if server is local
        if (ClDemo.demoplayback)
            return;

        // The original re-reads from the network here, preserving
        // net_message/msg_readcount around the read so precache-loop
        // parsing can resume afterward. ClDemo.GetMessage throws outside
        // demo playback (see its own doc comment) -- unreachable given the
        // early-out above, but this mirrors the original's exact I/O
        // ordering (which is why the early-out is checked first, not this).
    }

    /*
    ==================
    CL_ParseServerInfo
    ==================
    */
    public static void ParseServerInfo()
    {
        Con.DPrintf("Serverinfo packet received.\n"u8);

        ClMain.ClearState();

        // parse protocol version number
        int i = Msg.ReadLong();
        if (i != Protocol.PROTOCOL_NETQUAKE && i != Protocol.PROTOCOL_FITZQUAKE)
        {
            Con.Printf($"Server returned version {i}, not {Protocol.PROTOCOL_NETQUAKE} or {Protocol.PROTOCOL_FITZQUAKE}");
            return;
        }
        Cl.protocol = i; // cl.protocol = i (CL_ClearState in Mark V)

        // parse maxclients
        Cl.maxclients = Msg.ReadByte();
        if (Cl.maxclients < 1 || Cl.maxclients > QuakeDef.MAX_SCOREBOARD)
        {
            Con.Printf($"Bad maxclients ({Cl.maxclients}) from server\n");
            return;
        }
        // cl.scores = Hunk_AllocName(...) -- Cl.scores is already a fixed
        // MAX_SCOREBOARD-sized array (see Cl.cs), so no allocation is needed.

        // parse gametype
        Cl.gametype = Msg.ReadByte();

        // parse signon message
        byte* str = Msg.ReadString();
        Q.StrNCpy(Cl.levelname, str, Cl.LEVELNAME_LENGTH - 1);

        // separate the printfs so the server message can have a color
        Con.Printf("\n\n\x1d\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1e\x1f\n\n"u8);
        Con.Printf($"{(char)2}{str}\n");

        // first we go through and touch all of the precache data that still
        // happens to be in the cache, so precaching something else doesn't
        // needlessly purge it
        // (static NativeMemory buffers: at Mark V's 2048 models/sounds the original's
        // local char[MAX_MODELS][MAX_QPATH] arrays are 128 KB each -- too big for the stack)
        byte* modelNames = precacheModelNames;
        byte* soundNames = precacheSoundNames;

        // precache models
        NativeMemory.ClearPointerArray<Model_t>(Cl.model_precache, QuakeDef.MAX_MODELS);
        int nummodels;
        for (nummodels = 1; ; nummodels++)
        {
            str = Msg.ReadString();
            if (str[0] == 0)
                break;
            if (nummodels == QuakeDef.MAX_MODELS)
            {
                Con.Printf("Server sent too many model precaches\n"u8);
                return;
            }
            Q.StrCpy(modelNames + nummodels * QuakeDef.MAX_QPATH, str);
            Model.TouchModel(str);
        }

        // precache sounds
        NativeMemory.ClearPointerArray<Sfx_t>(Cl.sound_precache, QuakeDef.MAX_SOUNDS);
        int numsounds;
        for (numsounds = 1; ; numsounds++)
        {
            str = Msg.ReadString();
            if (str[0] == 0)
                break;
            if (numsounds == QuakeDef.MAX_SOUNDS)
            {
                Con.Printf("Server sent too many sound precaches\n"u8);
                return;
            }
            Q.StrCpy(soundNames + numsounds * QuakeDef.MAX_QPATH, str);
            SndDma.TouchSound(str);
        }

        // now we try to load everything else until a cache allocation fails
        for (i = 1; i < nummodels; i++)
        {
            Cl.model_precache[i] = Model.ForName(modelNames + i * QuakeDef.MAX_QPATH, false);
            if (Cl.model_precache[i] == null)
            {
                Con.Printf($"Model {modelNames + i * QuakeDef.MAX_QPATH} not found\n");
                return;
            }
            KeepaliveMessage();
        }

        SndDma.BeginPrecaching();
        for (i = 1; i < numsounds; i++)
        {
            Cl.sound_precache[i] = SndDma.PrecacheSound(soundNames + i * QuakeDef.MAX_QPATH);
            KeepaliveMessage();
        }
        SndDma.EndPrecaching();

        // local state
        ClEntities.cl_entities[0].Model = Cl.worldmodel = Cl.model_precache[1];

        RMain.NewMap();

        Hunk.Check(); // make sure nothing is hurt

        Cl.noclip_anglehack = false; // noclip is turned off at start
    }

    /*
    ==================
    CL_ParseUpdate

    Parse an entity update message from the server
    If an entities model or origin changes from frame to frame, it must be
    relinked.  Other attributes can change without relinking.
    ==================
    */
    public static void ParseUpdate(int bits)
    {
        if (Cls.signon == Cls.SIGNONS - 1)
        {
            // first update is the final signon stage
            Cls.signon = Cls.SIGNONS;
            SignonReply();
        }

        if ((bits & Protocol.U_MOREBITS) != 0)
        {
            int i = Msg.ReadByte();
            bits |= i << 8;
        }

        // johnfitz -- PROTOCOL_FITZQUAKE
        if (Cl.protocol == Protocol.PROTOCOL_FITZQUAKE)
        {
            if ((bits & Protocol.U_EXTEND1) != 0)
                bits |= Msg.ReadByte() << 16;
            if ((bits & Protocol.U_EXTEND2) != 0)
                bits |= Msg.ReadByte() << 24;
        }

        int num = (bits & Protocol.U_LONGENTITY) != 0 ? Msg.ReadShort() : Msg.ReadByte();

        Entity_t* ent = EntityNum(num);

        bool forcelink = ent->MsgTime != Cl.mtime[1];

        // johnfitz -- lerping
        if (ent->MsgTime + 0.2 < Cl.mtime[0]) // more than 0.2 seconds since the last message (most entities think every 0.1 sec)
            ent->LerpFlags |= LerpFlags.RESETANIM; // if we missed a think, we'd be lerping from the wrong frame

        ent->MsgTime = Cl.mtime[0];

        int modnum;
        if ((bits & Protocol.U_MODEL) != 0)
        {
            modnum = Msg.ReadByte();
            if (modnum >= QuakeDef.MAX_MODELS)
                Host.Error("CL_ParseModel: bad modnum"u8);
        }
        else
            modnum = ent->Baseline.ModelIndex;

        ent->Frame = (bits & Protocol.U_FRAME) != 0 ? Msg.ReadByte() : ent->Baseline.Frame;

        int colorIndex = (bits & Protocol.U_COLORMAP) != 0 ? Msg.ReadByte() : ent->Baseline.ColorMap;
        if (colorIndex == 0)
            ent->ColorMap = Vid.vid.Colormap;
        else
        {
            if (colorIndex > Cl.maxclients)
                Sys.Error("i >= cl.maxclients"u8); // the original genuinely calls Sys_Error directly here, not Host_Error
            ent->ColorMap = Cl.scores[colorIndex - 1].Translations;
        }

        ent->SkinNum = (bits & Protocol.U_SKIN) != 0 ? Msg.ReadByte() : ent->Baseline.Skin;

        ent->Effects = (bits & Protocol.U_EFFECTS) != 0 ? Msg.ReadByte() : ent->Baseline.Effects;

        // shift the known values for interpolation
        ent->MsgOrigins[1] = ent->MsgOrigins[0];
        ent->MsgAngles[1] = ent->MsgAngles[0];

        ent->MsgOrigins[0][0] = (bits & Protocol.U_ORIGIN1) != 0 ? Msg.ReadCoord() : ent->Baseline.Origin[0];
        ent->MsgAngles[0][0] = (bits & Protocol.U_ANGLE1) != 0 ? Msg.ReadAngle() : ent->Baseline.Angles[0];

        ent->MsgOrigins[0][1] = (bits & Protocol.U_ORIGIN2) != 0 ? Msg.ReadCoord() : ent->Baseline.Origin[1];
        ent->MsgAngles[0][1] = (bits & Protocol.U_ANGLE2) != 0 ? Msg.ReadAngle() : ent->Baseline.Angles[1];

        ent->MsgOrigins[0][2] = (bits & Protocol.U_ORIGIN3) != 0 ? Msg.ReadCoord() : ent->Baseline.Origin[2];
        ent->MsgAngles[0][2] = (bits & Protocol.U_ANGLE3) != 0 ? Msg.ReadAngle() : ent->Baseline.Angles[2];

        // johnfitz -- lerping for movetype_step entities; U_NOLERP is FitzQuake's U_STEP (same bit,
        // sent by the server for MOVETYPE_STEP)
        if ((bits & Protocol.U_NOLERP) != 0)
        {
            ent->LerpFlags |= LerpFlags.MOVESTEP;
            ent->ForceLink = true;
        }
        else
            ent->LerpFlags &= unchecked((byte)~LerpFlags.MOVESTEP);

        // johnfitz -- PROTOCOL_FITZQUAKE
        if (Cl.protocol == Protocol.PROTOCOL_FITZQUAKE)
        {
            ent->Alpha = (bits & Protocol.U_ALPHA) != 0 ? (byte)Msg.ReadByte() : ent->Baseline.Alpha;

            if ((bits & Protocol.U_FRAME2) != 0)
                ent->Frame = (ent->Frame & 0x00FF) | (Msg.ReadByte() << 8);
            if ((bits & Protocol.U_MODEL2) != 0)
                modnum = (modnum & 0x00FF) | (Msg.ReadByte() << 8);

            if ((bits & Protocol.U_LERPFINISH) != 0)
            {
                ent->LerpFinish = (float)ent->MsgTime + Msg.ReadByte() / 255.0f;
                ent->LerpFlags |= LerpFlags.FINISH;
            }
            else
                ent->LerpFlags &= unchecked((byte)~LerpFlags.FINISH);
        }

        // johnfitz -- moved here from above
        if (modnum >= QuakeDef.MAX_MODELS)
            Host.Error("CL_ParseModel: bad modnum"u8);
        Model_t* model = Cl.model_precache[modnum];
        if (model != ent->Model)
        {
            ent->Model = model;
            if (model != null)
            {
                ent->SyncBase = model->SyncType == SyncType_t.Rand ? (Q.Rand() & 0x7fff) / (float)0x7fff : 0.0f;
            }
            else
                forcelink = true; // hack to make null model players work

            ent->LerpFlags |= LerpFlags.RESETANIM; // johnfitz -- don't lerp animation across model changes
        }

        if (forcelink)
        {
            // didn't have an update last message
            ent->MsgOrigins[1] = ent->MsgOrigins[0];
            ent->Origin = ent->MsgOrigins[0];
            ent->MsgAngles[1] = ent->MsgAngles[0];
            ent->Angles = ent->MsgAngles[0];
            ent->ForceLink = true;
        }
    }

    /*
    ==================
    CL_ParseBaseline
    ==================
    */
    public static void ParseBaseline(Entity_t* ent, int version = 1)
    {
        // johnfitz -- PROTOCOL_FITZQUAKE (version 2 = svc_spawnbaseline2/svc_spawnstatic2)
        int bits = version == 2 ? Msg.ReadByte() : 0;

        ent->Baseline.ModelIndex = (bits & Protocol.B_LARGEMODEL) != 0 ? Msg.ReadShort() : Msg.ReadByte();
        ent->Baseline.Frame = (bits & Protocol.B_LARGEFRAME) != 0 ? Msg.ReadShort() : Msg.ReadByte();
        ent->Baseline.ColorMap = Msg.ReadByte();
        ent->Baseline.Skin = Msg.ReadByte();
        for (int i = 0; i < 3; i++)
        {
            ent->Baseline.Origin[i] = Msg.ReadCoord();
            ent->Baseline.Angles[i] = Msg.ReadAngle();
        }

        ent->Baseline.Alpha = (bits & Protocol.B_ALPHA) != 0 ? (byte)Msg.ReadByte() : (byte)Protocol.ENTALPHA_DEFAULT;
    }

    /*
    ==================
    CL_ParseClientdata

    Server information pertaining to this client only
    ==================
    */
    public static void ParseClientdata(int bits)
    {
        // johnfitz -- PROTOCOL_FITZQUAKE
        if ((bits & Protocol.SU_EXTEND1) != 0)
            bits |= Msg.ReadByte() << 16;
        if ((bits & Protocol.SU_EXTEND2) != 0)
            bits |= Msg.ReadByte() << 24;

        Cl.viewheight = (bits & Protocol.SU_VIEWHEIGHT) != 0 ? Msg.ReadChar() : Protocol.DEFAULT_VIEWHEIGHT;
        Cl.idealpitch = (bits & Protocol.SU_IDEALPITCH) != 0 ? Msg.ReadChar() : 0;

        Cl.mvelocity[1] = Cl.mvelocity[0];
        for (int i = 0; i < 3; i++)
        {
            Cl.punchangle[i] = (bits & (Protocol.SU_PUNCH1 << i)) != 0 ? Msg.ReadChar() : 0;
            Cl.mvelocity[0][i] = (bits & (Protocol.SU_VELOCITY1 << i)) != 0 ? Msg.ReadChar() * 16 : 0;
        }

        // [always sent] if (bits & SU_ITEMS)
        int i2 = Msg.ReadLong();

        if (Cl.items != i2)
        {
            // set flash times
            Sbar.Changed();
            for (int j = 0; j < 32; j++)
                if ((i2 & (1 << j)) != 0 && (Cl.items & (1 << j)) == 0)
                    Cl.item_gettime[j] = (float)Cl.time;
            Cl.items = i2;
        }

        Cl.onground = (bits & Protocol.SU_ONGROUND) != 0;
        Cl.inwater = (bits & Protocol.SU_INWATER) != 0;

        // the PROTOCOL_FITZQUAKE high bytes trail the message, so read every value first
        int weaponFrame = (bits & Protocol.SU_WEAPONFRAME) != 0 ? Msg.ReadByte() : 0;
        int armor = (bits & Protocol.SU_ARMOR) != 0 ? Msg.ReadByte() : 0;
        int weapon = (bits & Protocol.SU_WEAPON) != 0 ? Msg.ReadByte() : 0;
        int health = Msg.ReadShort();
        int ammo = Msg.ReadByte();
        int* ammos = stackalloc int[4];
        for (int i = 0; i < 4; i++)
            ammos[i] = Msg.ReadByte();
        int activeWeapon = Msg.ReadByte();

        // johnfitz -- PROTOCOL_FITZQUAKE
        if ((bits & Protocol.SU_WEAPON2) != 0)
            weapon |= Msg.ReadByte() << 8;
        if ((bits & Protocol.SU_ARMOR2) != 0)
            armor |= Msg.ReadByte() << 8;
        if ((bits & Protocol.SU_AMMO2) != 0)
            ammo |= Msg.ReadByte() << 8;
        for (int i = 0; i < 4; i++)
            if ((bits & (Protocol.SU_SHELLS2 << i)) != 0)
                ammos[i] |= Msg.ReadByte() << 8;
        if ((bits & Protocol.SU_WEAPONFRAME2) != 0)
            weaponFrame |= Msg.ReadByte() << 8;
        Cl.viewent->Alpha = (bits & Protocol.SU_WEAPONALPHA) != 0 ? (byte)Msg.ReadByte() : (byte)Protocol.ENTALPHA_DEFAULT;

        Cl.stats[(int)Stat.WeaponFrame] = weaponFrame;

        if (Cl.stats[(int)Stat.Armor] != armor)
        {
            Cl.stats[(int)Stat.Armor] = armor;
            Sbar.Changed();
        }

        if (Cl.stats[(int)Stat.Weapon] != weapon)
        {
            Cl.stats[(int)Stat.Weapon] = weapon;
            Sbar.Changed();
        }

        if (Cl.stats[(int)Stat.Health] != health)
        {
            Cl.stats[(int)Stat.Health] = health;
            Sbar.Changed();
        }

        if (Cl.stats[(int)Stat.Ammo] != ammo)
        {
            Cl.stats[(int)Stat.Ammo] = ammo;
            Sbar.Changed();
        }

        for (int i = 0; i < 4; i++)
        {
            if (Cl.stats[(int)Stat.Shells + i] != ammos[i])
            {
                Cl.stats[(int)Stat.Shells + i] = ammos[i];
                Sbar.Changed();
            }
        }

        if (Com.standard_quake)
        {
            if (Cl.stats[(int)Stat.ActiveWeapon] != activeWeapon)
            {
                Cl.stats[(int)Stat.ActiveWeapon] = activeWeapon;
                Sbar.Changed();
            }
        }
        else
        {
            if (Cl.stats[(int)Stat.ActiveWeapon] != (1 << activeWeapon))
            {
                Cl.stats[(int)Stat.ActiveWeapon] = 1 << activeWeapon;
                Sbar.Changed();
            }
        }
    }


    /*
    =====================
    CL_NewTranslation
    =====================
    */
    public static void NewTranslation(int slot)
    {
        if (slot > Cl.maxclients)
            Sys.Error("CL_NewTranslation: slot > cl.maxclients"u8);

        ScoreBoard_t* score = &Cl.scores[slot];
        byte* dest = score->Translations;
        byte* source = Vid.vid.Colormap;
        Unsafe.CopyBlockUnaligned(dest, source, Vid.VID_GRADES * 256);

        int top = score->Colors & 0xf0;
        int bottom = (score->Colors & 15) << 4;

        for (int i = 0; i < Vid.VID_GRADES; i++, dest += 256, source += 256)
        {
            if (top < 128) // the artists made some backwards ranges. sigh.
                new Span<byte>(source + top, 16).CopyTo(new Span<byte>(dest + RMain.TOP_RANGE, 16));
            else
                for (int j = 0; j < 16; j++)
                    dest[RMain.TOP_RANGE + j] = source[top + 15 - j];

            if (bottom < 128)
                new Span<byte>(source + bottom, 16).CopyTo(new Span<byte>(dest + RMain.BOTTOM_RANGE, 16));
            else
                for (int j = 0; j < 16; j++)
                    dest[RMain.BOTTOM_RANGE + j] = source[bottom + 15 - j];
        }
    }

    /*
    =====================
    CL_ParseStatic
    =====================
    */
    public static void ParseStatic(int version = 1)
    {
        int i = Cl.num_statics;
        if (i >= ClEntities.MAX_STATIC_ENTITIES)
            Host.Error("Too many static entities"u8);
        Entity_t* ent = &ClEntities.cl_static_entities[i];
        Cl.num_statics++;
        ParseBaseline(ent, version);

        // copy it to the current state
        ent->Model = Cl.model_precache[ent->Baseline.ModelIndex];
        ent->Alpha = ent->Baseline.Alpha; // johnfitz -- alpha
        ent->Frame = ent->Baseline.Frame;
        ent->ColorMap = Vid.vid.Colormap;
        ent->SkinNum = ent->Baseline.Skin;
        ent->Effects = ent->Baseline.Effects;

        ent->Origin = ent->Baseline.Origin;
        ent->Angles = ent->Baseline.Angles;
        ent->LerpFlags |= LerpFlags.RESETANIM | LerpFlags.RESETMOVE; // johnfitz -- lerping (Mark_V: added RESETMOVE)
        REfrag.AddEfrags(ent);
    }

    /*
    ===================
    CL_ParseStaticSound
    ===================
    */
    public static void ParseStaticSound(int version = 1)
    {
        Unsafe.SkipInit(out Vec3_t org);
        for (int i = 0; i < 3; i++)
            org[i] = Msg.ReadCoord();
        int sound_num = version == 2 ? Msg.ReadShort() : Msg.ReadByte(); // johnfitz -- PROTOCOL_FITZQUAKE
        if ((uint)sound_num >= QuakeDef.MAX_SOUNDS)
            Host.Error($"CL_ParseStaticSound: bad sound_num {sound_num}");
        int vol = Msg.ReadByte();
        int atten = Msg.ReadByte();

        SndDma.StaticSound(Cl.sound_precache[sound_num], org, vol, atten);
    }

    /*
    =====================
    CL_SignonReply

    An (int)Svc.SignOnNum has been received, perform a client side setup
    =====================
    */
    public static void SignonReply()
    {
        Con.DPrintf($"CL_SignonReply: {Cls.signon}\n");

        SizeBuf_t* msg = Cls.message;
        switch (Cls.signon)
        {
            case 1:
                Msg.WriteByte(msg, (int)Clc.StringCmd);
                Msg.WriteString(msg, "prespawn"u8);
                break;

            case 2:
                Msg.WriteByte(msg, (int)Clc.StringCmd);
                Msg.WriteString(msg, $"name \"{Cl.cl_name->String}\"\n");

                Msg.WriteByte(msg, (int)Clc.StringCmd);
                Msg.WriteString(msg, $"color {(int)Cl.cl_color->Value >> 4} {(int)Cl.cl_color->Value & 15}\n");

                Msg.WriteByte(msg, (int)Clc.StringCmd);
                Msg.WriteString(msg, $"spawn {Cls.spawnparms}");
                break;

            case 3:
                Msg.WriteByte(msg, (int)Clc.StringCmd);
                Msg.WriteString(msg, "begin"u8);
                Cache.Report(); // print remaining memory
                break;

            case 4:
                Screen.EndLoadingPlaque(); // allow normal screen updates
                break;
        }
    }

    /*
    =====================
    CL_ParseServerMessage
    =====================
    */
    public static void ParseServerMessage()
    {
        if (Cl.cl_shownet->Value == 1)
            Con.Printf($"{Net.net_message->CurSize} ");
        else if (Cl.cl_shownet->Value == 2)
            Con.Printf("------------------\n"u8);

        Cl.onground = false; // unless the server says otherwise

        Msg.BeginReading();

        while (true)
        {
            if (Msg.msg_badread)
                Host.Error("CL_ParseServerMessage: Bad server message"u8);

            int cmd = Msg.ReadByte();

            if (cmd == -1)
            {
                ShowNet("END OF MESSAGE"u8);
                return; // end of message
            }

            // if the high bit of the command byte is set, it is a fast update
            if ((cmd & 128) != 0)
            {
                ShowNet("fast update"u8);
                ParseUpdate(cmd & 127);
                continue;
            }

            // The original's SHOWNET(svc_strings[cmd]) indexes the table
            // unconditionally (out-of-bounds read in C for unknown commands);
            // only look up the name when it will be printed, and bounds-check
            // so unknown commands reach the "Illegible server message" default.
            if (Cl.cl_shownet->Value == 2)
                ShowNet((uint)cmd < (uint)svc_strings.Length ? svc_strings[cmd] : "unknown svc"u8);

            switch ((Svc)cmd)
            {
                default:
                    Host.Error("CL_ParseServerMessage: Illegible server message\n"u8);
                    break;

                case Svc.Nop:
                    break;

                case Svc.Time:
                    Cl.mtime[1] = Cl.mtime[0];
                    Cl.mtime[0] = Msg.ReadFloat();
                    break;

                case Svc.ClientData:
                    ParseClientdata(Msg.ReadShort());
                    break;

                case Svc.Version:
                    int version = Msg.ReadLong();
                    if (version != Protocol.PROTOCOL_NETQUAKE && version != Protocol.PROTOCOL_FITZQUAKE)
                        Host.Error($"CL_ParseServerMessage: Server is protocol {version} instead of {Protocol.PROTOCOL_NETQUAKE} or {Protocol.PROTOCOL_FITZQUAKE}\n");
                    Cl.protocol = version;
                    break;

                case Svc.Disconnect:
                    Host.EndGame($"Server disconnected\n");
                    return;

                case Svc.Print:
                    Con.Printf($"{Msg.ReadString()}");
                    break;

                case Svc.CenterPrint:
                    Screen.CenterPrint(Msg.ReadString());
                    break;

                case Svc.StuffText:
                    Cmd.AddText(Msg.ReadString());
                    break;

                case Svc.Damage:
                    View.ParseDamage();
                    break;

                case Svc.ServerInfo:
                    ParseServerInfo();
                    Vid.vid.RecalcRefdef = 1; // leave intermission full screen
                    break;

                case Svc.SetAngle:
                    for (int i = 0; i < 3; i++)
                        Cl.viewangles[i] = Msg.ReadAngle();
                    break;

                case Svc.SetView:
                    Cl.viewentity = Msg.ReadShort();
                    break;

                case Svc.LightStyle:
                {
                    int i = Msg.ReadByte();
                    if (i >= QuakeDef.MAX_LIGHTSTYLES)
                        Sys.Error("Svc.LightStyle > MAX_LIGHTSTYLES"u8);
                    {
                        LightStyle_t* ls = &ClLightStyle.cl_lightstyle[i];
                        Q.StrCpy(ls->Map, Msg.ReadString());
                        ls->Length = Q.StrLen(ls->Map);
                    }
                    break;
                }

                case Svc.Sound:
                    ParseStartSoundPacket();
                    break;

                case Svc.StopSound:
                {
                    int i = Msg.ReadShort();
                    SndDma.StopSound(i >> 3, i & 7);
                    break;
                }

                case Svc.UpdateName:
                {
                    Sbar.Changed();
                    int i = Msg.ReadByte();
                    if (i >= Cl.maxclients)
                        Host.Error("CL_ParseServerMessage: (int)Svc.UpdateName > MAX_SCOREBOARD"u8);
                    Q.StrCpy(Cl.scores[i].Name, Msg.ReadString());
                    break;
                }

                case Svc.UpdateFrags:
                {
                    Sbar.Changed();
                    int i = Msg.ReadByte();
                    if (i >= Cl.maxclients)
                        Host.Error("CL_ParseServerMessage: (int)Svc.UpdateFrags > MAX_SCOREBOARD"u8);
                    Cl.scores[i].Frags = Msg.ReadShort();
                    break;
                }

                case Svc.UpdateColors:
                {
                    Sbar.Changed();
                    int i = Msg.ReadByte();
                    if (i >= Cl.maxclients)
                        Host.Error("CL_ParseServerMessage: (int)Svc.UpdateColors > MAX_SCOREBOARD"u8);
                    Cl.scores[i].Colors = Msg.ReadByte();
                    NewTranslation(i);
                    break;
                }

                case Svc.Particle:
                    RPart.ParseParticleEffect();
                    break;

                case Svc.SpawnBaseline:
                {
                    int i = Msg.ReadShort();
                    // must use CL_EntityNum() to force cl.num_entities up
                    ParseBaseline(EntityNum(i));
                    break;
                }

                case Svc.SpawnStatic:
                    ParseStatic();
                    break;

                // johnfitz -- PROTOCOL_FITZQUAKE
                case Svc.SpawnBaseline2:
                {
                    int i = Msg.ReadShort();
                    // must use CL_EntityNum() to force cl.num_entities up
                    ParseBaseline(EntityNum(i), 2);
                    break;
                }

                case Svc.SpawnStatic2:
                    ParseStatic(2);
                    break;

                case Svc.SpawnStaticSound2:
                    ParseStaticSound(2);
                    break;

                case Svc.Skybox:
                    Con.DPrintf($"svc_skybox: {Msg.ReadString()}\n"); // parsed only; no skybox rendering
                    break;

                case Svc.Bf:
                    Cmd.ExecuteString("bf"u8, CmdSource_t.SrcCommand);
                    break;

                case Svc.Fog:
                    Cl.fog_density = Msg.ReadByte() / 255.0f;
                    Cl.fog_red = Msg.ReadByte() / 255.0f;
                    Cl.fog_green = Msg.ReadByte() / 255.0f;
                    Cl.fog_blue = Msg.ReadByte() / 255.0f;
                    Cl.fog_time = Msg.ReadShort() / 100.0f;
                    break;

                case Svc.TempEntity:
                    ClTEnt.ParseTEnt();
                    break;

                case Svc.SetPause:
                {
                    Cl.paused = Msg.ReadByte() != 0;
                    if (Cl.paused)
                        CdAudio.Pause();
                    else
                        CdAudio.Resume();
                    // VID_HandlePause is Win32-only (sys_win.c) -- skipped per CLAUDE.md rule 5.
                    break;
                }

                case Svc.SignOnNum:
                {
                    int i = Msg.ReadByte();
                    if (i <= Cls.signon)
                        Host.Error($"Received signon {i} when at {Cls.signon}");
                    Cls.signon = i;
                    SignonReply();
                    break;
                }

                case Svc.KilledMonster:
                    Cl.stats[(int)Stat.Monsters]++;
                    break;

                case Svc.FoundSecret:
                    Cl.stats[(int)Stat.Secrets]++;
                    break;

                case Svc.UpdateStat:
                {
                    int i = Msg.ReadByte();
                    if (i is < 0 or >= 32) // MAX_CL_STATS
                        Sys.Error($"(int)Svc.UpdateStat: {i} is invalid");
                    Cl.stats[i] = Msg.ReadLong();
                    break;
                }

                case Svc.SpawnStaticSound:
                    ParseStaticSound();
                    break;

                case Svc.CdTrack:
                    Cl.cdtrack = Msg.ReadByte();
                    Cl.looptrack = Msg.ReadByte();
                    if ((ClDemo.demoplayback || ClDemo.demorecording) && ClDemo.forcetrack != -1)
                        CdAudio.Play((byte)ClDemo.forcetrack, true);
                    else
                        CdAudio.Play((byte)Cl.cdtrack, true);
                    break;

                case Svc.Intermission:
                    Cl.intermission = 1;
                    Cl.completed_time = (int)Cl.time;
                    Vid.vid.RecalcRefdef = 1; // go to full screen
                    break;

                case Svc.Finale:
                    Cl.intermission = 2;
                    Cl.completed_time = (int)Cl.time;
                    Vid.vid.RecalcRefdef = 1; // go to full screen
                    Screen.CenterPrint(Msg.ReadString());
                    break;

                case Svc.CutScene:
                    Cl.intermission = 3;
                    Cl.completed_time = (int)Cl.time;
                    Vid.vid.RecalcRefdef = 1; // go to full screen
                    Screen.CenterPrint(Msg.ReadString());
                    break;

                case Svc.SellScreen:
                    Cmd.ExecuteString("help"u8, CmdSource_t.SrcCommand);
                    break;
            }
        }
    }
}
