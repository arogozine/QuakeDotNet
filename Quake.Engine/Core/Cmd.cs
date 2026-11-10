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

// cmd_source_t
public enum CmdSource_t
{
    SrcClient,  // came in over a net connection as a (int)Clc.StringCmd
                // host_client will be valid during this state.
    SrcCommand, // from the command buffer
}

/// <summary>
/// cmd.c -- Quake script command processing module.
/// </summary>
public static unsafe class Cmd
{
    private const int MAX_ALIAS_NAME = 32;
    private const int MAX_ARGS = 80;

    // cmd_alias
    // internal (rather than private) so tests can reset this Hunk/Zone-backed
    // linked list between test methods, which each get a fresh Hunk/Zone
    // arena via MemoryManager.Memory_Init -- see Quake.UnitTests.CmdTests.
    internal static CmdAlias_t* cmd_alias;

    // cmd_wait
    private static bool cmd_wait;

    /*
    =============================================================================
                        COMMAND BUFFER
    =============================================================================
    */

    // cmd_text
    internal static readonly SizeBuf_t* cmd_text = NativeMemory.AllocZeroed<SizeBuf_t>();

    // Cbuf_Init
    public static void BufInit()
    {
        Sz.Alloc(cmd_text, 256 * 1024); // space for commands and script files -- Mark V: 256 KB, formerly 8 KB
    }

    // Cbuf_AddText
    // Adds command text at the end of the buffer
    public static void AddText(Utf8String text)
    {
        int l = Q.StrLen(text);

        if (cmd_text->CurSize + l >= cmd_text->MaxSize)
        {
            Con.Printf("Cbuf_AddText: overflow\n"u8);
            return;
        }

        Sz.Write(cmd_text, text, Q.StrLen(text));
    }

    public static void AddText([InterpolatedStringHandlerArgument] scoped Utf8StringHandler text)
    {
        scoped ReadOnlySpan<byte> buffer = text.GetWrittenBuffer();

        int l = buffer.Length;

        if (cmd_text->CurSize + l >= cmd_text->MaxSize)
        {
            Con.Printf("Cbuf_AddText: overflow\n"u8);
            return;
        }

        Sz.Write(cmd_text, buffer);
    }
    // Cbuf_InsertText
    // Adds command text immediately after the current command
    // FIXME: actually change the command buffer to do less copying

    public static void InsertText([InterpolatedStringHandlerArgument] scoped Utf8StringHandler text)
    {
        InsertText((Utf8String)text);
    }

    public static void InsertText(Utf8String text)
    {
        int templen = cmd_text->CurSize;
        byte* temp = null;

        if (templen != 0)
        {
            temp = (byte*)Zone.Malloc(templen);
            Q.MemCpy(temp, cmd_text->Data, templen);
            Sz.Clear(cmd_text);
        }

        AddText(text);

        if (templen != 0)
        {
            Sz.Write(cmd_text, temp, templen);
            Zone.Free(temp);
        }
    }

    // Cbuf_Execute
    public static void BufExecute()
    {
        byte* line = stackalloc byte[1024];

        while (cmd_text->CurSize != 0)
        {
            byte* text = cmd_text->Data;

            int quotes = 0;
            int i;
            for (i = 0; i < cmd_text->CurSize; i++)
            {
                if (text[i] == (byte)'"')
                {
                    quotes++;
                }

                if ((quotes & 1) == 0 && text[i] == (byte)';')
                {
                    break; // don't break if inside a quoted string
                }

                if (text[i] == (byte)'\n')
                {
                    break;
                }
            }

            Q.MemCpy(line, text, i);
            line[i] = 0;

            // delete the text from the command buffer and move remaining commands
            // down: this is necessary because commands (exec, alias) can insert
            // data at the beginning of the text buffer
            if (i == cmd_text->CurSize)
            {
                cmd_text->CurSize = 0;
            }
            else
            {
                i++;
                cmd_text->CurSize -= i;
                Q.MemCpy(text, text + i, cmd_text->CurSize);
            }

            ExecuteString(line, CmdSource_t.SrcCommand);

            if (cmd_wait)
            {
                // skip out while text still remains in buffer, leaving it for
                // next frame
                cmd_wait = false;
                break;
            }
        }
    }

