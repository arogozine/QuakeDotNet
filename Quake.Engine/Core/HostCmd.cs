// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.CompilerServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// host_cmd.c -- console commands. "map"/"connect"/"reconnect"/"quit" are
/// ported, as are the handshake commands the signon sequence needs --
/// "prespawn"/
/// "spawn"/"begin" (Host_PreSpawn_f/Host_Spawn_f/Host_Begin_f) and
/// "name"/"color" (Host_Name_f/Host_Color_f, which the handshake's
/// (int)Clc.StringCmd messages also name, see ClParse.cs's SignonReply) -- so
/// that SvUser.cs's SV_ReadClientMessage has real commands to dispatch to.
/// The single-player debug/cheat commands ("god"/"notarget"/"noclip"/"fly"/
/// "kill"/"give"/"pause"/"version") are ported too (Host_Give_f's rogue/
/// hipnotic mission-pack branches partially excepted -- see that method's
/// own comment). "changelevel"/"restart" are ported too, now that
/// Host.ShutdownServer/Sv.SaveSpawnparms/Sv.SendReconnect exist for them to
/// call (Host_Map_f and Host_Quit_f both call Host.ShutdownServer too).
/// The debug/demo-loop commands are ported too: "viewmodel"/"viewframe"/
/// "viewnext"/"viewprev" (Host_Viewmodel_f/Viewframe_f/Viewnext_f/Viewprev_f,
/// via the private FindViewthing helper) and "startdemos"/"demos"/"stopdemo"
/// (Host_Startdemos_f/Demos_f/Stopdemo_f, on top of ClDemo.NextDemo --
/// cl_main.c's CL_NextDemo, now ported there too). "mcache" (model.c's
/// Mod_Print) is registered here too since host_cmd.c's Host_InitCommands is
/// where the original wires it up, even though the method itself lives in
/// Model.cs.
/// The multiplayer-only administrative commands are ported too --
/// "status"/"ping" (Host_Status_f/Host_Ping_f), "say"/"say_team"/"tell"
/// (the shared private Say helper plus Host_Tell_f), and "kick"
/// (Host_Kick_f) -- now that svs.maxclients can really exceed 1
/// (Net.cs/NetDgrm.cs) there's a real second client to exercise them
/// against.
/// </summary>
public static unsafe class HostCmd
{
    private static void MapCmd() => Map_f();
    private static void ConnectCmd() => Connect_f();
    private static void ReconnectCmd() => Reconnect_f();
    private static void ChangelevelCmd() => Changelevel_f();
    private static void RestartCmd() => Restart_f();
    private static void QuitCmd() => Quit_f();
    private static void NameCmd() => Name_f();
    private static void ColorCmd() => Color_f();
    private static void PreSpawnCmd() => PreSpawn_f();
    private static void SpawnCmd() => Spawn_f();
    private static void BeginCmd() => Begin_f();


    /*
    ======================
    Host_Map_f

    handle a
    map <servername>
    command from the console.  Active clients are kicked off.
    ======================
    */
    public static void Map_f()
    {
        ClDemo.demonum = -1; // stop demo loop in case this fails

        ClMain.Disconnect();
        Host.ShutdownServer(false);

        Keys.key_dest = KeyDest.Game; // remove console or menu
        Screen.BeginLoadingPlaque();

        Cls.mapstring[0] = 0;
        for (int i = 0; i < Cmd.Argc(); i++)
        {
            Q.StrCat(Cls.mapstring, Cmd.Argv(i));
            Q.StrCat(Cls.mapstring, " "u8);
        }
        Q.StrCat(Cls.mapstring, "\n"u8);

        Svs.serverflags = 0; // haven't completed an episode yet

        Sv.SpawnServer(Cmd.Argv(1));
        if (!Sv.active)
            return;

        if (Cls.state != CActive.Dedicated)
        {
            // cls.spawnparms -- left at its default empty string (see
            // Cl.cs's own comment on Cls.spawnparms): this port's "map"
            // command has no changelevel/save-restore flow that would ever
            // populate argv(2..) with real spawn parms.

            Cmd.ExecuteString("connect local"u8, CmdSource_t.SrcCommand);
        }
    }

    /*
    =====================
    Host_Connect_f

    User command to connect to server
    =====================
    */
    public static void Connect_f()
    {
        ClDemo.demonum = -1; // stop demo loop in case this fails
        if (ClDemo.demoplayback)
        {
            ClDemo.StopPlayback();
            ClMain.Disconnect();
        }

        ClMain.EstablishConnection(Cmd.Argv(1));
        Reconnect_f();
    }

    /*
    ==================
    Host_Reconnect_f

    This command causes the client to wait for the signon messages again.
    This is sent just before a server changes levels
    ==================
    */
    public static void Reconnect_f()
    {
        Screen.BeginLoadingPlaque();
        Cls.signon = 0; // need new connection messages
    }

