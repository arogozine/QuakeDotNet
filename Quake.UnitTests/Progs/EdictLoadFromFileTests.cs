// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// pr_edict.c's ED_LoadFromFile (EdictSerialization.LoadFromFile). Wires up a
// fully synthetic minimal progs environment (no progs.dat file needed) the
// same way MainLoopTests'
// SetupTrivialProgs does, extended with real field/function name strings so
// ED_ParseEdict's FindField/FindFunction lookups actually resolve. Shares
// Sv/Progs/World's static state with SvPhysTests/ProgsTests/MainLoopTests,
// so it can't run concurrently with anything else touching that state.
[TestClass]
public sealed unsafe class EdictLoadFromFileTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private List<nint> _allocations = null!;
    private byte* _strings = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        _allocations = [];

        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;

        Svs.maxclients = 1;
        SaveGame.current_skill = 1;
        Host.deathmatch->Value = 0;

        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        // [0] world, [1] reserved for the (unconnected) client slot -- matches
        // Sv.SpawnServer's own `num_edicts = Svs.maxclients + 1;` before any
        // ED_Alloc call.
        Sv.edicts = AllocZeroed<EDict_t>(8);
        Sv.num_edicts = Svs.maxclients + 1;
        Sv.max_edicts = 8;
        Sv.time = 0;

        SetupTrivialProgsWithSpawnFunction();
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
            NativeMemory.Free((void*)p);
        NativeMemory.Free(_buf);
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged
    {
        T* p = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
        _allocations.Add((nint)p);
        return p;
    }

    private static int FieldOffset(string name) =>
        name switch
        {
            nameof(EntVars_t.ClassName) => Marshal.OffsetOf<EntVars_t>(nameof(EntVars_t.ClassName)).ToInt32() / 4,
            nameof(EntVars_t.SpawnFlags) => Marshal.OffsetOf<EntVars_t>(nameof(EntVars_t.SpawnFlags)).ToInt32() / 4,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

    // Builds pr_functions/pr_fielddefs/pr_strings so FindField("classname")/
    // FindField("spawnflags")/FindFunction("spawn_func") all resolve, and
    // function 1 ("spawn_func") is a trivial Op_t.Done body that just
    // records it ran (via a static counter) rather than doing real QuakeC
    // work.
    private void SetupTrivialProgsWithSpawnFunction()
    {
        // pr_strings layout: \0 classname\0 spawnflags\0 spawn_func\0
        const string classname = "classname";
        const string spawnflags = "spawnflags";
        const string spawnFunc = "spawn_func";
        byte[] stringTable = Encoding.ASCII.GetBytes($"\0{classname}\0{spawnflags}\0{spawnFunc}\0");
        int classnameOfs = 1;
        int spawnflagsOfs = classnameOfs + classname.Length + 1;
        int spawnFuncOfs = spawnflagsOfs + spawnflags.Length + 1;

        // Hunk-allocated like LoadProgs's own pr_strings (inside the
        // Hunk-loaded progs.dat), so EdictSerialization.NewString's
        // Hunk.Alloc'd strings resolve through PR_SetString.
        byte* strings = (byte*)Hunk.AllocName(stringTable.Length, "strings"u8);
        for (int i = 0; i < stringTable.Length; i++)
            strings[i] = stringTable[i];
        _strings = strings;
        Progs.pr_strings = strings;

        DPrograms_t* progs = AllocZeroed<DPrograms_t>(1);
        progs->NumFunctions = 2;
        progs->NumFieldDefs = 2;
        Progs.progs = progs;

        DFunction_t* functions = AllocZeroed<DFunction_t>(2);
        functions[1].First_Statement = 1;
        functions[1].S_Name = spawnFuncOfs;

        DStatement_t* statements = AllocZeroed<DStatement_t>(2);
        statements[1].Op = (ushort)Op_t.Done;

        DDef_t* fielddefs = AllocZeroed<DDef_t>(2);
        fielddefs[0].Type = (ushort)EType_t.String;
        fielddefs[0].Ofs = (ushort)FieldOffset(nameof(EntVars_t.ClassName));
        fielddefs[0].S_Name = classnameOfs;
        fielddefs[1].Type = (ushort)EType_t.Float;
        fielddefs[1].Ofs = (ushort)FieldOffset(nameof(EntVars_t.SpawnFlags));
        fielddefs[1].S_Name = spawnflagsOfs;

        Progs.pr_functions = functions;
        Progs.pr_statements = statements;
        Progs.pr_fielddefs = fielddefs;

        Progs.pr_global_struct->StartFrame = 1;
        Progs.pr_global_struct->PlayerPreThink = 1;
        Progs.pr_global_struct->PlayerPostThink = 1;
        Progs.pr_global_struct->SetNewParms = 1;
    }

    private static byte* Text(string s)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s + "\0");
        byte* p = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
            p[i] = bytes[i];
        return p;
    }

    [TestMethod]
    public void LoadFromFile_SingleEntityWithSpawnFunction_CallsSpawnAndBindsWorld()
    {
        byte* data = Text("{\n\"classname\" \"spawn_func\"\n}\n");

        EdictSerialization.LoadFromFile(data);

        EDict_t* world = Progs.EdictNum(0);
        Assert.AreEqual(0, world->Free);
        Assert.AreEqual(Svs.maxclients + 1, Sv.num_edicts); // only the world entity was parsed; no ED_Alloc calls made
        NativeMemory.Free(data);
    }

    [TestMethod]
    public void LoadFromFile_SecondEntityWithSpawnFunction_AllocatesAndSpawnsIt()
    {
        byte* data = Text(
            "{\n\"classname\" \"spawn_func\"\n}\n" +
            "{\n\"classname\" \"spawn_func\"\n}\n");

        EdictSerialization.LoadFromFile(data);

        Assert.AreEqual(Svs.maxclients + 2, Sv.num_edicts); // world (in place) + one ED_Alloc'd entity
        EDict_t* second = Progs.EdictNum(2);
        Assert.AreEqual(0, second->Free);
        NativeMemory.Free(data);
    }

    [TestMethod]
    public void LoadFromFile_NoClassname_FreesEntityWithoutSpawning()
    {
        byte* data = Text(
            "{\n\"classname\" \"spawn_func\"\n}\n" +
            "{\n\"spawnflags\" \"0\"\n}\n"); // second entity has no classname key

        EdictSerialization.LoadFromFile(data);

        // the second entity was still ED_Alloc'd before the missing-classname
        // check ran (matching the original's own order), then ED_Free'd.
        Assert.AreEqual(Svs.maxclients + 2, Sv.num_edicts);
        EDict_t* second = Progs.EdictNum(2);
        Assert.AreEqual(1, second->Free);
        NativeMemory.Free(data);
    }

    [TestMethod]
    public void LoadFromFile_UnknownSpawnFunction_FreesEntity()
    {
        byte* data = Text("{\n\"classname\" \"no_such_function\"\n}\n");

        EdictSerialization.LoadFromFile(data);

        EDict_t* world = Progs.EdictNum(0);
        Assert.AreEqual(1, world->Free); // ED_Free was called on it
    }

    [TestMethod]
    public void LoadFromFile_SpawnflagNotEasyOnEasySkill_InhibitsEntity()
    {
        SaveGame.current_skill = 0; // easy
        byte* data = Text(
            "{\n\"classname\" \"spawn_func\"\n}\n" +
            "{\n\"classname\" \"spawn_func\"\n\"spawnflags\" \"256\"\n}\n"); // SPAWNFLAG_NOT_EASY

        EdictSerialization.LoadFromFile(data);

        // ED_Alloc'd the second entity, but it was immediately ED_Free'd
        // (inhibited) rather than spawned -- num_edicts still grew by one.
        Assert.AreEqual(Svs.maxclients + 2, Sv.num_edicts);
        EDict_t* second = Progs.EdictNum(2);
        Assert.AreEqual(1, second->Free);
        NativeMemory.Free(data);
    }

    [TestMethod]
    public void LoadFromFile_DeathmatchInhibitsNotDeathmatchFlag()
    {
        Host.deathmatch->Value = 1;
        byte* data = Text(
            "{\n\"classname\" \"spawn_func\"\n}\n" +
            "{\n\"classname\" \"spawn_func\"\n\"spawnflags\" \"2048\"\n}\n"); // SPAWNFLAG_NOT_DEATHMATCH

        EdictSerialization.LoadFromFile(data);

        EDict_t* second = Progs.EdictNum(2);
        Assert.AreEqual(1, second->Free);
        NativeMemory.Free(data);
    }
}
