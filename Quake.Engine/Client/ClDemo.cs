// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// cl_demo.c -- demo file record/write/playback only.
/// </summary>
public static unsafe class ClDemo
{

    // MAX_DEMOS / MAX_DEMONAME (client.h)
    public const int MAX_DEMOS = 8;
    public const int MAX_DEMONAME = 16;

    // cls.demofile -- a Sys file handle stands in for the original's FILE*;
    // -1 when no demo is open (matching Sys.FileOpenRead/Write's failure code).
    public static int demofile = -1;

    // cls.demoplayback
    public static bool demoplayback;

    // cls.demorecording
    public static bool demorecording;

    // cls.forcetrack
    public static int forcetrack;

    // cls.demonum -- index into the -1-terminated demo-cycle list set by
    // startdemos; menu.c's M_Menu_Main_f/M_Main_Key are the earliest ported
    // consumers (they save/restore it while the menu is up). The actual
    // menu.c's M_Menu_Main_f/M_Main_Key save/restore it while the menu is up;
    // NextDemo below (CL_NextDemo, cl_main.c) now drives it too.
    public static int demonum;

    // cls.demos[MAX_DEMOS][MAX_DEMONAME]
    public static readonly byte* demos =
        NativeMemory.AllocArrayZeroed<byte>(MAX_DEMOS * MAX_DEMONAME);


    /*
    ==============
    CL_StopPlayback

    Called when a demo file runs out, or the user starts a game
    ==============
    */
    public static void StopPlayback()
    {
        if (!demoplayback)
            return;

        Sys.FileClose(demofile);
        demoplayback = false;
        demofile = -1;
        Cls.state = CActive.Disconnected;
        if (Cls.timedemo)
            FinishTimeDemo();
    }

    /*
    =====================
    CL_NextDemo

    Called to play the next demo in the demo loop
    =====================
    */
    public static void NextDemo()
    {
        if (demonum == -1)
            return; // don't play demos

        Screen.BeginLoadingPlaque();

        byte* current = demos + demonum * MAX_DEMONAME;
        if (*current == 0 || demonum == MAX_DEMOS)
        {
            demonum = 0;
            current = demos;
            if (*current == 0)
            {
                Con.Printf("No demos listed with startdemos\n"u8);
                demonum = -1;
                return;
            }
        }

        byte* str = stackalloc byte[1024];
        Q.StrCpy(str, "playdemo "u8);
        Q.StrCat(str, current);
        Q.StrCat(str, "\n"u8);
        Cmd.InsertText(str);
        demonum++;
    }

    /*
    ====================
    CL_WriteDemoMessage

    Dumps the current net message, prefixed by the length and view angles
    ====================
    */
    public static void WriteDemoMessage()
    {
        int len = Com.LittleLong(Net.net_message->CurSize);
        Sys.FileWrite(demofile, &len, 4);
        for (int i = 0; i < 3; i++)
        {
            float f = Com.LittleFloat(Cl.viewangles[i]);
            Sys.FileWrite(demofile, &f, 4);
        }
        Sys.FileWrite(demofile, Net.net_message->Data, Net.net_message->CurSize);
        // fflush(cls.demofile) omitted: Sys.cs exposes no flush primitive over
        // its FileStream handles; the stream flushes on Sys.FileClose.
    }

    /*
    ====================
    CL_GetMessage

    Handles recording and playback of demos, on top of NET_ code.
    ====================
    */
    public static int GetMessage()
    {
        if (demoplayback)
        {
            // decide if it is time to grab the next message
            if (Cls.signon == Cls.SIGNONS) // always grab until fully connected
            {
                if (Cls.timedemo)
                {
                    if (Host.host_framecount == Cls.td_lastframe)
                        return 0; // already read this frame's message
                    Cls.td_lastframe = Host.host_framecount;
                    // if this is the second frame, grab the real td_starttime
                    // so the bogus time on the first frame doesn't count
                    if (Host.host_framecount == Cls.td_startframe + 1)
                        Cls.td_starttime = (float)Host.realtime;
                }
                else if (Cl.time <= Cl.mtime[0])
                    return 0; // don't need another message yet
            }

            int cursize;
            Sys.FileRead(demofile, &cursize, 4);

            Cl.mviewangles[1] = Cl.mviewangles[0];
            for (int i = 0; i < 3; i++)
            {
                float f;
                Sys.FileRead(demofile, &f, 4);
                Cl.mviewangles[0][i] = Com.LittleFloat(f);
            }

            Net.net_message->CurSize = Com.LittleLong(cursize);
            if (Net.net_message->CurSize > MAX_MSGLEN)
                Sys.Error("Demo message > MAX_MSGLEN"u8);

            int r = Sys.FileRead(demofile, Net.net_message->Data, Net.net_message->CurSize);
            if (r != Net.net_message->CurSize)
            {
                StopPlayback();
                return 0;
            }

            return 1;
        }

        int ret;
        while (true)
        {
            ret = Net.GetMessage(Cls.netcon);

            if (ret != 1 && ret != 2)
                return ret;

            // discard nop keepalive message
            if (Net.net_message->CurSize == 1 && Net.net_message->Data[0] == (int)Svc.Nop)
                Con.Printf("<-- server to client keepalive\n"u8);
            else
                break;
        }

        if (demorecording)
            WriteDemoMessage();

        return ret;
    }

