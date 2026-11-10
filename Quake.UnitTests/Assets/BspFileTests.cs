// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Model/Com share static state, so, like CommonTests/WadTests/ProgsTests,
// these can't run concurrently with each other.
[TestClass]
public sealed unsafe class BspFileTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;

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

        NativeMemory.Clear(Model.mod_known, Model.mod_numknown);
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.InitTextures(); // R_InitTextures: Host_Init builds r_notexture_mip before any map loads
        Model.Init();
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

    // Appends a section to `body`, returning its (fileofs, filelen) as a lump_t
    // relative to the start of the whole file (header size is added by the caller).
    private static (int ofs, int len) AppendSection(MemoryStream body, byte[] payload, int headerSize)
    {
        int ofs = headerSize + (int)body.Length;
        body.Write(payload);
        return (ofs, payload.Length);
    }

    // Builds a minimal synthetic BSPVERSION-29 .bsp: 1 plane, 3 vertices, 1
    // triangular face (4 edges incl. the unused edge 0, 3 surfedges), 1
    // texinfo, a 1-entry miptex lump with dataofs=-1 (skips texture data
    // entirely -- Mod_LoadTexinfo falls back to the notexture placeholder),
    // 1 leaf, 1 node, 1 marksurface, 0 clipnodes, empty vis/lighting, an
    // entity lump, and 1 submodel.
    private static byte[] BuildBspFile()
    {
        const int headerSize = 4 + (BspFile.HEADER_LUMPS * 8); // version + 15 lump_t

        using var body = new MemoryStream();
        using var bw = new BinaryWriter(body);

        // vertexes: (0,0,0) (10,0,0) (0,10,0)
        using var vertexes = new MemoryStream();
        using (var vbw = new BinaryWriter(vertexes))
        {
            vbw.Write(0f); vbw.Write(0f); vbw.Write(0f);
            vbw.Write(10f); vbw.Write(0f); vbw.Write(0f);
            vbw.Write(0f); vbw.Write(10f); vbw.Write(0f);
        }
        var (vertOfs, vertLen) = AppendSection(body, vertexes.ToArray(), headerSize);

        // edges: edge 0 unused by convention, edges 1-3 form the triangle
        using var edges = new MemoryStream();
        using (var ebw = new BinaryWriter(edges))
        {
            ebw.Write((ushort)0); ebw.Write((ushort)0);
            ebw.Write((ushort)0); ebw.Write((ushort)1);
            ebw.Write((ushort)1); ebw.Write((ushort)2);
            ebw.Write((ushort)2); ebw.Write((ushort)0);
        }
        var (edgeOfs, edgeLen) = AppendSection(body, edges.ToArray(), headerSize);

        // surfedges
        using var surfedges = new MemoryStream();
        using (var sbw = new BinaryWriter(surfedges))
        {
            sbw.Write(1); sbw.Write(2); sbw.Write(3);
        }
        var (surfedgeOfs, surfedgeLen) = AppendSection(body, surfedges.ToArray(), headerSize);

        // textures: dmiptexlump_t with nummiptex=1, dataofs[0]=-1 (no payload)
        using var textures = new MemoryStream();
        using (var tbw = new BinaryWriter(textures))
        {
            tbw.Write(1);  // nummiptex
            tbw.Write(-1); // dataofs[0]
        }
        var (texOfs, texLen) = AppendSection(body, textures.ToArray(), headerSize);

        // planes: normal (0,0,1), dist 0, type (int)PlaneType.Z
        using var planes = new MemoryStream();
        using (var pbw = new BinaryWriter(planes))
        {
            pbw.Write(0f); pbw.Write(0f); pbw.Write(1f);
            pbw.Write(0f);
            pbw.Write((int)PlaneType.Z);
        }
        var (planeOfs, planeLen) = AppendSection(body, planes.ToArray(), headerSize);

        // texinfo
        using var texinfo = new MemoryStream();
        using (var xbw = new BinaryWriter(texinfo))
        {
            xbw.Write(1f); xbw.Write(0f); xbw.Write(0f); xbw.Write(0f); // vecs[0]
            xbw.Write(0f); xbw.Write(1f); xbw.Write(0f); xbw.Write(0f); // vecs[1]
            xbw.Write(0);  // miptex
            xbw.Write(0);  // flags
        }
        var (texinfoOfs, texinfoLen) = AppendSection(body, texinfo.ToArray(), headerSize);

        // faces
        using var faces = new MemoryStream();
        using (var fbw = new BinaryWriter(faces))
        {
            fbw.Write((short)0);  // planenum
            fbw.Write((short)0);  // side
            fbw.Write(0);         // firstedge (index into surfedges)
            fbw.Write((short)3);  // numedges
            fbw.Write((short)0);  // texinfo
            fbw.Write((byte)0); fbw.Write((byte)255); fbw.Write((byte)255); fbw.Write((byte)255); // styles
            fbw.Write(-1); // lightofs
        }
        var (faceOfs, faceLen) = AppendSection(body, faces.ToArray(), headerSize);

        // marksurfaces
        using var marksurfaces = new MemoryStream();
        using (var mbw = new BinaryWriter(marksurfaces))
        {
            mbw.Write((short)0);
        }
        var (markOfs, markLen) = AppendSection(body, marksurfaces.ToArray(), headerSize);

        // leafs
        using var leafs = new MemoryStream();
        using (var lbw = new BinaryWriter(leafs))
        {
            lbw.Write((int)Contents.Empty); // contents
            lbw.Write(-1); // visofs
            lbw.Write((short)-8); lbw.Write((short)-8); lbw.Write((short)-8); // mins
            lbw.Write((short)8); lbw.Write((short)8); lbw.Write((short)8);    // maxs
            lbw.Write((ushort)0); // firstmarksurface
            lbw.Write((ushort)1); // nummarksurfaces
            lbw.Write((byte)0); lbw.Write((byte)0); lbw.Write((byte)0); lbw.Write((byte)0); // ambient_level
        }
        var (leafOfs, leafLen) = AppendSection(body, leafs.ToArray(), headerSize);

        // nodes: both children point at leaf 0 (-1 - p == 0 => p == -1)
        using var nodes = new MemoryStream();
        using (var nbw = new BinaryWriter(nodes))
        {
            nbw.Write(0); // planenum
            nbw.Write((short)-1); nbw.Write((short)-1); // children
            nbw.Write((short)-8); nbw.Write((short)-8); nbw.Write((short)-8); // mins
            nbw.Write((short)8); nbw.Write((short)8); nbw.Write((short)8);    // maxs
            nbw.Write((ushort)0); // firstface
            nbw.Write((ushort)1); // numfaces
        }
        var (nodeOfs, nodeLen) = AppendSection(body, nodes.ToArray(), headerSize);

        // clipnodes: none
        var (clipOfs, clipLen) = AppendSection(body, [], headerSize);

        // entities
        var (entOfs, entLen) = AppendSection(body, Encoding.ASCII.GetBytes("{}\0"), headerSize);

        // visibility / lighting: empty
        var (visOfs, visLen) = AppendSection(body, [], headerSize);
        var (lightOfs, lightLen) = AppendSection(body, [], headerSize);

        // models (submodels)
        using var models = new MemoryStream();
        using (var modbw = new BinaryWriter(models))
        {
            modbw.Write(0f); modbw.Write(0f); modbw.Write(0f);    // mins
            modbw.Write(10f); modbw.Write(10f); modbw.Write(0f); // maxs
            modbw.Write(0f); modbw.Write(0f); modbw.Write(0f);    // origin
            modbw.Write(0); modbw.Write(0); modbw.Write(0); modbw.Write(0); // headnode[4]
            modbw.Write(1); // visleafs
            modbw.Write(0); // firstface
            modbw.Write(1); // numfaces
        }
        var (modelOfs, modelLen) = AppendSection(body, models.ToArray(), headerSize);

        using var file = new MemoryStream();
        using var hw = new BinaryWriter(file);

        hw.Write(BspFile.BSPVERSION);

        void WriteLump(int ofs, int len) { hw.Write(ofs); hw.Write(len); }

        WriteLump(entOfs, entLen);       // (int)BspLump.Entities
        WriteLump(planeOfs, planeLen);   // (int)BspLump.Planes
        WriteLump(texOfs, texLen);       // (int)BspLump.Textures
        WriteLump(vertOfs, vertLen);     // (int)BspLump.Vertexes
        WriteLump(visOfs, visLen);       // (int)BspLump.Visibility
        WriteLump(nodeOfs, nodeLen);     // (int)BspLump.Nodes
        WriteLump(texinfoOfs, texinfoLen); // (int)BspLump.TexInfo
        WriteLump(faceOfs, faceLen);     // (int)BspLump.Faces
        WriteLump(lightOfs, lightLen);   // (int)BspLump.Lighting
        WriteLump(clipOfs, clipLen);     // (int)BspLump.ClipNodes
        WriteLump(leafOfs, leafLen);     // (int)BspLump.Leafs
        WriteLump(markOfs, markLen);     // (int)BspLump.MarkSurfaces
        WriteLump(edgeOfs, edgeLen);     // (int)BspLump.Edges
        WriteLump(surfedgeOfs, surfedgeLen); // (int)BspLump.SurfEdges
        WriteLump(modelOfs, modelLen);   // (int)BspLump.Models

        hw.Write(body.ToArray());

        return file.ToArray();
    }

    private static string WriteBspToGamedir(byte[] bytes, string name = "maps/test.bsp")
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, Path.GetDirectoryName(name) ?? ""));
        File.WriteAllBytes(Path.Combine(gamedir, name), bytes);
        return gamedir;
    }

    [TestMethod]
    public void ModForName_ParsesBrushModelCountsAndData()
    {
        string gamedir = WriteBspToGamedir(BuildBspFile());
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            Model_t* mod = Model.ForName(Ansi("maps/test.bsp"), true);

            Assert.AreNotEqual(0, (nint)mod);
            Assert.AreEqual(ModType_t.Brush, mod->Type);
            Assert.AreEqual(3, mod->NumVertexes);
            Assert.AreEqual(4, mod->NumEdges);
            Assert.AreEqual(3, mod->NumSurfedges);
            Assert.AreEqual(1, mod->NumPlanes);
            Assert.AreEqual(1, mod->NumTexinfo);
            Assert.AreEqual(1, mod->NumSurfaces);
            Assert.AreEqual(1, mod->NumLeafs); // visleafs from the submodel
            Assert.AreEqual(1, mod->NumNodes);
            Assert.AreEqual(1, mod->NumMarksurfaces);
            Assert.AreEqual(0, mod->NumClipnodes);
            Assert.AreEqual(1, mod->NumSubmodels);
            Assert.AreEqual(1, mod->NumTextures);

            // Mod_LoadVertexes byte-swap/copy round-trip
            Assert.AreEqual(10f, mod->Vertexes[1][0]);
            Assert.AreEqual(10f, mod->Vertexes[2][1]);

            // Mod_LoadTexinfo: dataofs==-1 falls back to the notexture placeholder
            Assert.AreNotEqual(0, (nint)mod->Surfaces[0].TexInfo->Texture);
            Assert.AreEqual((nint)RMain.r_notexture_mip, (nint)mod->Surfaces[0].TexInfo->Texture);
            Assert.AreEqual(16u, mod->Surfaces[0].TexInfo->Texture->Width); // R_InitTextures checkerboard

            // Mod_LoadFaces / CalcSurfaceExtents ran without throwing and set a plane
            Assert.IsTrue((nint)mod->Surfaces[0].Plane != 0);
            Assert.AreEqual(3, mod->Surfaces[0].NumEdges);

            // Mod_LoadEntities
            Assert.AreEqual("{}", Marshal.PtrToStringAnsi((nint)mod->Entities));

            // Mod_MakeHull0 / Mod_SetParent ran: leaf's parent is the root node
            Assert.IsTrue((nint)mod->Leafs[0].Parent == (nint)mod->Nodes);

            // Mod_LeafPVS: leaf 0 is the solid leaf shortcut -> mod_novis
            Assert.AreEqual((nint)Model.mod_novis, (nint)Model.LeafPVS(&mod->Leafs[0], mod));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    // Mod_LoadVertexes: dvertex_t -> mvertex_t copy (little-endian bulk path)
    [TestMethod]
    public void LoadVertexes_CopiesAllComponents()
    {
        float* file = NativeMemory.AllocArray<float>(16);
        Model_t model = default;
        byte* savedBase = Model.mod_base;
        Model_t* savedModel = Model.loadmodel;
        try
        {
            float[] src = [1.5f, -2.25f, 3f, 0f, 1024f, -8192.5f, 4f, 5f, 6f];
            for (int i = 0; i < src.Length; i++)
            {
                file[i] = src[i];
            }

            Lump_t lump = new() { FileOfs = 0, FileLen = 3 * sizeof(System.Numerics.Vector3) };

            Model.mod_base = (byte*)file;
            Model.loadmodel = &model;
            Model.LoadVertexes(&lump);

            Assert.AreEqual(3, model.NumVertexes);
            Assert.AreEqual(new System.Numerics.Vector3(1.5f, -2.25f, 3f), model.Vertexes[0]);
            Assert.AreEqual(new System.Numerics.Vector3(0f, 1024f, -8192.5f), model.Vertexes[1]);
            Assert.AreEqual(new System.Numerics.Vector3(4f, 5f, 6f), model.Vertexes[2]);
        }
        finally
        {
            Model.mod_base = savedBase;
            Model.loadmodel = savedModel;
            NativeMemory.Free(file);
        }
    }

    [TestMethod]
    public void LoadVertexes_EmptyLump_YieldsZeroVertexes()
    {
        Model_t model = default;
        Model_t* savedModel = Model.loadmodel;
        try
        {
            Lump_t lump = new() { FileOfs = 0, FileLen = 0 };
            Model.loadmodel = &model;
            Model.LoadVertexes(&lump);
            Assert.AreEqual(0, model.NumVertexes);
        }
        finally { Model.loadmodel = savedModel; }
    }

    [TestMethod]
    public void LoadVertexes_FunnyLumpSize_ThrowsSysError()
    {
        Model_t model = default;
        Model_t* savedModel = Model.loadmodel;
        try
        {
            Lump_t* lump = NativeMemory.AllocZeroed<Lump_t>();
            lump->FileLen = sizeof(System.Numerics.Vector3) + 1;
            Model.loadmodel = &model;
            try { Assert.ThrowsExactly<QuakeSysErrorException>(() => Model.LoadVertexes(lump)); }
            finally { NativeMemory.Free(lump); }
        }
        finally { Model.loadmodel = savedModel; }
    }

    [TestMethod]
    public void DVertex_t_MatchesOnDiskLayout()
    {
        Assert.AreEqual(12, sizeof(System.Numerics.Vector3));
        Assert.AreEqual(sizeof(System.Numerics.Vector3), sizeof(System.Numerics.Vector3));
    }

    [TestMethod]
    public void ModForName_WrongVersion_ThrowsSysError()
    {
        byte[] bsp = BuildBspFile();
        // corrupt the version field (first 4 bytes)
        bsp[0] = 1; bsp[1] = 0; bsp[2] = 0; bsp[3] = 0;

        string gamedir = WriteBspToGamedir(bsp);
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Assert.ThrowsExactly<QuakeSysErrorException>(() => Model.ForName(Ansi("maps/test.bsp"), true));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void ModForName_MissingFile_ReturnsNullWhenNotCrashing()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Model_t* mod = Model.ForName(Ansi("maps/missing.bsp"), false);
            Assert.AreEqual(0, (nint)mod);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }
}