    /*
    ==================
    Host_Changelevel_f

    Goes to a new map, taking all clients along
    ==================
    */
    public static void Changelevel_f()
    {
        if (Cmd.Argc() != 2)
        {
            Con.Printf("changelevel <levelname> : continue game on a new level\n"u8);
            return;
        }
        if (!Sv.active || ClDemo.demoplayback)
        {
            Con.Printf("Only the server may changelevel\n"u8);
            return;
        }
        Sv.SaveSpawnparms();

        // level[MAX_QPATH]; Q_strcpy(level, Cmd_Argv(1)) in the original --
        // a defensive copy, not just style: SV_SpawnServer (below) still
        // has sv.active set to the OLD level at this point, so it calls
        // SV_SendReconnect, which calls Cmd_ExecuteString("reconnect\n") --
        // a NESTED Cmd_TokenizeString that Zone_Frees/reallocates every
        // cmd_argv[] slot, including this one. Without the copy, `level`
        // (a bare Cmd.Argv(1) pointer) is a dangling/reused pointer by the
        // time SV_SpawnServer's Q_strcpy(sv.name, server) actually reads it
        // -- a Zone use-after-free that (depending on what reused the freed
        // block) reads garbage into the next level's name, e.g. surfacing
        // as "FindFile: can't find maps/1.000000.bsp".
        byte* level = stackalloc byte[QuakeDef.MAX_QPATH];
        Q.StrCpy(level, Cmd.Argv(1));
        Sv.SpawnServer(level);
    }

    /*
    ==================
    Host_Restart_f

    Restarts the current server for a dead player
    ==================
    */
    public static void Restart_f()
    {
        if (ClDemo.demoplayback || !Sv.active)
            return;

        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        // mapname must be copied out, because it gets cleared in
        // SV_SpawnServer.
        byte* mapname = stackalloc byte[QuakeDef.MAX_QPATH];
        Q.StrCpy(mapname, Sv.Name);
        Sv.SpawnServer(mapname);
    }

    /*
    ==================
    Host_Quit_f
    ==================
    */
    public static void Quit_f()
    {
        if (Keys.key_dest != KeyDest.Console && Cls.state != CActive.Dedicated)
        {
            Menu.Menu_Quit_f();
            return;
        }

        ClMain.Disconnect();
        Host.ShutdownServer(false);

        Sys.Quit();
    }

    /*
    ======================
    Host_Name_f
    ======================
    */
    public static void Name_f()
    {
        if (Cmd.Argc() == 1)
        {
            Con.Printf($"\"name\" is \"{Cl.cl_name->String}\"\n");
            return;
        }

        byte* newName = Cmd.Argc() == 2 ? Cmd.Argv(1) : Cmd.Args();
        if (Q.StrLen(newName) > 15)
            newName[15] = 0;

        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            if (Q.StrCmp(Cl.cl_name->String, newName) == 0)
                return;
            Cvar.Set("_cl_name"u8, newName);
            if (Cls.state == CActive.Connected)
                Cmd.ForwardToServer();
            return;
        }

        if (Svs.host_client->Name[0] != 0 && Q.StrCmp(Svs.host_client->Name, "unconnected"u8) != 0)
            if (Q.StrCmp(Svs.host_client->Name, newName) != 0)
                Con.Printf($"{Svs.host_client->Name} renamed to {newName}\n");
        Q.StrCpy(Svs.host_client->Name, newName);
        // host_client->edict->v.netname = host_client->name - pr_strings
        // (Svs.clients and pr_strings are both in the Hunk)
        Svs.host_client->Edict->V.NetName = Progs.PR_SetString(Svs.host_client->Name);

