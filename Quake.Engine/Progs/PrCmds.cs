// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// MSG_BROADCAST/MSG_ONE/MSG_ALL/MSG_INIT (pr_cmds.c) -- destination
// semantics for the PF_Write* family. WriteDest (below) now routes to the
// real destination sizebuf_t for each of these, matching the original.
public enum MsgDest
{
    Broadcast = 0,
    One = 1,
    All = 2,
    Init = 3,
}

/// <summary>
/// pr_cmds.c -- QuakeC engine builtins (PF_* functions), registered
/// positionally into Progs.pr_builtins. Now that real TCP/IP networking
/// exists (Net.cs/NetDgrm.cs), every builtin that writes a server->client
/// message in the original does so for real here too:
/// PF_bprint/PF_sprint/PF_centerprint/PF_lightstyle/PF_stuffcmd write
/// straight into the target client(s)' own Message buffer, PF_particle
/// (via Sv.StartParticle) and SvPhys.StartSound write into Sv.datagram, and
/// PF_makestatic/PF_ambientsound write into Sv.signon -- all real,
/// per-frame-flushed sizebuf_t's (Sv.cs's SendClientMessages/
/// UpdateToReliableMessages) that the always-local player already reads
/// back over its own loopback connection (ClParse.cs's (int)Svc.Print/
/// (int)Svc.CenterPrint/(int)Svc.LightStyle/(int)Svc.Particle/(int)Svc.Sound/(int)Svc.SpawnStatic/
/// (int)Svc.SpawnStaticSound handlers), exactly like a real remote client would.
/// The PF_Write* family (MESSAGE WRITING, below) works the same way, via
/// WriteDest.
/// </summary>
public static unsafe class PrCmds
{

    // SvPhys.FL_NOTARGET (defs.qc) -- not already declared elsewhere in the port.

    // DAMAGE_AIM (defs.qc)
    private const int DAMAGE_AIM = 2;

    /*
    ===============================================================================
    SHARED HELPERS
    ===============================================================================
    */

    // PF_VarString -- concatenates QC string args [first, pr_argc) into a
    // stable scratch buffer, matching the original's static char out[256].
    internal static readonly byte* varStringBuf = (byte*)NativeMemory.AllocZeroed(256);
    public static byte* PF_VarString(int first)
    {
        int len = 0;
        for (int i = first; i < Progs.pr_argc; i++)
        {
            byte* s = Progs.G_STRING(Progs.OFS_PARM(i));
            int slen = Q.StrLen(s);
            if (len + slen > 255)
                slen = 255 - len;
            for (int j = 0; j < slen; j++)
                varStringBuf[len + j] = s[j];
            len += slen;
        }
        varStringBuf[len] = 0;
        return varStringBuf;
    }

    // SetMinMaxSize (pr_cmds.c) -- the original's rotate path is dead code
    // there too (force-disabled by a "FIXME: implement rotation properly
    // again" before ever reading the rotate parameter), so only the
    // non-rotated path is ported.
    public static void SetMinMaxSize(EDict_t* e, in Vec3_t min, in Vec3_t max, bool rotate)
    {
        for (int i = 0; i < 3; i++)
            if (min[i] > max[i])
                Progs.RunError("backwards mins/maxs"u8);

        e->V.Mins = min;
        e->V.Maxs = max;
        Vec3_t size = max - min;
        e->V.Size = size;

        World.LinkEdict(e, false);
    }

    // E_STRING(ed, fieldofs) (progs.h) -- fieldofs is in 4-byte-word units,
    // matching ExecuteProgram's own Load_S/Load_Fld opcode handling.
    private static byte* EString(EDict_t* ed, int fieldOfs) => Progs.pr_strings + *((int*)&ed->V + fieldOfs);

    private static void CheckEmptyString(byte* s)
    {
        if (s[0] <= (byte)' ')
            Progs.RunError("Bad string"u8);
    }

