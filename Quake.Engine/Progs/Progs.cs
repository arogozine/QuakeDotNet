// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// etype_t
public enum EType_t
{
    Void,
    String,
    Float,
    Vector,
    Entity,
    Field,
    Function,
    Pointer,
}

// op_t (pr_comp.h) -- QuakeC bytecode opcodes, in original declaration order.
public enum Op_t : ushort
{
    Done,
    Mul_F, Mul_V, Mul_FV, Mul_VF,
    Div_F,
    Add_F, Add_V,
    Sub_F, Sub_V,
    Eq_F, Eq_V, Eq_S, Eq_E, Eq_Fnc,
    Ne_F, Ne_V, Ne_S, Ne_E, Ne_Fnc,
    Le, Ge, Lt, Gt,
    Load_F, Load_V, Load_S, Load_Ent, Load_Fld, Load_Fnc,
    Address,
    Store_F, Store_V, Store_S, Store_Ent, Store_Fld, Store_Fnc,
    StoreP_F, StoreP_V, StoreP_S, StoreP_Ent, StoreP_Fld, StoreP_Fnc,
    Return,
    Not_F, Not_V, Not_S, Not_Ent, Not_Fnc,
    If, IfNot,
    Call0, Call1, Call2, Call3, Call4, Call5, Call6, Call7, Call8,
    State,
    Goto,
    And, Or,
    BitAnd, BitOr,
}

/// <summary>
/// pr_edict.c/pr_exec.c -- progs.dat loading, edict allocation, and the
/// QuakeC bytecode interpreter (PR_ExecuteProgram). Builtin functions
/// (pr_cmds.c's ~79 PF_* engine callbacks QuakeC calls into -- spawn,
/// sound, traceline, etc) are implemented in PrCmds.cs; pr_builtins is
/// populated from there, positionally matching the original's pr_builtin[]
/// table.
/// </summary>
public static unsafe class Progs
{
    public const int PROG_VERSION = 6;

    // PROGHEADER_CRC (progdefs.q1 -- QUAKE2 is never defined for WinQuake)
    public const int PROGHEADER_CRC = 5927;

    public const int DEF_SAVEGLOBAL = 1 << 15;

    // progs
    public static DPrograms_t* progs;

    // pr_functions
    public static DFunction_t* pr_functions;

    // pr_strings
    public static byte* pr_strings;

    // pr_globaldefs
    public static DDef_t* pr_globaldefs;

    // pr_fielddefs
    public static DDef_t* pr_fielddefs;

    // pr_statements
    public static DStatement_t* pr_statements;

    // pr_global_struct / pr_globals -- same backing memory, two views (typed
    // struct pointer vs. raw float array), matching the original.
    public static GlobalVars_t* pr_global_struct;
    public static float* pr_globals;

    // pr_edict_size
    public static int pr_edict_size;

    // pr_crc
    public static ushort pr_crc;