        // send notification to all clients
        SizeBuf_t* rdg = Sv.reliable_datagram;
        Msg.WriteByte(rdg, (int)Svc.UpdateName);
        Msg.WriteByte(rdg, (int)(Svs.host_client - Svs.clients));
        Msg.WriteString(rdg, Svs.host_client->Name);
    }

    /*
    ==================
    Host_Color_f
    ==================
    */
    public static void Color_f()
    {
        if (Cmd.Argc() == 1)
        {
            Con.Printf($"\"color\" is \"{(int)Cl.cl_color->Value >> 4} {(int)Cl.cl_color->Value & 0x0f}\"\n");
            Con.Printf("color <0-13> [0-13]\n"u8);
            return;
        }

        int top, bottom;
        if (Cmd.Argc() == 2)
            top = bottom = Q.Atoi(Cmd.Argv(1));
        else
        {
            top = Q.Atoi(Cmd.Argv(1));
            bottom = Q.Atoi(Cmd.Argv(2));
        }

        top &= 15;
        if (top > 13)
            top = 13;
        bottom &= 15;
        if (bottom > 13)
            bottom = 13;

        int playercolor = top * 16 + bottom;

        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cvar.SetValue("_cl_color"u8, playercolor);
            if (Cls.state == CActive.Connected)
                Cmd.ForwardToServer();
            return;
        }

        Svs.host_client->Colors = playercolor;
        Svs.host_client->Edict->V.Team = bottom + 1;

        // send notification to all clients
        SizeBuf_t* rdg = Sv.reliable_datagram;
        Msg.WriteByte(rdg, (int)Svc.UpdateColors);
        Msg.WriteByte(rdg, (int)(Svs.host_client - Svs.clients));
        Msg.WriteByte(rdg, Svs.host_client->Colors);
    }

    /*
    ==================
    Host_Say
    ==================
    */
    private static void Say(bool teamonly)
    {
        bool fromServer = false;

        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            if (Cls.state == CActive.Dedicated)
            {
                fromServer = true;
                teamonly = false;
            }
            else
            {
                Cmd.ForwardToServer();
                return;
            }
        }

        if (Cmd.Argc() < 2)
            return;

        Client_t* save = Svs.host_client;

        byte* p = Cmd.Args();
        // remove quotes if present
        if (*p == (byte)'"')
        {
            p++;
            p[Q.StrLen(p) - 1] = 0;
        }

        // char text[64] -- kept as raw bytes so Quake's high-bit (coloured) characters survive
        byte* text = stackalloc byte[CHAT_TEXT_SIZE];

        // turn on color set 1
        text[0] = 1;
        text[1] = 0;
        if (!fromServer)
        {
            CatBounded(text, save->Name, CHAT_TEXT_SIZE - 2);
            CatBounded(text, ": "u8, CHAT_TEXT_SIZE - 2);
        }
        else
        {
            CatBounded(text, "<"u8, CHAT_TEXT_SIZE - 2);
            CatBounded(text, Cvar.VariableString("hostname"u8), CHAT_TEXT_SIZE - 2);
            CatBounded(text, "> "u8, CHAT_TEXT_SIZE - 2);
        }

        // check length & truncate if necessary
        CatBounded(text, p, CHAT_TEXT_SIZE - 2); // -2 for \n and null terminator
        CatBounded(text, "\n"u8, CHAT_TEXT_SIZE - 1);

        int length = Q.StrLen(text);

        for (int i = 0; i < Svs.maxclients; i++)
        {
            Client_t* client = &Svs.clients[i];
            if (!client->Active || !client->Spawned)
                continue;
            if (Host.teamplay->Value != 0 && teamonly && client->Edict->V.Team != save->Edict->V.Team)
                continue;
            Svs.host_client = client;
            Host.ClientPrintf(new ReadOnlySpan<byte>(text, length));
        }
        Svs.host_client = save;

        Sys.Printf(new ReadOnlySpan<byte>(text + 1, length - 1));
    }

    // Size of the chat line buffer (char text[64] in Host_Say_f / Host_Tell_f).
    private const int CHAT_TEXT_SIZE = 64;

    // strcat that never lets the result grow past maxLen characters. The original
    // sprintf/strcat into text[64] overflows on a long hostname; this truncates.
    private static void CatBounded(byte* dest, Utf8String src_, int maxLen)
    {
        byte* src = src_;
        int len = Q.StrLen(dest);
        int n = Math.Min(Q.StrLen(src), Math.Max(0, maxLen - len));
        Q.MemCpy(dest + len, src, n);
        dest[len + n] = 0;
    }

    public static void Say_f() => Say(false);

    public static void Say_Team_f() => Say(true);

    /*
    ==================
    Host_Tell_f
    ==================
    */
    public static void Tell_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Cmd.Argc() < 3)
            return;

        byte* p = Cmd.Args();
        // remove quotes if present
        if (*p == (byte)'"')
        {
            p++;
            p[Q.StrLen(p) - 1] = 0;
        }

        // char text[64] -- kept as raw bytes so Quake's high-bit (coloured) characters survive
        byte* text = stackalloc byte[CHAT_TEXT_SIZE];
        text[0] = 0;
        CatBounded(text, Svs.host_client->Name, CHAT_TEXT_SIZE - 2);
        CatBounded(text, ": "u8, CHAT_TEXT_SIZE - 2);

        // check length & truncate if necessary
        CatBounded(text, p, CHAT_TEXT_SIZE - 2); // -2 for \n and null terminator
        CatBounded(text, "\n"u8, CHAT_TEXT_SIZE - 1);

        int length = Q.StrLen(text);

        Client_t* save = Svs.host_client;
        for (int i = 0; i < Svs.maxclients; i++)
        {
            Client_t* client = &Svs.clients[i];
            if (!client->Active || !client->Spawned)
                continue;
            if (Q.StrCaseCmp(client->Name, Cmd.Argv(1)) != 0)
                continue;
            Svs.host_client = client;
            Host.ClientPrintf(new ReadOnlySpan<byte>(text, length));
            break;
        }
        Svs.host_client = save;
    }

    /*
    ==================
    Host_PreSpawn_f
    ==================
    */
    public static void PreSpawn_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Con.Printf("prespawn is not valid from the console\n"u8);
            return;
        }

        if (Svs.host_client->Spawned)
        {
            Con.Printf("prespawn not valid -- already spawned\n"u8);
            return;
        }

        SizeBuf_t* sig = Sv.signon;
        Sz.Write(&Svs.host_client->Message, sig->Data, sig->CurSize);
        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.SignOnNum);
        Msg.WriteByte(&Svs.host_client->Message, 2);
        Svs.host_client->SendSignon = true;
    }

    /*
    ==================
    Host_Spawn_f
    ==================
    */
    public static void Spawn_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Con.Printf("spawn is not valid from the console\n"u8);
            return;
        }

        if (Svs.host_client->Spawned)
        {
            Con.Printf("Spawn not valid -- already spawned\n"u8);
            return;
        }

        // run the entrance script
        if (Sv.loadgame)
        {
            // loaded games are fully inited already
            // if this is the last client to be connected, unpause
            Sv.paused = false;
        }
        else
        {
            // set up the edict
            EDict_t* ent = Svs.host_client->Edict;

            // memset(&ent->v, 0, progs->entityfields * 4) -- clears the QuakeC-defined
            // fields past the fixed EntVars_t too, not just sizeof(EntVars_t).
            Q.MemSet(&ent->V, 0, Progs.progs->EntityFields * 4);
            ent->V.ColorMap = Progs.NumForEdict(ent);
            ent->V.Team = (Svs.host_client->Colors & 15) + 1;
            // ent->v.netname = host_client->name - pr_strings
            ent->V.NetName = Progs.PR_SetString(Svs.host_client->Name);

            // copy spawn parms out of the client_t
            float* parm1 = &Progs.pr_global_struct->Parm1;
            for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
                parm1[i] = Svs.host_client->spawn_parms[i];

            // call the spawn function
            Progs.pr_global_struct->Time = (float)Sv.time;
            Progs.pr_global_struct->Self = Progs.EdictToProg(Sv.sv_player);
            Progs.ExecuteProgram(Progs.pr_global_struct->ClientConnect);

            // Sys_Printf("%s entered the game\n", ...) diagnostic (gated on
            // Sys_FloatTime()/netconnection->connecttime) is stdout-only
            // logging with no gameplay effect -- skipped, matching this
            // port's other Sys_Printf-only omissions.

            Progs.ExecuteProgram(Progs.pr_global_struct->PutClientInServer);
        }

        // send all current names, colors, and frag counts
        Sz.Clear(&Svs.host_client->Message);

        // send time of update
        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.Time);
        Msg.WriteFloat(&Svs.host_client->Message, (float)Sv.time);

        for (int i = 0; i < Svs.maxclients; i++)
        {
            Client_t* client = &Svs.clients[i];
            Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateName);
            Msg.WriteByte(&Svs.host_client->Message, i);
            Msg.WriteString(&Svs.host_client->Message, client->Name);
            Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateFrags);
            Msg.WriteByte(&Svs.host_client->Message, i);
            Msg.WriteShort(&Svs.host_client->Message, client->OldFrags);
            Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateColors);
            Msg.WriteByte(&Svs.host_client->Message, i);
            Msg.WriteByte(&Svs.host_client->Message, client->Colors);
        }

        // send all current light styles
        for (int i = 0; i < QuakeDef.MAX_LIGHTSTYLES; i++)
        {
            Msg.WriteByte(&Svs.host_client->Message, (int)Svc.LightStyle);
            Msg.WriteByte(&Svs.host_client->Message, (byte)i);
            Msg.WriteString(&Svs.host_client->Message, Sv.lightstyles[i]);
        }

        // send some stats
        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateStat);
        Msg.WriteByte(&Svs.host_client->Message, (int)Stat.TotalSecrets);
        Msg.WriteLong(&Svs.host_client->Message, (int)Progs.pr_global_struct->TotalSecrets);

        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateStat);
        Msg.WriteByte(&Svs.host_client->Message, (int)Stat.TotalMonsters);
        Msg.WriteLong(&Svs.host_client->Message, (int)Progs.pr_global_struct->TotalMonsters);

        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateStat);
        Msg.WriteByte(&Svs.host_client->Message, (int)Stat.Secrets);
        Msg.WriteLong(&Svs.host_client->Message, (int)Progs.pr_global_struct->FoundSecrets);

        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.UpdateStat);
        Msg.WriteByte(&Svs.host_client->Message, (int)Stat.Monsters);
        Msg.WriteLong(&Svs.host_client->Message, (int)Progs.pr_global_struct->KilledMonsters);

        // send a fixangle
        // Never send a roll angle, because savegames can catch the server
        // in a state where it is expecting the client to correct the angle
        // and it won't happen if the game was just loaded, so you wind up
        // with a permanent head tilt
        EDict_t* fixEnt = Progs.EdictNum(1 + (int)(Svs.host_client - Svs.clients));
        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.SetAngle);
        for (int i = 0; i < 2; i++)
            Msg.WriteAngle(&Svs.host_client->Message, fixEnt->V.Angles[i]);
        Msg.WriteAngle(&Svs.host_client->Message, 0);

        Sv.WriteClientdataToMessage(Sv.sv_player, &Svs.host_client->Message);

        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.SignOnNum);
        Msg.WriteByte(&Svs.host_client->Message, 3);
        Svs.host_client->SendSignon = true;
    }

    /*
    ==================
    Host_Begin_f
    ==================
    */
    public static void Begin_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Con.Printf("begin is not valid from the console\n"u8);
            return;
        }

        Svs.host_client->Spawned = true;
    }

    // SvPhys.FL_GODMODE/SvPhys.FL_NOTARGET (server.h) -- not already declared elsewhere in
    // this port's HostCmd.cs (PrCmds.cs privately declares its own copy of
    // SvPhys.FL_NOTARGET, matching the original's single shared #define being used
    // from multiple .c files).

    // IT_*/HIT_* (quakedef.h) -- item bits "give" ORs into sv_player->v.items.

    /*
    ==================
    Host_Status_f
    ==================
    */
    public static void Status_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            if (!Sv.active)
            {
                Cmd.ForwardToServer();
                return;
            }
        }

        bool toConsole = Cmd.cmd_source == CmdSource_t.SrcCommand;

        Print($"host:    {Cvar.VariableString("hostname"u8)}\n");
        Print($"version: {VERSION:F2}\n");
        if (NetIp.tcpipAvailable)
            Print($"tcp/ip:  {NetIp.my_tcpip_address}\n");
        // ipxAvailable is always false in this port (NetIp.cs/Menu.cs -- no
        // IPX transport exists), so the original's "ipx:" line never prints
        // and is skipped here too.
        Print($"map:     {Sv.Name}\n");
        Print($"players: {Net.net_activeconnections} active ({Svs.maxclients} max)\n\n");

        for (int j = 0; j < Svs.maxclients; j++)
        {
            Client_t* client = &Svs.clients[j];
            if (!client->Active)
                continue;

            int seconds = (int)(Net.net_time - client->NetConnection->ConnectTime);
            int minutes = seconds / 60;
            int hours = 0;
            if (minutes != 0)
            {
                seconds -= minutes * 60;
                hours = minutes / 60;
                if (hours != 0)
                    minutes -= hours * 60;
            }

            Print($"#{j + 1,-2} {client->Name,-16}  {(int)client->Edict->V.Frags,3}  {hours,2}:{minutes:D2}:{seconds:D2}\n");
            Print($"   {client->NetConnection->Address}\n");
        }

        void Print([InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler text)
        {
            if (toConsole)
                Con.Printf(text.GetWrittenBuffer());
            else
                Host.ClientPrintf(text.GetWrittenBuffer());
        }
    }

    /*
    ==================
    Host_God_f

    Sets client to godmode
    ==================
    */
    public static void God_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Progs.pr_global_struct->DeathMatch != 0 && !Svs.host_client->Privileged)
            return;

        Sv.sv_player->V.Flags = (int)Sv.sv_player->V.Flags ^ SvPhys.FL_GODMODE;
        if (((int)Sv.sv_player->V.Flags & SvPhys.FL_GODMODE) == 0)
            Host.ClientPrintf("godmode OFF\n"u8);
        else
            Host.ClientPrintf("godmode ON\n"u8);
    }

    public static void Notarget_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Progs.pr_global_struct->DeathMatch != 0 && !Svs.host_client->Privileged)
            return;

        Sv.sv_player->V.Flags = (int)Sv.sv_player->V.Flags ^ SvPhys.FL_NOTARGET;
        if (((int)Sv.sv_player->V.Flags & SvPhys.FL_NOTARGET) == 0)
            Host.ClientPrintf("notarget OFF\n"u8);
        else
            Host.ClientPrintf("notarget ON\n"u8);
    }

    public static void Noclip_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Progs.pr_global_struct->DeathMatch != 0 && !Svs.host_client->Privileged)
            return;

        if ((int)Sv.sv_player->V.MoveType != (int)MoveType.NoClip)
        {
            Cl.noclip_anglehack = true;
            Sv.sv_player->V.MoveType = (int)MoveType.NoClip;
            Host.ClientPrintf("noclip ON\n"u8);
        }
        else
        {
            Cl.noclip_anglehack = false;
            Sv.sv_player->V.MoveType = (int)MoveType.Walk;
            Host.ClientPrintf("noclip OFF\n"u8);
        }
    }

    /*
    ==================
    Host_Fly_f

    Sets client to flymode
    ==================
    */
    public static void Fly_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Progs.pr_global_struct->DeathMatch != 0 && !Svs.host_client->Privileged)
            return;

        if ((int)Sv.sv_player->V.MoveType != (int)MoveType.Fly)
        {
            Sv.sv_player->V.MoveType = (int)MoveType.Fly;
            Host.ClientPrintf("flymode ON\n"u8);
        }
        else
        {
            Sv.sv_player->V.MoveType = (int)MoveType.Walk;
            Host.ClientPrintf("flymode OFF\n"u8);
        }
    }

    /*
    ==================
    Host_Ping_f
    ==================
    */
    public static void Ping_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        Host.ClientPrintf("Client ping times:\n"u8);
        for (int i = 0; i < Svs.maxclients; i++)
        {
            Client_t* client = &Svs.clients[i];
            if (!client->Active)
                continue;

            float total = 0;
            for (int j = 0; j < Client_t.NUM_PING_TIMES; j++)
                total += client->PingTimes[j];
            total /= Client_t.NUM_PING_TIMES;
            Host.ClientPrintf($"{(int)(total * 1000),4} {client->Name}\n");
        }
    }

    /*
    ==================
    Host_Kill_f
    ==================
    */
    public static void Kill_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Sv.sv_player->V.Health <= 0)
        {
            Host.ClientPrintf("Can't suicide -- allready dead!\n"u8);
            return;
        }

        Progs.pr_global_struct->Time = (float)Sv.time;
        Progs.pr_global_struct->Self = Progs.EdictToProg(Sv.sv_player);
        Progs.ExecuteProgram(Progs.pr_global_struct->ClientKill);
    }

    /*
    ==================
    Host_Pause_f
    ==================
    */
    public static void Pause_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Host.pausable->Value == 0)
            Host.ClientPrintf("Pause not allowed.\n"u8);
        else
        {
            Sv.paused = !Sv.paused;

            if (Sv.paused)
                Host.BroadcastPrintf($"{Progs.pr_strings + Sv.sv_player->V.NetName} paused the game\n");
            else
                Host.BroadcastPrintf($"{Progs.pr_strings + Sv.sv_player->V.NetName} unpaused the game\n");

            // send notification to all clients
            SizeBuf_t* rdg = Sv.reliable_datagram;
            Msg.WriteByte(rdg, (int)Svc.SetPause);
            Msg.WriteByte(rdg, Sv.paused ? 1 : 0);
        }
    }

    // VERSION (quakedef.h) -- Sv.cs privately declares its own copy too,
    // matching the original's single shared #define being used from
    // multiple .c files.

    public static void Version_f()
    {
        Con.Printf($"Version {VERSION:F2}\n");
        // "Exe: " __TIME__ " " __DATE__ -- build-timestamp diagnostic line,
        // stdout/console-only info with no gameplay effect; skipped like
        // this port's other Sys_Printf-only omissions.
    }

    /*
    ==================
    Host_Give_f

    The rogue mission-pack ammo fields ("ammo_shells1" etc.) are read via
    EdictSerialization.GetEdictFieldValue exactly like the original's
    GetEdictFieldValue -- both resolve fields by name against whatever
    progs.dat is actually loaded (Progs.cs's pr_edict_size sizes edicts for
    progs->EntityFields, not just the fixed EntVars_t fields), so this
    resolves correctly once rogue's progs.dat is loaded, same as the
    already-ported "items2" lookup in Sv.cs.
    ==================
    */
    public static void Give_f()
    {
        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            Cmd.ForwardToServer();
            return;
        }

        if (Progs.pr_global_struct->DeathMatch != 0 && !Svs.host_client->Privileged)
            return;

        byte* t = Cmd.Argv(1);
        int v = Q.Atoi(Cmd.Argv(2));

        switch ((char)t[0])
        {
            case >= '0' and <= '9':
                if (Com.hipnotic)
                {
                    if (t[0] == '6')
                    {
                        if (t[1] == (byte)'a')
                            Sv.sv_player->V.Items = (int)Sv.sv_player->V.Items | HIT_PROXIMITY_GUN;
                        else
                            Sv.sv_player->V.Items = (int)Sv.sv_player->V.Items | IT_GRENADE_LAUNCHER;
                    }
                    else if (t[0] == '9')
                        Sv.sv_player->V.Items = (int)Sv.sv_player->V.Items | HIT_LASER_CANNON;
                    else if (t[0] == '0')
                        Sv.sv_player->V.Items = (int)Sv.sv_player->V.Items | HIT_MJOLNIR;
                    else if (t[0] >= '2')
                        Sv.sv_player->V.Items = (int)Sv.sv_player->V.Items | (IT_SHOTGUN << (t[0] - '2'));
                }
                else
                {
                    if (t[0] >= '2')
                        Sv.sv_player->V.Items = (int)Sv.sv_player->V.Items | (IT_SHOTGUN << (t[0] - '2'));
                }
                break;

            case 's':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_shells1"u8);
                    if (val != null)
                        *val = v;
                }

                Sv.sv_player->V.AmmoShells = v;
                break;
            case 'n':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_nails1"u8);
                    if (val != null)
                    {
                        *val = v;
                        if (Sv.sv_player->V.Weapon <= IT_LIGHTNING)
                            Sv.sv_player->V.AmmoNails = v;
                    }
                }
                else
                {
                    Sv.sv_player->V.AmmoNails = v;
                }
                break;
            case 'l':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_lava_nails"u8);
                    if (val != null)
                    {
                        *val = v;
                        if (Sv.sv_player->V.Weapon > IT_LIGHTNING)
                            Sv.sv_player->V.AmmoNails = v;
                    }
                }
                break;
            case 'r':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_rockets1"u8);
                    if (val != null)
                    {
                        *val = v;
                        if (Sv.sv_player->V.Weapon <= IT_LIGHTNING)
                            Sv.sv_player->V.AmmoRockets = v;
                    }
                }
                else
                {
                    Sv.sv_player->V.AmmoRockets = v;
                }
                break;
            case 'm':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_multi_rockets"u8);
                    if (val != null)
                    {
                        *val = v;
                        if (Sv.sv_player->V.Weapon > IT_LIGHTNING)
                            Sv.sv_player->V.AmmoRockets = v;
                    }
                }
                break;
            case 'h':
                Sv.sv_player->V.Health = v;
                break;
            case 'c':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_cells1"u8);
                    if (val != null)
                    {
                        *val = v;
                        if (Sv.sv_player->V.Weapon <= IT_LIGHTNING)
                            Sv.sv_player->V.AmmoCells = v;
                    }
                }
                else
                {
                    Sv.sv_player->V.AmmoCells = v;
                }
                break;
            case 'p':
                if (Com.rogue)
                {
                    float* val = EdictSerialization.GetEdictFieldValue(Sv.sv_player, "ammo_plasma"u8);
                    if (val != null)
                    {
                        *val = v;
                        if (Sv.sv_player->V.Weapon > IT_LIGHTNING)
                            Sv.sv_player->V.AmmoCells = v;
                    }
                }
                break;
        }
    }

    /*
    ==================
    Host_Kick_f

    Kicks a user off of the server
    ==================
    */
    public static void Kick_f()
    {
        byte* message = null;
        bool byNumber = false;

        if (Cmd.cmd_source == CmdSource_t.SrcCommand)
        {
            if (!Sv.active)
            {
                Cmd.ForwardToServer();
                return;
            }
        }
        else if (Progs.pr_global_struct->DeathMatch != 0 && !Svs.host_client->Privileged)
            return;

        Client_t* save = Svs.host_client;

        int i;
        if (Cmd.Argc() > 2 && Q.StrCmp(Cmd.Argv(1), "#"u8) == 0)
        {
            i = (int)Q.Atof(Cmd.Argv(2)) - 1;
            if (i < 0 || i >= Svs.maxclients)
                return;
            if (!Svs.clients[i].Active)
                return;
            Svs.host_client = &Svs.clients[i];
            byNumber = true;
        }
        else
        {
            for (i = 0, Svs.host_client = Svs.clients; i < Svs.maxclients; i++, Svs.host_client++)
            {
                if (!Svs.host_client->Active)
                    continue;
                if (Q.StrCaseCmp(Svs.host_client->Name, Cmd.Argv(1)) == 0)
                    break;
            }
        }

        if (i < Svs.maxclients)
        {
            string who;
            if (Cmd.cmd_source == CmdSource_t.SrcCommand)
                who = Cls.state == CActive.Dedicated ? "Console" : CStr.ToString(Cl.cl_name->String);
            else
                who = CStr.ToString(save->Name);

            // can't kick yourself!
            if (Svs.host_client == save)
                return;

            if (Cmd.Argc() > 2)
            {
                byte* msg = Com.Parse(Cmd.Args());
                if (byNumber)
                {
                    msg++; // skip the #
                    while (*msg == (byte)' ') // skip white space
                        msg++;
                    msg += Q.StrLen(Cmd.Argv(2)); // skip the number
                }
                while (*msg != 0 && *msg == (byte)' ')
                    msg++;
                message = msg;
            }
            if (message != null)
                Host.ClientPrintf($"Kicked by {who}: {message}\n");
            else
                Host.ClientPrintf($"Kicked by {who}\n");
            Host.DropClient(false);
        }

        Svs.host_client = save;
    }

    // FindViewthing -- scans for the "viewthing" entity the viewmodel/
    // viewframe/viewnext/viewprev commands below inspect (dropped into a map
    // via the "viewthing.mdl"-holding test maps id1's original tools used).
    private static EDict_t* FindViewthing()
    {
        for (int i = 0; i < Sv.num_edicts; i++)
        {
            EDict_t* e = Progs.EdictNum(i);
            if (Q.StrCmp(Progs.pr_strings + e->V.ClassName, "viewthing"u8) == 0)
                return e;
        }
        Con.Printf("No viewthing on map\n"u8);
        return null;
    }

    /*
    ==================
    Host_Viewmodel_f
    ==================
    */
    public static void Viewmodel_f()
    {
        EDict_t* e = FindViewthing();
        if (e == null)
            return;

        Model_t* m = Model.ForName(Cmd.Argv(1), false);
        if (m == null)
        {
            Con.Printf($"Can't load {Cmd.Argv(1)}\n");
            return;
        }

        e->V.Frame = 0;
        Cl.model_precache[(int)e->V.ModelIndex] = m;
    }

    /*
    ==================
    Host_Viewframe_f
    ==================
    */
    public static void Viewframe_f()
    {
        EDict_t* e = FindViewthing();
        if (e == null)
            return;
        Model_t* m = Cl.model_precache[(int)e->V.ModelIndex];

        int f = Q.Atoi(Cmd.Argv(1));
        if (f >= m->NumFrames)
            f = m->NumFrames - 1;

        e->V.Frame = f;
    }

    private static void PrintFrameName(Model_t* m, int frame)
    {
        AliasHdr_t* hdr = (AliasHdr_t*)Model.Extradata(m);
        if (hdr == null)
            return;
        MAliasFrameDesc_t* pframedesc = &hdr->Frames + frame;

        Con.Printf($"frame {frame}: {pframedesc->Name}\n");
    }

    /*
    ==================
    Host_Viewnext_f
    ==================
    */
    public static void Viewnext_f()
    {
        EDict_t* e = FindViewthing();
        if (e == null)
            return;
        Model_t* m = Cl.model_precache[(int)e->V.ModelIndex];

        e->V.Frame = e->V.Frame + 1;
        if (e->V.Frame >= m->NumFrames)
            e->V.Frame = m->NumFrames - 1;

        PrintFrameName(m, (int)e->V.Frame);
    }

    /*
    ==================
    Host_Viewprev_f
    ==================
    */
    public static void Viewprev_f()
    {
        EDict_t* e = FindViewthing();
        if (e == null)
            return;

        Model_t* m = Cl.model_precache[(int)e->V.ModelIndex];

        e->V.Frame = e->V.Frame - 1;
        if (e->V.Frame < 0)
            e->V.Frame = 0;

        PrintFrameName(m, (int)e->V.Frame);
    }

    /*
    ===============================================================================

    DEMO LOOP CONTROL

    ===============================================================================
    */

    /*
    ==================
    Host_Startdemos_f
    ==================
    */
    public static void Startdemos_f()
    {
        if (Cls.state == CActive.Dedicated)
        {
            if (!Sv.active)
                Cmd.AddText("map start\n"u8);
            return;
        }

        int c = Cmd.Argc() - 1;
        if (c > ClDemo.MAX_DEMOS)
        {
            Con.Printf($"Max {ClDemo.MAX_DEMOS} demos in demoloop\n");
            c = ClDemo.MAX_DEMOS;
        }
        Con.Printf($"{c} demo(s) in loop\n");

        for (int i = 1; i < c + 1; i++)
            Q.StrNCpy(ClDemo.demos + (i - 1) * ClDemo.MAX_DEMONAME, Cmd.Argv(i), ClDemo.MAX_DEMONAME - 1);

        if (!Sv.active && ClDemo.demonum != -1 && !ClDemo.demoplayback)
        {
            ClDemo.demonum = 0;
            ClDemo.NextDemo();
        }
        else
            ClDemo.demonum = -1;
    }

    /*
    ==================
    Host_Demos_f

    Return to looping demos
    ==================
    */
    public static void Demos_f()
    {
        if (Cls.state == CActive.Dedicated)
            return;
        if (ClDemo.demonum == -1)
            ClDemo.demonum = 1;
        ClMain.Disconnect_f();
        ClDemo.NextDemo();
    }

    /*
    ==================
    Host_Stopdemo_f

    Return to looping demos
    ==================
    */
    public static void Stopdemo_f()
    {
        if (Cls.state == CActive.Dedicated)
            return;
        if (!ClDemo.demoplayback)
            return;
        ClDemo.StopPlayback();
        ClMain.Disconnect();
    }

    public static void InitCommands()
    {
        Cmd.AddCommand("map"u8, &MapCmd);
        Cmd.AddCommand("connect"u8, &ConnectCmd);
        Cmd.AddCommand("reconnect"u8, &ReconnectCmd);
        Cmd.AddCommand("changelevel"u8, &ChangelevelCmd);
        Cmd.AddCommand("restart"u8, &RestartCmd);
        Cmd.AddCommand("quit"u8, &QuitCmd);
        Cmd.AddCommand("name"u8, &NameCmd);
        Cmd.AddCommand("color"u8, &ColorCmd);
        Cmd.AddCommand("spawn"u8, &SpawnCmd);
        Cmd.AddCommand("begin"u8, &BeginCmd);
        Cmd.AddCommand("prespawn"u8, &PreSpawnCmd);
        Cmd.AddCommand("save"u8, &SaveGame.Savegame_f);
        Cmd.AddCommand("load"u8, &SaveGame.Loadgame_f);
        Cmd.AddCommand("god"u8, &God_f);
        Cmd.AddCommand("notarget"u8, &Notarget_f);
        Cmd.AddCommand("noclip"u8, &Noclip_f);
        Cmd.AddCommand("fly"u8, &Fly_f);
        Cmd.AddCommand("kill"u8, &Kill_f);
        Cmd.AddCommand("pause"u8, &Pause_f);
        Cmd.AddCommand("give"u8, &Give_f);
        Cmd.AddCommand("version"u8, &Version_f);

        Cmd.AddCommand("status"u8, &Status_f);
        Cmd.AddCommand("ping"u8, &Ping_f);
        Cmd.AddCommand("say"u8, &Say_f);
        Cmd.AddCommand("say_team"u8, &Say_Team_f);
        Cmd.AddCommand("tell"u8, &Tell_f);
        Cmd.AddCommand("kick"u8, &Kick_f);

        Cmd.AddCommand("startdemos"u8, &Startdemos_f);
        Cmd.AddCommand("demos"u8, &Demos_f);
        Cmd.AddCommand("stopdemo"u8, &Stopdemo_f);

        Cmd.AddCommand("viewmodel"u8, &Viewmodel_f);
        Cmd.AddCommand("viewframe"u8, &Viewframe_f);
        Cmd.AddCommand("viewnext"u8, &Viewnext_f);
        Cmd.AddCommand("viewprev"u8, &Viewprev_f);

        Cmd.AddCommand("mcache"u8, &Model.Print);
    }
}
