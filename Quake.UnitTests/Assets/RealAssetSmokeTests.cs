// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Smoke tests: every .bsp (Mod_LoadBrushModel), every .mdl (Mod_LoadAliasModel) and
// gfx.wad (W_LoadWadFile, model.c / wad.c) found in the real game directories load
// without Sys.Error and with sane counts. Opt-in like RealPakFileTests: the directories
// come from configuration.json ("PakFilesDirectory", "RogueDirectory",
// "HipnoticDirectory", see TestConfiguration) and the test is Inconclusive when one is
// not configured or has no pak0.pak. Failures are collected so one run reports every
// bad asset rather than only the first.
//
// Com/Hunk/Model share static state with RealPakFileTests/CommonTests, so this can't
// run concurrently with them.
[TestClass]
public sealed unsafe class RealAssetSmokeTests
{
    private byte* _buf;
    private const int Size = 64 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;
        Com.com_modified = false;
        ResetModels();
        Cache.Init();
        // built by R_Init at startup; Mod_LoadTexinfo falls back to it for textures with no data
        RMain.InitTextures();
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);
        Com.com_searchpaths = null;

        NativeMemory.Free(_buf);
    }

    private static void ResetModels()
    {
        NativeMemory.Clear(Model.mod_known, Model.mod_numknown);
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        Model.Init();
    }

    private static string RequireDirectory(string key)
    {
        string? dir = key switch
        {
            "id1" => TestConfiguration.PakFilesDirectory,
            "rogue" => TestConfiguration.RogueDirectory,
            _ => TestConfiguration.HipnoticDirectory,
        };
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive($"The {key} directory is not configured in configuration.json; skipping real asset smoke test.");
        if (!File.Exists(Path.Combine(dir, "pak0.pak")) && !File.Exists(Path.Combine(dir, "PAK0.PAK")))
            Assert.Inconclusive($"pak0.pak was not found in the configured {key} directory ({dir}); skipping real asset smoke test.");
        return dir.Replace('\\', '/');
    }

    // Names of every file with `extension` in every pack on the search path.
    private static List<string> PackFileNames(string extension)
    {
        List<string> names = [];
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
        {
            if (s->Pack == null)
                continue;
            for (int i = 0; i < s->Pack->NumFiles; i++)
            {
                string name = Marshal.PtrToStringAnsi((nint)s->Pack->Files[i].Name)!;
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && !names.Contains(name))
                    names.Add(name);
            }
        }
        return names;
    }

    private delegate string? ModelCheck(string name, Model_t* mod);

    // Loads each named model on its own, frees what it allocated, and returns the
    // failures. `check` returns an error message or null.
    private static List<string> LoadEach(List<string> names, ModelCheck check)
    {
        List<string> failures = [];
        int mark = Hunk.LowMark();
        foreach (string name in names)
        {
            try
            {
                Model_t* mod = Model.ForName(Ansi(name), true);
                string? error = mod == null ? "ForName returned null" : check(name, mod);
                if (error != null)
                    failures.Add($"{name}: {error}");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: {e.GetType().Name} {e.Message}\n{e.StackTrace}");
            }

            Hunk.FreeToLowMark(mark);
            ResetModels();
            Cache.Init();
        }
        return failures;
    }

    [TestMethod]
    [DataRow("id1", 0)]
    [DataRow("rogue", 0)]
    [DataRow("hipnotic", 0)]
    public void ModForName_EveryBspInPak_LoadsBrushModel(string pack, int _)
    {
        Com.AddGameDirectory(Ansi(RequireDirectory(pack)));
        List<string> maps = PackFileNames(".bsp");
        Assert.IsNotEmpty(maps, $"{pack} should contain .bsp files");

        List<string> failures = LoadEach(maps, (name, mod) =>
        {
            if (mod->Type != ModType_t.Brush) return $"type {mod->Type}, expected Brush";
            if (mod->NumVertexes <= 0) return "no vertexes";
            if (mod->NumEdges <= 0) return "no edges";
            if (mod->NumSurfaces <= 0) return "no surfaces";
            if (mod->NumNodes <= 0) return "no nodes";
            if (mod->NumLeafs <= 0) return "no leafs";
            if (mod->NumSubmodels < 1) return "no submodels";
            if ((nint)mod->Entities == 0) return "no entity lump";
            return null;
        });

        Assert.IsEmpty(failures, $"{failures.Count}/{maps.Count} {pack} maps failed:\n{string.Join('\n', failures)}");
    }

    [TestMethod]
    [DataRow("id1", 0)]
    [DataRow("rogue", 0)]
    [DataRow("hipnotic", 0)]
    public void ModForName_EveryMdlInPak_LoadsAliasModel(string pack, int _)
    {
        Com.AddGameDirectory(Ansi(RequireDirectory(pack)));
        List<string> models = PackFileNames(".mdl");
        Assert.IsNotEmpty(models, $"{pack} should contain .mdl files");

        List<string> failures = LoadEach(models, (name, mod) =>
        {
            if (mod->Type != ModType_t.Alias) return $"type {mod->Type}, expected Alias";
            if (mod->NumFrames <= 0) return "no frames";
            AliasHdr_t* hdr = (AliasHdr_t*)mod->Cache.Data;
            if (hdr == null) return "not resident in the cache";
            Mdl_t* mdl = (Mdl_t*)AliasModel.Offset(hdr, hdr->Model);
            if (mdl->NumSkins <= 0) return "no skins";
            if (mdl->NumVerts <= 0) return "no verts";
            if (mdl->NumTris <= 0) return "no tris";
            return null;
        });

        Assert.IsEmpty(failures, $"{failures.Count}/{models.Count} {pack} models failed:\n{string.Join('\n', failures)}");
    }

    [TestMethod]
    public void LoadWadFile_RealGfxWad_ParsesEveryLumpAndKnownPics()
    {
        Com.AddGameDirectory(Ansi(RequireDirectory("id1")));

        Wad.LoadWadFile(Ansi("gfx.wad"));

        Assert.IsGreaterThan(0, Wad.wad_numlumps);
        Assert.IsTrue(Wad.wad_lumps != null);

        // every lump resolves by name and by number to the same bytes, and lies inside the wad
        for (int i = 0; i < Wad.wad_numlumps; i++)
        {
            LumpInfo_t* lump = Wad.wad_lumps + i;
            string name = Marshal.PtrToStringAnsi((nint)lump->Name)!;
            Assert.IsGreaterThanOrEqualTo(0, lump->FilePos, $"lump {i} ({name}) FilePos");
            Assert.IsGreaterThanOrEqualTo(0, lump->Size, $"lump {i} ({name}) Size");
            Assert.IsTrue(Wad.GetLumpNum(i) == Wad.wad_base + lump->FilePos, $"lump {i} ({name}) address");
            Assert.IsTrue(Wad.GetLumpName(Ansi(name)) != null, $"lump {name} should be findable by name");
        }

        // known retail lumps: the console font and the status bar / menu pics
        foreach (string pic in new[] { "num_0", "ibar", "face1", "disc", "sb_armor1" })
        {
            QPic_t* qpic = (QPic_t*)Wad.GetLumpName(Ansi(pic));
            Assert.IsGreaterThan(0, qpic->Width, $"{pic} width");
            Assert.IsGreaterThan(0, qpic->Height, $"{pic} height");
        }
        Assert.IsTrue(Wad.GetLumpName(Ansi("conchars")) != null);
    }
}