    /*
    ====================
    CL_Stop_f

    stop recording a demo
    ====================
    */
    public static void Stop_f()
    {
        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        if (!demorecording)
        {
            Con.Printf("Not recording a demo.\n"u8);
            return;
        }

        // write a disconnect message to the demo file
        SizeBuf_t* pMsg = Net.net_message;
        Sz.Clear(pMsg);
        Msg.WriteByte(pMsg, (int)Svc.Disconnect);
        WriteDemoMessage();

        // finish up
        Sys.FileClose(demofile);
        demofile = -1;
        demorecording = false;
        Con.Printf("Completed demo\n"u8);
    }

    /*
    ====================
    CL_Record_f

    record <demoname> <map> [cd track]
    ====================
    */
    public static void Record_f()
    {
        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        int c = Cmd.Argc();
        if (c != 2 && c != 3 && c != 4)
        {
            Con.Printf("record <demoname> [<map> [cd track]]\n"u8);
            return;
        }

        if (CStr.ToString(Cmd.Argv(1)).Contains(".."))
        {
            Con.Printf("Relative pathnames are not allowed.\n"u8);
            return;
        }

        if (Cls.state == CActive.Connected)
        {
            Con.Printf("Can not record - already connected to server\nClient demo recording must be started before connecting\n"u8);
            return;
        }

        int track;
        if (c == 4)
        {
            track = Q.Atoi(Cmd.Argv(3));
            Con.Printf($"Forcing CD track to {forcetrack}\n");
        }
        else
            track = -1;

        byte* name = stackalloc byte[QuakeDef.MAX_OSPATH];
        Q.StrCpy(name, Com.com_gamedir);
        Q.StrCat(name, "/"u8);
        Q.StrCat(name, Cmd.Argv(1));

        // start the map up
        if (c > 2)
        {
            byte* mapCmd = stackalloc byte[QuakeDef.MAX_OSPATH];
            Q.StrCpy(mapCmd, "map "u8);
            Q.StrCat(mapCmd, Cmd.Argv(2));
            Cmd.ExecuteString(mapCmd, CmdSource_t.SrcCommand);
        }

        // open the demo file
        Com.DefaultExtension(name, ".dem"u8);

        Con.Printf($"recording to {name}.\n");
        demofile = Sys.FileOpenWrite(name);
        // Sys.FileOpenWrite throws QuakeSysErrorException on failure rather
        // than returning -1 like the original fopen, so this branch is
        // effectively unreachable; kept for structural fidelity.
        if (demofile < 0)
        {
            Con.Printf("ERROR: couldn't open.\n"u8);
            return;
        }

        forcetrack = track;
        Utf8StringHandler trackLine = $"{forcetrack}\n";
        Sys.FileWrite(demofile, trackLine.GetWrittenBufferPtr(), trackLine.GetWrittenBuffer().Length);

        demorecording = true;
    }

    /*
    ====================
    CL_PlayDemo_f

    play [demoname]
    ====================
    */
    public static void PlayDemo_f()
    {
        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        if (Cmd.Argc() != 2)
        {
            Con.Printf("play <demoname> : plays a demo\n"u8);
            return;
        }

        ClMain.Disconnect();

        byte* name = stackalloc byte[256];
        Q.StrCpy(name, Cmd.Argv(1));
        Com.DefaultExtension(name, ".dem"u8);

        Con.Printf($"Playing demo from {name}.\n");
        nint handle;
        Com.FOpenFile(name, &handle);
        demofile = (int)handle;
        if (demofile < 0)
        {
            Con.Printf("ERROR: couldn't open.\n"u8);
            demonum = -1;
            return;
        }

        demoplayback = true;
        Cls.state = CActive.Connected;
        forcetrack = 0;

        bool neg = false;
        int c;
        while ((c = ReadDemoByte()) != '\n')
        {
            if (c == '-')
                neg = true;
            else
                forcetrack = forcetrack * 10 + (c - '0');
        }

        if (neg)
            forcetrack = -forcetrack;
        // ZOID, fscanf is evil
    }

    /*
    ====================
    CL_FinishTimeDemo
    ====================
    */
    public static void FinishTimeDemo()
    {
        Cls.timedemo = false;

        // the first frame didn't count
        int frames = (Host.host_framecount - Cls.td_startframe) - 1;
        float time = (float)(Host.realtime - Cls.td_starttime);
        if (time == 0)
            time = 1;
        Con.Printf($"{frames} frames {time,5:F1} seconds {frames / time,5:F1} fps\n");
    }

    /*
    ====================
    CL_TimeDemo_f

    timedemo [demoname]
    ====================
    */
    public static void TimeDemo_f()
    {
        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        if (Cmd.Argc() != 2)
        {
            Con.Printf("timedemo <demoname> : gets demo speeds\n"u8);
            return;
        }

        PlayDemo_f();

        // cls.td_starttime will be grabbed at the second frame of the demo, so
        // all the loading time doesn't get counted
        Cls.timedemo = true;
        Cls.td_startframe = Host.host_framecount;
        Cls.td_lastframe = -1; // get a new message this frame
    }

    private static int ReadDemoByte()
    {
        byte b;
        Sys.FileRead(demofile, &b, 1);
        return b;
    }
}
