// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// EdictSerialization/Progs/Com share static state, so, like ProgsTests, these
// can't run concurrently with anything else touching them.
[TestClass]
public sealed unsafe class EdictSerializationTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    // field ofs layout (in 4-byte v-block slots):
    //   0..2 myvector (vector)   3 myfloat (float)   4 mystring (string)
    //   5 myentity (entity)      6 myfieldref (field) 7 myfunctionref (function)
    //   8..10 angles (vector, for the "angle" scalar-expansion hack)
    private const int OfsVector = 0;
    private const int OfsFloat = 3;
    private const int OfsString = 4;
    private const int OfsEntity = 5;
    private const int OfsFieldRef = 6;
    private const int OfsFunctionRef = 7;
    private const int OfsAngles = 8;
    private const int EdictSizeInts = 11;
    private const int MaxEdicts = 8;

    private List<nint> _allocations = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;
        _allocations = [];

        SetupSyntheticProgs();
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
        {
            NativeMemory.Free((void*)p);
        }

        NativeMemory.Free(_buf);
    }

    private byte* AllocBytes(byte[] bytes)
    {
        byte* p = NativeMemory.AllocArray<byte>(bytes.Length);
        _allocations.Add((nint)p);
        Marshal.Copy(bytes, 0, (nint)p, bytes.Length);
        return p;
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged
    {
        T* p = NativeMemory.AllocArrayZeroed<T>(count);
        _allocations.Add((nint)p);
        return p;
    }

    private static string Str(byte* s) => Marshal.PtrToStringAnsi((nint)s) ?? string.Empty;

    // Builds a self-contained set of pr_fielddefs/pr_globaldefs/pr_functions/
    // pr_strings/pr_globals directly (bypassing LoadProgs/progs.dat entirely
    // -- EdictSerialization only reads these already-swapped, already-parsed
    // arrays, so there's no need to round-trip through a binary file here).
    private void SetupSyntheticProgs()
    {
        // pr_strings: concatenated null-terminated names
        string[] names = ["", "myvector", "myfloat", "mystring", "myentity", "myfieldref", "myfunctionref", "angles", "myfunc", "myglobalstring", "myglobalfloat", "myglobalentity", "nonsaveglobal"];
        var offsets = new Dictionary<string, int>();
        using (var ms = new MemoryStream())
        {
            foreach (string n in names)
            {
                offsets[n] = (int)ms.Position;
                byte[] b = Encoding.ASCII.GetBytes(n + "\0");
                ms.Write(b, 0, b.Length);
            }
            // Hunk-allocated like LoadProgs's own pr_strings (inside the
            // Hunk-loaded progs.dat), so NewString_UnescapesBackslashN below
            // (via Hunk.Alloc + PR_SetString) resolves back through it.
            byte[] namesBytes = ms.ToArray();
            byte* strings = (byte*)Hunk.AllocName(namesBytes.Length, "strings"u8);
            Marshal.Copy(namesBytes, 0, (nint)strings, namesBytes.Length);
            Progs.pr_strings = strings;
        }

        // pr_fielddefs: [0] reserved dummy, then one per test field
        DDef_t* fields = AllocZeroed<DDef_t>(8);
        fields[0] = new DDef_t { Type = (ushort)EType_t.Void, Ofs = 0, S_Name = offsets[""] };
        fields[1] = new DDef_t { Type = (ushort)EType_t.Vector, Ofs = OfsVector, S_Name = offsets["myvector"] };
        fields[2] = new DDef_t { Type = (ushort)EType_t.Float, Ofs = OfsFloat, S_Name = offsets["myfloat"] };
        fields[3] = new DDef_t { Type = (ushort)EType_t.String, Ofs = OfsString, S_Name = offsets["mystring"] };
        fields[4] = new DDef_t { Type = (ushort)EType_t.Entity, Ofs = OfsEntity, S_Name = offsets["myentity"] };
        fields[5] = new DDef_t { Type = (ushort)EType_t.Field, Ofs = OfsFieldRef, S_Name = offsets["myfieldref"] };
        fields[6] = new DDef_t { Type = (ushort)EType_t.Function, Ofs = OfsFunctionRef, S_Name = offsets["myfunctionref"] };
        fields[7] = new DDef_t { Type = (ushort)EType_t.Vector, Ofs = OfsAngles, S_Name = offsets["angles"] };

        // pr_globaldefs: a handful of DEF_SAVEGLOBAL globals plus one that isn't
        DDef_t* globals = AllocZeroed<DDef_t>(4);
        globals[0] = new DDef_t { Type = (ushort)(EType_t.String | (EType_t)Progs.DEF_SAVEGLOBAL), Ofs = 0, S_Name = offsets["myglobalstring"] };
        globals[1] = new DDef_t { Type = (ushort)(EType_t.Float | (EType_t)Progs.DEF_SAVEGLOBAL), Ofs = 1, S_Name = offsets["myglobalfloat"] };
        globals[2] = new DDef_t { Type = (ushort)(EType_t.Entity | (EType_t)Progs.DEF_SAVEGLOBAL), Ofs = 2, S_Name = offsets["myglobalentity"] };
        globals[3] = new DDef_t { Type = (ushort)EType_t.Float, Ofs = 3, S_Name = offsets["nonsaveglobal"] }; // no SAVEGLOBAL bit

        // pr_functions: [0] reserved dummy, [1] "myfunc"
        DFunction_t* functions = AllocZeroed<DFunction_t>(2);
        functions[1].S_Name = offsets["myfunc"];

        float* globalsArr = (float*)AllocZeroed<float>(8);

        DPrograms_t* progs = AllocZeroed<DPrograms_t>(1);
        progs->NumFieldDefs = 8;
        progs->NumGlobalDefs = 4;
        progs->NumFunctions = 2;
        // the field defs above only use the first EdictSizeInts slots, but
        // edicts are real EDict_t structs, so entityfields covers all of
        // EntVars_t -- same as the stock progs.dat
        progs->EntityFields = sizeof(EntVars_t) / 4;

        Progs.progs = progs;
        Progs.pr_fielddefs = fields;
        Progs.pr_globaldefs = globals;
        Progs.pr_functions = functions;
        Progs.pr_globals = globalsArr;
        Progs.pr_global_struct = (GlobalVars_t*)globalsArr;
        Progs.pr_edict_size = sizeof(EDict_t);

        Sv.edicts = AllocZeroed<EDict_t>(MaxEdicts);
        Sv.num_edicts = MaxEdicts;
        Sv.max_edicts = MaxEdicts;
    }

    private static int* Fields(EDict_t* ent) => (int*)&ent->V;

    [TestMethod]
    public void FindField_ReturnsMatchingDef_OrNull()
    {
        DDef_t* def = EdictSerialization.FindField(Ansi("myfloat"));
        Assert.AreNotEqual(0, (nint)def);
        Assert.AreEqual(OfsFloat, def->Ofs);

        Assert.AreEqual(0, (nint)EdictSerialization.FindField(Ansi("nosuchfield")));
    }

    [TestMethod]
    public void FindGlobal_ReturnsMatchingDef_OrNull()
    {
        DDef_t* def = EdictSerialization.FindGlobal(Ansi("myglobalfloat"));
        Assert.AreNotEqual(0, (nint)def);
        Assert.AreEqual(1, def->Ofs);

        Assert.AreEqual(0, (nint)EdictSerialization.FindGlobal(Ansi("nosuchglobal")));
    }

    [TestMethod]
    public void FindFunction_ReturnsMatchingFunc_OrNull()
    {
        DFunction_t* f = EdictSerialization.FindFunction(Ansi("myfunc"));
        Assert.AreNotEqual(0, (nint)f);
        Assert.AreEqual(0, (nint)EdictSerialization.FindFunction(Ansi("nosuchfunc")));
    }

    [TestMethod]
    public void FieldAtOfs_ReturnsMatchingDef_OrNull()
    {
        DDef_t* def = EdictSerialization.FieldAtOfs(OfsString);
        Assert.AreNotEqual(0, (nint)def);
        Assert.AreEqual("mystring", Str(Progs.pr_strings + def->S_Name));

        Assert.AreEqual(0, (nint)EdictSerialization.FieldAtOfs(999));
    }

    [TestMethod]
    public void NewString_UnescapesBackslashN_AndBackslashBackslash()
    {
        int offset = EdictSerialization.NewString(Ansi("line1\\nline2\\\\end"));
        Assert.AreEqual("line1\nline2\\end", Str(Progs.pr_strings + offset));
    }

    [TestMethod]
    public void NewString_AllocatesFromHunk()
    {
        int lowBefore = Hunk.LowMark();

        int offset = EdictSerialization.NewString(Ansi("abc"));

        byte* p = Progs.pr_strings + offset;
        Assert.IsTrue(Hunk.LowMark() > lowBefore);
        Assert.IsTrue(p >= Hunk.hunk_base + lowBefore && p < Hunk.hunk_base + Hunk.LowMark());
    }

    [TestMethod]
    public void ParseEpair_FloatType_ParsesValue()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myfloat"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("3.5"));

        Assert.IsTrue(ok);
        Assert.AreEqual(3.5f, *(float*)(v + OfsFloat));
    }

    [TestMethod]
    public void ParseEpair_VectorType_ParsesThreeComponents()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myvector"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("1 2 3"));

        Assert.IsTrue(ok);
        float* fv = (float*)(v + OfsVector);
        Assert.AreEqual(1f, fv[0]);
        Assert.AreEqual(2f, fv[1]);
        Assert.AreEqual(3f, fv[2]);
    }

    [TestMethod]
    public void ParseEpair_StringType_InternsViaNewString()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("mystring"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("hi\\nthere"));

        Assert.IsTrue(ok);
        int strOfs = v[OfsString];
        Assert.AreEqual("hi\nthere", Str(Progs.pr_strings + strOfs));
    }

    [TestMethod]
    public void ParseEpair_EntityType_EncodesAsIndexTimesEdictSize()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myentity"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("5"));

        Assert.IsTrue(ok);
        Assert.AreEqual(5 * Progs.pr_edict_size, v[OfsEntity]);
    }

    [TestMethod]
    public void ParseEpair_FunctionType_ResolvesToFunctionIndex()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myfunctionref"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("myfunc"));

        Assert.IsTrue(ok);
        Assert.AreEqual(1, v[OfsFunctionRef]);
    }

    [TestMethod]
    public void ParseEpair_FunctionType_UnknownName_ReturnsFalse()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myfunctionref"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("nosuchfunc"));

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ParseEpair_FieldType_ReproducesOriginalGIntQuirk()
    {
        // ED_ParseEpair's ev_field case stores G_INT(def->ofs) -- a *global*
        // at that index, not the field's own offset -- reproduced verbatim.
        DDef_t* mystringField = EdictSerialization.FindField(Ansi("mystring"));
        *(int*)&Progs.pr_globals[mystringField->Ofs] = 0x1234;

        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myfieldref"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("mystring"));

        Assert.IsTrue(ok);
        Assert.AreEqual(0x1234, v[OfsFieldRef]);
    }

    [TestMethod]
    public void ParseEpair_FieldType_UnknownName_ReturnsFalse()
    {
        int* v = stackalloc int[EdictSizeInts];
        DDef_t* key = EdictSerialization.FindField(Ansi("myfieldref"));

        bool ok = EdictSerialization.ParseEpair(v, key, Ansi("nosuchfield"));

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ParseGlobals_ParsesKeyValuePairs_IntoPrGlobals()
    {
        EdictSerialization.ParseGlobals(Ansi("\"myglobalfloat\" \"7.5\" \"myglobalstring\" \"hello\" }"));

        Assert.AreEqual(7.5f, Progs.pr_globals[1]);
        int strOfs = *(int*)&Progs.pr_globals[0];
        Assert.AreEqual("hello", Str(Progs.pr_strings + strOfs));
    }

    [TestMethod]
    public void ParseGlobals_UnknownGlobal_SkipsWithoutThrowing()
    {
        EdictSerialization.ParseGlobals(Ansi("\"nosuchglobal\" \"1\" }"));
        // no assertion beyond "did not throw" -- matches the original's Con_Printf+continue
    }

    [TestMethod]
    public void ParseEdict_ParsesFieldsIntoEntVars()
    {
        EDict_t* ent = Progs.EdictNum(1);
        int* v = Fields(ent);

        EdictSerialization.ParseEdict(
            Ansi("\"myfloat\" \"2.25\" \"mystring\" \"hi\" }"), ent);

        Assert.AreEqual(0, ent->Free);
        Assert.AreEqual(2.25f, *(float*)(v + OfsFloat));
        Assert.AreEqual("hi", Str(Progs.pr_strings + v[OfsString]));
    }

    [TestMethod]
    public void ParseEdict_NoKeys_SetsFree()
    {
        EDict_t* ent = Progs.EdictNum(1);
        EdictSerialization.ParseEdict(Ansi("}"), ent);

        Assert.AreEqual(1, ent->Free);
    }

    [TestMethod]
    public void ParseEdict_NonWorldEdict_ClearsEntVarsFirst()
    {
        EDict_t* ent = Progs.EdictNum(1);
        int* v = Fields(ent);
        *(float*)(v + OfsFloat) = 99f;

        EdictSerialization.ParseEdict(Ansi("\"mystring\" \"hi\" }"), ent);

        Assert.AreEqual(0f, *(float*)(v + OfsFloat));
    }

    [TestMethod]
    public void ParseEdict_WorldEdict_IsNotCleared()
    {
        // "if (ent != sv.edicts) // hack" -- the world keeps whatever
        // SV_SpawnServer already put in it
        EDict_t* ent = Progs.EdictNum(0);
        int* v = Fields(ent);
        *(float*)(v + OfsFloat) = 99f;

        EdictSerialization.ParseEdict(Ansi("\"mystring\" \"hi\" }"), ent);

        Assert.AreEqual(99f, *(float*)(v + OfsFloat));
    }

    [TestMethod]
    public void ParseEdict_ClearDoesNotTouchNextEdict()
    {
        // the clear covers progs->entityfields * 4 bytes from &ent->v, so the
        // next edict's header must survive
        EDict_t* next = Progs.EdictNum(2);
        next->Free = 1;
        next->FreeTime = 123f;

        EdictSerialization.ParseEdict(Ansi("\"myfloat\" \"1\" }"), Progs.EdictNum(1));

        Assert.AreEqual(1, next->Free);
        Assert.AreEqual(123f, next->FreeTime);
    }

    [TestMethod]
    public void ParseEdict_LeadingUnderscoreKey_IsSkipped()
    {
        EDict_t* ent = Progs.EdictNum(1);
        int* v = Fields(ent);

        // "_comment" isn't a real field, but the underscore prefix means it's
        // discarded before ever reaching ED_FindField
        EdictSerialization.ParseEdict(Ansi("\"_comment\" \"whatever\" \"myfloat\" \"9\" }"), ent);

        Assert.AreEqual(0, ent->Free);
        Assert.AreEqual(9f, *(float*)(v + OfsFloat));
    }

    [TestMethod]
    public void ParseEdict_AngleHack_ExpandsScalarIntoVector()
    {
        EDict_t* ent = Progs.EdictNum(1);
        int* v = Fields(ent);

        // "angle" renames to "angles" and turns the scalar "45" into "0 45 0"
        EdictSerialization.ParseEdict(Ansi("\"angle\" \"45\" }"), ent);

        Assert.AreEqual(0, ent->Free);
        float* angles = (float*)(v + OfsAngles);
        Assert.AreEqual(0f, angles[0]);
        Assert.AreEqual(45f, angles[1]);
        Assert.AreEqual(0f, angles[2]);
    }

    [TestMethod]
    public void ParseEdict_LightHack_RenamesToLightLev()
    {
        // "light" renames to "light_lev"; since that field isn't registered
        // in this fixture, it's looked up, not found, logged, and skipped --
        // exercised here purely to confirm it doesn't throw or corrupt state.
        EDict_t* ent = Progs.EdictNum(1);
        int* v = Fields(ent);

        EdictSerialization.ParseEdict(Ansi("\"light\" \"200\" \"myfloat\" \"1\" }"), ent);

        Assert.AreEqual(0, ent->Free);
        Assert.AreEqual(1f, *(float*)(v + OfsFloat));
    }

    [TestMethod]
    public void Write_FreeEdict_WritesEmptyBraces()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            int handle = Sys.FileOpenWrite(Ansi(path));
            EDict_t* ent = Progs.EdictNum(1);
            ent->Free = 1;
            EdictSerialization.Write(handle, ent);
            Sys.FileClose(handle);

            Assert.AreEqual("{\n}\n", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Write_ThenParseEdict_RoundTripsNonZeroFields()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            EDict_t* ent = Progs.EdictNum(1);
            int* v = Fields(ent);
            *(float*)(v + OfsFloat) = 4.5f;
            float* vec = (float*)(v + OfsVector);
            vec[0] = 1f; vec[1] = 2f; vec[2] = 3f;
            v[OfsEntity] = Progs.EdictToProg(Progs.EdictNum(3));

            int handle = Sys.FileOpenWrite(Ansi(path));
            EdictSerialization.Write(handle, ent);
            Sys.FileClose(handle);

            byte[] bytes = File.ReadAllBytes(path);
            fixed (byte* p = bytes)
            {
                EDict_t* ent2 = Progs.EdictNum(2);
                int* v2 = Fields(ent2);
                // matches how Host_Loadgame_f's block scanner uses this: it
                // Com.Parse()s the opening "{" itself and hands ParseEdict
                // only what comes after.
                byte* start = Com.Parse(p);
                EdictSerialization.ParseEdict(start, ent2);

                Assert.AreEqual(0, ent2->Free);
                Assert.AreEqual(Progs.EdictToProg(Progs.EdictNum(3)), v2[OfsEntity]);
                Assert.AreEqual(4.5f, *(float*)(v2 + OfsFloat));
                float* vec2 = (float*)(v2 + OfsVector);
                Assert.AreEqual(1f, vec2[0]);
                Assert.AreEqual(2f, vec2[1]);
                Assert.AreEqual(3f, vec2[2]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Write_FloatField_UsesPrintfFFormat()
    {
        // pr_edict.c PR_UglyValueString: sprintf (line, "%f", val->_float)
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            EDict_t* ent = Progs.EdictNum(1);
            int* v = Fields(ent);
            *(float*)(v + OfsFloat) = 1.5f;
            float* vec = (float*)(v + OfsVector);
            vec[0] = 1f; vec[1] = -2.25f; vec[2] = 3f;

            int handle = Sys.FileOpenWrite(Ansi(path));
            EdictSerialization.Write(handle, ent);
            Sys.FileClose(handle);

            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "\"myfloat\" \"1.500000\"\n");
            StringAssert.Contains(text, "\"myvector\" \"1.000000 -2.250000 3.000000\"\n");
            StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex(":F6"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteGlobals_FloatGlobal_UsesPrintfFFormat()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Progs.pr_globals[1] = 42f; // myglobalfloat

            int handle = Sys.FileOpenWrite(Ansi(path));
            EdictSerialization.WriteGlobals(handle);
            Sys.FileClose(handle);

            StringAssert.Contains(File.ReadAllText(path), "\"myglobalfloat\" \"42.000000\"\n");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteGlobals_ThenParseGlobals_RoundTripsSaveGlobalFields()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Progs.pr_globals[1] = 42f; // myglobalfloat

            int handle = Sys.FileOpenWrite(Ansi(path));
            EdictSerialization.WriteGlobals(handle);
            Sys.FileClose(handle);

            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "\"myglobalfloat\"");
            StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("nonsaveglobal"));

            // reset and re-parse
            Progs.pr_globals[1] = 0f;
            byte[] bytes = File.ReadAllBytes(path);
            fixed (byte* p = bytes)
            {
                EdictSerialization.ParseGlobals(p + 2); // skip the leading "{\n"
            }

            Assert.AreEqual(42f, Progs.pr_globals[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