    /*
    ==============================================================================
                        SCRIPT COMMANDS
    ==============================================================================
    */

    // Cmd_Wait_f
    // Causes execution of the remainder of the command buffer to be delayed
    // until next frame.
    public static void Wait_f()
    {
        cmd_wait = true;
    }

    // Cmd_StuffCmds_f
    // Adds command line parameters as script statements
    public static void StuffCmds_f()
    {
        if (Argc() != 1)
        {
            Con.Printf("stuffcmds : execute command line parameters\n"u8);
            return;
        }

        // build the combined string to parse from
        int s = 0;
        for (int i = 1; i < Com.com_argc; i++)
        {
            if (Com.com_argv[i] == null)
            {
                continue; // NEXTSTEP nulls out -NXHost
            }

            s += Q.StrLen(Com.com_argv[i]) + 1;
        }

        if (s == 0)
        {
            return;
        }

        byte* text = (byte*)Zone.Malloc(s + 1);
        text[0] = 0;
        for (int i = 1; i < Com.com_argc; i++)
        {
            if (Com.com_argv[i] == null)
            {
                continue;
            }

            Q.StrCat(text, Com.com_argv[i]);
            if (i != Com.com_argc - 1)
            {
                Q.StrCat(text, " "u8);
            }
        }

        // pull out the commands
        byte* build = (byte*)Zone.Malloc(s + 1);
        build[0] = 0;

        for (int i = 0; i < s - 1; i++)
        {
            if (text[i] == (byte)'+')
            {
                i++;

                int j;
                for (j = i; text[j] != (byte)'+' && text[j] != (byte)'-' && text[j] != 0; j++)
                {
                }

                byte c = text[j];
                text[j] = 0;

                Q.StrCat(build, text + i);
                Q.StrCat(build, "\n"u8);
                text[j] = c;
                i = j - 1;
            }
        }

        if (build[0] != 0)
        {
            InsertText(build);
        }

        Zone.Free(text);
        Zone.Free(build);
    }

    // Cmd_Exec_f
    public static void Exec_f()
    {
        if (Argc() != 2)
        {
            Con.Printf("exec <filename> : execute a script file\n"u8);
            return;
        }

        int mark = Hunk.LowMark();
        byte* f = Com.LoadHunkFile(Argv(1));
        if (f == null)
        {
            Con.Printf($"couldn't exec {Argv(1)}\n");
            return;
        }

        Con.Printf($"execing {Argv(1)}\n");

        InsertText(f);
        Hunk.FreeToLowMark(mark);
    }

    // Cmd_Echo_f
    // Just prints the rest of the line to the console
    public static void Echo_f()
    {
        for (int i = 1; i < Argc(); i++)
        {
            Con.Printf($"{Argv(i)} ");
        }

        Con.Printf("\n"u8);
    }

    // CopyString (cmd.c local helper)
    private static byte* CopyString(byte* @in)
    {
        byte* @out = (byte*)Zone.Malloc(Q.StrLen(@in) + 1);
        Q.StrCpy(@out, @in);
        return @out;
    }

