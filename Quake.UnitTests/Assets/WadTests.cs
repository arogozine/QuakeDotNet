// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Wad/Com/Hunk share static state, so, like CommonTests, these can't run
// concurrently with each other.
[TestClass]
public sealed unsafe class WadTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

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
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(_buf);
    }

    // Builds a minimal WAD2 file: header + lump directory + payloads.
    // entries: (name, type, payload bytes)
    private static void WriteWadFile(string path, params (string Name, byte Type, byte[] Payload)[] entries)
    {
        const int headerSize = 12; // identification[4] + numlumps + infotableofs

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        int[] filepositions = new int[entries.Length];
        int ofs = headerSize;
        foreach ((int i, var entry) in entries.Select((e, i) => (i, e)))
        {
            filepositions[i] = ofs;
            ofs += entry.Payload.Length;
        }

        int infotableofs = ofs;

        bw.Write((byte)'W'); bw.Write((byte)'A'); bw.Write((byte)'D'); bw.Write((byte)'2');
        bw.Write(entries.Length);
        bw.Write(infotableofs);

        foreach (var entry in entries)
            bw.Write(entry.Payload);

        for (int i = 0; i < entries.Length; i++)
        {
            bw.Write(filepositions[i]);      // filepos
            bw.Write(entries[i].Payload.Length); // disksize
            bw.Write(entries[i].Payload.Length); // size
            bw.Write(entries[i].Type);       // type
            bw.Write((byte)0);               // compression
            bw.Write((byte)0);               // pad1
            bw.Write((byte)0);               // pad2
            byte[] nameBytes = new byte[16];
            Encoding.ASCII.GetBytes(entries[i].Name).CopyTo(nameBytes, 0);
            bw.Write(nameBytes);
        }
    }

    [TestMethod]
    public void CleanupName_LowercasesAndNullPads()
    {
        byte* @out = stackalloc byte[16];
        Wad.CleanupName(Ansi("CONCHARS"), @out);

        Assert.AreEqual("conchars", Marshal.PtrToStringAnsi((nint)@out));
        Assert.AreEqual(0, @out[8]);
        Assert.AreEqual(0, @out[15]);
    }

    [TestMethod]
    public void LoadWadFile_ParsesLumpsAndSwapsQPicDimensions()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            // a (int)WadLumpType.QPic payload: width, height, then pixel bytes
            byte[] pic = new byte[8 + 4];
            BitConverter.GetBytes(4).CopyTo(pic, 0); // width
            BitConverter.GetBytes(2).CopyTo(pic, 4); // height
            byte[] miptex = Encoding.ASCII.GetBytes("miptexdata");

            WriteWadFile(Path.Combine(gamedir, "gfx.wad"),
                ("CONCHARS", (byte)(int)WadLumpType.QPic, pic), ("wall1", (byte)(int)WadLumpType.MipTex, miptex));

            Com.AddGameDirectory(Ansi(gamedir));
            Wad.LoadWadFile(Ansi("gfx.wad"));

            Assert.AreEqual(2, Wad.wad_numlumps);

            LumpInfo_t* first = Wad.GetLumpinfo(Ansi("conchars"));
            Assert.AreEqual("conchars", Marshal.PtrToStringAnsi((nint)first->Name));

            QPic_t* qpic = (QPic_t*)Wad.GetLumpName(Ansi("CONCHARS"));
            Assert.AreEqual(4, qpic->Width);
            Assert.AreEqual(2, qpic->Height);

            byte* second = (byte*)Wad.GetLumpNum(1);
            Assert.AreEqual("miptexdata", Marshal.PtrToStringAnsi((nint)second, miptex.Length));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void GetLumpinfo_ThrowsSysError_WhenLumpMissing()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            WriteWadFile(Path.Combine(gamedir, "gfx.wad"), ("wall1", (byte)(int)WadLumpType.MipTex, Encoding.ASCII.GetBytes("x")));
            Com.AddGameDirectory(Ansi(gamedir));
            Wad.LoadWadFile(Ansi("gfx.wad"));

            Assert.ThrowsExactly<QuakeSysErrorException>(() => Wad.GetLumpinfo(Ansi("doesnotexist")));
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }
}