    // pr_builtins / pr_numbuiltins (pr_cmds.c) -- positionally exact against
    // the original's pr_builtin[] table (pr_cmds.c lines 1831-1933).
    public static readonly delegate*<void>[] pr_builtins = new delegate*<void>[]
    {
        &PrCmds.PF_Fixme,               // #0 (unused)
        &PrCmds.PF_makevectors,         // #1
        &PrCmds.PF_setorigin,           // #2
        &PrCmds.PF_setmodel,            // #3
        &PrCmds.PF_setsize,             // #4
        &PrCmds.PF_Fixme,               // #5 setabssize
        &PrCmds.PF_break,               // #6
        &PrCmds.PF_random,              // #7
        &PrCmds.PF_sound,               // #8
        &PrCmds.PF_normalize,           // #9
        &PrCmds.PF_error,               // #10
        &PrCmds.PF_objerror,            // #11
        &PrCmds.PF_vlen,                // #12
        &PrCmds.PF_vectoyaw,            // #13
        &PrCmds.PF_Spawn,               // #14
        &PrCmds.PF_Remove,              // #15
        &PrCmds.PF_traceline,           // #16
        &PrCmds.PF_checkclient,         // #17
        &PrCmds.PF_Find,                // #18
        &PrCmds.PF_precache_sound,      // #19
        &PrCmds.PF_precache_model,      // #20
        &PrCmds.PF_stuffcmd,            // #21
        &PrCmds.PF_findradius,          // #22
        &PrCmds.PF_bprint,              // #23
        &PrCmds.PF_sprint,              // #24
        &PrCmds.PF_dprint,              // #25
        &PrCmds.PF_ftos,                // #26
        &PrCmds.PF_vtos,                // #27
        &PrCmds.PF_coredump,            // #28
        &PrCmds.PF_traceon,             // #29
        &PrCmds.PF_traceoff,            // #30
        &PrCmds.PF_eprint,              // #31
        &PrCmds.PF_walkmove,            // #32
        &PrCmds.PF_Fixme,               // #33 (dup walkmove slot)
        &PrCmds.PF_droptofloor,         // #34
        &PrCmds.PF_lightstyle,          // #35
        &PrCmds.PF_rint,                // #36
        &PrCmds.PF_floor,               // #37
        &PrCmds.PF_ceil,                // #38
        &PrCmds.PF_Fixme,               // #39
        &PrCmds.PF_checkbottom,         // #40
        &PrCmds.PF_pointcontents,       // #41
        &PrCmds.PF_Fixme,               // #42
        &PrCmds.PF_fabs,                // #43
        &PrCmds.PF_aim,                 // #44
        &PrCmds.PF_cvar,                // #45
        &PrCmds.PF_localcmd,            // #46
        &PrCmds.PF_nextent,             // #47
        &PrCmds.PF_particle,            // #48
        &PrCmds.PF_changeyaw,           // #49
        &PrCmds.PF_Fixme,               // #50
        &PrCmds.PF_vectoangles,         // #51
        &PrCmds.PF_WriteByte,           // #52
        &PrCmds.PF_WriteChar,           // #53
        &PrCmds.PF_WriteShort,          // #54
        &PrCmds.PF_WriteLong,           // #55
        &PrCmds.PF_WriteCoord,          // #56
        &PrCmds.PF_WriteAngle,          // #57
        &PrCmds.PF_WriteString,         // #58
        &PrCmds.PF_WriteEntity,         // #59
        &PrCmds.PF_Fixme,               // #60 QUAKE2-only (PF_sin)
        &PrCmds.PF_Fixme,               // #61 QUAKE2-only (PF_cos)
        &PrCmds.PF_Fixme,               // #62 QUAKE2-only (PF_sqrt)
        &PrCmds.PF_Fixme,               // #63 QUAKE2-only (PF_changepitch)
        &PrCmds.PF_Fixme,               // #64 QUAKE2-only (PF_TraceToss)
        &PrCmds.PF_Fixme,               // #65 QUAKE2-only (PF_etos)
        &PrCmds.PF_Fixme,               // #66 QUAKE2-only (PF_WaterMove)
        &PrCmds.PF_MoveToGoal,          // #67 SV_MoveToGoal
        &PrCmds.PF_precache_file,       // #68
        &PrCmds.PF_makestatic,          // #69
        &PrCmds.PF_changelevel,         // #70
        &PrCmds.PF_Fixme,               // #71
        &PrCmds.PF_cvar_set,            // #72
        &PrCmds.PF_centerprint,         // #73
        &PrCmds.PF_ambientsound,        // #74
        &PrCmds.PF_precache_model,      // #75 (precache_model2)
        &PrCmds.PF_precache_sound,      // #76 (precache_sound2)
        &PrCmds.PF_precache_file,       // #77
        &PrCmds.PF_setspawnparms,       // #78
    };
    public static int pr_numbuiltins => pr_builtins.Length;

    /*
    ===============================================================================
    MARSHALLING HELPERS (pr_comp.h's G_ and RETURN_ macros) -- used by
    PrCmds.cs's PF_ builtin bodies to read arguments from and write results
    to the QuakeC global float array, matching the original macros exactly.
    ===============================================================================
    */

    public static float G_FLOAT(int ofs) => pr_globals[ofs];
    public static void G_FLOAT_SET(int ofs, float v) => pr_globals[ofs] = v;
    public static ref Vec3_t G_VECTOR(int ofs) => ref *(Vec3_t*)&pr_globals[ofs];
    public static byte* G_STRING(int ofs) => pr_strings + *(int*)&pr_globals[ofs];
    public static int G_STRING_OFS(int ofs) => *(int*)&pr_globals[ofs];
    public static EDict_t* G_EDICT(int ofs) => ProgToEdict(*(int*)&pr_globals[ofs]);
    public static int G_EDICTNUM(int ofs) => NumForEdict(G_EDICT(ofs));
    public static int G_INT(int ofs) => *(int*)&pr_globals[ofs];
    public static void G_INT_SET(int ofs, int v) => *(int*)&pr_globals[ofs] = v;

