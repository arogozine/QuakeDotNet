// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// pr_edict.c -- the ED_* text (de)serialization primitives used by
/// savegames and the .bsp entity lump (LoadFromFile).
///
/// Faithfully reproduces an original quirk rather than fixing it:
/// ED_ParseEpair's ev_field case reads G_INT(def->ofs) -- i.e. a *global* at
/// that index, not the field's own offset.
/// </summary>
public static unsafe class EdictSerialization
{
    // SPAWNFLAG_NOT_* (server.h) -- LoadFromFile's skill/deathmatch entity
    // filter.
    private const int SPAWNFLAG_NOT_EASY = 256;
    private const int SPAWNFLAG_NOT_MEDIUM = 512;
    private const int SPAWNFLAG_NOT_HARD = 1024;
    private const int SPAWNFLAG_NOT_DEATHMATCH = 2048;

    // pr_comp.h's type_size[] indexed by etype_t (ev_void..ev_pointer): every
    // type is one 4-byte slot except ev_vector, which is three.
    private static readonly int[] TypeSize = [1, 1, 1, 3, 1, 1, 1, 1];


    private static void WriteText(int fileHandle, scoped ReadOnlySpan<byte> text)
    {
        Sys.FileWrite(fileHandle, text);
    }

    private static void WriteText(int fileHandle, [InterpolatedStringHandlerArgument] scoped Utf8StringHandler handler)
    {
        Sys.FileWrite(fileHandle, handler.GetWrittenBuffer());
    }

    // ED_FieldAtOfs
    public static DDef_t* FieldAtOfs(int ofs)
    {
        for (int i = 0; i < Progs.progs->NumFieldDefs; i++)
        {
            DDef_t* def = &Progs.pr_fielddefs[i];
            if (def->Ofs == ofs)
            {
                return def;
            }
        }
        return null;
    }

    // ED_FindField
    public static DDef_t* FindField(byte* name)
    {
        for (int i = 0; i < Progs.progs->NumFieldDefs; i++)
        {
            DDef_t* def = &Progs.pr_fielddefs[i];
            if (Q.StrCmp(Progs.pr_strings + def->S_Name, name) == 0)
            {
                return def;
            }
        }
        return null;
    }

    // GetEdictFieldValue -- gefvCache (a 2-slot MRU field-lookup cache) is a
    // pure performance optimization with no observable behavior difference;
    // skipped here, same status as other perf-only omissions in this port.
    // Returns a float* rather than the original's eval_t* union pointer --
    // every ported call site only ever reads the `_float` member.
    public static float* GetEdictFieldValue(EDict_t* ed, Utf8String field)
    {
        DDef_t* def = FindField(field);
        if (def == null)
        {
            return null;
        }

        return (float*)((byte*)&ed->V + (def->Ofs * 4));
    }

    // ED_FindGlobal
    public static DDef_t* FindGlobal(byte* name)
    {
        for (int i = 0; i < Progs.progs->NumGlobalDefs; i++)
        {
            DDef_t* def = &Progs.pr_globaldefs[i];
            if (Q.StrCmp(Progs.pr_strings + def->S_Name, name) == 0)
            {
                return def;
            }
        }
        return null;
    }

    // ED_FindFunction
    public static DFunction_t* FindFunction(byte* name)
    {
        for (int i = 0; i < Progs.progs->NumFunctions; i++)
        {
            DFunction_t* func = &Progs.pr_functions[i];
            if (Q.StrCmp(Progs.pr_strings + func->S_Name, name) == 0)
            {
                return func;
            }
        }
        return null;
    }

    // ED_NewString -- Hunk_Alloc'd like the original, so it's freed with the
    // rest of the level by Host_ClearMemory. Returns the string_t directly
    // (ED_ParseEpair's `ED_NewString(s) - pr_strings` folded in via
    // Progs.PR_SetString).
    public static int NewString(byte* s)
    {
        int l = Q.StrLen(s) + 1;
        byte* newStr = (byte*)Hunk.Alloc(l);
        byte* newP = newStr;

        for (int i = 0; i < l; i++)
        {
            if (s[i] == (byte)'\\' && i < l - 1)
            {
                i++;
                *newP++ = s[i] == (byte)'n' ? (byte)'\n' : (byte)'\\';
            }
            else
            {
                *newP++ = s[i];
            }
        }

        return Progs.PR_SetString(newStr);
    }