    // Cmd_Alias_f
    // Creates a new command that executes a command string (possibly ; seperated)
    public static void Alias_f()
    {
        if (Argc() == 1)
        {
            Con.Printf("Current alias commands:\n"u8);
            for (CmdAlias_t* a = cmd_alias; a != null; a = a->Next)
            {
                Con.Printf($"{a->Name} : {a->Value}\n");
            }

            return;
        }

        byte* s = Argv(1);
        if (Q.StrLen(s) >= MAX_ALIAS_NAME)
        {
            Con.Printf("Alias name is too long\n"u8);
            return;
        }

        // if the alias allready exists, reuse it
        CmdAlias_t* alias = null;
        for (CmdAlias_t* a = cmd_alias; a != null; a = a->Next)
        {
            if (Q.StrCmp(s, a->Name) == 0)
            {
                Zone.Free(a->Value);
                alias = a;
                break;
            }
        }

        if (alias == null)
        {
            alias = (CmdAlias_t*)Zone.Malloc(sizeof(CmdAlias_t));
            alias->Next = cmd_alias;
            cmd_alias = alias;
        }

        Q.StrCpy(alias->Name, s);

        // copy the rest of the command line
        byte* cmd = stackalloc byte[1024];
        cmd[0] = 0; // start out with a null string
        int c = Argc();
        for (int i = 2; i < c; i++)
        {
            Q.StrCat(cmd, Argv(i));
            if (i != c)
            {
                Q.StrCat(cmd, " "u8);
            }
        }

        Q.StrCat(cmd, "\n"u8);

        alias->Value = CopyString(cmd);
    }

    /*
    =============================================================================
                        COMMAND EXECUTION
    =============================================================================
    */

    // internal: TokenizeString Zone.Free's cmd_argv[0..cmd_argc) on the next
    // call, so a leftover count from a previous test's (now-freed) Hunk/Zone
    // arena needs resetting too -- see Quake.UnitTests.CmdTests.
    internal static int cmd_argc;
    private static readonly byte** cmd_argv = NativeMemory.AllocPointerArray<byte>(MAX_ARGS);
    private static readonly Utf8String cmd_null_string = Utf8String.Empty;
    private static byte* cmd_args;

    // cmd_source
    public static CmdSource_t cmd_source;

    // cmd_functions -- possible commands to execute. internal (rather than
    // private) so tests can reset this Hunk-backed linked list between test
    // methods -- see Quake.UnitTests.CmdTests/ConsoleTests.
    internal static CmdFunction_t* cmd_functions;

    // Cmd_Init
    public static void Init()
    {
        // register our commands
        AddCommand("stuffcmds"u8, &StuffCmds_f);
        AddCommand("exec"u8, &Exec_f);
        AddCommand("echo"u8, &Echo_f);
        AddCommand("alias"u8, &Alias_f);
        AddCommand("cmd"u8, &ForwardToServerCmd);
        AddCommand("wait"u8, &Wait_f);
    }

    // Cmd_Argc
    public static int Argc() => cmd_argc;

    // Cmd_Argv
    public static byte* Argv(int arg)
    {
        if ((uint)arg >= cmd_argc)
        {
            return cmd_null_string;
        }

        return cmd_argv[arg];
    }

    // Cmd_Args
    public static byte* Args() => cmd_args;

    // Cmd_TokenizeString
    // Parses the given string into command line tokens.
    public static void TokenizeString(byte* text)
    {
        // clear the args from the last string
        for (int i = 0; i < cmd_argc; i++)
        {
            Zone.Free(cmd_argv[i]);
        }

        cmd_argc = 0;
        cmd_args = null;

        while (true)
        {
            // skip whitespace up to a /n
            while (*text != 0 && *text <= (byte)' ' && *text != (byte)'\n')
            {
                text++;
            }

            if (*text == (byte)'\n')
            {
                // a newline seperates commands in the buffer
                text++;
                break;
            }

            if (*text == 0)
            {
                return;
            }

            if (cmd_argc == 1)
            {
                cmd_args = text;
            }

            text = Com.Parse(text);
            if (text == null)
            {
                return;
            }

            if (cmd_argc < MAX_ARGS)
            {
                cmd_argv[cmd_argc] = (byte*)Zone.Malloc(Q.StrLen(Com.com_token) + 1);
                Q.StrCpy(cmd_argv[cmd_argc], Com.com_token);
                cmd_argc++;
            }
        }
    }