    public static void RETURN_FLOAT(float v) => pr_globals[OFS_RETURN] = v;
    public static void RETURN_VECTOR(in Vec3_t v) => G_VECTOR(OFS_RETURN) = v;
    public static void RETURN_STRING(byte* s) => G_INT_SET(OFS_RETURN, PR_NewString(s));
    public static void RETURN_EDICT(EDict_t* e) => G_INT_SET(OFS_RETURN, EdictToProg(e));
    public static void RETURN_INT(int v) => G_INT_SET(OFS_RETURN, v);

    public static int OFS_PARM(int i) => OFS_PARM0 + i * 3;

    /*
    ===============================================================================
    STRING_T CONVERSION

    A QuakeC string_t is a 32-bit offset from pr_strings. The original forms
    one with a raw `p - pr_strings` (PF_ftos, ED_ParseEpair, SV_SpawnServer,
    Host_Name_f, ...), which on 32-bit pointers always fits. This port keeps
    pr_strings inside the Hunk (where Com.LoadHunkFile put progs.dat), and the
    Hunk is a single contiguous block sized by an int, so `p - pr_strings`
    also fits for any p inside the Hunk -- the original arithmetic carries
    over unchanged as long as the string lives in the Hunk.
    ===============================================================================
    */

    // `p - pr_strings` from the original, checked: a pointer outside the Hunk
    // would silently truncate to a wild string_t on 64-bit, so fail loudly
    // instead.
    public static int PR_SetString(byte* p)
    {
        long d = p - pr_strings;
        if (d != (int)d)
            Sys.Error("PR_SetString: string outside the Hunk"u8);
        return (int)d;
    }

    // pr_string_temp (pr_cmds.c) -- the original's single 128-byte static
    // buffer, extended to a small ring of slots so several fabricated strings
    // can be live in one statement (e.g. bprint(ftos(a), ftos(b))) without
    // aliasing. Hunk-allocated (not static) so its pr_strings-relative offset
    // fits in a string_t; allocated from PR_Init, below host_hunklevel, so it
    // persists across levels like the original's static array.
    private const int PR_STRING_RING_SIZE = 16;
    private const int PR_STRING_SLOT_LEN = 128;
    internal static byte* pr_string_temp;
    private static int pr_string_ring_index;

    internal static void AllocStringTemp()
    {
        pr_string_temp = (byte*)Hunk.AllocName(PR_STRING_RING_SIZE * PR_STRING_SLOT_LEN, "strtemp"u8);
        pr_string_ring_index = 0;
    }

    // PR_NewString -- copies src into the next pr_string_temp slot, NUL
    // terminated, and returns its string_t (the original's
    // `pr_string_temp - pr_strings`). Valid only for the lifetime of the
    // current statement, since the ring wraps after PR_STRING_RING_SIZE calls.
    public static int PR_NewString(byte* src)
    {
        byte* dst = pr_string_temp + pr_string_ring_index * PR_STRING_SLOT_LEN;
        pr_string_ring_index = (pr_string_ring_index + 1) % PR_STRING_RING_SIZE;

        int i = 0;
        for (; i < PR_STRING_SLOT_LEN - 1 && src[i] != 0; i++)
            dst[i] = src[i];
        dst[i] = 0;

        return PR_SetString(dst);
    }