    // PR_UglyValueString -- only what ED_Write/ED_WriteGlobals need
    private static ReadOnlySpan<byte> UglyValueString(int type, int* val)
    {
        type &= ~Progs.DEF_SAVEGLOBAL;

        switch ((EType_t)type)
        {
            case EType_t.String:
                return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(Progs.pr_strings + *val);
            case EType_t.Entity:
                return ((Utf8StringHandler)$"{Progs.NumForEdict(Progs.ProgToEdict(*val))}").GetWrittenBuffer();
            case EType_t.Function:
                {
                    DFunction_t* f = Progs.pr_functions + *val;
                    return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(Progs.pr_strings + f->S_Name);
                }
            case EType_t.Field:
                {
                    DDef_t* def = FieldAtOfs(*val);
                    return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(Progs.pr_strings + def->S_Name);
                }
            case EType_t.Void:
                return "void"u8;
            case EType_t.Float:
                return ((Utf8StringHandler)$"{*(float*)val:F6}").GetWrittenBuffer();
            case EType_t.Vector:
                {
                    float* v = (float*)val;
                    return ((Utf8StringHandler)$"{v[0]:F6} {v[1]:F6} {v[2]:F6}").GetWrittenBuffer();
                }
            default:
                return ((Utf8StringHandler)$"bad type {type}").GetWrittenBuffer();
        }
    }

    // ED_ParseEpair
    // Can parse either fields or globals
    // returns false if error
    public static bool ParseEpair(void* @base, DDef_t* key, byte* s)
    {
        int* d = (int*)@base + key->Ofs;

        switch ((EType_t)(key->Type & ~Progs.DEF_SAVEGLOBAL))
        {
            case EType_t.String:
                *d = NewString(s);
                break;

            case EType_t.Float:
                *(float*)d = Q.Atof(s);
                break;

            case EType_t.Vector:
                {
                    byte* str = stackalloc byte[128];
                    Q.StrCpy(str, s);
                    byte* v = str;
                    byte* w = str;
                    float* fd = (float*)d;
                    for (int i = 0; i < 3; i++)
                    {
                        while (*v != 0 && *v != (byte)' ')
                        {
                            v++;
                        }

                        *v = 0;
                        fd[i] = Q.Atof(w);
                        w = v = v + 1;
                    }
                    break;
                }

            case EType_t.Entity:
                *d = Progs.EdictToProg(Progs.EdictNum(Q.Atoi(s)));
                break;

            case EType_t.Field:
                {
                    DDef_t* def = FindField(s);
                    if (def == null)
                    {
                        Con.Printf($"Can't find field {s}\n");
                        return false;
                    }
                    // G_INT(def->ofs) -- reads a *global* at that index, not the
                    // field's own offset; reproduced verbatim (see class comment).
                    *d = *(int*)&Progs.pr_globals[def->Ofs];
                    break;
                }

            case EType_t.Function:
                {
                    DFunction_t* func = FindFunction(s);
                    if (func == null)
                    {
                        Con.Printf($"Can't find function {s}\n");
                        return false;
                    }
                    *d = (int)(func - Progs.pr_functions);
                    break;
                }

            default:
                break;
        }
        return true;
    }

    // ED_ParseGlobals
    public static void ParseGlobals(byte* data)
    {
        byte* keyname = stackalloc byte[64];

        while (true)
        {
            data = Com.Parse(data);
            if (Com.com_token[0] == (byte)'}')
            {
                break;
            }

            if (data == null)
            {
                Sys.Error("ED_ParseEntity: EOF without closing brace"u8);
            }

            Q.StrCpy(keyname, Com.com_token);

            data = Com.Parse(data);
            if (data == null)
            {
                Sys.Error("ED_ParseEntity: EOF without closing brace"u8);
            }

            if (Com.com_token[0] == (byte)'}')
            {
                Sys.Error("ED_ParseEntity: closing brace without data"u8);
            }

            DDef_t* key = FindGlobal(keyname);
            if (key == null)
            {
                Con.Printf($"'{keyname}' is not a global\n");
                continue;
            }

            if (!ParseEpair(Progs.pr_globals, key, Com.com_token))
            {
                Host.Error("ED_ParseGlobals: parse error"u8);
            }
        }
    }

