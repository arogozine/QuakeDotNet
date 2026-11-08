// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// host_cmd.c -- Host_Savegame_f / Host_Loadgame_f and Host_SavegameComment. Reads and
/// writes the real Sv/Svs/Cl state; owns host_cmd.c's file-scope current_skill.
/// </summary>
public static unsafe class SaveGame
{
    public const int SAVEGAME_VERSION = 5;

    // current_skill
    public static int current_skill;

    private static void WriteText(int fileHandle, [InterpolatedStringHandlerArgument] ref Utf8StringHandler utf8String)
    {
        Sys.FileWrite(fileHandle, utf8String.GetWrittenBuffer());
    }

    private static void WriteText(int fileHandle, scoped ReadOnlySpan<byte> text)
    {
        Sys.FileWrite(fileHandle, text);
    }

    // Host_SavegameComment
    public static void SavegameComment(byte* text)
    {
        for (int i = 0; i < SAVEGAME_COMMENT_LENGTH; i++)
            text[i] = (byte)' ';

        int levelLen = Q.StrLen(Cl.levelname);
        Q.MemCpy(text, Cl.levelname, levelLen);

        Utf8StringHandler kills = $"kills:{Cl.stats[(int)Stat.Monsters],3}/{Cl.stats[(int)Stat.TotalMonsters],3}";
        kills.GetWrittenBuffer().CopyTo(new Span<byte>(text + 22, kills.GetWrittenBuffer().Length));

        // convert space to _ to make stdio happy
        for (int i = 0; i < SAVEGAME_COMMENT_LENGTH; i++)
            if (text[i] == (byte)' ')
                text[i] = (byte)'_';
        text[SAVEGAME_COMMENT_LENGTH] = 0;
    }

    private static byte* SkipWhitespace(byte* p)
    {
        while (*p == ' ' || *p == '\n' || *p == '\r' || *p == '\t')
            p++;
        return p;
    }

    // Reads one fscanf("%s")-shaped token: skip leading whitespace, copy
    // until the next whitespace (or buffer limit).
    private static byte* ReadToken(byte* p, byte* dest, int maxLen)
    {
        p = SkipWhitespace(p);
        int i = 0;
        while (*p != 0 && *p != ' ' && *p != '\n' && *p != '\r' && *p != '\t' && i < maxLen - 1)
            dest[i++] = *p++;
        dest[i] = 0;
        return p;
    }

    private static byte* ReadInt(byte* p, out int value)
    {
        byte* token = stackalloc byte[32];
        p = ReadToken(p, token, 32);
        value = Q.Atoi(token);
        return p;
    }

    private static byte* ReadFloat(byte* p, out float value)
    {
        byte* token = stackalloc byte[32];
        p = ReadToken(p, token, 32);
        value = Q.Atof(token);
        return p;
    }

    // Host_Savegame_f
    public static void Savegame_f()
    {
        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        if (!Sv.active)
        {
            Con.Printf("Not playing a local game.\n"u8);
            return;
        }

        if (Cl.intermission != 0)
        {
            Con.Printf("Can't save in intermission.\n"u8);
            return;
        }

        if (Svs.maxclients != 1)
        {
            Con.Printf("Can't save multiplayer games.\n"u8);
            return;
        }

        if (Cmd.Argc() != 2)
        {
            Con.Printf("save <savename> : save a game\n"u8);
            return;
        }

        if (MemoryMarshal.CreateReadOnlySpanFromNullTerminated(Cmd.Argv(1)).ContainsAny(".."u8))
        {
            Con.Printf("Relative pathnames are not allowed.\n"u8);
            return;
        }

        for (int i = 0; i < Svs.maxclients; i++)
        {
            if (Svs.clients[i].Active && Svs.clients[i].Edict->V.Health <= 0)
            {
                Con.Printf("Can't savegame with a dead player\n"u8);
                return;
            }
        }

        byte* name = stackalloc byte[256];
        Q.StrCpy(name, Com.com_gamedir);
        Q.StrCat(name, "/"u8);
        Q.StrCat(name, Cmd.Argv(1));
        Com.DefaultExtension(name, ".sav"u8);

        Con.Printf($"Saving game to {name}...\n");
        int f = Sys.FileOpenWrite(name);
        // Sys.FileOpenWrite throws QuakeSysErrorException on failure rather
        // than returning -1 (see ClDemo.cs), so this branch is unreachable;
        // kept for structural fidelity.
        if (f < 0)
        {
            Con.Printf("ERROR: couldn't open.\n"u8);
            return;
        }

        WriteText(f, $"{SAVEGAME_VERSION}\n");

        byte* comment = stackalloc byte[SAVEGAME_COMMENT_LENGTH + 1];
        SavegameComment(comment);
        WriteText(f, $"{comment}\n");

        for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
            WriteText(f, $"{Svs.clients->spawn_parms[i]:F6}\n");
        WriteText(f, $"{current_skill}\n");
        WriteText(f, $"{Sv.Name}\n");
        WriteText(f, $"{Sv.time:F6}\n");

        // write the light styles
        for (int i = 0; i < MAX_LIGHTSTYLES; i++)
        {
            if (Sv.lightstyles[i] != null)
                WriteText(f, $"{Sv.lightstyles[i]}\n");
            else
                WriteText(f, "m\n"u8);
        }

        EdictSerialization.WriteGlobals(f);
        for (int i = 0; i < Sv.num_edicts; i++)
        {
            EdictSerialization.Write(f, Progs.EdictNum(i));
        }

        Sys.FileClose(f);
        Con.Printf("done.\n"u8);
    }