    // PR_LoadProgs
    public static void LoadProgs()
    {
        Crc.Init(out pr_crc);

        progs = (DPrograms_t*)Com.LoadHunkFile("progs.dat"u8);

        if (progs == null)
            Sys.Error("PR_LoadProgs: couldn't load progs.dat"u8);

        for (int i = 0; i < Com.com_filesize; i++)
            Crc.ProcessByte(ref pr_crc, ((byte*)progs)[i]);

        // byte swap the header
        int* headerInts = (int*)progs;
        for (int i = 0; i < sizeof(DPrograms_t) / 4; i++)
            headerInts[i] = Com.LittleLong(headerInts[i]);

        if (progs->Version != PROG_VERSION)
            Sys.Error($"progs.dat has wrong version number ({progs->Version} should be {PROG_VERSION})");
        if (progs->Crc != PROGHEADER_CRC)
            Sys.Error("progs.dat system vars have been modified, progdefs.h is out of date"u8);

        pr_functions = (DFunction_t*)((byte*)progs + progs->Ofs_Functions);
        pr_strings = (byte*)progs + progs->Ofs_Strings;

        pr_globaldefs = (DDef_t*)((byte*)progs + progs->Ofs_GlobalDefs);
        pr_fielddefs = (DDef_t*)((byte*)progs + progs->Ofs_FieldDefs);
        pr_statements = (DStatement_t*)((byte*)progs + progs->Ofs_Statements);

        pr_globals = (float*)((byte*)progs + progs->Ofs_Globals);
        pr_global_struct = (GlobalVars_t*)pr_globals;

        // pr_edict_size = entityfields*4 + sizeof(edict_t) - sizeof(entvars_t).
        // progs.dat's entityfields count (195 for the stock id1 progs.dat)
        // is larger than entvars_t's own field count (105): QuakeC's
        // defs.qc/progs.src declares extra entity fields beyond the ones the
        // C engine ever names directly, and qcc packs them immediately
        // after entvars_t's fields within the same per-entity block. The
        // original never validates this at runtime (it's a compile-time
        // guarantee of progdefs.h matching progs.src); not validated here
        // either, matching that.
        pr_edict_size = progs->EntityFields * 4 + sizeof(EDict_t) - sizeof(EntVars_t);

        // byte swap the lumps
        for (int i = 0; i < progs->NumStatements; i++)
        {
            pr_statements[i].Op = (ushort)Com.LittleShort((short)pr_statements[i].Op);
            pr_statements[i].A = Com.LittleShort(pr_statements[i].A);
            pr_statements[i].B = Com.LittleShort(pr_statements[i].B);
            pr_statements[i].C = Com.LittleShort(pr_statements[i].C);
        }

        for (int i = 0; i < progs->NumFunctions; i++)
        {
            pr_functions[i].First_Statement = Com.LittleLong(pr_functions[i].First_Statement);
            pr_functions[i].Parm_Start = Com.LittleLong(pr_functions[i].Parm_Start);
            pr_functions[i].S_Name = Com.LittleLong(pr_functions[i].S_Name);
            pr_functions[i].S_File = Com.LittleLong(pr_functions[i].S_File);
            pr_functions[i].NumParms = Com.LittleLong(pr_functions[i].NumParms);
            pr_functions[i].Locals = Com.LittleLong(pr_functions[i].Locals);
        }

        for (int i = 0; i < progs->NumGlobalDefs; i++)
        {
            pr_globaldefs[i].Type = (ushort)Com.LittleShort((short)pr_globaldefs[i].Type);
            pr_globaldefs[i].Ofs = (ushort)Com.LittleShort((short)pr_globaldefs[i].Ofs);
            pr_globaldefs[i].S_Name = Com.LittleLong(pr_globaldefs[i].S_Name);
        }

        for (int i = 0; i < progs->NumFieldDefs; i++)
        {
            pr_fielddefs[i].Type = (ushort)Com.LittleShort((short)pr_fielddefs[i].Type);
            if ((pr_fielddefs[i].Type & DEF_SAVEGLOBAL) != 0)
                Sys.Error("PR_LoadProgs: pr_fielddefs[i].type & DEF_SAVEGLOBAL"u8);
            pr_fielddefs[i].Ofs = (ushort)Com.LittleShort((short)pr_fielddefs[i].Ofs);
            pr_fielddefs[i].S_Name = Com.LittleLong(pr_fielddefs[i].S_Name);
        }

        int* globalInts = (int*)pr_globals;
        for (int i = 0; i < progs->NumGlobals; i++)
            globalInts[i] = Com.LittleLong(globalInts[i]);
    }

    /*
    ===============================================================================
    EDICT MANAGEMENT (pr_edict.c)
    ===============================================================================
    */

    // EDICT_NUM
    public static EDict_t* EdictNum(int n)
    {
        if (n < 0 || n >= Sv.max_edicts)
            Sys.Error($"EDICT_NUM: bad number {n}");
        return (EDict_t*)((byte*)Sv.edicts + n * pr_edict_size);
    }

    // NUM_FOR_EDICT
    public static int NumForEdict(EDict_t* e)
    {
        long b = (byte*)e - (byte*)Sv.edicts;
        long n = b / pr_edict_size;

        if (n < 0 || n >= Sv.num_edicts)
            Sys.Error("NUM_FOR_EDICT: bad pointer"u8);
        return (int)n;
    }

    // NEXT_EDICT
    public static EDict_t* NextEdict(EDict_t* e) => (EDict_t*)((byte*)e + pr_edict_size);

    // EDICT_TO_PROG
    public static int EdictToProg(EDict_t* e) => (int)((byte*)e - (byte*)Sv.edicts);

    // PROG_TO_EDICT
    public static EDict_t* ProgToEdict(int e) => (EDict_t*)((byte*)Sv.edicts + e);