    // ED_ParseEdict
    // Parses an edict out of the given string, returning the new position.
    // ed should be a properly initialized empty edict.
    // Used for initial level load and for savegames.
    public static byte* ParseEdict(byte* data, EDict_t* ent)
    {
        bool init = false;

        // clear it
        if (ent != Sv.edicts) // hack
        {
            Q.MemSet(&ent->V, 0, Progs.progs->EntityFields * 4);
        }

        byte* keyname = stackalloc byte[256];
        byte* temp = stackalloc byte[32];

        while (true)
        {
            data = Com.Parse(data);
            if (Com.com_token[0] == (byte)'}')
            {
                break;
            }

            if (data == null)
            {
                Sys.Error("ED_ParseEntity: EOF without closing brace"u8);
            }

            // anglehack is to allow QuakeEd to write single scalar angles
            // and allow them to be turned into vectors. (FIXME...)
            bool anglehack;
            if (Q.StrCmp(Com.com_token, "angle"u8) == 0)
            {
                Q.StrCpy(Com.com_token, "angles"u8);
                anglehack = true;
            }
            else
            {
                anglehack = false;
            }

            // FIXME: change light to _light to get rid of this hack
            if (Q.StrCmp(Com.com_token, "light"u8) == 0)
            {
                Q.StrCpy(Com.com_token, "light_lev"u8); // hack for single light def
            }

            Q.StrCpy(keyname, Com.com_token);

            // another hack to fix keynames with trailing spaces
            int n = Q.StrLen(keyname);
            while (n != 0 && keyname[n - 1] == (byte)' ')
            {
                keyname[n - 1] = 0;
                n--;
            }

            data = Com.Parse(data);
            if (data == null)
            {
                Sys.Error("ED_ParseEntity: EOF without closing brace"u8);
            }

            if (Com.com_token[0] == (byte)'}')
            {
                Sys.Error("ED_ParseEntity: closing brace without data"u8);
            }

            init = true;

            // keynames with a leading underscore are used for utility comments,
            // and are immediately discarded by quake
            if (keyname[0] == (byte)'_')
            {
                continue;
            }

            DDef_t* key = FindField(keyname);
            if (key == null)
            {
                Con.Printf($"'{keyname}' is not a field\n");
                continue;
            }

            if (anglehack)
            {
                Q.StrCpy(temp, Com.com_token);
                Q.StrCpy(Com.com_token, "0 "u8);
                Q.StrCat(Com.com_token, temp);
                Q.StrCat(Com.com_token, " 0"u8);
            }

            if (!ParseEpair(&ent->V, key, Com.com_token))
            {
                Host.Error("ED_ParseEdict: parse error"u8);
            }
        }

        if (!init)
        {
            ent->Free = 1;
        }

        return data;
    }