    // Builds a temporary native C string (the original sprintf's into
    // pr_string_temp) and hands it to RETURN_STRING, which copies it into the
    // Hunk-resident pr_string_temp ring -- used by PF_ftos/PF_vtos, the only
    // builtins that fabricate a new string from a managed C# string rather
    // than an existing byte*.
    private static void ReturnNewString([InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler text)
    {
        // The handler's buffer is a native ring slot, NUL-terminated by GetWrittenBufferPtr.
        Progs.RETURN_STRING(text.GetWrittenBufferPtr());
    }

    /*
    ===============================================================================
    PURE MATH / STRING FORMATTING
    ===============================================================================
    */

    // #1
    public static void PF_makevectors()
    {
        ref Vec3_t angles = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        MathLib.AngleVectors(in angles, out Progs.pr_global_struct->VForward, out Progs.pr_global_struct->VRight, out Progs.pr_global_struct->VUp);
    }

    // #9
    public static void PF_normalize()
    {
        ref Vec3_t value1 = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        Vec3_t newvalue = value1;
        MathLib.VectorNormalize(&newvalue);
        Progs.RETURN_VECTOR(newvalue);
    }

    // #12
    public static void PF_vlen()
    {
        ref Vec3_t value1 = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        Progs.RETURN_FLOAT(value1.Length());
    }

    // #13
    public static void PF_vectoyaw()
    {
        ref Vec3_t value1 = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        Progs.RETURN_FLOAT(MathLib.VectoYaw(value1));
    }

    // #51
    public static void PF_vectoangles()
    {
        ref Vec3_t value1 = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        Vec3_t result = default;
        MathLib.VectoAngles(value1, &result);
        Progs.RETURN_VECTOR(result);
    }

    // #7
    public static void PF_random()
    {
        float num = (Q.Rand() & 0x7fff) / (float)0x7fff;
        Progs.RETURN_FLOAT(num);
    }

    // #36
    public static void PF_rint()
    {
        float f = Progs.G_FLOAT(Progs.OFS_PARM0);
        Progs.RETURN_FLOAT(f > 0 ? (int)(f + 0.5f) : (int)(f - 0.5f));
    }

    // #37
    public static void PF_floor() => Progs.RETURN_FLOAT(MathF.Floor(Progs.G_FLOAT(Progs.OFS_PARM0)));

    // #38
    public static void PF_ceil() => Progs.RETURN_FLOAT(MathF.Ceiling(Progs.G_FLOAT(Progs.OFS_PARM0)));

    // #43
    public static void PF_fabs() => Progs.RETURN_FLOAT(MathF.Abs(Progs.G_FLOAT(Progs.OFS_PARM0)));

    // #26
    public static void PF_ftos()
    {
        float v = Progs.G_FLOAT(Progs.OFS_PARM0);
        if (v == (int)v)
            ReturnNewString($"{(int)v}");
        else
            ReturnNewString($"{v,5:F1}");
    }

    // #27
    public static void PF_vtos()
    {
        ref Vec3_t v = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        ReturnNewString($"'{v[0],5:F1} {v[1],5:F1} {v[2],5:F1}'");
    }

    /*
    ===============================================================================
    EDICT / WORLD
    ===============================================================================
    */

    // #2
    public static void PF_setorigin()
    {
        EDict_t* e = Progs.G_EDICT(Progs.OFS_PARM0);
        ref Vec3_t org = ref Progs.G_VECTOR(Progs.OFS_PARM(1));
        e->V.Origin = org;
        World.LinkEdict(e, false);
    }

    // #4
    public static void PF_setsize()
    {
        EDict_t* e = Progs.G_EDICT(Progs.OFS_PARM0);
        ref Vec3_t min = ref Progs.G_VECTOR(Progs.OFS_PARM(1));
        ref Vec3_t max = ref Progs.G_VECTOR(Progs.OFS_PARM(2));
        SetMinMaxSize(e, min, max, false);
    }

    // #3
    public static void PF_setmodel()
    {
        EDict_t* e = Progs.G_EDICT(Progs.OFS_PARM0);
        byte* m = Progs.G_STRING(Progs.OFS_PARM(1));

        int i;
        for (i = 0; i < QuakeDef.MAX_MODELS && Sv.model_precache[i] != null; i++)
            if (Q.StrCmp(Sv.model_precache[i], m) == 0)
                break;

        if (i >= QuakeDef.MAX_MODELS || Sv.model_precache[i] == null)
            Progs.RunError($"no precache: {m}\n");

        e->V.Model = Progs.G_STRING_OFS(Progs.OFS_PARM(1));
        e->V.ModelIndex = i;

        Model_t* mod = Sv.models[i];
        if (mod != null)
            SetMinMaxSize(e, mod->Mins, mod->Maxs, true);
        else
            SetMinMaxSize(e, MathLib.vec3_origin, MathLib.vec3_origin, true);
    }

    // #14
    public static void PF_Spawn() => Progs.RETURN_EDICT(Progs.Alloc());

    // #15
    public static void PF_Remove() => Progs.Free(Progs.G_EDICT(Progs.OFS_PARM0));

    // #16
    public static void PF_traceline()
    {
        ref Vec3_t v1 = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        ref Vec3_t v2 = ref Progs.G_VECTOR(Progs.OFS_PARM(1));
        int type = (int)Progs.G_FLOAT(Progs.OFS_PARM(2));
        EDict_t* ent = Progs.G_EDICT(Progs.OFS_PARM(3));

        Trace_t trace = World.Move(v1, MathLib.vec3_origin, MathLib.vec3_origin, v2, type, ent);

        Progs.pr_global_struct->TraceAllSolid = trace.AllSolid ? 1 : 0;
        Progs.pr_global_struct->TraceStartSolid = trace.StartSolid ? 1 : 0;
        Progs.pr_global_struct->TraceFraction = trace.Fraction;
        Progs.pr_global_struct->TraceInWater = trace.InWater ? 1 : 0;
        Progs.pr_global_struct->TraceInOpen = trace.InOpen ? 1 : 0;
        Progs.pr_global_struct->TraceEndPos = trace.EndPos;
        Progs.pr_global_struct->TracePlaneNormal = trace.Plane.Normal;
        Progs.pr_global_struct->TracePlaneDist = trace.Plane.Dist;
        Progs.pr_global_struct->TraceEnt = trace.Ent != null ? Progs.EdictToProg(trace.Ent) : Progs.EdictToProg(Sv.edicts);
    }

    // #34
    public static void PF_droptofloor()
    {
        EDict_t* ent = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        Progs.RETURN_FLOAT(World.DropToFloor(ent) ? 1 : 0);
    }

    // #40
    public static void PF_checkbottom()
    {
        EDict_t* ent = Progs.G_EDICT(Progs.OFS_PARM0);
        Progs.RETURN_FLOAT(SvMove.CheckBottom(ent) ? 1 : 0);
    }

    // #41
    public static void PF_pointcontents()
    {
        ref Vec3_t v = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        Progs.RETURN_FLOAT(World.PointContents(v));
    }

    // #32
    public static void PF_walkmove()
    {
        EDict_t* ent = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        float yaw = Progs.G_FLOAT(Progs.OFS_PARM0);
        float dist = Progs.G_FLOAT(Progs.OFS_PARM(1));

        if (((int)ent->V.Flags & (SvPhys.FL_ONGROUND | SvPhys.FL_FLY | SvPhys.FL_SWIM)) == 0)
        {
            Progs.RETURN_FLOAT(0);
            return;
        }

        yaw = yaw * MathLib.M_PI * 2 / 360;
        Vec3_t move = default;
        move[0] = MathF.Cos(yaw) * dist;
        move[1] = MathF.Sin(yaw) * dist;
        move[2] = 0;

        // save program state, because SV_movestep may call other progs
        DFunction_t* oldf = Progs.pr_xfunction;
        int oldself = Progs.pr_global_struct->Self;

        bool result = SvMove.MoveStep(ent, move, true);
        Progs.RETURN_FLOAT(result ? 1 : 0);

        // restore program state
        Progs.pr_xfunction = oldf;
        Progs.pr_global_struct->Self = oldself;
    }

    // #49
    public static void PF_changeyaw() => SvMove.ChangeYaw();

    // #67 SV_MoveToGoal -- already ported as SvMove.MoveToGoal; this is
    // just its builtin-table registration point.
    public static void PF_MoveToGoal() => SvMove.MoveToGoal();

    // #47
    public static void PF_nextent()
    {
        int i = Progs.G_EDICTNUM(Progs.OFS_PARM0);
        while (true)
        {
            i++;
            if (i == Sv.num_edicts)
            {
                Progs.RETURN_EDICT(Sv.edicts);
                return;
            }
            EDict_t* ent = Progs.EdictNum(i);
            if (ent->Free == 0)
            {
                Progs.RETURN_EDICT(ent);
                return;
            }
        }
    }

    // #22
    public static void PF_findradius()
    {
        EDict_t* chain = Sv.edicts;
        ref Vec3_t org = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        float rad = Progs.G_FLOAT(Progs.OFS_PARM(1));

        EDict_t* ent = Progs.NextEdict(Sv.edicts);
        for (int i = 1; i < Sv.num_edicts; i++, ent = Progs.NextEdict(ent))
        {
            if (ent->Free != 0)
                continue;
            if ((int)ent->V.Solid == (int)SolidType.Not)
                continue;

            Vec3_t eorg = default;
            for (int j = 0; j < 3; j++)
                eorg[j] = org[j] - (ent->V.Origin[j] + (ent->V.Mins[j] + ent->V.Maxs[j]) * 0.5f);
            if (eorg.Length() > rad)
                continue;

            ent->V.Chain = Progs.EdictToProg(chain);
            chain = ent;
        }

        Progs.RETURN_EDICT(chain);
    }

    // #18 (WinQuake's non-QUAKE2 branch, pr_cmds.c line 1025)
    public static void PF_Find()
    {
        int e = Progs.G_EDICTNUM(Progs.OFS_PARM0);
        int f = Progs.G_INT(Progs.OFS_PARM(1));
        byte* s = Progs.G_STRING(Progs.OFS_PARM(2));
        if (s == null)
            Progs.RunError("PF_Find: bad search string"u8);

        for (e++; e < Sv.num_edicts; e++)
        {
            EDict_t* ed = Progs.EdictNum(e);
            if (ed->Free != 0)
                continue;
            byte* t = EString(ed, f);
            if (t == null)
                continue;
            if (Q.StrCmp(t, s) == 0)
            {
                Progs.RETURN_EDICT(ed);
                return;
            }
        }

        Progs.RETURN_EDICT(Sv.edicts);
    }

    // #44
    public static void PF_aim()
    {
        EDict_t* ent = Progs.G_EDICT(Progs.OFS_PARM0);

        Vec3_t start = ent->V.Origin;
        start[2] += 20;

        Vec3_t dir = Progs.pr_global_struct->VForward;
        Vec3_t end = start + 2048 * dir;
        Trace_t tr = World.Move(start, MathLib.vec3_origin, MathLib.vec3_origin, end, (int)ClipMoveType.Normal, ent);
        if (tr.Ent != null && tr.Ent->V.TakeDamage == DAMAGE_AIM
            && (Host.teamplay->Value == 0 || ent->V.Team <= 0 || ent->V.Team != tr.Ent->V.Team))
        {
            Progs.RETURN_VECTOR(Progs.pr_global_struct->VForward);
            return;
        }

        Vec3_t bestdir = dir;
        float bestdist = Sv.sv_aim->Value;
        EDict_t* bestent = null;

        EDict_t* check = Progs.NextEdict(Sv.edicts);
        for (int i = 1; i < Sv.num_edicts; i++, check = Progs.NextEdict(check))
        {
            if (check->V.TakeDamage != DAMAGE_AIM)
                continue;
            if (check == ent)
                continue;
            if (Host.teamplay->Value != 0 && ent->V.Team > 0 && ent->V.Team == check->V.Team)
                continue;

            Vec3_t testEnd = default;
            for (int j = 0; j < 3; j++)
                testEnd[j] = check->V.Origin[j] + 0.5f * (check->V.Mins[j] + check->V.Maxs[j]);
            Vec3_t testDir = testEnd - start;
            MathLib.VectorNormalize(&testDir);
            float dist = Vector3.Dot(testDir, Progs.pr_global_struct->VForward);
            if (dist < bestdist)
                continue;
            Trace_t tr2 = World.Move(start, MathLib.vec3_origin, MathLib.vec3_origin, testEnd, (int)ClipMoveType.Normal, ent);
            if (tr2.Ent == check)
            {
                bestdist = dist;
                bestent = check;
            }
        }

        if (bestent != null)
        {
            Vec3_t d2 = bestent->V.Origin - ent->V.Origin;
            float dist2 = Vector3.Dot(d2, Progs.pr_global_struct->VForward);
            Vec3_t scaled = Progs.pr_global_struct->VForward * dist2;
            scaled[2] = d2[2];
            MathLib.VectorNormalize(&scaled);
            Progs.RETURN_VECTOR(scaled);
        }
        else
        {
            Progs.RETURN_VECTOR(bestdir);
        }
    }

    // #17
    internal static readonly byte* checkpvs = (byte*)NativeMemory.AllocZeroed(BspFile.MAX_MAP_LEAFS / 8);
    public static int c_invis, c_notvis;

    private static int NewCheckClient(int check)
    {
        if (check < 1)
            check = 1;
        if (check > Svs.maxclients)
            check = Svs.maxclients;

        int i = check == Svs.maxclients ? 1 : check + 1;

        EDict_t* ent;
        while (true)
        {
            if (i == Svs.maxclients + 1)
                i = 1;

            ent = Progs.EdictNum(i);

            if (i == check)
                break; // didn't find anything else

            if (ent->Free != 0) { i++; continue; }
            if (ent->V.Health <= 0) { i++; continue; }
            if (((int)ent->V.Flags & SvPhys.FL_NOTARGET) != 0) { i++; continue; }

            break;
        }

        Vec3_t org = ent->V.Origin + ent->V.ViewOfs;
        MLeaf_t* leaf = Model.PointInLeaf(org, Sv.worldmodel);
        byte* pvs = Model.LeafPVS(leaf, Sv.worldmodel);
        Q.MemCpy(checkpvs, pvs, (Sv.worldmodel->NumLeafs + 7) >> 3);

        return i;
    }

    public static void PF_checkclient()
    {
        if (Sv.time - Sv.lastchecktime >= 0.1)
        {
            Sv.lastcheck = NewCheckClient(Sv.lastcheck);
            Sv.lastchecktime = Sv.time;
        }

        EDict_t* ent = Progs.EdictNum(Sv.lastcheck);
        if (ent->Free != 0 || ent->V.Health <= 0)
        {
            Progs.RETURN_EDICT(Sv.edicts);
            return;
        }

        EDict_t* self = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        Vec3_t view = self->V.Origin + self->V.ViewOfs;
        MLeaf_t* leaf = Model.PointInLeaf(view, Sv.worldmodel);
        int l = (int)(leaf - Sv.worldmodel->Leafs) - 1;
        if (l < 0 || (checkpvs[l >> 3] & (1 << (l & 7))) == 0)
        {
            c_notvis++;
            Progs.RETURN_EDICT(Sv.edicts);
            return;
        }

        c_invis++;
        Progs.RETURN_EDICT(ent);
    }

    // #78
    public static void PF_setspawnparms()
    {
        EDict_t* ent = Progs.G_EDICT(Progs.OFS_PARM0);
        Svs.EnsureLocalPlayerConnected();
        int i = Progs.NumForEdict(ent);
        if (i < 1 || i > Svs.maxclients)
            Progs.RunError("Entity is not a client"u8);

        ref Client_t client = ref Svs.clients[i - 1];
        float* parms = &Progs.pr_global_struct->Parm1;
        for (int p = 0; p < Svs.NUM_SPAWN_PARMS; p++)
            parms[p] = client.spawn_parms[p];
    }

    /*
    ===============================================================================
    PRECACHE / CVAR / CONSOLE / DEBUG
    ===============================================================================
    */

    // #19, #76
    public static void PF_precache_sound()
    {
        if (Sv.state != ServerState_t.Loading)
            Progs.RunError("PF_Precache_*: Precache can only be done in spawn functions"u8);

        byte* s = Progs.G_STRING(Progs.OFS_PARM0);
        Progs.RETURN_INT(Progs.G_INT(Progs.OFS_PARM0));
        CheckEmptyString(s);

        int maxSounds = Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE ? Protocol.MAX_P15_SOUNDS : QuakeDef.MAX_SOUNDS; // protocol 15 indices are sent as a byte
        for (int i = 0; i < maxSounds; i++)
        {
            if (Sv.sound_precache[i] == null)
            {
                Sv.sound_precache[i] = s;
                return;
            }
            if (Q.StrCmp(Sv.sound_precache[i], s) == 0)
                return;
        }
        Progs.RunError("PF_precache_sound: overflow"u8);
    }

    // #20, #75
    public static void PF_precache_model()
    {
        if (Sv.state != ServerState_t.Loading)
            Progs.RunError("PF_Precache_*: Precache can only be done in spawn functions"u8);

        byte* s = Progs.G_STRING(Progs.OFS_PARM0);
        Progs.RETURN_INT(Progs.G_INT(Progs.OFS_PARM0));
        CheckEmptyString(s);

        int maxModels = Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE ? Protocol.MAX_P15_MODELS : QuakeDef.MAX_MODELS; // protocol 15 indices are sent as a byte
        for (int i = 0; i < maxModels; i++)
        {
            if (Sv.model_precache[i] == null)
            {
                Sv.model_precache[i] = s;
                Sv.models[i] = Model.ForName(s, true);
                return;
            }
            if (Q.StrCmp(Sv.model_precache[i], s) == 0)
                return;
        }
        Progs.RunError("PF_precache_model: overflow"u8);
    }

    // #68, #77 -- precache_file is only used to copy files with qcc; it
    // does nothing at runtime.
    public static void PF_precache_file() => Progs.RETURN_INT(Progs.G_INT(Progs.OFS_PARM0));

    // #45
    public static void PF_cvar() => Progs.RETURN_FLOAT(Cvar.VariableValue(Progs.G_STRING(Progs.OFS_PARM0)));

    // #72
    public static void PF_cvar_set() => Cvar.Set(Progs.G_STRING(Progs.OFS_PARM0), Progs.G_STRING(Progs.OFS_PARM(1)));

    // #46
    public static void PF_localcmd() => Cmd.AddText(Progs.G_STRING(Progs.OFS_PARM0));

    // #25
    public static void PF_dprint() => Con.DPrintf($"{PF_VarString(0)}");

    // #10
    public static void PF_error()
    {
        byte* s = PF_VarString(0);
        Con.Printf($"======SERVER ERROR in {Progs.pr_strings + Progs.pr_xfunction->S_Name}:\n{s}\n");
        EDict_t* ed = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        Progs.PrintNum(Progs.NumForEdict(ed));
        Host.Error("Program error"u8);
    }

    // #11
    public static void PF_objerror()
    {
        byte* s = PF_VarString(0);
        Con.Printf($"======OBJECT ERROR in {Progs.pr_strings + Progs.pr_xfunction->S_Name}:\n{s}\n");
        EDict_t* ed = Progs.ProgToEdict(Progs.pr_global_struct->Self);
        Progs.PrintNum(Progs.NumForEdict(ed));
        Progs.Free(ed);
        Host.Error("Program error"u8);
    }

    // #28
    public static void PF_coredump() => Progs.PrintEdicts();

    // #31
    public static void PF_eprint() => Progs.PrintNum(Progs.G_EDICTNUM(Progs.OFS_PARM0));

    // #29
    public static void PF_traceon() => Progs.pr_trace = true;

    // #30
    public static void PF_traceoff() => Progs.pr_trace = false;

    // #6
    public static void PF_break()
    {
        Con.Printf("break statement\n"u8);
        System.Diagnostics.Debugger.Break();
    }

    /*
    ===============================================================================
    MESSAGE DELIVERY
    ===============================================================================
    */

    // #23
    public static void PF_bprint() => Host.BroadcastPrintf($"{PF_VarString(0)}");

    // #24 -- single print to a specific client ((int)Svc.Print into that
    // client's own message buffer, same as the original).
    public static void PF_sprint()
    {
        int entnum = Progs.G_EDICTNUM(Progs.OFS_PARM0);
        byte* s = PF_VarString(1);
        if (entnum < 1 || entnum > Svs.maxclients)
        {
            Con.Printf("tried to sprint to a non-client\n"u8);
            return;
        }
        Client_t* client = &Svs.clients[entnum - 1];
        Msg.WriteChar(&client->Message, (int)Svc.Print);
        Msg.WriteString(&client->Message, s);
    }

    // #73
    public static void PF_centerprint()
    {
        int entnum = Progs.G_EDICTNUM(Progs.OFS_PARM0);
        byte* s = PF_VarString(1);
        if (entnum < 1 || entnum > Svs.maxclients)
        {
            Con.Printf("tried to sprint to a non-client\n"u8);
            return;
        }
        Client_t* client = &Svs.clients[entnum - 1];
        Msg.WriteChar(&client->Message, (int)Svc.CenterPrint);
        Msg.WriteString(&client->Message, s);
    }

    // #8
    public static void PF_sound()
    {
        EDict_t* entity = Progs.G_EDICT(Progs.OFS_PARM0);
        int channel = (int)Progs.G_FLOAT(Progs.OFS_PARM(1));
        byte* sample = Progs.G_STRING(Progs.OFS_PARM(2));
        int volume = (int)(Progs.G_FLOAT(Progs.OFS_PARM(3)) * 255);
        float attenuation = Progs.G_FLOAT(Progs.OFS_PARM(4));

        if (volume < 0 || volume > 255)
            Sys.Error($"SV_StartSound: volume = {volume}");
        if (attenuation < 0 || attenuation > 4)
            Sys.Error($"SV_StartSound: attenuation = {attenuation:F6}");
        if (channel < 0 || channel > 7)
            Sys.Error($"SV_StartSound: channel = {channel}");

        SvPhys.StartSound(entity, channel, sample, volume, attenuation);
    }

    // #48
    public static void PF_particle()
    {
        ref Vec3_t org = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        ref Vec3_t dir = ref Progs.G_VECTOR(Progs.OFS_PARM(1));
        int color = (int)Progs.G_FLOAT(Progs.OFS_PARM(2));
        int count = (int)Progs.G_FLOAT(Progs.OFS_PARM(3));
        Sv.StartParticle(org, dir, color, count);
    }

    // #35
    public static void PF_lightstyle()
    {
        int style = (int)Progs.G_FLOAT(Progs.OFS_PARM0);
        byte* val = Progs.G_STRING(Progs.OFS_PARM(1));

        // change the string in sv
        Sv.lightstyles[style] = val;

        // send message to all clients on this server
        if (Sv.state != ServerState_t.Active)
            return;

        for (int j = 0; j < Svs.maxclients; j++)
        {
            Client_t* client = &Svs.clients[j];
            if (client->Active || client->Spawned)
            {
                Msg.WriteChar(&client->Message, (int)Svc.LightStyle);
                Msg.WriteChar(&client->Message, style);
                Msg.WriteString(&client->Message, val);
            }
        }
    }

    // #69
    public static void PF_makestatic()
    {
        EDict_t* ent = Progs.G_EDICT(Progs.OFS_PARM0);

        // johnfitz -- don't send invisible static entities
        if (Sv.UpdateEntAlpha(ent) == Protocol.ENTALPHA_ZERO)
        {
            Progs.Free(ent);
            return;
        }

        int bits = 0; // johnfitz -- PROTOCOL_FITZQUAKE
        int modelIndex = Sv.ModelIndex(Progs.pr_strings + ent->V.Model);
        if (Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE)
        {
            if ((modelIndex & 0xFF00) != 0 || ((int)ent->V.Frame & 0xFF00) != 0)
            {
                Progs.Free(ent);
                return; // can't display the correct model & frame, so don't show it at all
            }
        }
        else
        {
            if ((modelIndex & 0xFF00) != 0)
                bits |= Protocol.B_LARGEMODEL;
            if (((int)ent->V.Frame & 0xFF00) != 0)
                bits |= Protocol.B_LARGEFRAME;
            if (ent->Alpha != Protocol.ENTALPHA_DEFAULT)
                bits |= Protocol.B_ALPHA;
        }

        SizeBuf_t* sig = Sv.signon;
        if (bits != 0)
        {
            Msg.WriteByte(sig, (int)Svc.SpawnStatic2);
            Msg.WriteByte(sig, bits);
        }
        else
            Msg.WriteByte(sig, (int)Svc.SpawnStatic);

        if ((bits & Protocol.B_LARGEMODEL) != 0)
            Msg.WriteShort(sig, modelIndex);
        else
            Msg.WriteByte(sig, modelIndex);

        if ((bits & Protocol.B_LARGEFRAME) != 0)
            Msg.WriteShort(sig, (int)ent->V.Frame);
        else
            Msg.WriteByte(sig, (int)ent->V.Frame);

        Msg.WriteByte(sig, (int)ent->V.ColorMap);
        Msg.WriteByte(sig, (int)ent->V.Skin);
        for (int i = 0; i < 3; i++)
        {
            Msg.WriteCoord(sig, ent->V.Origin[i]);
            Msg.WriteAngle(sig, ent->V.Angles[i]);
        }

        if ((bits & Protocol.B_ALPHA) != 0)
            Msg.WriteByte(sig, ent->Alpha);

        // throw the entity away now
        Progs.Free(ent);
    }

    // #74
    public static void PF_ambientsound()
    {
        ref Vec3_t pos = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        byte* samp = Progs.G_STRING(Progs.OFS_PARM(1));
        float vol = Progs.G_FLOAT(Progs.OFS_PARM(2));
        float attenuation = Progs.G_FLOAT(Progs.OFS_PARM(3));

        // check to see if samp was properly precached
        int soundnum;
        for (soundnum = 0; soundnum < QuakeDef.MAX_SOUNDS && Sv.sound_precache[soundnum] != null; soundnum++)
            if (Q.StrCmp(Sv.sound_precache[soundnum], samp) == 0)
                break;

        if (soundnum >= QuakeDef.MAX_SOUNDS || Sv.sound_precache[soundnum] == null)
        {
            Con.Printf($"no precache: {samp}\n");
            return;
        }

        // johnfitz -- PROTOCOL_FITZQUAKE
        bool large = false;
        if (soundnum > 255)
        {
            if (Sv.current_protocol == Protocol.PROTOCOL_NETQUAKE)
                return; // don't send any info protocol can't support
            large = true;
        }

        // add an svc_spawnambient command to the level signon packet
        SizeBuf_t* sig = Sv.signon;
        Msg.WriteByte(sig, large ? (int)Svc.SpawnStaticSound2 : (int)Svc.SpawnStaticSound);
        for (int i = 0; i < 3; i++)
            Msg.WriteCoord(sig, pos[i]);

        if (large)
            Msg.WriteShort(sig, soundnum);
        else
            Msg.WriteByte(sig, soundnum);

        Msg.WriteByte(sig, (int)(vol * 255));
        Msg.WriteByte(sig, (int)(attenuation * 64));
    }

    // #21 -- PF_stuffcmd: queues the text as an svc_stufftext on the target
    // client's reliable message (Host_ClientCommands). The local player
    // receives it through the loopback connection like any other client.
    public static void PF_stuffcmd()
    {
        int entnum = Progs.G_EDICTNUM(Progs.OFS_PARM0);
        if (entnum < 1 || entnum > Svs.maxclients)
            Progs.RunError("Parm 0 not a client"u8);
        byte* str = Progs.G_STRING(Progs.OFS_PARM(1));

        Client_t* old = Svs.host_client;
        Svs.host_client = &Svs.clients[entnum - 1];
        Host.ClientCommands(str);
        Svs.host_client = old;
    }

    // #70
    public static void PF_changelevel()
    {
        if (Svs.changelevel_issued)
            return;
        Svs.changelevel_issued = true;

        byte* s = Progs.G_STRING(Progs.OFS_PARM0);
        Cmd.AddText($"changelevel {CStr.ToString(s)}\n");
    }

    /*
    ===============================================================================
    MESSAGE WRITING

    WriteDest now returns the real destination sizebuf_t (Sv.datagram/
    Svs.clients[n].Message/Sv.reliable_datagram/Sv.signon) instead of just
    validating and discarding it into a local `writeBuffer` -- SendClientMessages/
    UpdateToReliableMessages (Sv.cs) already read all four of these every
    frame, so a mod/QuakeC path using WriteByte et al now actually reaches
    the client, matching the original's MSG_BROADCAST/MSG_ONE/MSG_ALL/
    MSG_INIT routing exactly.
    ===============================================================================
    */

    private static SizeBuf_t* WriteDest()
    {
        int dest = (int)Progs.G_FLOAT(Progs.OFS_PARM0);
        switch (dest)
        {
            case (int)MsgDest.Broadcast: // unreliable to all
                return Sv.datagram;

            case (int)MsgDest.One: // reliable to one (msg_entity)
            {
                EDict_t* ent = Progs.ProgToEdict(Progs.pr_global_struct->MsgEntity);
                int entnum = Progs.NumForEdict(ent);
                if (entnum < 1 || entnum > Svs.maxclients)
                    Progs.RunError("WriteDest: not a client"u8);
                return &Svs.clients[entnum - 1].Message;
            }

            case (int)MsgDest.All: // reliable to all
                return Sv.reliable_datagram;

            case (int)MsgDest.Init: // write to the init string
                return Sv.signon;

            default:
                Progs.RunError("WriteDest: bad destination"u8);
                return null;
        }
    }

    // #52
    public static void PF_WriteByte() => Msg.WriteByte(WriteDest(), (int)Progs.G_FLOAT(Progs.OFS_PARM(1)));

    // #53
    public static void PF_WriteChar() => Msg.WriteChar(WriteDest(), (int)Progs.G_FLOAT(Progs.OFS_PARM(1)));

    // #54
    public static void PF_WriteShort() => Msg.WriteShort(WriteDest(), (int)Progs.G_FLOAT(Progs.OFS_PARM(1)));

    // #55
    public static void PF_WriteLong() => Msg.WriteLong(WriteDest(), (int)Progs.G_FLOAT(Progs.OFS_PARM(1)));

    // #57
    public static void PF_WriteAngle() => Msg.WriteAngle(WriteDest(), Progs.G_FLOAT(Progs.OFS_PARM(1)));

    // #56
    public static void PF_WriteCoord() => Msg.WriteCoord(WriteDest(), Progs.G_FLOAT(Progs.OFS_PARM(1)));

    // #58
    public static void PF_WriteString() => Msg.WriteString(WriteDest(), Progs.G_STRING(Progs.OFS_PARM(1)));

    // #59
    public static void PF_WriteEntity() => Msg.WriteShort(WriteDest(), Progs.G_EDICTNUM(Progs.OFS_PARM(1)));

    // #0 (unused), #5, #33, #39, #42, #50, #60-66 (QUAKE2-only), #71
    public static void PF_Fixme() => Progs.RunError("unimplemented builtin"u8);
}