    // ED_ClearEdict
    public static void ClearEdict(EDict_t* e)
    {
        Q.MemSet(&e->V, 0, progs->EntityFields * 4);
        e->Free = 0;
    }

    /*
    =================
    ED_Alloc

    Either finds a free edict, or allocates a new one.
    Try to avoid reusing an entity that was recently freed, because it
    can cause the client to think the entity morphed into something else
    instead of being removed and recreated, which can cause interpolated
    angles and bad trails.
    =================
    */
    public static EDict_t* Alloc()
    {
        int i;
        EDict_t* e = null;

        for (i = Svs.maxclients + 1; i < Sv.num_edicts; i++)
        {
            e = EdictNum(i);
            // the first couple seconds of server time can involve a lot of
            // freeing and allocating, so relax the replacement policy
            if (e->Free != 0 && (e->FreeTime < 2 || Sv.time - e->FreeTime > 0.5))
            {
                ClearEdict(e);
                return e;
            }
        }

        if (i == QuakeDef.MAX_EDICTS)
            Sys.Error("ED_Alloc: no free edicts"u8);

        Sv.num_edicts++;
        e = EdictNum(i);
        ClearEdict(e);

        return e;
    }

    /*
    =================
    ED_Free

    Marks the edict as free
    FIXME: walk all entities and NULL out references to this entity
    =================
    */
    public static void Free(EDict_t* ed)
    {
        World.UnlinkEdict(ed); // unlink from world bsp

        ed->Free = 1;
        ed->V.Model = 0;
        ed->V.TakeDamage = 0;
        ed->V.ModelIndex = 0;
        ed->V.ColorMap = 0;
        ed->V.Skin = 0;
        ed->V.Frame = 0;
        ed->V.Origin = default;
        ed->V.Angles = default;
        ed->V.NextThink = -1;
        ed->V.Solid = 0;

        ed->FreeTime = (float)Sv.time;
    }

    /*
    ===============================================================================
    PR_ExecuteProgram (pr_exec.c) -- the QuakeC bytecode interpreter.
    ===============================================================================
    */

    internal struct PrStack_t
    {
        public int S;
        public DFunction_t* F;
    }

    internal const int MAX_STACK_DEPTH = 64; // pr_exec.c -- Mark V (was 32)
    internal static readonly PrStack_t* pr_stack = NativeMemory.AllocArrayZeroed<PrStack_t>(MAX_STACK_DEPTH);
    private static int pr_depth;

    internal const int LOCALSTACK_SIZE = 2048;
    internal static readonly int* localstack = NativeMemory.AllocArrayZeroed<int>(LOCALSTACK_SIZE);
    private static int localstack_used;

    public static bool pr_trace;
    public static DFunction_t* pr_xfunction;
    public static int pr_xstatement;

    public static int pr_argc;

    // OFS_PARM0.. / OFS_RETURN (pr_comp.h) -- public: SvMove.MoveToGoal (a
    // pr_cmds.c-shaped builtin body that sv_move.c itself defines) reads its
    // "dist" parameter and writes its return value through these directly,
    // same as PR_ExecuteProgram's own builtin-call plumbing does.
    public const int OFS_RETURN = 1;
    public const int OFS_PARM0 = 4;

    // PR_RunError
    public static void RunError(scoped ReadOnlySpan<byte> error)
    {
        Con.Printf($"PR_RunError: statement {pr_xstatement}\n");
        Con.Printf(error);
        Con.Printf("\n"u8);

        pr_depth = 0; // dump the stack so host_error can shutdown functions

        Host.Error("Program error"u8);
    }

