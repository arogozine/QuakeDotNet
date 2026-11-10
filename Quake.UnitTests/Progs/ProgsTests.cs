// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Progs/Com share static state, so, like CommonTests/WadTests, these can't
// run concurrently with each other.
[TestClass]
public sealed unsafe class ProgsTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

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
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
        {
            if (s->Pack != null)
            {
                Sys.FileClose(s->Pack->Handle);
            }
        }

        NativeMemory.Free(_buf);
    }

    // Builds a minimal synthetic progs.dat: header + 1 statement + 1 function +
    // 1 globaldef + 1 fielddef + strings + 2 globals.
    private static byte[] BuildProgsDat(int version = Progs.PROG_VERSION, int crc = Progs.PROGHEADER_CRC,
        bool fieldDefSaveGlobal = false)
    {
        const int headerSize = 60; // 15 ints
        const int statementSize = 8;
        const int functionSize = 36; // 7 ints (28) + Parm_Size[8]
        const int defSize = 8;

        int ofsStatements = headerSize;
        int ofsFunctions = ofsStatements + statementSize;
        int ofsGlobalDefs = ofsFunctions + functionSize;
        int ofsFieldDefs = ofsGlobalDefs + defSize;
        int ofsStrings = ofsFieldDefs + defSize;

        byte[] strings = Encoding.ASCII.GetBytes("\0hello\0");
        int ofsGlobals = ofsStrings + strings.Length;
        float[] globals = [1.5f, -2.5f];

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // dprograms_t
        bw.Write(version);
        bw.Write(crc);
        bw.Write(ofsStatements);
        bw.Write(1); // numstatements
        bw.Write(ofsGlobalDefs);
        bw.Write(1); // numglobaldefs
        bw.Write(ofsFieldDefs);
        bw.Write(1); // numfielddefs
        bw.Write(ofsFunctions);
        bw.Write(1); // numfunctions
        bw.Write(ofsStrings);
        bw.Write(strings.Length);
        bw.Write(ofsGlobals);
        bw.Write(globals.Length);
        bw.Write(3); // entityfields

        // dstatement_t
        bw.Write((ushort)5); // op
        bw.Write((short)1); bw.Write((short)2); bw.Write((short)3);

        // dfunction_t
        bw.Write(10); // first_statement
        bw.Write(0);  // parm_start
        bw.Write(4);  // locals
        bw.Write(0);  // profile
        bw.Write(1);  // s_name
        bw.Write(0);  // s_file
        bw.Write(2);  // numparms
        bw.Write(new byte[8]); // parm_size

        // ddef_t (globaldef)
        bw.Write((ushort)EType_t.Float);
        bw.Write((ushort)0); // ofs
        bw.Write(1); // s_name

        // ddef_t (fielddef)
        bw.Write((ushort)((int)EType_t.Float | (fieldDefSaveGlobal ? Progs.DEF_SAVEGLOBAL : 0)));
        bw.Write((ushort)0); // ofs
        bw.Write(1); // s_name

        bw.Write(strings);

        foreach (float g in globals)
        {
            bw.Write(g);
        }

        return ms.ToArray();
    }

    private static string WriteProgsDatToGamedir(byte[] bytes)
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        File.WriteAllBytes(Path.Combine(gamedir, "progs.dat"), bytes);
        return gamedir;
    }

    [TestMethod]
    public void LoadProgs_ParsesHeaderCountsAndSwapsLumps()
    {
        string gamedir = WriteProgsDatToGamedir(BuildProgsDat());
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Progs.LoadProgs();

            Assert.AreEqual(Progs.PROG_VERSION, Progs.progs->Version);
            Assert.AreEqual(Progs.PROGHEADER_CRC, Progs.progs->Crc);
            Assert.AreEqual(1, Progs.progs->NumStatements);
            Assert.AreEqual(1, Progs.progs->NumFunctions);
            Assert.AreEqual(1, Progs.progs->NumGlobalDefs);
            Assert.AreEqual(1, Progs.progs->NumFieldDefs);
            Assert.AreEqual(2, Progs.progs->NumGlobals);
            Assert.AreEqual(3, Progs.progs->EntityFields);
            // pr_edict_size = entityfields*4 + sizeof(edict_t) - sizeof(entvars_t)
            unsafe
            {
                Assert.AreEqual((3 * 4) + sizeof(EDict_t) - sizeof(EntVars_t), Progs.pr_edict_size);
            }

            Assert.AreEqual((ushort)5, Progs.pr_statements[0].Op);
            Assert.AreEqual((short)1, Progs.pr_statements[0].A);
            Assert.AreEqual((short)2, Progs.pr_statements[0].B);
            Assert.AreEqual((short)3, Progs.pr_statements[0].C);

            Assert.AreEqual(10, Progs.pr_functions[0].First_Statement);
            Assert.AreEqual(2, Progs.pr_functions[0].NumParms);

            Assert.AreEqual((ushort)EType_t.Float, Progs.pr_globaldefs[0].Type);
            Assert.AreEqual(1, Progs.pr_globaldefs[0].S_Name);
            Assert.AreEqual((ushort)EType_t.Float, Progs.pr_fielddefs[0].Type);

            Assert.AreEqual("hello", Marshal.PtrToStringAnsi((nint)(Progs.pr_strings + 1)));

            // pr_strings stays inside the Hunk-loaded progs.dat, as in the
            // original, so Hunk-allocated strings resolve via PR_SetString.
            Assert.IsTrue(Progs.pr_strings >= Hunk.hunk_base && Progs.pr_strings < Hunk.hunk_base + Hunk.hunk_size);

            Assert.AreEqual(1.5f, Progs.pr_globals[0]);
            Assert.AreEqual(-2.5f, Progs.pr_globals[1]);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void LoadProgs_WrongVersion_ThrowsSysError()
    {
        string gamedir = WriteProgsDatToGamedir(BuildProgsDat(version: 99));
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Assert.ThrowsExactly<QuakeSysErrorException>(() => Progs.LoadProgs());
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void LoadProgs_WrongCrc_ThrowsSysError()
    {
        string gamedir = WriteProgsDatToGamedir(BuildProgsDat(crc: 1234));
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Assert.ThrowsExactly<QuakeSysErrorException>(() => Progs.LoadProgs());
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void LoadProgs_FieldDefWithSaveGlobalBit_ThrowsSysError()
    {
        string gamedir = WriteProgsDatToGamedir(BuildProgsDat(fieldDefSaveGlobal: true));
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Assert.ThrowsExactly<QuakeSysErrorException>(() => Progs.LoadProgs());
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void PR_SetString_HunkPointer_RoundTripsThroughPrStrings()
    {
        Progs.pr_strings = (byte*)Hunk.AllocName(16, "strings"u8);
        byte* later = (byte*)Hunk.AllocName(16, "later"u8);

        int ofs = Progs.PR_SetString(later);

        Assert.IsTrue(later == Progs.pr_strings + ofs);
    }

    [TestMethod]
    public void PR_SetString_PointerBeforePrStrings_GivesNegativeOffset()
    {
        // e.g. Svs.clients names, allocated before progs.dat in the Hunk
        byte* earlier = (byte*)Hunk.AllocName(16, "earlier"u8);
        Progs.pr_strings = (byte*)Hunk.AllocName(16, "strings"u8);

        int ofs = Progs.PR_SetString(earlier);

        Assert.IsTrue(ofs < 0);
        Assert.IsTrue(earlier == Progs.pr_strings + ofs);
    }

    [TestMethod]
    public void PR_SetString_PointerOutOfInt32Range_ThrowsSysError()
    {
        Progs.pr_strings = (byte*)Hunk.AllocName(16, "strings"u8);

        Assert.ThrowsExactly<QuakeSysErrorException>(() => Progs.PR_SetString(Progs.pr_strings + (long)int.MaxValue + 1));
    }
}
