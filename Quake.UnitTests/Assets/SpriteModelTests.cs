// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Model/SpriteModel/Com share static state, so, like the other loader tests,
// these can't run concurrently with each other.
[TestClass]
public sealed unsafe class SpriteModelTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;

        NativeMemory.Clear(Model.mod_known, (nuint)(Model.mod_numknown * sizeof(Model_t)));
        Model.mod_numknown = 0;
        Model.loadmodel = null;
        RMain.r_notexture_mip = null;
        Model.Init();
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(_buf);
    }

    // Builds a minimal synthetic IDSP/v1 .spr: 1 single frame with a small
    // pixel payload.
    private static byte[] BuildSprFile()
    {
        const int width = 4, height = 4;
        const int originX = -2, originY = 3;
        int size = width * height;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // dsprite_t
        bw.Write(Model.IDSPRITEHEADER);
        bw.Write(SpriteModel.SPRITE_VERSION);
        bw.Write((int)SpriteType.VpParallel);
        bw.Write(4f);   // boundingradius
        bw.Write(width);
        bw.Write(height);
        bw.Write(1);    // numframes
        bw.Write(0.1f); // beamlength
        bw.Write((int)SyncType_t.Rand); // synctype

        // frame: dspriteframetype_t + dspriteframe_t + pixel payload
        bw.Write((int)SpriteFrameType_t.Single);
        bw.Write(originX);
        bw.Write(originY);
        bw.Write(width);
        bw.Write(height);
        for (int i = 0; i < size; i++)
            bw.Write((byte)(i + 1));

        return ms.ToArray();
    }

    private static string WriteSprToGamedir(byte[] bytes, string name = "progs/test.spr")
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, Path.GetDirectoryName(name) ?? ""));
        File.WriteAllBytes(Path.Combine(gamedir, name), bytes);
        return gamedir;
    }

    [TestMethod]
    public void ModForName_ParsesSpriteModelFrameCountDimensionsAndOrigin()
    {
        string gamedir = WriteSprToGamedir(BuildSprFile());
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            Model_t* mod = Model.ForName(Ansi("progs/test.spr"), true);

            Assert.AreNotEqual(0, (nint)mod);
            Assert.AreEqual(ModType_t.Sprite, mod->Type);
            Assert.AreEqual(1, mod->NumFrames);
            Assert.AreEqual(SyncType_t.Rand, mod->SyncType);

            MSprite_t* psprite = (MSprite_t*)mod->Cache.Data;
            Assert.AreNotEqual(0, (nint)psprite);
            Assert.AreEqual(SpriteType.VpParallel, psprite->Type);
            Assert.AreEqual(4, psprite->MaxWidth);
            Assert.AreEqual(4, psprite->MaxHeight);
            Assert.AreEqual(1, psprite->NumFrames);

            Assert.AreEqual(SpriteFrameType_t.Single, psprite->Frames.Type);
            MSpriteFrame_t* frame = psprite->Frames.FramePtr;
            Assert.AreNotEqual(0, (nint)frame);
            Assert.AreEqual(4, frame->Width);
            Assert.AreEqual(4, frame->Height);

            // up/down/left/right derive from origin (-2, 3) and width/height 4
            Assert.AreEqual(3f, frame->Up);
            Assert.AreEqual(3f - 4f, frame->Down);
            Assert.AreEqual(-2f, frame->Left);
            Assert.AreEqual(4f + -2f, frame->Right);

            byte* pixels = (byte*)&frame->Pixels[0];
            Assert.AreEqual((byte)1, pixels[0]);
            Assert.AreEqual((byte)16, pixels[15]);

            // model bounds derive from maxwidth/maxheight (mod->mins/maxs)
            Assert.AreEqual(-2f, mod->Mins[0]);
            Assert.AreEqual(2f, mod->Maxs[0]);
            Assert.AreEqual(-2f, mod->Mins[2]);
            Assert.AreEqual(2f, mod->Maxs[2]);
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
        byte[] spr = BuildSprFile();
        // corrupt the version field (bytes 4..7, right after the IDSP magic)
        spr[4] = 99; spr[5] = 0; spr[6] = 0; spr[7] = 0;

        string gamedir = WriteSprToGamedir(spr);
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));
            Assert.ThrowsExactly<QuakeSysErrorException>(() => Model.ForName(Ansi("progs/test.spr"), true));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }
}