    public static void RunError([InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler error)
    {
        // Copy out first: every interpolated string shares one buffer, so
        // the Con.Printf($"...") below would overwrite the message.
        scoped ReadOnlySpan<byte> written = error.GetWrittenBuffer();
        Span<byte> message = stackalloc byte[written.Length];
        written.CopyTo(message);

        Con.Printf($"PR_RunError: statement {pr_xstatement}\n");
        Con.Printf(message);
        Con.Printf("\n"u8);

        pr_depth = 0; // dump the stack so host_error can shutdown functions

        Host.Error("Program error"u8);
    }

    // PR_EnterFunction -- returns the new program statement counter
    public static int EnterFunction(DFunction_t* f)
    {
        pr_stack[pr_depth].S = pr_xstatement;
        pr_stack[pr_depth].F = pr_xfunction;
        pr_depth++;
        if (pr_depth >= MAX_STACK_DEPTH)
            RunError("stack overflow"u8);

        int c = f->Locals;
        if (localstack_used + c > LOCALSTACK_SIZE)
            RunError("PR_ExecuteProgram: locals stack overflow\n"u8);

        for (int i = 0; i < c; i++)
            localstack[localstack_used + i] = ((int*)pr_globals)[f->Parm_Start + i];
        localstack_used += c;

        int o = f->Parm_Start;
        for (int i = 0; i < f->NumParms; i++)
        {
            for (int j = 0; j < f->Parm_Size[i]; j++)
            {
                ((int*)pr_globals)[o] = ((int*)pr_globals)[OFS_PARM0 + i * 3 + j];
                o++;
            }
        }

        pr_xfunction = f;
        return f->First_Statement - 1; // offset the s++
    }

    // PR_LeaveFunction
    public static int LeaveFunction()
    {
        if (pr_depth <= 0)
            Sys.Error("prog stack underflow"u8);

        int c = pr_xfunction->Locals;
        localstack_used -= c;
        if (localstack_used < 0)
            RunError("PR_ExecuteProgram: locals stack underflow\n"u8);

        for (int i = 0; i < c; i++)
            ((int*)pr_globals)[pr_xfunction->Parm_Start + i] = localstack[localstack_used + i];

        pr_depth--;
        pr_xfunction = pr_stack[pr_depth].F;
        return pr_stack[pr_depth].S;
    }

    // eval_t -- pr_comp.h's union, read/written through a float* window into
    // pr_globals (or an edict's field block), matching the original's
    // pointer-cast access exactly (no managed union type needed).

    /*
    ====================
    PR_ExecuteProgram
    ====================
    */
    public static void ExecuteProgram(int fnum)
    {
        if (fnum == 0 || fnum >= progs->NumFunctions)
            Sys.Error("PR_ExecuteProgram: NULL function"u8); // ED_Print(self) omitted -- debug aid only

        DFunction_t* f = &pr_functions[fnum];

        int runaway = 100000;
        pr_trace = false;

        int exitdepth = pr_depth;

        int s = EnterFunction(f);

        while (true)
        {
            s++; // next statement

            DStatement_t* st = &pr_statements[s];
            float* a = &pr_globals[st->A];
            float* b = &pr_globals[st->B];
            float* c = &pr_globals[st->C];
            int* ai = (int*)a, bi = (int*)b, ci = (int*)c;

            if (--runaway == 0)
                RunError("runaway loop error"u8);

            pr_xfunction->Profile++;
            pr_xstatement = s;

            switch ((Op_t)st->Op)
            {
                case Op_t.Add_F: *c = *a + *b; break;
                case Op_t.Add_V:
                    c[0] = a[0] + b[0]; c[1] = a[1] + b[1]; c[2] = a[2] + b[2];
                    break;

                case Op_t.Sub_F: *c = *a - *b; break;
                case Op_t.Sub_V:
                    c[0] = a[0] - b[0]; c[1] = a[1] - b[1]; c[2] = a[2] - b[2];
                    break;

                case Op_t.Mul_F: *c = *a * *b; break;
                case Op_t.Mul_V: *c = a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; break;
                case Op_t.Mul_FV:
                    c[0] = *a * b[0]; c[1] = *a * b[1]; c[2] = *a * b[2];
                    break;
                case Op_t.Mul_VF:
                    c[0] = *b * a[0]; c[1] = *b * a[1]; c[2] = *b * a[2];
                    break;

                case Op_t.Div_F: *c = *a / *b; break;

                case Op_t.BitAnd: *c = (int)*a & (int)*b; break;
                case Op_t.BitOr: *c = (int)*a | (int)*b; break;

                case Op_t.Ge: *c = *a >= *b ? 1 : 0; break;
                case Op_t.Le: *c = *a <= *b ? 1 : 0; break;
                case Op_t.Gt: *c = *a > *b ? 1 : 0; break;
                case Op_t.Lt: *c = *a < *b ? 1 : 0; break;
                case Op_t.And: *c = *a != 0 && *b != 0 ? 1 : 0; break;
                case Op_t.Or: *c = *a != 0 || *b != 0 ? 1 : 0; break;

                case Op_t.Not_F: *c = *a == 0 ? 1 : 0; break;
                case Op_t.Not_V: *c = a[0] == 0 && a[1] == 0 && a[2] == 0 ? 1 : 0; break;
                case Op_t.Not_S: *ci = *ai == 0 || pr_strings[*ai] == 0 ? 1 : 0; break;
                case Op_t.Not_Fnc: *ci = *ai == 0 ? 1 : 0; break;
                case Op_t.Not_Ent: *ci = ProgToEdict(*ai) == Sv.edicts ? 1 : 0; break;

                case Op_t.Eq_F: *c = *a == *b ? 1 : 0; break;
                case Op_t.Eq_V: *c = a[0] == b[0] && a[1] == b[1] && a[2] == b[2] ? 1 : 0; break;
                case Op_t.Eq_S: *c = Q.StrCmp(pr_strings + *ai, pr_strings + *bi) == 0 ? 1 : 0; break;
                case Op_t.Eq_E: *ci = *ai == *bi ? 1 : 0; break;
                case Op_t.Eq_Fnc: *ci = *ai == *bi ? 1 : 0; break;

                case Op_t.Ne_F: *c = *a != *b ? 1 : 0; break;
                case Op_t.Ne_V: *c = a[0] != b[0] || a[1] != b[1] || a[2] != b[2] ? 1 : 0; break;
                case Op_t.Ne_S: *c = Q.StrCmp(pr_strings + *ai, pr_strings + *bi) != 0 ? 1 : 0; break;
                case Op_t.Ne_E: *ci = *ai != *bi ? 1 : 0; break;
                case Op_t.Ne_Fnc: *ci = *ai != *bi ? 1 : 0; break;

                case Op_t.Store_F:
                case Op_t.Store_Ent:
                case Op_t.Store_Fld:
                case Op_t.Store_S:
                case Op_t.Store_Fnc:
                    *bi = *ai;
                    break;
                case Op_t.Store_V:
                    b[0] = a[0]; b[1] = a[1]; b[2] = a[2];
                    break;

                case Op_t.StoreP_F:
                case Op_t.StoreP_Ent:
                case Op_t.StoreP_Fld:
                case Op_t.StoreP_S:
                case Op_t.StoreP_Fnc:
                {
                    int* ptr = (int*)((byte*)Sv.edicts + *bi);
                    *ptr = *ai;
                    break;
                }
                case Op_t.StoreP_V:
                {
                    float* ptr = (float*)((byte*)Sv.edicts + *bi);
                    ptr[0] = a[0]; ptr[1] = a[1]; ptr[2] = a[2];
                    break;
                }

                case Op_t.Address:
                {
                    EDict_t* ed = ProgToEdict(*ai);
                    if (ed == Sv.edicts && Sv.state == ServerState_t.Active)
                        RunError("assignment to world entity"u8);
                    *ci = (int)((byte*)((int*)&ed->V + *bi) - (byte*)Sv.edicts);
                    break;
                }

                case Op_t.Load_F:
                case Op_t.Load_Fld:
                case Op_t.Load_Ent:
                case Op_t.Load_S:
                case Op_t.Load_Fnc:
                {
                    EDict_t* ed = ProgToEdict(*ai);
                    int* p = (int*)&ed->V + *bi;
                    *ci = *p;
                    break;
                }

                case Op_t.Load_V:
                {
                    EDict_t* ed = ProgToEdict(*ai);
                    float* p = (float*)((int*)&ed->V + *bi);
                    c[0] = p[0]; c[1] = p[1]; c[2] = p[2];
                    break;
                }

                case Op_t.IfNot:
                    if (*ai == 0)
                        s += st->B - 1;
                    break;

                case Op_t.If:
                    if (*ai != 0)
                        s += st->B - 1;
                    break;

                case Op_t.Goto:
                    s += st->A - 1;
                    break;

                case Op_t.Call0:
                case Op_t.Call1:
                case Op_t.Call2:
                case Op_t.Call3:
                case Op_t.Call4:
                case Op_t.Call5:
                case Op_t.Call6:
                case Op_t.Call7:
                case Op_t.Call8:
                {
                    pr_argc = st->Op - (int)Op_t.Call0;
                    if (*ai == 0)
                        RunError("NULL function"u8);

                    DFunction_t* newf = &pr_functions[*ai];

                    if (newf->First_Statement < 0)
                    {
                        int bi2 = -newf->First_Statement;
                        if (bi2 >= pr_numbuiltins)
                            RunError("Bad builtin call number"u8);
                        pr_builtins[bi2]();
                        break;
                    }

                    s = EnterFunction(newf);
                    break;
                }

                case Op_t.Done:
                case Op_t.Return:
                    pr_globals[OFS_RETURN] = pr_globals[st->A];
                    pr_globals[OFS_RETURN + 1] = pr_globals[st->A + 1];
                    pr_globals[OFS_RETURN + 2] = pr_globals[st->A + 2];

                    s = LeaveFunction();
                    if (pr_depth == exitdepth)
                        return; // all done
                    break;

                case Op_t.State:
                {
                    EDict_t* ed = ProgToEdict(pr_global_struct->Self);
                    ed->V.NextThink = pr_global_struct->Time + 0.1f;
                    if (*a != ed->V.Frame)
                        ed->V.Frame = *a;
                    ed->V.Think = *bi;
                    break;
                }

                default:
                    RunError($"Bad opcode {st->Op}");
                    break;
            }
        }
    }

    /*
    =================
    PR_Init
    =================
    */
    public static readonly Cvar_t* nomonsters = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* gamecfg = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scratch1 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scratch2 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scratch3 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* scratch4 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* savedgamecfg = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* saved1 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* saved2 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* saved3 = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* saved4 = NativeMemory.AllocZeroed<Cvar_t>();

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        Cvar.RegisterVariable(cvar);
    }