    // Host_Loadgame_f
    public static void Loadgame_f()
    {
        if (Cmd.cmd_source != CmdSource_t.SrcCommand)
            return;

        if (Cmd.Argc() != 2)
        {
            Con.Printf("load <savename> : load a game\n"u8);
            return;
        }

        ClDemo.demonum = -1; // stop demo loop in case this fails

        byte* name = stackalloc byte[QuakeDef.MAX_OSPATH];
        Q.StrCpy(name, Com.com_gamedir);
        Q.StrCat(name, "/"u8);
        Q.StrCat(name, Cmd.Argv(1));
        Com.DefaultExtension(name, ".sav"u8);

        Con.Printf($"Loading game from {name}...\n");

        int handle;
        int len = Sys.FileOpenRead(name, &handle);
        if (len < 0)
        {
            Con.Printf("ERROR: couldn't open.\n"u8);
            return;
        }

        // The original reads via fscanf directly off the FILE*; Sys.cs has no
        // buffered text-stream reader, so the whole file is slurped up front
        // and parsed from an in-memory cursor instead (fscanf's %s/%i/%f
        // whitespace-skipping semantics are replicated by hand above).
        byte* cp = (byte*)NativeMemory.Alloc((nuint)len + 1);
        try
        {
            Sys.FileRead(handle, cp, len);
            Sys.FileClose(handle);
            cp[len] = 0;

            byte* p = cp;

            p = ReadInt(p, out int version);
            if (version != SAVEGAME_VERSION)
            {
                Con.Printf($"Savegame is version {version}, not {SAVEGAME_VERSION}\n");
                return;
            }

            // comment line -- fscanf("%s") only reads the first whitespace-
            // delimited token and discards it, exactly like the original.
            byte* discard = stackalloc byte[32768];
            p = ReadToken(p, discard, 32768);

            // float spawn_parms[Svs.NUM_SPAWN_PARMS] -- a local in the original too
            float* spawn_parms = stackalloc float[Svs.NUM_SPAWN_PARMS];
            for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
                p = ReadFloat(p, out spawn_parms[i]);

            // this silliness is so we can load 1.06 save files, which have float skill values
            p = ReadFloat(p, out float tfloat);
            current_skill = (int)(tfloat + 0.1f);
            Cvar.SetValue("skill"u8, current_skill);

            byte* mapname = stackalloc byte[QuakeDef.MAX_QPATH];
            p = ReadToken(p, mapname, QuakeDef.MAX_QPATH);
            // sv.name -- no separate copy needed; Sv.SpawnServer(mapname)
            // below sets the real Sv.Name from this same buffer.

            p = ReadFloat(p, out float time);

            ClMain.Disconnect_f();

            // SV_SpawnServer(mapname)
            Sv.SpawnServer(mapname);

            if (!Sv.active)
            {
                Con.Printf("Couldn't load map\n"u8);
                return;
            }
            Sv.paused = true;   // pause until all clients connect
            Sv.loadgame = true;

            // load the light styles
            byte* style = stackalloc byte[32768];
            for (int i = 0; i < MAX_LIGHTSTYLES; i++)
            {
                p = ReadToken(p, style, 32768);
                Sv.lightstyles[i] = (byte*)Hunk.Alloc(Q.StrLen(style) + 1);
                Q.StrCpy(Sv.lightstyles[i], style);
            }

            // load the edicts out of the savegame file
            int entnum = -1; // -1 is the globals
            byte* block = stackalloc byte[32768];
            while (*p != 0)
            {
                int i;
                for (i = 0; i < 32768 - 1; i++)
                {
                    byte c = *p;
                    if (c == 0)
                        break;
                    p++;
                    block[i] = c;
                    if (c == (byte)'}')
                    {
                        i++;
                        break;
                    }
                }
                if (i == 32768 - 1)
                    Sys.Error("Loadgame buffer overflow"u8);
                block[i] = 0;

                byte* start = Com.Parse(block);
                if (Com.com_token[0] == 0)
                    break; // end of file

                if (Q.StrCmp(Com.com_token, "{"u8) != 0)
                    Sys.Error("First token isn't a brace"u8);

                if (entnum == -1)
                {
                    // parse the global vars
                    EdictSerialization.ParseGlobals(start);
                }
                else
                {
                    // parse an edict
                    EDict_t* ent = Progs.EdictNum(entnum);
                    Q.MemSet(&ent->V, 0, Progs.progs->EntityFields * 4);
                    ent->Free = 0;
                    EdictSerialization.ParseEdict(start, ent);

                    // link it into the bsp tree
                    if (ent->Free == 0)
                        World.LinkEdict(ent, false);
                }

                entnum++;
            }

            Sv.num_edicts = entnum;
            Sv.time = time;

            for (int i = 0; i < Svs.NUM_SPAWN_PARMS; i++)
                Svs.clients[0].spawn_parms[i] = spawn_parms[i];

            if (Cls.state != CActive.Dedicated)
            {
                ClMain.EstablishConnection("local"u8);
                HostCmd.Reconnect_f();
            }
        }
        finally
        {
            NativeMemory.Free(cp);
        }
    }
}
