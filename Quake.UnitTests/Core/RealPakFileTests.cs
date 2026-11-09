// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Loads real retail PAK0.PAK/PAK1.PAK files from disk. Opt-in: the location is read from
// configuration.json's "PakFilesDirectory" (see TestConfiguration), which is blank by
// default. When it's blank, or the expected file isn't found there, these tests report
// Inconclusive instead of failing, so CI/machines without a Quake install stay green.
//
// Beyond just parsing the pak directory, some tests here also register the pack as a
// search path (mirroring what COM_AddGameDirectory does for pakN.pak) and then run real
// file contents from inside it through their actual format loaders (Mod_ForName for
// .bsp/.mdl/.spr, PR_LoadProgs for progs.dat), proving those parsers work against real
// retail data, not just synthetic test fixtures.
//
// Com/Hunk/Model share static state with CommonTests/WadTests/ProgsTests/BspFileTests/
// AliasModelTests/SpriteModelTests, so this can't run concurrently with them.
[TestClass]
public sealed unsafe class RealPakFileTests
{
    private byte* _buf;
    private const int Size = 16 * 1024 * 1024;

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
        Com.com_modified = false;

        NativeMemory.Clear(Model.mod_known, Model.mod_numknown);
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;
        Model.Init();

        // Mod_LoadAliasModel/Mod_LoadSpriteModel cache their parsed data via
        // Cache_Alloc, which walks the cache_head sentinel -- it must be
        // initialized (as Host_Init does at real startup) before that runs.
        Cache.Init();
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(_buf);
    }

    private static string RequirePakFile(string fileName)
    {
        string? dir = TestConfiguration.PakFilesDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive("PakFilesDirectory is not configured in configuration.json; skipping real PAK file test.");

        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            Assert.Inconclusive($"{fileName} was not found in the configured PakFilesDirectory ({dir}); skipping real PAK file test.");

        return path;
    }

    // Registers an already-loaded pack as a search path, the same way
    // COM_AddGameDirectory links pakN.pak onto com_searchpaths, so Com.LoadFile
    // (and everything built on it -- Mod_ForName, PR_LoadProgs, ...) can find
    // files inside it by name.
    private static void RegisterPack(Pack_t* pack)
    {
        SearchPath_t* search = (SearchPath_t*)Hunk.Alloc(sizeof(SearchPath_t));
        search->Pack = pack;
        search->Next = Com.com_searchpaths;
        Com.com_searchpaths = search;
    }

    [TestMethod]
    public void LoadPackFile_Pak0_LoadsCoreGameData()
    {
        string path = RequirePakFile("PAK0.PAK");

        Pack_t* pack = Com.LoadPackFile(Ansi(path));

        Assert.IsTrue(pack != null, "PAK0.PAK should load successfully");
        Assert.IsGreaterThan(0, pack->NumFiles, "PAK0.PAK should contain files");

        bool foundProgs = false;
        for (int i = 0; i < pack->NumFiles; i++)
        {
            if (Marshal.PtrToStringAnsi((nint)pack->Files[i].Name) == "progs.dat")
            {
                foundProgs = true;
                break;
            }
        }

        Assert.IsTrue(foundProgs, "PAK0.PAK (core game data) should contain progs.dat");
    }

    [TestMethod]
    public void LoadPackFile_Pak1_LoadsEpisodeData()
    {
        string path = RequirePakFile("PAK1.PAK");

        Pack_t* pack = Com.LoadPackFile(Ansi(path));

        Assert.IsTrue(pack != null, "PAK1.PAK should load successfully");
        Assert.IsGreaterThan(0, pack->NumFiles, "PAK1.PAK (later episodes) should contain files");
    }

    [TestMethod]
    public void LoadProgs_FromRealPak0_ParsesRealProgsDat()
    {
        string path = RequirePakFile("PAK0.PAK");
        RegisterPack(Com.LoadPackFile(Ansi(path)));

        Progs.LoadProgs();

        Assert.IsTrue(Progs.progs != null, "progs.dat should have loaded from PAK0.PAK");
        Assert.AreEqual(Progs.PROG_VERSION, Progs.progs->Version);
        Assert.AreEqual(Progs.PROGHEADER_CRC, Progs.progs->Crc);
        Assert.IsGreaterThan(0, Progs.progs->NumFunctions);
        Assert.IsGreaterThan(0, Progs.progs->NumStatements);
        Assert.IsGreaterThan(0, Progs.progs->NumGlobalDefs);
        Assert.IsGreaterThan(0, Progs.progs->NumFieldDefs);
        Assert.IsGreaterThan(0, Progs.pr_edict_size);
    }

    [TestMethod]
    public void ModForName_AliasModel_FromRealPak0_ParsesRealPlayerMdl()
    {
        string path = RequirePakFile("PAK0.PAK");
        RegisterPack(Com.LoadPackFile(Ansi(path)));

        Model_t* mod = Model.ForName(Ansi("progs/player.mdl"), true);

        Assert.IsTrue(mod != null, "progs/player.mdl should load from PAK0.PAK");
        Assert.AreEqual(ModType_t.Alias, mod->Type);
        Assert.IsGreaterThan(0, mod->NumFrames);

        AliasHdr_t* hdr = (AliasHdr_t*)mod->Cache.Data;
        Assert.IsTrue(hdr != null, "the alias model should be resident in the cache after loading");

        Mdl_t* mdl = (Mdl_t*)AliasModel.Offset(hdr, hdr->Model);
        Assert.IsGreaterThan(0, mdl->NumSkins);
        Assert.IsGreaterThan(0, mdl->NumVerts);
        Assert.IsGreaterThan(0, mdl->NumTris);
    }

    [TestMethod]
    public void ModForName_BrushModel_FromRealPak0_ParsesRealE1M1Bsp()
    {
        string path = RequirePakFile("PAK0.PAK");
        RegisterPack(Com.LoadPackFile(Ansi(path)));

        Model_t* mod = Model.ForName(Ansi("maps/e1m1.bsp"), true);

        Assert.IsTrue(mod != null, "maps/e1m1.bsp should load from PAK0.PAK");
        Assert.AreEqual(ModType_t.Brush, mod->Type);
        Assert.IsGreaterThan(0, mod->NumVertexes);
        Assert.IsGreaterThan(0, mod->NumEdges);
        Assert.IsGreaterThan(0, mod->NumSurfaces);
        Assert.IsGreaterThan(0, mod->NumNodes);
        Assert.IsGreaterThan(0, mod->NumLeafs);
        Assert.IsGreaterThan(0, mod->NumTextures);
        Assert.IsGreaterThanOrEqualTo(1, mod->NumSubmodels);
        Assert.IsTrue((nint)mod->Entities != 0, "Mod_LoadEntities should have parsed the entity lump");
    }

    [TestMethod]
    public void ModForName_SpriteModel_FromRealPak0_ParsesRealExplosionSpr()
    {
        string path = RequirePakFile("PAK0.PAK");
        RegisterPack(Com.LoadPackFile(Ansi(path)));

        Model_t* mod = Model.ForName(Ansi("progs/s_explod.spr"), true);

        Assert.IsTrue(mod != null, "progs/s_explod.spr should load from PAK0.PAK");
        Assert.AreEqual(ModType_t.Sprite, mod->Type);

        MSprite_t* sprite = (MSprite_t*)mod->Cache.Data;
        Assert.IsTrue(sprite != null, "the sprite model should be resident in the cache after loading");
        Assert.IsGreaterThan(0, sprite->NumFrames);
    }

    [TestMethod]
    public void ModForName_BrushModel_FromRealPak1_ParsesRealE2M1Bsp()
    {
        string path = RequirePakFile("PAK1.PAK");
        RegisterPack(Com.LoadPackFile(Ansi(path)));

        Model_t* mod = Model.ForName(Ansi("maps/e2m1.bsp"), true);

        Assert.IsTrue(mod != null, "maps/e2m1.bsp should load from PAK1.PAK");
        Assert.AreEqual(ModType_t.Brush, mod->Type);
        Assert.IsGreaterThan(0, mod->NumVertexes);
        Assert.IsGreaterThan(0, mod->NumSurfaces);
    }
}