    // ED_Count -- (int)SolidType.Bsp/(int)MoveType.Step (server.h)

    public static void Count()
    {
        int active = 0, models = 0, solid = 0, step = 0;
        for (int i = 0; i < Sv.num_edicts; i++)
        {
            EDict_t* ent = EdictNum(i);
            if (ent->Free != 0)
                continue;
            active++;
            if (ent->V.Solid != 0)
                solid++;
            if (ent->V.Model != 0)
                models++;
            if (ent->V.MoveType == (int)MoveType.Step)
                step++;
        }
        Con.Printf($"num_edicts:{Sv.num_edicts}\n");
        Con.Printf($"active    :{active}\n");
        Con.Printf($"view      :{models}\n");
        Con.Printf($"touch     :{solid}\n");
        Con.Printf($"step      :{step}\n");
    }

    // ED_PrintNum -- simplified vs. the original's full per-field dump
    // (ED_Print's ddef_t iteration over every non-zero field, formatted with
    // PR_ValueString): prints classname + origin only, enough to identify an
    // edict from the console. Justification (Rule 8): debug-only output.
    public static void PrintNum(int ent)
    {
        EDict_t* ed = EdictNum(ent);
        Con.Printf($"\nEDICT {ent}:\n");
        if (ed->Free != 0)
        {
            Con.Printf("FREE\n"u8);
            return;
        }
        byte* classname = pr_strings + ed->V.ClassName;
        Con.Printf($"classname: {classname}\n");
        Con.Printf($"origin: {ed->V.Origin[0]} {ed->V.Origin[1]} {ed->V.Origin[2]}\n");
    }