    // Cmd_AddCommand
    public static void AddCommand(Utf8String cmd_name, delegate*<void> function)
    {
        if (Host.host_initialized) // because hunk allocation would get stomped
        {
            Sys.Error("Cmd_AddCommand after host_initialized"u8);
        }

        // fail if the command is a variable name
        if (Cvar.VariableString(cmd_name)[0] != 0)
        {
            Con.Printf($"Cmd_AddCommand: {cmd_name} already defined as a var\n");
            return;
        }

        // fail if the command already exists
        for (CmdFunction_t* cmd = cmd_functions; cmd != null; cmd = cmd->Next)
        {
            if (Q.StrCmp(cmd_name, cmd->Name) == 0)
            {
                Con.Printf($"Cmd_AddCommand: {cmd_name} already defined\n");
                return;
            }
        }

        CmdFunction_t* newCmd = (CmdFunction_t*)Hunk.Alloc(sizeof(CmdFunction_t));
        newCmd->Name = cmd_name;
        newCmd->Function = function;
        newCmd->Next = cmd_functions;
        cmd_functions = newCmd;
    }

    // Cmd_Exists
    public static bool Exists(byte* cmd_name)
    {
        for (CmdFunction_t* cmd = cmd_functions; cmd != null; cmd = cmd->Next)
        {
            if (Q.StrCmp(cmd_name, cmd->Name) == 0)
            {
                return true;
            }
        }

        return false;
    }

    // Cmd_CompleteCommand
    public static byte* CompleteCommand(byte* partial)
    {
        int len = Q.StrLen(partial);

        if (len == 0)
        {
            return null;
        }

        // check functions
        for (CmdFunction_t* cmd = cmd_functions; cmd != null; cmd = cmd->Next)
        {
            if (Q.StrNCmp(partial, cmd->Name, len) == 0)
            {
                return cmd->Name;
            }
        }

        return null;
    }

    // Cmd_ExecuteString
    // A complete command line has been parsed, so try to execute it
    // FIXME: lookupnoadd the token to speed search?
    public static void ExecuteString(Utf8String text, CmdSource_t src)
    {
        cmd_source = src;
        TokenizeString(text);

        // execute the command line
        if (Argc() == 0)
        {
            return; // no tokens
        }

        // check functions
        for (CmdFunction_t* cmd = cmd_functions; cmd != null; cmd = cmd->Next)
        {
            if (Q.StrCaseCmp(cmd_argv[0], cmd->Name) == 0)
            {
                cmd->Function();
                return;
            }
        }

        // check alias
        for (CmdAlias_t* a = cmd_alias; a != null; a = a->Next)
        {
            if (Q.StrCaseCmp(cmd_argv[0], a->Name) == 0)
            {
                InsertText(a->Value);
                return;
            }
        }

        // check cvars
        if (!Cvar.Command())
        {
            Con.Printf($"Unknown command \"{Argv(0)}\"\n");
        }
    }

    // Cmd_ForwardToServer
    // Sends the entire command line over to the server
    private static void ForwardToServerCmd() => ForwardToServer();

    public static void ForwardToServer()
    {
        if (Cls.state != CActive.Connected)
        {
            Con.Printf($"Can't \"{Argv(0)}\", not connected\n");
            return;
        }

        if (ClDemo.demoplayback)
        {
            return; // not really connected
        }

        SizeBuf_t* msg = Cls.message;
        Msg.WriteByte(msg, (int)Clc.StringCmd);
        if (Q.StrCaseCmp(Argv(0), "cmd"u8) != 0)
        {
            Sz.Print(msg, Argv(0));
            Sz.Print(msg, " "u8);
        }
        if (Argc() > 1)
        {
            Sz.Print(msg, Args());
        }
        else
        {
            Sz.Print(msg, "\n"u8);
        }
    }

    // Cmd_CheckParm
    // Returns the position (1 to argc-1) in the command's argument list
    // where the given parameter apears, or 0 if not present
    public static int CheckParm(Utf8String parm_)
    {
        byte* parm = parm_;
        if (parm == null)
        {
            Sys.Error("Cmd_CheckParm: NULL"u8);
        }

        for (int i = 1; i < Argc(); i++)
        {
            if (Q.StrCaseCmp(parm, Argv(i)) == 0)
            {
                return i;
            }
        }

        return 0;
    }

}