    /*
    ================
    ED_LoadFromFile

    The entities are directly placed in the array, rather than allocated with
    ED_Alloc, because otherwise an error loading the map would have entity
    number references out of order.

    Creates a server's entity / program execution context by parsing textual
    entity definitions out of an ent file.
    ================
    */
    public static void LoadFromFile(byte* data)
    {
        EDict_t* ent = null;
        int inhibit = 0;
        Progs.pr_global_struct->Time = (float)Sv.time;

        // parse ents
        while (true)
        {
            // parse the opening brace
            data = Com.Parse(data);
            if (data == null)
            {
                break;
            }

            if (Com.com_token[0] != (byte)'{')
            {
                Sys.Error($"ED_LoadFromFile: found {Com.com_token} when expecting {{");
            }

            if (ent == null)
            {
                ent = Progs.EdictNum(0);
            }
            else
            {
                ent = Progs.Alloc();
            }

            data = ParseEdict(data, ent);

            // remove things from different skill levels or deathmatch
            if (Host.deathmatch->Value != 0)
            {
                if (((int)ent->V.SpawnFlags & SPAWNFLAG_NOT_DEATHMATCH) != 0)
                {
                    Progs.Free(ent);
                    inhibit++;
                    continue;
                }
            }
            else if ((SaveGame.current_skill == 0 && ((int)ent->V.SpawnFlags & SPAWNFLAG_NOT_EASY) != 0)
                     || (SaveGame.current_skill == 1 && ((int)ent->V.SpawnFlags & SPAWNFLAG_NOT_MEDIUM) != 0)
                     || (SaveGame.current_skill >= 2 && ((int)ent->V.SpawnFlags & SPAWNFLAG_NOT_HARD) != 0))
            {
                Progs.Free(ent);
                inhibit++;
                continue;
            }

            // immediately call spawn function
            if (ent->V.ClassName == 0)
            {
                Con.Printf("No classname for:\n"u8);
                Progs.PrintNum(Progs.NumForEdict(ent));
                Progs.Free(ent);
                continue;
            }

            // look for the spawn function
            DFunction_t* func = FindFunction(Progs.pr_strings + ent->V.ClassName);
            if (func == null)
            {
                Con.Printf("No spawn function for:\n"u8);
                Progs.PrintNum(Progs.NumForEdict(ent));
                Progs.Free(ent);
                continue;
            }

            Progs.pr_global_struct->Self = Progs.EdictToProg(ent);
            Progs.ExecuteProgram((int)(func - Progs.pr_functions));
        }

        Con.DPrintf($"{inhibit} entities inhibited\n");
    }

    // ED_Write
    // For savegames. `fileHandle` is a Sys file handle standing in for the
    // original's FILE* (see ClDemo.cs).
    public static void Write(int fileHandle, EDict_t* ed)
    {
        WriteText(fileHandle, "{\n"u8);

        if (ed->Free != 0)
        {
            WriteText(fileHandle, "}\n"u8);
            return;
        }

        for (int i = 1; i < Progs.progs->NumFieldDefs; i++)
        {
            DDef_t* d = &Progs.pr_fielddefs[i];
            byte* name = Progs.pr_strings + d->S_Name;
            if (name[Q.StrLen(name) - 2] == (byte)'_')
            {
                continue; // skip _x, _y, _z vars
            }

            int* val = (int*)((byte*)&ed->V + (d->Ofs * 4));

            // if the value is still all 0, skip the field
            int type = d->Type & ~Progs.DEF_SAVEGLOBAL;
            int size = TypeSize[type];
            int j;
            for (j = 0; j < size; j++)
            {
                if (val[j] != 0)
                {
                    break;
                }
            }

            if (j == size)
            {
                continue;
            }

            WriteText(fileHandle, $"\"{name}\" ");
            WriteText(fileHandle, $"\"{UglyValueString(d->Type, val)}\"\n");
        }

        WriteText(fileHandle, "}\n"u8);
    }

    // ED_WriteGlobals
    public static void WriteGlobals(int fileHandle)
    {
        WriteText(fileHandle, "{\n"u8);

        for (int i = 0; i < Progs.progs->NumGlobalDefs; i++)
        {
            DDef_t* def = &Progs.pr_globaldefs[i];
            int type = def->Type;
            if ((def->Type & Progs.DEF_SAVEGLOBAL) == 0)
            {
                continue;
            }

            type &= ~Progs.DEF_SAVEGLOBAL;

            if (type != (int)EType_t.String && type != (int)EType_t.Float && type != (int)EType_t.Entity)
            {
                continue;
            }

            byte* name = Progs.pr_strings + def->S_Name;
            WriteText(fileHandle, $"\"{name}\" ");
            WriteText(fileHandle, $"\"{UglyValueString(type, (int*)&Progs.pr_globals[def->Ofs])}\"\n");
        }

        WriteText(fileHandle, "}\n"u8);
    }
}
