// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// host.c -- the central orchestrator. Host_Init/Host_Frame are both fully
/// wired now: everything up to and including SV_Init/Menu.Init/Sbar.Init/
/// CdAudio.Init/Net.Init/ClMain.Init, plus VID_Init/IN_Init (via Init's
/// optional `videoInit` callback -- see its own comment) and CL_SendCmd/
/// CL_ReadFromServer (see _Host_Frame's own comment) is wired here in the
/// original's exact call order. S_Init is now wired too (SndDma.cs's mixer
/// + SndHost.cs's OpenTK/OpenAL device layer), called right before
/// CdAudio.Init() per sys_win.c's WinMain. Host_InitCommands is ported in
/// HostCmd.cs. Deliberately NOT ported: Host_InitVCR (VCR playback/record --
/// a debug feature; every -playback/-record code path is skipped, matching
/// this port's already-established pattern of dropping debug-only tooling).
/// host_basepal/host_colormap ARE loaded here (R_InitTextures and the
/// palette/colormap lumps are needed the moment any drawing happens).
/// SV_ClientPrintf/SV_BroadcastPrintf (also host.c) are ported too, for the
/// "god"/"notarget"/"noclip"/"fly"/"kill"/"give"/"pause" cheat commands
/// HostCmd.cs now registers.
/// Host_Error/Host_EndGame are ported too, throwing QuakeHostAbortException
/// (this port's setjmp(host_abortserver)/longjmp equivalent, see that
/// exception's own comment) instead of a graceful longjmp; every former
/// "Host_Error substitute" call site across the port (ClMain.cs/ClParse.cs/
/// EdictSerialization.cs/Progs.cs) now calls Host.Error for real, matching
/// the original's Host_Error vs Sys_Error split call-by-call.
/// </summary>
public static unsafe partial class Host
{
    public static readonly Cvar_t* host_framerate = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* host_speeds = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* sys_ticrate = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* serverprofile = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* fraglimit = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* timelimit = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* teamplay = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* samelevel = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* noexit = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* developer = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* skill = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* deathmatch = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* coop = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* pausable = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* temp1 = NativeMemory.AllocZeroed<Cvar_t>();

    // host_colormap -- host.c's loaded gfx/colormap.lmp, alongside
    // RMisc.host_basepal (gfx/palette.lmp).
    public static byte* host_colormap;

