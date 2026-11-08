// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// COM_InitFilesystem / COM_AddGameDirectory (common.c) with the -rogue and
// -hipnotic mission packs, plus loading their real resources.
//
// The InitFilesystem_* tests are synthetic (temp directories holding tiny paks) and
// always run. The rest need the retail mission packs: configuration.json's optional
// "RogueDirectory" / "HipnoticDirectory" (see TestConfiguration) point at the
// rogue/ and hipnotic/ directories. When a key is blank, or the pak isn't there, the
// test reports Inconclusive instead of failing. Tests that also need id1 use
// "PakFilesDirectory" the same way.
//
// Com/Hunk/Model share static state with RealPakFileTests/CommonTests, so this can't
// run concurrently with them.
[TestClass]
public sealed unsafe class RealMissionPackTests
{
    private byte* _buf;
    private const int Size = 16 * 1024 * 1024;
    private string? _tempDir;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via Cmd.AddCommand,
        // which walks cmd_functions -- reset it first so that walk doesn't dangle
        // into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;
        Com.com_modified = false;
        Com.com_cachedir[0] = 0;
        Com.standard_quake = true;
        Com.rogue = false;
        Com.hipnotic = false;

        NativeMemory.Clear(Model.mod_known, (nuint)(Model.mod_numknown * sizeof(Model_t)));
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;
        Model.Init();
        Cache.Init();
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);
        Com.com_searchpaths = null;
        Com.rogue = false;
        Com.hipnotic = false;
        Com.standard_quake = true;

        NativeMemory.Free(_buf);

        // Best effort: -path drops the id1 search path COM_InitFilesystem had already
        // added (as in the original), leaving that pak's handle open with no way to
        // close it from here. Windows then refuses to delete the file.
        if (_tempDir != null)
        {
            try { Directory.Delete(_tempDir, true); }
            catch (IOException) { }
        }
    }

    // ---- helpers ----

    private static string? ConfiguredDirectory(string missionPack) => missionPack switch
    {
        "rogue" => TestConfiguration.RogueDirectory,
        "hipnotic" => TestConfiguration.HipnoticDirectory,
        _ => TestConfiguration.PakFilesDirectory,
    };

    // Returns the configured directory for `missionPack` ("id1", "rogue" or "hipnotic").
    private static string RequireDirectory(string missionPack)
    {
        string? dir = ConfiguredDirectory(missionPack);
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive($"The {missionPack} directory is not configured in configuration.json; skipping real mission pack test.");

        string pak = Path.Combine(dir, "pak0.pak");
        if (!File.Exists(pak) && !File.Exists(Path.Combine(dir, "PAK0.PAK")))
            Assert.Inconclusive($"pak0.pak was not found in the configured {missionPack} directory ({dir}); skipping real mission pack test.");

        return dir;
    }

    private static string FindPak0(string dir)
    {
        string lower = Path.Combine(dir, "pak0.pak");
        return File.Exists(lower) ? lower : Path.Combine(dir, "PAK0.PAK");
    }

    // Length of `name` as COM_FindFile reports it through the current search path
    // (-1 when it isn't found), closing the handle again.
    private static int FindFileLength(string name)
    {
        int handle;
        int len = Com.OpenFile(Ansi(name), &handle);
        if (handle != -1)
            Com.CloseFile(handle);
        return len;
    }

    private static string ReadFile(string name)
    {
        int handle;
        int len = Com.OpenFile(Ansi(name), &handle);
        Assert.IsGreaterThan(-1, len, $"{name} should be found on the search path");
        byte* buf = stackalloc byte[256];
        Sys.FileRead(handle, buf, Math.Min(len, 255));
        Com.CloseFile(handle);
        return Encoding.ASCII.GetString(buf, Math.Min(len, 255));
    }

    private static bool PackContains(Pack_t* pack, string name)
    {
        for (int i = 0; i < pack->NumFiles; i++)
            if (Marshal.PtrToStringAnsi((nint)pack->Files[i].Name) == name)
                return true;
        return false;
    }

    private static int PackFileLength(Pack_t* pack, string name)
    {
        for (int i = 0; i < pack->NumFiles; i++)
            if (Marshal.PtrToStringAnsi((nint)pack->Files[i].Name) == name)
                return pack->Files[i].FileLen;
        return -1;
    }

    private static void WritePakFile(string path, params (string Name, byte[] Payload)[] entries)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        int ofs = 12;
        int[] fileposes = new int[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            fileposes[i] = ofs;
            ofs += entries[i].Payload.Length;
        }

        bw.Write((byte)'P'); bw.Write((byte)'A'); bw.Write((byte)'C'); bw.Write((byte)'K');
        bw.Write(ofs);
        bw.Write(entries.Length * 64);

        foreach ((string _, byte[] payload) in entries)
            bw.Write(payload);

        for (int i = 0; i < entries.Length; i++)
        {
            byte[] nameBytes = new byte[56];
            Encoding.ASCII.GetBytes(entries[i].Name).CopyTo(nameBytes, 0);
            bw.Write(nameBytes);
            bw.Write(fileposes[i]);
            bw.Write(entries[i].Payload.Length);
        }
    }

    // basedir/{id1,rogue,hipnotic,mymod}/pak0.pak, each holding marker.txt (its own
    // directory name) so the winner of the search order can be read back. Only id1
    // also carries id1_only.txt.
    private string BuildSyntheticBaseDir()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"qtest_basedir_{Guid.NewGuid():N}");
        foreach (string name in new[] { "id1", "rogue", "hipnotic", "mymod" })
        {
            string dir = Path.Combine(_tempDir, name);
            Directory.CreateDirectory(dir);
            (string, byte[])[] entries = name == "id1"
                ? [("marker.txt", Encoding.ASCII.GetBytes(name)), ("id1_only.txt", Encoding.ASCII.GetBytes("base"))]
                : [("marker.txt", Encoding.ASCII.GetBytes(name))];
            WritePakFile(Path.Combine(dir, "pak0.pak"), entries);
        }
        return _tempDir;
    }

    private static void InitFilesystemWith(string baseDir, params string[] extraArgs)
    {
        byte*[] argv = new byte*[3 + extraArgs.Length];
        argv[0] = Ansi("quake");
        argv[1] = Ansi("-basedir");
        argv[2] = Ansi(baseDir.Replace('\\', '/'));
        for (int i = 0; i < extraArgs.Length; i++)
            argv[3 + i] = Ansi(extraArgs[i]);
        Com.InitArgv(argv.Length, argv);

        Com.InitFilesystem();
    }

    // ---- synthetic: COM_InitFilesystem search order ----

    [TestMethod]
    public void InitFilesystem_NoMissionFlags_SearchesId1Only()
    {
        InitFilesystemWith(BuildSyntheticBaseDir());

        Assert.AreEqual("id1", ReadFile("marker.txt"));
        Assert.EndsWith("/id1", Marshal.PtrToStringAnsi((nint)Com.com_gamedir)!);
        Assert.IsTrue(Com.standard_quake);
        Assert.IsFalse(Com.rogue);
        Assert.IsFalse(Com.hipnotic);
    }

    [TestMethod]
    [DataRow("-rogue", "rogue")]
    [DataRow("-hipnotic", "hipnotic")]
    public void InitFilesystem_MissionFlag_MissionPackShadowsId1(string flag, string missionPack)
    {
        InitFilesystemWith(BuildSyntheticBaseDir(), flag);

        Assert.AreEqual(missionPack, ReadFile("marker.txt"));
        Assert.EndsWith("/" + missionPack, Marshal.PtrToStringAnsi((nint)Com.com_gamedir)!);
        Assert.IsFalse(Com.standard_quake, "a mission pack flag clears standard_quake");
        Assert.AreEqual(flag == "-rogue", Com.rogue);
        Assert.AreEqual(flag == "-hipnotic", Com.hipnotic);
        Assert.IsGreaterThan(-1, FindFileLength("id1_only.txt"), "files the mission pack lacks still resolve from id1");
    }

    [TestMethod]
    public void InitFilesystem_RogueAndHipnotic_HipnoticIsSearchedFirst()
    {
        // COM_InitFilesystem adds id1, then rogue, then hipnotic; later paths go to
        // the head of com_searchpaths and so win.
        InitFilesystemWith(BuildSyntheticBaseDir(), "-rogue", "-hipnotic");

        Assert.AreEqual("hipnotic", ReadFile("marker.txt"));
        Assert.IsTrue(Com.rogue);
        Assert.IsTrue(Com.hipnotic);
    }

    [TestMethod]
    public void InitFilesystem_GameFlag_OverridesMissionPackAndSetsModified()
    {
        InitFilesystemWith(BuildSyntheticBaseDir(), "-hipnotic", "-game", "mymod");

        Assert.AreEqual("mymod", ReadFile("marker.txt"));
        Assert.IsTrue(Com.com_modified);
    }

    [TestMethod]
    public void InitFilesystem_PathFlag_ReplacesGeneratedSearchPath()
    {
        string baseDir = BuildSyntheticBaseDir();
        string roguePak = Path.Combine(baseDir, "rogue", "pak0.pak").Replace('\\', '/');

        // -path fully specifies the search path: id1 (added first) is dropped.
        // As in COM_InitFilesystem, a directory entry is not scanned for paks, so
        // the pak itself is named.
        InitFilesystemWith(baseDir, "-path", roguePak);

        Assert.AreEqual("rogue", ReadFile("marker.txt"));
        Assert.AreEqual(-1, FindFileLength("id1_only.txt"));
        Assert.IsTrue(Com.com_modified);
    }

    // ---- real mission pack data ----

    [TestMethod]
    [DataRow("hipnotic", "maps/hip1m1.bsp")]
    [DataRow("rogue", "maps/r1m1.bsp")]
    public void LoadPackFile_MissionPak0_ContainsProgsAndFirstMap(string missionPack, string firstMap)
    {
        string dir = RequireDirectory(missionPack);

        Pack_t* pack = Com.LoadPackFile(Ansi(FindPak0(dir).Replace('\\', '/')));

        Assert.IsTrue(pack != null, $"{missionPack} pak0.pak should load successfully");
        Assert.IsGreaterThan(0, pack->NumFiles);
        Assert.IsTrue(PackContains(pack, "progs.dat"), $"{missionPack} pak0.pak carries its own progs.dat");
        Assert.IsTrue(PackContains(pack, firstMap), $"{missionPack} pak0.pak should contain {firstMap}");
        Assert.IsTrue(Com.com_modified, "a mission pack is not the original id1 pak0 (COM_LoadPackFile)");
        Sys.FileClose(pack->Handle);
    }

    [TestMethod]
    [DataRow("hipnotic")]
    [DataRow("rogue")]
    public void AddGameDirectory_MissionPack_FindsPakFilesAndSetsGameDir(string missionPack)
    {
        string dir = RequireDirectory(missionPack).Replace('\\', '/');

        Com.AddGameDirectory(Ansi(dir));

        Assert.AreEqual(dir, Marshal.PtrToStringAnsi((nint)Com.com_gamedir));
        Assert.IsGreaterThan(-1, FindFileLength("progs.dat"), "progs.dat should resolve through pak0.pak");
    }

    [TestMethod]
    [DataRow("hipnotic", "maps/hip1m1.bsp")]
    [DataRow("rogue", "maps/r1m1.bsp")]
    public void ModForName_BrushModel_FromRealMissionPack_ParsesFirstMap(string missionPack, string firstMap)
    {
        string dir = RequireDirectory(missionPack).Replace('\\', '/');
        Com.AddGameDirectory(Ansi(dir));

        Model_t* mod = Model.ForName(Ansi(firstMap), true);

        Assert.IsTrue(mod != null, $"{firstMap} should load from the {missionPack} pak");
        Assert.AreEqual(ModType_t.Brush, mod->Type);
        Assert.IsGreaterThan(0, mod->NumVertexes);
        Assert.IsGreaterThan(0, mod->NumSurfaces);
        Assert.IsGreaterThan(0, mod->NumLeafs);
        Assert.IsGreaterThan(0, mod->NumTextures);
        Assert.IsTrue((nint)mod->Entities != 0, "Mod_LoadEntities should have parsed the entity lump");
    }

    [TestMethod]
    [DataRow("hipnotic")]
    [DataRow("rogue")]
    public void LoadProgs_FromRealMissionPack_ParsesMissionProgsDat(string missionPack)
    {
        string dir = RequireDirectory(missionPack).Replace('\\', '/');
        Com.AddGameDirectory(Ansi(dir));

        Progs.LoadProgs();

        Assert.IsTrue(Progs.progs != null, $"progs.dat should have loaded from the {missionPack} pak");
        Assert.AreEqual(Progs.PROG_VERSION, Progs.progs->Version);
        Assert.AreEqual(Progs.PROGHEADER_CRC, Progs.progs->Crc, "mission packs keep the retail progdefs layout");
        Assert.IsGreaterThan(0, Progs.progs->NumFunctions);
        Assert.IsGreaterThan(0, Progs.progs->NumStatements);
    }

    [TestMethod]
    [DataRow("hipnotic")]
    [DataRow("rogue")]
    public void FindFile_MissionPackAfterId1_ShadowsProgsAndFallsThroughForOtherFiles(string missionPack)
    {
        string id1 = RequireDirectory("id1").Replace('\\', '/');
        string mission = RequireDirectory(missionPack).Replace('\\', '/');

        Com.AddGameDirectory(Ansi(id1));
        int id1ProgsLength = FindFileLength("progs.dat");
        Com.AddGameDirectory(Ansi(mission));

        // the mission pack's own progs.dat wins over id1's...
        int missionProgsLength = -1;
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
        {
            if (s->Pack != null && PackContains(s->Pack, "progs.dat") &&
                Marshal.PtrToStringAnsi((nint)s->Pack->FileName)!.Contains(missionPack))
            {
                missionProgsLength = PackFileLength(s->Pack, "progs.dat");
                break;
            }
        }
        Assert.IsGreaterThan(0, missionProgsLength);
        Assert.AreNotEqual(id1ProgsLength, missionProgsLength, "the test needs distinguishable progs.dat files");
        Assert.AreEqual(missionProgsLength, FindFileLength("progs.dat"), "mission pack must shadow id1's progs.dat");

        // ...but resources the mission pack lacks still come from id1.
        Assert.IsGreaterThan(-1, FindFileLength("gfx/palette.lmp"));
        Assert.IsGreaterThan(-1, FindFileLength("maps/e1m1.bsp"));
    }

    [TestMethod]
    [DataRow("hipnotic", "-hipnotic")]
    [DataRow("rogue", "-rogue")]
    public void InitFilesystem_RealMissionPackFlag_LoadsMissionPakOverId1(string missionPack, string flag)
    {
        string dir = RequireDirectory(missionPack);
        string baseDir = Path.GetDirectoryName(dir)!;
        if (!File.Exists(Path.Combine(baseDir, "id1", "PAK0.PAK")) && !File.Exists(Path.Combine(baseDir, "id1", "pak0.pak")))
            Assert.Inconclusive($"{missionPack} has no sibling id1 directory with pak0.pak under {baseDir}; skipping.");

        InitFilesystemWith(baseDir, flag);

        Assert.EndsWith("/" + missionPack, Marshal.PtrToStringAnsi((nint)Com.com_gamedir)!);
        Assert.IsFalse(Com.standard_quake);
        Assert.IsGreaterThan(-1, FindFileLength("progs.dat"));
        Assert.IsGreaterThan(-1, FindFileLength("gfx/palette.lmp"), "id1 resources remain reachable");
    }
}
