// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Model/AliasModel/Com share static state, so, like the other loader tests,
// these can't run concurrently with each other.
[TestClass]
public sealed unsafe class AliasModelTests
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
        RMain.r_notexture_mip = null;
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

    // Builds a minimal synthetic IDPO/v6 .mdl: 1 skin (single), 3 verts, 1
    // triangle, 1 single frame.
    private static byte[] BuildMdlFile()
    {
        const int numSkins = 1;
        const int numVerts = 3;
        const int numTris = 1;
        const int numFrames = 1;
        const int skinWidth = 4, skinHeight = 4; // must be a multiple of 4
        int skinSize = skinWidth * skinHeight;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // mdl_t
        bw.Write(Model.IDPOLYHEADER);
        bw.Write(AliasModel.ALIAS_VERSION);
        bw.Write(1f); bw.Write(1f); bw.Write(1f);    // scale
        bw.Write(0f); bw.Write(0f); bw.Write(0f);    // scale_origin
        bw.Write(10f);                               // boundingradius
        bw.Write(0f); bw.Write(0f); bw.Write(0f);    // eyeposition
        bw.Write(numSkins);
        bw.Write(skinWidth);
        bw.Write(skinHeight);
        bw.Write(numVerts);
        bw.Write(numTris);
        bw.Write(numFrames);
        bw.Write((int)SyncType_t.Sync); // synctype
        bw.Write(0);   // flags
        bw.Write(1f);  // size

        // skins: 1 single skin (daliasskintype_t + raw pixel payload)
        bw.Write((int)AliasSkinType_t.Single);
        for (int i = 0; i < skinSize; i++)
        {
            bw.Write((byte)(i & 0xFF));
        }

        // base s/t vertices (stvert_t): onseam, s, t
        for (int i = 0; i < numVerts; i++)
        {
            bw.Write(0);
            bw.Write(i);
            bw.Write(0);
        }

        // triangles (dtriangle_t): facesfront, vertindex[3]
        bw.Write(1); // facesfront
        bw.Write(0); bw.Write(1); bw.Write(2);

        // frames: 1 single frame (daliasframetype_t + daliasframe_t + trivertx_t[numVerts])
        bw.Write((int)AliasFrameType_t.Single);
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); // bboxmin
        bw.Write((byte)8); bw.Write((byte)8); bw.Write((byte)8); bw.Write((byte)0); // bboxmax
        byte[] nameBytes = new byte[16];
        Encoding.ASCII.GetBytes("frame0").CopyTo(nameBytes, 0);
        bw.Write(nameBytes);
        for (int i = 0; i < numVerts; i++)
        {
            bw.Write((byte)i); bw.Write((byte)i); bw.Write((byte)i); // v[3]
            bw.Write((byte)0); // lightnormalindex
        }

        return ms.ToArray();
    }

    private static string WriteMdlToGamedir(byte[] bytes, string name = "progs/test.mdl")
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, Path.GetDirectoryName(name) ?? ""));
        File.WriteAllBytes(Path.Combine(gamedir, name), bytes);
        return gamedir;
    }

    [TestMethod]
    public void ModForName_ParsesAliasModelCountsAndData()
    {
        string gamedir = WriteMdlToGamedir(BuildMdlFile());
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            Model_t* mod = Model.ForName(Ansi("progs/test.mdl"), true);

            Assert.AreNotEqual(0, (nint)mod);
            Assert.AreEqual(ModType_t.Alias, mod->Type);
            Assert.AreEqual(1, mod->NumFrames);
            Assert.AreEqual(-16f, mod->Mins[0]);
            Assert.AreEqual(16f, mod->Maxs[0]);

            AliasHdr_t* hdr = (AliasHdr_t*)mod->Cache.Data;
            Assert.AreNotEqual(0, (nint)hdr);

            Mdl_t* pmodel = (Mdl_t*)AliasModel.Offset(hdr, hdr->Model);
            Assert.AreEqual(3, pmodel->NumVerts);
            Assert.AreEqual(1, pmodel->NumTris);
            Assert.AreEqual(1, pmodel->NumFrames);
            Assert.AreEqual(1, pmodel->NumSkins);
            Assert.AreEqual(4, pmodel->SkinWidth);
            Assert.AreEqual(4, pmodel->SkinHeight);

            MAliasSkinDesc_t* skindesc = (MAliasSkinDesc_t*)AliasModel.Offset(hdr, hdr->SkinDesc);
            Assert.AreEqual(AliasSkinType_t.Single, skindesc->Type);
            byte* skinPixels = (byte*)AliasModel.Offset(hdr, skindesc->Skin);
            Assert.AreEqual(0, skinPixels[0]);
            Assert.AreEqual(5, skinPixels[5]);

            StVert_t* stverts = (StVert_t*)AliasModel.Offset(hdr, hdr->StVerts);
            Assert.AreEqual(0, stverts[0].S);
            Assert.AreEqual(1 << 16, stverts[1].S);

            MTriangle_t* tris = (MTriangle_t*)AliasModel.Offset(hdr, hdr->Triangles);
            Assert.AreEqual(1, tris[0].FacesFront);
            Assert.AreEqual(0, tris[0].VertIndex[0]);
            Assert.AreEqual(2, tris[0].VertIndex[2]);

            Assert.AreEqual(AliasFrameType_t.Single, hdr->Frames.Type);
            Assert.AreEqual("frame0", Marshal.PtrToStringAnsi((nint)hdr->Frames.Name));
            Assert.AreEqual((byte)0, hdr->Frames.BboxMin.V[0]);
            Assert.AreEqual((byte)8, hdr->Frames.BboxMax.V[0]);

            TriVertX_t* frameVerts = (TriVertX_t*)AliasModel.Offset(hdr, hdr->Frames.Frame);
            Assert.AreEqual((byte)0, frameVerts[0].V[0]);
            Assert.AreEqual((byte)2, frameVerts[2].V[0]);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void ModForName_WrongVersion_ThrowsSysError()
    {
        byte[] mdl = BuildMdlFile();
        // corrupt the version field (bytes 4..7, right after the IDPO magic)
        mdl[4] = 99; mdl[5] = 0; mdl[6] = 0; mdl[7] = 0;

        string gamedir = WriteMdlToGamedir(mdl);
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Assert.ThrowsExactly<QuakeSysErrorException>(() => Model.ForName(Ansi("progs/test.mdl"), true));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }
}
