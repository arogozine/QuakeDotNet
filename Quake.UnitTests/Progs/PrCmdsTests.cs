// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// PrCmds/Progs share static state (pr_globals/pr_strings/Sv.edicts/...), so
// like ProgsTests/CvarTests these can't run concurrently with anything else
// touching them. Rather than driving every builtin through a full synthetic
// progs.dat + ExecuteProgram round trip (ProgsTests.cs's BuildProgsDat
// pattern), most tests here call PF_* directly against a hand-populated
// pr_globals/pr_strings window -- builtins only ever read/write through
// Progs.pr_globals via OFS_PARM/OFS_RETURN, so this exercises the exact same
// code path without needing real bytecode. World-dependent builtins
// (setorigin, traceline, droptofloor, checkbottom, ...) need a real BSP
// world/area-node tree and are out of scope for this lightweight harness.
[TestClass]
public sealed unsafe class PrCmdsTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    private float* _globals;
    private byte* _strings;
    private const int GlobalsCount = 512;
    private const int StringsSize = 4096;

    private byte* _edictsBuf;
    private const int MaxEdicts = 16;
    private DPrograms_t* _progsHeader;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

        _globals = (float*)NativeMemory.AllocZeroed((nuint)(GlobalsCount * sizeof(float)));
        // pr_strings and pr_string_temp both live in the Hunk, matching
        // LoadProgs/PR_Init, so PR_NewString's offsets fit in a string_t.
        _strings = (byte*)Hunk.AllocName(StringsSize, "strings"u8);
        Progs.pr_globals = _globals;
        Progs.pr_global_struct = (GlobalVars_t*)_globals;
        Progs.pr_strings = _strings;
        Progs.AllocStringTemp();
        Progs.pr_argc = 0;

        Progs.pr_edict_size = sizeof(EDict_t);
        _edictsBuf = (byte*)NativeMemory.AllocZeroed((nuint)(Progs.pr_edict_size * MaxEdicts));
        Sv.edicts = (EDict_t*)_edictsBuf;
        Sv.max_edicts = MaxEdicts;
        Svs.maxclients = 1;
        Sv.num_edicts = Svs.maxclients + 1; // edict 0 = world, 1 = player slot

        // ED_ClearEdict (called by ED_Alloc/ED_Free's caller) reads
        // progs->EntityFields; point it at a minimal header describing an
        // edict whose entity-field block is exactly EntVars_t-sized.
        _progsHeader = (DPrograms_t*)NativeMemory.AllocZeroed((nuint)sizeof(DPrograms_t));
        _progsHeader->EntityFields = sizeof(EntVars_t) / 4;
        Progs.progs = _progsHeader;

        // PF_Write* (MESSAGE WRITING tests below) route into these real
        // destination sizebuf_t's now -- Hunk-allocated like Sv.cs's own
        // SpawnServer does, so no separate free is needed (MemoryManager
        // owns the whole arena, freed via _buf below).
        SizeBuf_t* dg = Sv.datagram; Sz.Alloc(dg, 1024);
        SizeBuf_t* rdg = Sv.reliable_datagram; Sz.Alloc(rdg, 1024);
        SizeBuf_t* sig = Sv.signon; Sz.Alloc(sig, 1024);
        Svs.clients[0] = default;
        Svs.clients[0].Message.Data = (byte*)NativeMemory.AllocZeroed(QuakeDef.MAX_MSGLEN);
        Svs.clients[0].Message.MaxSize = QuakeDef.MAX_MSGLEN;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(Svs.clients[0].Message.Data);
        NativeMemory.Free(_globals);
        NativeMemory.Free(_edictsBuf);
        NativeMemory.Free(_progsHeader);
        if (_readBuf != null)
            NativeMemory.Free(_readBuf);
        NativeMemory.Free(_buf);
    }

    private static string Str(byte* s) => Marshal.PtrToStringAnsi((nint)s) ?? string.Empty;

    private static string CaptureOutput(Action action)
    {
        TextWriter original = Console.Out;
        StringWriter sw = new();
        Console.SetOut(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    [TestMethod]
    public void PF_sound_AttenuationOutOfRange_MessageFormatsLikePrintfF()
    {
        // pr_cmds.c PF_sound: Sys_Error ("SV_StartSound: attenuation = %f", attenuation)
        Progs.G_FLOAT_SET(Progs.OFS_PARM(1), 0f);   // channel
        Progs.G_FLOAT_SET(Progs.OFS_PARM(3), 1f);   // volume
        Progs.G_FLOAT_SET(Progs.OFS_PARM(4), 5.5f); // attenuation

        var ex = Assert.ThrowsExactly<QuakeSysErrorException>(() => PrCmds.PF_sound());
        Assert.AreEqual("SV_StartSound: attenuation = 5.500000", ex.Message);
    }

    /*
    ===============================================================================
    MARSHALLING HELPERS
    ===============================================================================
    */

    [TestMethod]
    public void G_FLOAT_And_RETURN_FLOAT_RoundTrip()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 3.5f);
        Assert.AreEqual(3.5f, Progs.G_FLOAT(Progs.OFS_PARM0));

        Progs.RETURN_FLOAT(7.25f);
        Assert.AreEqual(7.25f, Progs.pr_globals[Progs.OFS_RETURN]);
    }

    [TestMethod]
    public void G_VECTOR_And_RETURN_VECTOR_RoundTrip()
    {
        Vec3_t v = default;
        v[0] = 1; v[1] = 2; v[2] = 3;
        Progs.G_VECTOR(Progs.OFS_PARM0) = v;

        ref Vec3_t readBack = ref Progs.G_VECTOR(Progs.OFS_PARM0);
        Assert.AreEqual(1f, readBack[0]);
        Assert.AreEqual(2f, readBack[1]);
        Assert.AreEqual(3f, readBack[2]);

        Progs.RETURN_VECTOR(v);
        Assert.AreEqual(1f, Progs.pr_globals[Progs.OFS_RETURN]);
        Assert.AreEqual(2f, Progs.pr_globals[Progs.OFS_RETURN + 1]);
        Assert.AreEqual(3f, Progs.pr_globals[Progs.OFS_RETURN + 2]);
    }

    [TestMethod]
    public void G_STRING_ResolvesThroughPrStrings()
    {
        Q.StrCpy(_strings + 10, Ansi("hello"));
        Progs.G_INT_SET(Progs.OFS_PARM0, 10);

        Assert.AreEqual("hello", Str(Progs.G_STRING(Progs.OFS_PARM0)));
        Assert.AreEqual(10, Progs.G_STRING_OFS(Progs.OFS_PARM0));
    }

    [TestMethod]
    public void G_EDICT_And_RETURN_EDICT_RoundTrip()
    {
        EDict_t* ed = Progs.EdictNum(0);
        Progs.RETURN_EDICT(ed);

        Assert.AreEqual(0, Progs.G_EDICTNUM(Progs.OFS_RETURN));
        Assert.IsTrue(Progs.G_EDICT(Progs.OFS_RETURN) == ed);
    }

    [TestMethod]
    public void OFS_PARM_SpacesParametersThreeFloatsApart()
    {
        Assert.AreEqual(Progs.OFS_PARM0, Progs.OFS_PARM(0));
        Assert.AreEqual(Progs.OFS_PARM0 + 3, Progs.OFS_PARM(1));
        Assert.AreEqual(Progs.OFS_PARM0 + 6, Progs.OFS_PARM(2));
    }

    /*
    ===============================================================================
    STRING ALLOCATOR
    ===============================================================================
    */

    [TestMethod]
    public void PR_NewString_ReturnsOffsetResolvingToCopiedText()
    {
        int ofs = Progs.PR_NewString(Ansi("scratch"));
        Assert.AreEqual("scratch", Str(Progs.pr_strings + ofs));
    }

    [TestMethod]
    public void PR_NewString_DistinctCallsDoNotAlias()
    {
        int ofsA = Progs.PR_NewString(Ansi("first"));
        int ofsB = Progs.PR_NewString(Ansi("second"));

        Assert.AreEqual("first", Str(Progs.pr_strings + ofsA));
        Assert.AreEqual("second", Str(Progs.pr_strings + ofsB));
    }

    /*
    ===============================================================================
    PURE MATH
    ===============================================================================
    */

    [TestMethod]
    public void PF_makevectors_MatchesAngleVectors()
    {
        Vec3_t angles = default;
        angles[0] = 0; angles[1] = 90; angles[2] = 0;
        Progs.G_VECTOR(Progs.OFS_PARM0) = angles;

        PrCmds.PF_makevectors();

        MathLib.AngleVectors(angles, out Vec3_t expectedF, out Vec3_t expectedR, out Vec3_t expectedU);

        Assert.AreEqual(expectedF[0], Progs.pr_global_struct->VForward[0], 1e-5f);
        Assert.AreEqual(expectedF[1], Progs.pr_global_struct->VForward[1], 1e-5f);
        Assert.AreEqual(expectedR[0], Progs.pr_global_struct->VRight[0], 1e-5f);
        Assert.AreEqual(expectedU[2], Progs.pr_global_struct->VUp[2], 1e-5f);
    }

    [TestMethod]
    public void PF_normalize_ScalesToUnitLength()
    {
        Vec3_t v = default;
        v[0] = 3; v[1] = 4; v[2] = 0;
        Progs.G_VECTOR(Progs.OFS_PARM0) = v;

        PrCmds.PF_normalize();

        Assert.AreEqual(0.6f, Progs.pr_globals[Progs.OFS_RETURN], 1e-5f);
        Assert.AreEqual(0.8f, Progs.pr_globals[Progs.OFS_RETURN + 1], 1e-5f);
    }

    [TestMethod]
    public void PF_vlen_ReturnsVectorLength()
    {
        Vec3_t v = default;
        v[0] = 3; v[1] = 4; v[2] = 0;
        Progs.G_VECTOR(Progs.OFS_PARM0) = v;

        PrCmds.PF_vlen();

        Assert.AreEqual(5f, Progs.pr_globals[Progs.OFS_RETURN], 1e-5f);
    }

    [TestMethod]
    public void PF_vectoyaw_MatchesOriginalFormula()
    {
        Vec3_t v = default;
        v[0] = 0; v[1] = 1; v[2] = 0;
        Progs.G_VECTOR(Progs.OFS_PARM0) = v;

        PrCmds.PF_vectoyaw();

        Assert.AreEqual(90f, Progs.pr_globals[Progs.OFS_RETURN], 1e-3f);
    }

    [TestMethod]
    public void PF_vectoangles_StraightUp_Returns90Pitch()
    {
        Vec3_t v = default;
        v[0] = 0; v[1] = 0; v[2] = 1;
        Progs.G_VECTOR(Progs.OFS_PARM0) = v;

        PrCmds.PF_vectoangles();

        Assert.AreEqual(90f, Progs.pr_globals[Progs.OFS_RETURN], 1e-5f);     // pitch
        Assert.AreEqual(0f, Progs.pr_globals[Progs.OFS_RETURN + 1], 1e-5f); // yaw
    }

    [TestMethod]
    public void PF_random_ReturnsValueInZeroToOneRange()
    {
        for (int i = 0; i < 50; i++)
        {
            PrCmds.PF_random();
            float r = Progs.pr_globals[Progs.OFS_RETURN];
            Assert.IsTrue(r >= 0f && r < 1f, $"PF_random returned {r}, expected [0,1)");
        }
    }

    [TestMethod]
    [DataRow(2.4f, 2f)]
    [DataRow(2.6f, 3f)]
    [DataRow(-2.6f, -3f)]
    public void PF_rint_RoundsToNearestInt(float input, float expected)
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, input);
        PrCmds.PF_rint();
        Assert.AreEqual(expected, Progs.pr_globals[Progs.OFS_RETURN]);
    }

    [TestMethod]
    public void PF_floor_And_PF_ceil_MatchMathF()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 2.7f);
        PrCmds.PF_floor();
        Assert.AreEqual(2f, Progs.pr_globals[Progs.OFS_RETURN]);

        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 2.1f);
        PrCmds.PF_ceil();
        Assert.AreEqual(3f, Progs.pr_globals[Progs.OFS_RETURN]);
    }

    [TestMethod]
    public void PF_fabs_ReturnsAbsoluteValue()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, -4.5f);
        PrCmds.PF_fabs();
        Assert.AreEqual(4.5f, Progs.pr_globals[Progs.OFS_RETURN]);
    }

    [TestMethod]
    public void PF_ftos_IntegralValue_FormatsWithoutDecimal()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 5f);
        PrCmds.PF_ftos();
        int ofs = Progs.G_INT(Progs.OFS_RETURN);
        Assert.AreEqual("5", Str(Progs.pr_strings + ofs));
    }

    [TestMethod]
    public void PF_ftos_FractionalValue_FormatsWithOneDecimal()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 5.7f);
        PrCmds.PF_ftos();
        int ofs = Progs.G_INT(Progs.OFS_RETURN);
        Assert.AreEqual("  5.7", Str(Progs.pr_strings + ofs));
    }

    [TestMethod]
    public void PF_vtos_FormatsAllThreeComponents()
    {
        Vec3_t v = default;
        v[0] = 1; v[1] = 2; v[2] = 3;
        Progs.G_VECTOR(Progs.OFS_PARM0) = v;

        PrCmds.PF_vtos();

        int ofs = Progs.G_INT(Progs.OFS_RETURN);
        Assert.AreEqual("'  1.0   2.0   3.0'", Str(Progs.pr_strings + ofs));
    }

    /*
    ===============================================================================
    CVAR
    ===============================================================================
    */

    [TestMethod]
    public void PF_cvar_ReturnsRegisteredValue()
    {
        Cvar_t* v = (Cvar_t*)NativeMemory.AllocZeroed((nuint)sizeof(Cvar_t));
        v->Name = Ansi("pr_cmds_test_cvar");
        v->String = Ansi("42");
        Cvar.RegisterVariable(v);

        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.PR_NewString(Ansi("pr_cmds_test_cvar")));
        PrCmds.PF_cvar();

        Assert.AreEqual(42f, Progs.pr_globals[Progs.OFS_RETURN]);
    }

    [TestMethod]
    public void PF_cvar_set_UpdatesRegisteredValue()
    {
        Cvar_t* v = (Cvar_t*)NativeMemory.AllocZeroed((nuint)sizeof(Cvar_t));
        v->Name = Ansi("pr_cmds_test_cvar2");
        v->String = Ansi("1");
        Cvar.RegisterVariable(v);

        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.PR_NewString(Ansi("pr_cmds_test_cvar2")));
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("99")));
        PrCmds.PF_cvar_set();

        Assert.AreEqual(99f, Cvar.VariableValue(Ansi("pr_cmds_test_cvar2")));
    }

    /*
    ===============================================================================
    EDICT ALLOC/FREE
    ===============================================================================
    */

    [TestMethod]
    public void PF_Spawn_AllocatesNewEdictPastPlayerSlots()
    {
        PrCmds.PF_Spawn();

        int newEdictNum = Progs.G_EDICTNUM(Progs.OFS_RETURN);
        Assert.AreEqual(2, newEdictNum); // 0 = world, 1 = player slot, 2 = first spawned
        Assert.AreEqual(3, Sv.num_edicts);
        Assert.AreEqual(0, Progs.EdictNum(2)->Free);
    }

    [TestMethod]
    public void PF_Spawn_ThenRemove_MarksEdictFree()
    {
        PrCmds.PF_Spawn();
        EDict_t* newEdict = Progs.G_EDICT(Progs.OFS_RETURN);

        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.EdictToProg(newEdict));
        PrCmds.PF_Remove();

        Assert.AreEqual(1, newEdict->Free);
    }

    /*
    ===============================================================================
    MESSAGE WRITING

    PF_Write* routes into the real destination sizebuf_t instead of a
    bookkeeping-only local list.
    ===============================================================================
    */

    [TestMethod]
    public void PF_WriteByte_Broadcast_WritesToSvDatagram()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, (float)MsgDest.Broadcast);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(1), 42);

        PrCmds.PF_WriteByte();

        Assert.AreEqual(1, Sv.datagram->CurSize);
        Assert.AreEqual((byte)42, Sv.datagram->Data[0]);
    }

    [TestMethod]
    public void PF_WriteLong_All_WritesToSvReliableDatagram()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, (float)MsgDest.All);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(1), 0x01020304);

        PrCmds.PF_WriteLong();

        Assert.AreEqual(4, Sv.reliable_datagram->CurSize);
        Assert.AreEqual((byte)0x04, Sv.reliable_datagram->Data[0]);
        Assert.AreEqual((byte)0x03, Sv.reliable_datagram->Data[1]);
        Assert.AreEqual((byte)0x02, Sv.reliable_datagram->Data[2]);
        Assert.AreEqual((byte)0x01, Sv.reliable_datagram->Data[3]);
    }

    [TestMethod]
    public void PF_WriteString_Init_WritesToSvSignon()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, (float)MsgDest.Init);

        // Progs.G_STRING reads pr_strings + offset, so the literal must live
        // inside the pr_strings arena, not a separately-allocated buffer.
        Q.StrCpy(Progs.pr_strings + 100, Ansi("hello"));
        Progs.G_INT_SET(Progs.OFS_PARM(1), 100);

        PrCmds.PF_WriteString();

        Assert.AreEqual((byte)'h', Sv.signon->Data[0]);
        Assert.AreEqual((byte)0, Sv.signon->Data[5]); // NUL terminator
        Assert.AreEqual(6, Sv.signon->CurSize);
    }

    [TestMethod]
    public void PF_WriteByte_One_WritesToTargetClientsMessage()
    {
        EDict_t* player = Progs.EdictNum(1);
        Progs.pr_global_struct->MsgEntity = Progs.EdictToProg(player);

        Progs.G_FLOAT_SET(Progs.OFS_PARM0, (float)MsgDest.One);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(1), 7);

        PrCmds.PF_WriteByte();

        Assert.AreEqual(1, Svs.clients[0].Message.CurSize);
        Assert.AreEqual((byte)7, Svs.clients[0].Message.Data[0]);
    }

    [TestMethod]
    public void PF_WriteByte_One_NonClientEntity_RunErrors()
    {
        EDict_t* world = Progs.EdictNum(0); // entnum 0 is never a client
        Progs.pr_global_struct->MsgEntity = Progs.EdictToProg(world);

        Progs.G_FLOAT_SET(Progs.OFS_PARM0, (float)MsgDest.One);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(1), 1);

        // PR_RunError now reaches the real Host_Error (Host.cs), which
        // unwinds via QuakeHostAbortException rather than crashing the
        // process like the Sys_Error substitute this test used to expect.
        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => PrCmds.PF_WriteByte());
    }

    [TestMethod]
    public void PF_WriteByte_BadDestination_RunErrors()
    {
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 99);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(1), 1);

        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => PrCmds.PF_WriteByte());
    }

    [TestMethod]
    public void PF_WriteEntity_Broadcast_WritesEdictNumAsShort()
    {
        EDict_t* ent = Progs.EdictNum(1);
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, (float)MsgDest.Broadcast);
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.EdictToProg(ent));

        PrCmds.PF_WriteEntity();

        Assert.AreEqual(2, Sv.datagram->CurSize);
        Assert.AreEqual((byte)1, Sv.datagram->Data[0]);
        Assert.AreEqual((byte)0, Sv.datagram->Data[1]);
    }

    /*
    ===============================================================================
    MESSAGE DELIVERY (real client message buffers / Sv.datagram / Sv.signon,
    now that real networking exists -- see PrCmds.cs's own type-level comment)
    ===============================================================================
    */

    // Points Msg's reader at a snapshot of `buf`'s bytes so Msg.Read* can
    // decode what a builtin wrote, the same way a real client would.
    private byte* _readBuf;

    private void BeginReadingFrom(in SizeBuf_t buf)
    {
        _readBuf = (byte*)NativeMemory.AllocZeroed((nuint)buf.CurSize);
        new Span<byte>(buf.Data, buf.CurSize).CopyTo(new Span<byte>(_readBuf, buf.CurSize));
        Net.net_message->Data = _readBuf; Net.net_message->MaxSize = buf.CurSize; Msg.net_message->CurSize = buf.CurSize;
        Msg.BeginReading();
    }

    [TestMethod]
    public void PF_bprint_WritesSvcPrintToEveryActiveSpawnedClient()
    {
        Svs.clients[0].Active = true;
        Svs.clients[0].Spawned = true;
        Progs.pr_argc = 1;
        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.PR_NewString(Ansi("hello all")));

        PrCmds.PF_bprint();

        Assert.AreEqual((byte)(int)Svc.Print, Svs.clients[0].Message.Data[0]);
        Assert.AreEqual("hello all", Str(Svs.clients[0].Message.Data + 1));
    }

    [TestMethod]
    public void PF_sprint_ValidClient_WritesSvcPrintToThatClientOnly()
    {
        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.EdictToProg(Progs.EdictNum(1))); // entnum 1
        Progs.pr_argc = 2;
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("just you")));

        PrCmds.PF_sprint();

        Assert.AreEqual((byte)(int)Svc.Print, Svs.clients[0].Message.Data[0]);
        Assert.AreEqual("just you", Str(Svs.clients[0].Message.Data + 1));
    }

    [TestMethod]
    public void PF_sprint_InvalidClient_PrintsErrorAndWritesNothing()
    {
        Progs.G_INT_SET(Progs.OFS_PARM0, 0);
        Progs.pr_argc = 2;
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("nope")));

        string output = CaptureOutput(() => PrCmds.PF_sprint());

        StringAssert.Contains(output, "tried to sprint to a non-client");
        Assert.AreEqual(0, Svs.clients[0].Message.CurSize);
    }

    [TestMethod]
    public void PF_centerprint_ValidClient_WritesSvcCenterprint()
    {
        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.EdictToProg(Progs.EdictNum(1)));
        Progs.pr_argc = 2;
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("center me")));

        PrCmds.PF_centerprint();

        Assert.AreEqual((byte)(int)Svc.CenterPrint, Svs.clients[0].Message.Data[0]);
        Assert.AreEqual("center me", Str(Svs.clients[0].Message.Data + 1));
    }

    [TestMethod]
    public void PF_stuffcmd_ValidClient_WritesSvcStuffTextToThatClientAndRestoresHostClient()
    {
        Client_t* oldHostClient = Svs.host_client;
        Svs.host_client = null;
        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.EdictToProg(Progs.EdictNum(1)));
        Progs.pr_argc = 2;
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("bf\n")));

        PrCmds.PF_stuffcmd();

        Assert.AreEqual((byte)(int)Svc.StuffText, Svs.clients[0].Message.Data[0]);
        Assert.AreEqual("bf\n", Str(Svs.clients[0].Message.Data + 1));
        Assert.IsTrue(Svs.host_client == null);
        Svs.host_client = oldHostClient;
    }

    [TestMethod]
    public void PF_error_UnwindsViaHostError_NotSysError()
    {
        Progs.pr_argc = 1;
        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.PR_NewString(Ansi("boom")));

        DFunction_t* oldFunction = Progs.pr_xfunction;
        Progs.pr_xfunction = (DFunction_t*)NativeMemory.AllocZeroed((nuint)sizeof(DFunction_t));
        try
        {
            Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => PrCmds.PF_error());
        }
        finally
        {
            NativeMemory.Free(Progs.pr_xfunction);
            Progs.pr_xfunction = oldFunction;
        }
    }

    [TestMethod]
    public void PF_particle_WritesSvcParticleToDatagram()
    {
        Progs.G_VECTOR(Progs.OFS_PARM0) = new Vec3_t { [0] = 1, [1] = 2, [2] = 3 };
        Progs.G_VECTOR(Progs.OFS_PARM(1)) = new Vec3_t { [0] = 0.5f, [1] = -0.5f, [2] = 0 };
        Progs.G_FLOAT_SET(Progs.OFS_PARM(2), 5); // color
        Progs.G_FLOAT_SET(Progs.OFS_PARM(3), 10); // count

        PrCmds.PF_particle();

        Assert.AreEqual((byte)(int)Svc.Particle, Sv.datagram->Data[0]);
        BeginReadingFrom(*Sv.datagram);
        Assert.AreEqual((int)Svc.Particle, Msg.ReadByte());
        Assert.AreEqual(1f, Msg.ReadCoord());
        Assert.AreEqual(2f, Msg.ReadCoord());
        Assert.AreEqual(3f, Msg.ReadCoord());
        Assert.AreEqual(8, Msg.ReadChar()); // 0.5 * 16
        Assert.AreEqual(-8, Msg.ReadChar());
        Assert.AreEqual(0, Msg.ReadChar());
        Assert.AreEqual(10, Msg.ReadByte());
        Assert.AreEqual(5, Msg.ReadByte());
    }

    [TestMethod]
    public void PF_lightstyle_ServerActive_BroadcastsToActiveOrSpawnedClients()
    {
        Sv.state = ServerState_t.Active;
        Svs.clients[0].Active = true;
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 3);
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("m")));

        PrCmds.PF_lightstyle();

        Assert.AreEqual("m", Str(Sv.lightstyles[3]));
        Assert.AreEqual((byte)(int)Svc.LightStyle, Svs.clients[0].Message.Data[0]);
        Assert.AreEqual((byte)3, Svs.clients[0].Message.Data[1]);
        Assert.AreEqual("m", Str(Svs.clients[0].Message.Data + 2));
    }

    [TestMethod]
    public void PF_lightstyle_ServerNotActive_DoesNotBroadcast()
    {
        Sv.state = ServerState_t.Loading;
        Svs.clients[0].Active = true;
        Progs.G_FLOAT_SET(Progs.OFS_PARM0, 2);
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("z")));

        PrCmds.PF_lightstyle();

        Assert.AreEqual(0, Svs.clients[0].Message.CurSize);
    }

    [TestMethod]
    public void PF_makestatic_WritesSvcSpawnstaticToSignonAndFreesEdict()
    {
        EDict_t* ent = Progs.EdictNum(1);
        ent->V.Model = Progs.PR_NewString(Ansi("progs/flame.mdl"));
        ent->V.Frame = 2;
        ent->V.ColorMap = 0;
        ent->V.Skin = 0;
        ent->V.Origin = new Vec3_t { [0] = 10, [1] = 20, [2] = 30 };
        Sv.model_precache[1] = Progs.pr_strings + ent->V.Model;
        Progs.G_INT_SET(Progs.OFS_PARM0, Progs.EdictToProg(ent));

        PrCmds.PF_makestatic();

        Assert.AreEqual((byte)(int)Svc.SpawnStatic, Sv.signon->Data[0]);
        Assert.AreEqual(1, ent->Free);
    }

    [TestMethod]
    public void PF_ambientsound_Precached_WritesSvcSpawnstaticsoundToSignon()
    {
        Sv.sound_precache[0] = Progs.PR_NewString(Ansi("ambience/drone.wav")) + Progs.pr_strings;
        Progs.G_VECTOR(Progs.OFS_PARM0) = new Vec3_t { [0] = 1, [1] = 2, [2] = 3 };
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("ambience/drone.wav")));
        Progs.G_FLOAT_SET(Progs.OFS_PARM(2), 1.0f);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(3), 1.0f);

        PrCmds.PF_ambientsound();

        Assert.AreEqual((byte)(int)Svc.SpawnStaticSound, Sv.signon->Data[0]);
    }

    [TestMethod]
    public void PF_ambientsound_NotPrecached_PrintsErrorAndWritesNothing()
    {
        Progs.G_VECTOR(Progs.OFS_PARM0) = default;
        Progs.G_INT_SET(Progs.OFS_PARM(1), Progs.PR_NewString(Ansi("missing.wav")));
        Progs.G_FLOAT_SET(Progs.OFS_PARM(2), 1.0f);
        Progs.G_FLOAT_SET(Progs.OFS_PARM(3), 1.0f);

        string output = CaptureOutput(() => PrCmds.PF_ambientsound());

        StringAssert.Contains(output, "no precache");
        Assert.AreEqual(0, Sv.signon->CurSize);
    }

    /*
    ===============================================================================
    TABLE COMPLETENESS
    ===============================================================================
    */

    [TestMethod]
    public void pr_builtins_HasSeventyNineEntries() => Assert.AreEqual(79, Progs.pr_numbuiltins);

    [TestMethod]
    public void pr_builtins_UnusedSlotZero_ThrowsRunError()
    {
        Assert.ThrowsExactly<Host.QuakeHostAbortException>(() => PrCmds.PF_Fixme());
    }
}
