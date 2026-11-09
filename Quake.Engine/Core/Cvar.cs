// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// cvar.c -- dynamic variable tracking.
/// </summary>
public static unsafe class Cvar
{
    // cvar_vars
    public static Cvar_t* cvar_vars;

    private static readonly Utf8String cvar_null_string = Utf8String.Empty;

    // Stand-in for a C "cvar_t x = {name, string, archive, server};" static
    // initializer. Cvars are linked into cvar_vars by address and live for the
    // whole process, so each gets its own zeroed NativeMemory block (rule 9)
    // rather than being a static struct field, whose address the runtime
    // doesn't guarantee to be stable. Never freed, like the C globals.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cvar_t* Alloc(Utf8String name, Utf8String value, bool archive = false, bool server = false, float floatValue = 0)
    {
        Cvar_t* cvar = NativeMemory.AllocZeroed<Cvar_t>();
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        cvar->Server = server;
        cvar->Value = floatValue;
        return cvar;
    }

    // Cvar_FindVar
    public static Cvar_t* FindVar(byte* var_name)
    {
        for (Cvar_t* var = cvar_vars; var != null; var = var->Next)
            if (Q.StrCmp(var_name, var->Name) == 0)
                return var;

        return null;
    }

    // Cvar_VariableValue
    public static float VariableValue(byte* var_name)
    {
        Cvar_t* var = FindVar(var_name);
        if (var == null)
            return 0;
        return Q.Atof(var->String);
    }

    // Cvar_VariableString
    public static byte* VariableString(Utf8String var_name)
    {
        Cvar_t* var = FindVar(var_name);
        if (var == null)
            return cvar_null_string;
        return var->String;
    }

    // Cvar_CompleteVariable
    public static byte* CompleteVariable(byte* partial)
    {
        int len = Q.StrLen(partial);

        if (len == 0)
            return null;

        // check functions
        for (Cvar_t* cvar = cvar_vars; cvar != null; cvar = cvar->Next)
            if (Q.StrNCmp(partial, cvar->Name, len) == 0)
                return cvar->Name;

        return null;
    }

    // Cvar_Set
    public static void Set(Utf8String var_name, Utf8String value)
    {
        Cvar_t* var = FindVar(var_name);
        if (var == null)
        {
            // there is an error in C code if this happens
            Con.Printf($"Cvar_Set: variable {var_name} not found\n");
            return;
        }

        bool changed = Q.StrCmp(var->String, value) != 0;

        Zone.Free(var->String); // free the old value string

        var->String = (byte*)Zone.Malloc(Q.StrLen(value) + 1);
        Q.StrCpy(var->String, value);
        var->Value = Q.Atof(var->String);

        if (var->Server && changed)
        {
            if (Sv.active)
                Host.BroadcastPrintf($"\"{var->Name}\" changed to \"{var->String}\"\n");
        }
    }

    // Cvar_SetValue
    [SkipLocalsInit]
    public static void SetValue(Utf8String var_name, float value)
    {
        // sprintf("%f") -- always '.' as the decimal separator, regardless of
        // the current culture, so Q.Atof can read it back
        // original: char val[32] (overflows for |value| >= 1e24); 48 holds the
        // worst case "-340282346638528859811704183484516925440.000000" (47 chars),
        // so TryFormat cannot fail for a float
        byte* val = stackalloc byte[48];
        value.TryFormat(new Span<byte>(val, 47), out int written, "F6", CultureInfo.InvariantCulture);
        val[written] = 0;

        Set(var_name, val);
    }

    // Cvar_RegisterVariable
    // Adds a freestanding variable to the variable list.
    public static void RegisterVariable(Cvar_t* variable)
    {
        // first check to see if it has allready been defined
        if (FindVar(variable->Name) != null)
        {
            Con.Printf($"Can't register variable {variable->Name}, allready defined\n");
            return;
        }

        // check for overlap with a command
        if (Cmd.Exists(variable->Name))
        {
            Con.Printf($"Cvar_RegisterVariable: {variable->Name} is a command\n");
            return;
        }

        // copy the value off, because future sets will Z_Free it
        byte* oldstr = variable->String;
        variable->String = (byte*)Zone.Malloc(Q.StrLen(oldstr) + 1);
        Q.StrCpy(variable->String, oldstr);
        variable->Value = Q.Atof(variable->String);

        // link the variable in
        variable->Next = cvar_vars;
        cvar_vars = variable;
    }

    // Cvar_Command
    // Handles variable inspection and changing from the console
    public static bool Command()
    {
        // check variables
        Cvar_t* v = FindVar(Cmd.Argv(0));
        if (v == null)
            return false;

        // perform a variable print or set
        if (Cmd.Argc() == 1)
        {
            Con.Printf($"\"{v->Name}\" is \"{v->String}\"\n");
            return true;
        }

        Set(v->Name, Cmd.Argv(1));
        return true;
    }

    // Cvar_WriteVariables
    // Writes lines containing "set variable value" for all variables
    // with the archive flag set to true.
    public static void WriteVariables(int handle)
    {
        for (Cvar_t* var = cvar_vars; var != null; var = var->Next)
        {
            if (var->Archive)
            {
                // fprintf(f, "%s \"%s\"\n", var->name, var->string)
                Sys.FileWrite(handle, var->Name, Q.StrLen(var->Name));
                Sys.FileWrite(handle, " \""u8);
                Sys.FileWrite(handle, var->String, Q.StrLen(var->String));
                Sys.FileWrite(handle, "\"\n"u8);
            }
        }
    }
}