    public static bool host_initialized;

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false, bool server = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        cvar->Server = server;
        Cvar.RegisterVariable(cvar);
    }

    /*
    ================
    Host_FindMaxClients

    -dedicated/-listen really do enable multiplayer (Net.cs's NetIp/NetDgrm)
    -- svs.maxclients can be >1, clamped to MAX_SCOREBOARD
    (QuakeDef.MAX_SCOREBOARD) same as the original.
    ================
    */
    public static void FindMaxClients()
    {
        Svs.maxclients = 1;

        int i = Com.CheckParm("-dedicated"u8);
        if (i != 0)
        {
            Cls.state = CActive.Dedicated;
            if (i != Com.com_argc - 1)
                Svs.maxclients = Q.Atoi(Com.com_argv[i + 1]);
            else
                Svs.maxclients = 8;
        }
        else
            Cls.state = CActive.Disconnected;

        i = Com.CheckParm("-listen"u8);
        if (i != 0)
        {
            if (Cls.state == CActive.Dedicated)
                Sys.Error("Only one of -dedicated or -listen can be specified"u8);
            if (i != Com.com_argc - 1)
                Svs.maxclients = Q.Atoi(Com.com_argv[i + 1]);
            else
                Svs.maxclients = 8;
        }

        if (Svs.maxclients < 1)
            Svs.maxclients = 8;
        else if (Svs.maxclients > QuakeDef.MAX_SCOREBOARD)
            Svs.maxclients = QuakeDef.MAX_SCOREBOARD;

        Svs.maxclientslimit = Svs.maxclients < 4 ? 4 : Svs.maxclients;
        Svs.AllocateClients(); // svs.clients = Hunk_AllocName(...)

        if (Svs.maxclients > 1)
            Cvar.SetValue("deathmatch"u8, 1.0f);
        else
            Cvar.SetValue("deathmatch"u8, 0.0f);
    }

    /*
    ===============
    Host_InitLocal
    ===============
    */
    public static void InitLocal()
    {
        HostCmd.InitCommands(); // Host_InitCommands (host_cmd.c)

        RegisterCvar(host_framerate, "host_framerate"u8, "0"u8);
        RegisterCvar(host_speeds, "host_speeds"u8, "0"u8);

        RegisterCvar(sys_ticrate, "sys_ticrate"u8, "0.05"u8);
        RegisterCvar(serverprofile, "serverprofile"u8, "0"u8);

        RegisterCvar(fraglimit, "fraglimit"u8, "0"u8, server: true);
        RegisterCvar(timelimit, "timelimit"u8, "0"u8, server: true);
        RegisterCvar(teamplay, "teamplay"u8, "0"u8, server: true);
        RegisterCvar(samelevel, "samelevel"u8, "0"u8);
        RegisterCvar(noexit, "noexit"u8, "0"u8, server: true);
        RegisterCvar(skill, "skill"u8, "1"u8);
        RegisterCvar(developer, "developer"u8, "0"u8);
        RegisterCvar(deathmatch, "deathmatch"u8, "0"u8);
        RegisterCvar(coop, "coop"u8, "0"u8);

        RegisterCvar(pausable, "pausable"u8, "1"u8);

        RegisterCvar(temp1, "temp1"u8, "0"u8);

        FindMaxClients();

        // host_time = 1.0 (so a think at time 0 won't get called) -- already
        // HostParms.cs's field initializer default; not reassigned here.
    }

    /*
    ========================================================================
    Host_Init

    `videoInit`, if given, is invoked at the exact point host.c calls
    VID_Init/IN_Init (right before Draw_Init) -- optional so existing
    callers (HostTests.cs, any dedicated-server-shaped caller) keep working
    unchanged with no display backend at all.
    ========================================================================
    */
    public static void Init(void* memBase, int memSize, Action? videoInit = null)
    {
        MemoryManager.Memory_Init(memBase, memSize);
        Cmd.BufInit();      // Cbuf_Init
        Cmd.Init();
        View.Init();
        Chase.Init();
        // Host_InitVCR -- deferred (debug-only, see type-level comment).
        Com.Init();
        InitLocal();
        Wad.LoadWadFile("gfx.wad"u8);
        Keys.Init();
        Con.Init();
        Menu.Init();
        Progs.Init(); // PR_Init
        Model.Init(); // Mod_Init
        Net.Init();   // NET_Init (loopback-only, per this project's scope)
        Sv.Init();    // SV_Init

        RMain.InitTextures(); // needed even for dedicated servers

        if (Cls.state != CActive.Dedicated)
        {
            RMisc.host_basepal = (byte*)Com.LoadHunkFile("gfx/palette.lmp"u8);
            if (RMisc.host_basepal == null)
                Sys.Error("Couldn't load gfx/palette.lmp"u8);

            host_colormap = (byte*)Com.LoadHunkFile("gfx/colormap.lmp"u8);
            if (host_colormap == null)
                Sys.Error("Couldn't load gfx/colormap.lmp"u8);

            // IN_Init/VID_Init -- OpenTK display layer, per open_tk.md.
            // Host_Init has no display-backend knowledge of its own (Rule 5);
            // Quake.Desktop passes its window/framebuffer setup in as this
            // delegate, invoked at the exact call site host.c itself calls
            // VID_Init (right before Draw_Init), keeping this method's call
            // order byte-for-byte faithful to the original regardless of
            // which display backend (or none, e.g. HostTests.cs) is plugged
            // in.
            videoInit?.Invoke();

            Draw.Init();
            Screen.Init();
            RMain.Init();

            // S_Init/CDAudio_Init -- SndHost.cs's OpenTK/OpenAL device layer
            // is wired up here; call order matches sys_win.c's WinMain
            // (S_Init right before CDAudio_Init).
            SndDma.Init();
            CdAudio.Init();

            Sbar.Init();

            ClMain.Init();
        }

        // Cbuf_InsertText("exec quake.rc\n") -- quake.rc (shipped in pak0.pak,
        // like default.cfg) is what chains exec default.cfg/config.cfg/
        // autoexec.cfg + stuffcmds; Cmd.Exec_f/StuffCmds_f are both ported
        // (Cmd.cs) and silently no-op on a missing file, matching the
        // original, so this is safe to issue even before any of those files
        // exist on disk. Its final "startdemos demo1 demo2 demo3\n" line --
        // the reason a freshly launched original Quake starts playing a demo
        // instead of sitting idle -- is issued by Program.cs right after
        // this method returns (its own comment explains why it lives there
        // instead of here).
        Cmd.InsertText("exec quake.rc\n"u8);

        Hunk.AllocName(0, "-HOST_HUNKLEVEL-"u8);
        host_hunklevel = Hunk.LowMark();

        host_initialized = true;
    }

    // host_hunklevel -- host.c's own global: the Hunk low-mark captured at
    // the end of Host_Init, so Host.ClearMemory() below knows how far back
    // it can safely free per-level Hunk allocations (SV_SpawnServer's
    // edicts/name buffers, Model.ForName's loaded BSPs, ...) without
    // clobbering anything Host_Init itself allocated.
    public static int host_hunklevel;

    /*
    ================
    Host_ClearMemory

    This clears all the memory used by both the client and server, but does
    not reinitialize anything.
    ================
    */
    public static void ClearMemory()
    {
        Con.DPrintf("Clearing memory\n"u8);
        DSurf.FlushCaches();
        Model.ClearAll();
        if (host_hunklevel != 0)
            Hunk.FreeToLowMark(host_hunklevel);

        Cls.signon = 0;
        // memset(&sv, 0, sizeof(sv))/memset(&cl, 0, sizeof(cl)) -- there's no
        // single `sv`/`cl` struct in this port to memset; Sv's own
        // ResetState() (called from SpawnServer, its only caller) covers the
        // server half. The client half is ClMain.ResetClientState() (shared
        // with CL_ClearState): without it, a level change driven by
        // SV_SpawnServer (which calls Host_ClearMemory directly, bypassing
        // CL_ClearState) left Cl.worldmodel pointing at the just-freed Hunk
        // memory of the previous map, which SndDma.UpdateAmbientSounds then
        // dereferenced via Model.PointInLeaf.
        ClMain.ResetClientState();

        // No original equivalent (rule 8): the music decoder (NVorbis, managed)
        // leaves a track's worth of garbage behind; collect it here, during a
        // map change, where a GC pause is invisible, rather than mid-gameplay.
        GC.Collect(2, GCCollectionMode.Optimized, true, false);
    }

    /*
    =================
    SV_ClientPrintf

    Sends text across to be displayed
    FIXME (original comment): make this just a stuffed echo?
    =================
    */
    public static void ClientPrintf([InterpolatedStringHandlerArgument] scoped Utf8StringHandler u8String)
    {
        ClientPrintf(u8String.GetWrittenBuffer());
    }

    // SV_ClientPrintf for a "..."u8 literal (no format arguments).
    public static void ClientPrintf(scoped ReadOnlySpan<byte> u8String)
    {
        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.Print);
        Msg.WriteString(&Svs.host_client->Message, u8String);
    }

    /*
    =================
    Host_ClientCommands

    Send text over to the client to be executed
    =================
    */
    public static void ClientCommands(byte* str)
    {
        Msg.WriteByte(&Svs.host_client->Message, (int)Svc.StuffText);
        Msg.WriteString(&Svs.host_client->Message, str);
    }

    /*
    =================
    SV_BroadcastPrintf

    Sends text to all active clients
    =================
    */
    public static void BroadcastPrintf([InterpolatedStringHandlerArgument] scoped Utf8StringHandler u8String)
    {
        BroadcastPrintf(u8String.GetWrittenBuffer());
    }

    // SV_BroadcastPrintf for a "..."u8 literal (no format arguments).
    public static void BroadcastPrintf(scoped ReadOnlySpan<byte> u8String)
    {
        for (int i = 0; i < Svs.maxclients; i++)
        {
            if (Svs.clients[i].Active && Svs.clients[i].Spawned)
            {
                Msg.WriteByte(&Svs.clients[i].Message, (int)Svc.Print);
                Msg.WriteString(&Svs.clients[i].Message, u8String);
            }
        }
    }

    // host_abortserver (host.c's setjmp(host_abortserver)/longjmp(host_abortserver,1)
    // pair) -- the setjmp/longjmp crash-recovery idiom doesn't translate to
    // C# directly; Host_Error/Host_EndGame below throw this to unwind out of
    // the current frame, and Frame() below catches it exactly where the
    // original's setjmp recovers.
    public sealed class QuakeHostAbortException : Exception;

    // static qboolean inerror (Host_Error's file-local recursion guard) --
    // internal (not private) so Quake.UnitTests (InternalsVisibleTo) can
    // reset it between tests exercising the dedicated-server branch, which
    // never reaches this port's `inerror = false` reset (matching the
    // original: Sys_Error never returns there either).
    internal static bool inerror;

    /*
    ================
    Host_EndGame
    ================
    */
    // The message becomes a managed string only on this (abort) path.
    [DoesNotReturn]
    public static void EndGame([InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler message)
    {
        // Copy out first: the interpolated strings below share the message's
        // buffer and would overwrite it while it is being read.
        scoped ReadOnlySpan<byte> written = message.GetWrittenBuffer();
        Span<byte> copy = stackalloc byte[written.Length];
        written.CopyTo(copy);
        scoped ReadOnlySpan<byte> s = copy;
        Con.DPrintf($"Host_EndGame: {s}\n");

        if (Sv.active)
            ShutdownServer(false);

        if (Cls.state == CActive.Dedicated)
            Sys.Error($"Host_EndGame: {s}\n"); // dedicated servers exit

        if (ClDemo.demonum != -1)
            ClDemo.NextDemo();
        else
            ClMain.Disconnect();

        throw new QuakeHostAbortException();
    }

    /*
    ================
    Host_Error

    This shuts down both the client and server
    ================
    */
    [DoesNotReturn]
    public static void Error(scoped ReadOnlySpan<byte> s)
    {
        if (inerror)
            Sys.Error("Host_Error: recursively entered"u8);
        inerror = true;

        // The original clears inerror right before longjmp; here the throw
        // unwinds instead, so try/finally guarantees it is cleared even if
        // ShutdownServer/Disconnect throw (otherwise the next error would be
        // a fatal "recursively entered").
        try
        {
            Screen.EndLoadingPlaque(); // reenable screen updates

            /*
            // Copy out first: the interpolated strings below share the
            // message's buffer and would overwrite it while it is being read.
            scoped ReadOnlySpan<byte> written = error.GetWrittenBuffer();
            Span<byte> copy = stackalloc byte[written.Length];
            written.CopyTo(copy);
            scoped ReadOnlySpan<byte> s = copy;
            */
            Con.Printf($"Host_Error: {s}\n");

            if (Sv.active)
                ShutdownServer(false);

            if (Cls.state == CActive.Dedicated)
                Sys.Error($"Host_Error: {s}\n"); // dedicated servers exit

            ClMain.Disconnect();
            ClDemo.demonum = -1;
        }
        finally
        {
            inerror = false;
        }

        throw new QuakeHostAbortException();
    }

    [DoesNotReturn]
    public static void Error([InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler error)
    {
        if (inerror)
            Sys.Error("Host_Error: recursively entered"u8);
        inerror = true;

        // The original clears inerror right before longjmp; here the throw
        // unwinds instead, so try/finally guarantees it is cleared even if
        // ShutdownServer/Disconnect throw (otherwise the next error would be
        // a fatal "recursively entered").
        try
        {
            Screen.EndLoadingPlaque(); // reenable screen updates

            // Copy out first: the interpolated strings below share the
            // message's buffer and would overwrite it while it is being read.
            scoped ReadOnlySpan<byte> written = error.GetWrittenBuffer();
            Span<byte> copy = stackalloc byte[written.Length];
            written.CopyTo(copy);
            scoped ReadOnlySpan<byte> s = copy;
            Con.Printf($"Host_Error: {s}\n");

            if (Sv.active)
                ShutdownServer(false);

            if (Cls.state == CActive.Dedicated)
                Sys.Error($"Host_Error: {s}\n"); // dedicated servers exit

            ClMain.Disconnect();
            ClDemo.demonum = -1;
        }
        finally
        {
            inerror = false;
        }

        throw new QuakeHostAbortException();
    }

    /*
    ===================
    Host_FilterTime

    Returns false if the time is too short to run a frame
    ===================
    */
    public static bool FilterTime(float time)
    {
        realtime += time;

        if (!Cls.timedemo && realtime - oldrealtime < 1.0 / 72.0)
            return false; // framerate is too high

        host_frametime = (float)(realtime - oldrealtime);
        oldrealtime = realtime;

        if (host_framerate->Value > 0)
            host_frametime = host_framerate->Value;
        else
        { // don't allow really long or short frames
            if (host_frametime > 0.1f)
                host_frametime = 0.1f;
            if (host_frametime < 0.001f)
                host_frametime = 0.001f;
        }

        return true;
    }

    /*
    ===================
    Host_GetConsoleCommands

    Add them exactly as if they had been typed at the console
    ===================
    */
    public static void GetConsoleCommands()
    {
        while (true)
        {
            byte* cmd = Sys.ConsoleInput();
            if (cmd == null)
                break;
            Cmd.AddText(cmd);
        }
    }

    /*
    ==================
    Host_ServerFrame

    FPS_20 is dead code in the original (#ifdef FPS_20, never defined) --
    only the #else branch is ported.
    ==================
    */
    public static void ServerFrame()
    {
        // run the world state
        Progs.pr_global_struct->FrameTime = host_frametime;

        // set the time and clear the general datagram
        Sv.ClearDatagram();

        // check for new clients
        Sv.CheckForNewClients();

        // read client messages
        SvUser.RunClients();

        // move things around and think
        // always pause in single player if in console or menus
        if (!Sv.paused && (Svs.maxclients > 1 || Keys.key_dest == KeyDest.Game))
            SvPhys.Physics();

        // send all messages to the clients
        Sv.SendClientMessages();
    }

    /*
    =====================
    SV_DropClient

    Called when the player is getting totally kicked off the host.
    if (crash = true), don't bother sending signoffs
    =====================
    */
    public static void DropClient(bool crash)
    {
        if (!crash)
        {
            // send any final messages (don't check for errors)
            if (Net.CanSendMessage(Svs.host_client->NetConnection))
            {
                Msg.WriteByte(&Svs.host_client->Message, (int)Svc.Disconnect);
                Net.SendMessage(Svs.host_client->NetConnection, &Svs.host_client->Message);
            }

            if (Svs.host_client->Edict != null && Svs.host_client->Spawned)
            {
                // call the prog function for removing a client
                // this will set the body to a dead frame, among other things
                int saveSelf = Progs.pr_global_struct->Self;
                Progs.pr_global_struct->Self = Progs.EdictToProg(Svs.host_client->Edict);
                Progs.ExecuteProgram(Progs.pr_global_struct->ClientDisconnect);
                Progs.pr_global_struct->Self = saveSelf;
            }

            Con.Printf($"Client {Svs.host_client->Name} removed\n");
        }

        // break the net connection
        Net.Close(Svs.host_client->NetConnection);
        Svs.host_client->NetConnection = null;

        // free the client (the body stays around)
        Svs.host_client->Active = false;
        Svs.host_client->Name[0] = 0;
        Svs.host_client->OldFrags = -999999;
        Net.net_activeconnections--;

        // send notification to all clients
        for (int i = 0; i < Svs.maxclients; i++)
        {
            Client_t* client = &Svs.clients[i];
            if (!client->Active)
                continue;
            Msg.WriteByte(&client->Message, (int)Svc.UpdateName);
            Msg.WriteByte(&client->Message, (int)(Svs.host_client - Svs.clients));
            Msg.WriteString(&client->Message, null);
            Msg.WriteByte(&client->Message, (int)Svc.UpdateFrags);
            Msg.WriteByte(&client->Message, (int)(Svs.host_client - Svs.clients));
            Msg.WriteShort(&client->Message, 0);
            Msg.WriteByte(&client->Message, (int)Svc.UpdateColors);
            Msg.WriteByte(&client->Message, (int)(Svs.host_client - Svs.clients));
            Msg.WriteByte(&client->Message, 0);
        }
    }

    /*
    ==================
    Host_ShutdownServer

    This only happens at the end of a game, not between levels
    ==================
    */
    public static void ShutdownServer(bool crash)
    {
        if (!Sv.active)
            return;

        Sv.active = false;

        // stop all client sounds immediately
        if (Cls.state == CActive.Connected)
            ClMain.Disconnect();

        // flush any pending messages - like the score!!!
        double start = Sys.FloatTime();
        int count;
        do
        {
            count = 0;
            for (int i = 0; i < Svs.maxclients; i++)
            {
                Svs.host_client = &Svs.clients[i];
                if (Svs.host_client->Active && Svs.host_client->Message.CurSize != 0)
                {
                    if (Net.CanSendMessage(Svs.host_client->NetConnection))
                    {
                        Net.SendMessage(Svs.host_client->NetConnection, &Svs.host_client->Message);
                        Sz.Clear(&Svs.host_client->Message);
                    }
                    else
                    {
                        Net.GetMessage(Svs.host_client->NetConnection);
                        count++;
                    }
                }
            }
            if ((Sys.FloatTime() - start) > 3.0)
                break;
        } while (count != 0);

        // make sure all the clients know we're disconnecting
        byte* messageBuf = stackalloc byte[4];
        SizeBuf_t buf = default;
        buf.Data = messageBuf;
        buf.MaxSize = 4;
        buf.CurSize = 0;
        Msg.WriteByte(&buf, (int)Svc.Disconnect);
        count = Net.SendToAll(&buf, 5);
        if (count != 0)
            Con.Printf($"Host_ShutdownServer: NET_SendToAll failed for {count} clients\n");

        for (int i = 0; i < Svs.maxclients; i++)
        {
            Svs.host_client = &Svs.clients[i];
            if (Svs.host_client->Active)
                DropClient(crash);
        }

        // clear structures
        Sv.ResetState(); // memset(&sv, 0, sizeof(sv))
        Q.MemSet(Svs.clients, 0, Svs.maxclientslimit * sizeof(Client_t));
    }

    private static bool isdown;

    /*
    ===============
    Host_WriteConfiguration

    Writes key bindings and archived cvars to config.cfg
    ===============
    */
    public static void WriteConfiguration()
    {
        // dedicated servers never initialise the keys/cvars worth saving
        if (!host_initialized || Cls.state == CActive.Dedicated)
            return;

        byte* path = stackalloc byte[QuakeDef.MAX_OSPATH];
        Q.StrCpy(path, Com.com_gamedir);
        Q.StrCat(path, "/config.cfg"u8);

        int f = Sys.FileOpenWrite(path);
        Keys.WriteBindings(f);
        Cvar.WriteVariables(f);
        Sys.FileClose(f);
    }

    /*
    ===============
    Host_Shutdown

    FIXME: this is a callback from Sys_Quit and Sys_Error. It would be better
    to run quit through here before the final handoff to the sys code.

    VID_Shutdown/IN_Shutdown are Quake.Desktop's job (VidWindow.Shutdown,
    called from Program.cs's main loop after this returns) per Rule 5 -- this
    class stays platform-plumbing-free.
    ===============
    */
    public static void Shutdown()
    {
        if (isdown)
        {
            Con.Printf("recursive shutdown\n"u8);
            return;
        }
        isdown = true;

        // Not in the original: a failure writing config.cfg (read-only game
        // directory, etc.) must not skip the remaining teardown or turn a
        // normal quit into a fatal error. Sys.Error throws here rather than
        // exiting, so it is caught along with the raw .NET IO exceptions.
        try
        {
            WriteConfiguration();
        }
        catch (Exception e)
        {
            Con.Printf($"Couldn't write config.cfg: {e.Message}\n");
        }

        CdAudio.Shutdown();
        Net.Shutdown();
        SndDma.Shutdown();
    }

    /*
    ==================
    Host_Frame

    Runs all active servers. Sys_SendKeyEvents/IN_Commands (Quake.Desktop's
    window-event pump and its own equivalent of "flush queued input into
    ClInput's kbutton_t state", per open_tk.md) happen in Quake.Desktop's
    Program.cs before this is called each frame, not inside this method --
    host.c's WinMain-equivalent loop driver is Quake.Desktop's job now, not
    Host.cs's (Rule 5: this class stays platform-plumbing-free).
    ==================
    */
    public static void _Host_Frame(float time)
    {
        // rand() -- "keep the random time dependent"; nothing in this port
        // reads the C runtime's global rand() state, so this call has no
        // observable effect and is dropped rather than ported.

        // decide the simulation time
        if (!FilterTime(time))
            return; // don't run too fast, or packets will flood out

        // process console commands
        Cmd.BufExecute();

        Net.Poll();

        // if running the server locally, make intentions now
        if (Sv.active)
            ClMain.SendCmd();

        //-------------------
        // server operations
        //-------------------

        GetConsoleCommands();

        if (Sv.active)
            ServerFrame();

        //-------------------
        // client operations
        //-------------------

        // if running the server remotely, send intentions now after the
        // incoming messages have been read
        if (!Sv.active)
            ClMain.SendCmd();

        host_time += host_frametime;

        // fetch results from server
        if (Cls.state == CActive.Connected)
            ClMain.ReadFromServer();

        // update video
        Screen.UpdateScreen();

        // update audio
        if (Cls.signon == Cls.SIGNONS)
        {
            SndDma.Update(RShared.r_origin, RShared.vpn, RShared.vright, RShared.vup);
            ClMain.DecayLights();
        }
        else
        {
            SndDma.Update(MathLib.vec3_origin, MathLib.vec3_origin, MathLib.vec3_origin, MathLib.vec3_origin);
        }

        CdAudio.Update();

        host_framecount++;
    }

    public static void Frame(float time)
    {
        try
        {
            _Host_Frame(time);
        }
        catch (QuakeHostAbortException)
        {
            // something bad happened, or the server disconnected
        }

        // serverprofile timing (host.c's Host_Frame wrapper) -- a debug-only
        // stat dump gated on the "serverprofile" cvar; dropped like this
        // port's other debug-only tooling (see Host.cs's type-level comment
        // on Host_InitVCR).
    }
}