    // ED_PrintEdicts
    public static void PrintEdicts()
    {
        Con.Printf($"{Sv.num_edicts} entities\n");
        for (int i = 0; i < Sv.num_edicts; i++)
            PrintNum(i);
    }

    // ED_PrintEdict_f
    public static void PrintEdict_f()
    {
        int i = Q.Atoi(Cmd.Argv(1));
        if (i >= Sv.num_edicts)
        {
            Con.Printf("Bad edict number\n"u8);
            return;
        }
        PrintNum(i);
    }

    private static void EdictCmd() => PrintEdict_f();
    private static void EdictsCmd() => PrintEdicts();
    private static void EdictCountCmd() => Count();
    private static void ProfileCmd() { } // PR_Profile_f -- profiling aid, no-op (pr_functions[].profile isn't accumulated meaningfully without builtins)

    public static void Init()
    {
        AllocStringTemp();
        Cmd.AddCommand("edict"u8, &EdictCmd);
        Cmd.AddCommand("edicts"u8, &EdictsCmd);
        Cmd.AddCommand("edictcount"u8, &EdictCountCmd);
        Cmd.AddCommand("profile"u8, &ProfileCmd);
        RegisterCvar(nomonsters, "nomonsters"u8, "0"u8);
        RegisterCvar(gamecfg, "gamecfg"u8, "0"u8);
        RegisterCvar(scratch1, "scratch1"u8, "0"u8);
        RegisterCvar(scratch2, "scratch2"u8, "0"u8);
        RegisterCvar(scratch3, "scratch3"u8, "0"u8);
        RegisterCvar(scratch4, "scratch4"u8, "0"u8);
        RegisterCvar(savedgamecfg, "savedgamecfg"u8, "0"u8, true);
        RegisterCvar(saved1, "saved1"u8, "0"u8, true);
        RegisterCvar(saved2, "saved2"u8, "0"u8, true);
        RegisterCvar(saved3, "saved3"u8, "0"u8, true);
        RegisterCvar(saved4, "saved4"u8, "0"u8, true);
    }
}
