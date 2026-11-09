// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Com/Sz/Msg share static state (com_argc/com_argv, bigendien, net_message),
// so — like MemoryManagerTests — these can't run concurrently with each other.
[TestClass]
public sealed unsafe class CommonTests
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

        // com_searchpaths/com_modified/static_registered/proghack/com_cachedir
        // are plain statics, not Hunk-allocated, so they survive a fresh arena
        // and must be reset explicitly between tests.
        Com.com_searchpaths = null;
        Com.com_modified = false;
        Com.static_registered = 1;
        Com.proghack = false;
        Com.com_cachedir[0] = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        // any pack files opened via LoadPackFile/AddGameDirectory during the
        // test keep their Sys file handle open (matching COM_CloseFile's
        // "don't really close a pack handle" behavior) -- close them here so
        // temp files/directories can be deleted.
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(_buf);
    }

    private static void WritePackDirEntry(BinaryWriter bw, string name, int filepos, int filelen)
    {
        byte[] nameBytes = new byte[56];
        Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
        bw.Write(nameBytes);
        bw.Write(filepos);
        bw.Write(filelen);
    }

    private static void WritePakFile(string path, params (string Name, byte[] Payload)[] entries)
    {
        const int headerSize = 12;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        int[] fileposes = new int[entries.Length];
        int ofs = headerSize;
        for (int i = 0; i < entries.Length; i++)
        {
            fileposes[i] = ofs;
            ofs += entries[i].Payload.Length;
        }

        int dirofs = ofs;
        int dirlen = entries.Length * 64;

        bw.Write((byte)'P'); bw.Write((byte)'A'); bw.Write((byte)'C'); bw.Write((byte)'K');
        bw.Write(dirofs);
        bw.Write(dirlen);

        foreach ((string _, byte[] payload) in entries)
            bw.Write(payload);

        for (int i = 0; i < entries.Length; i++)
            WritePackDirEntry(bw, entries[i].Name, fileposes[i], entries[i].Payload.Length);
    }

    [TestMethod]
    public void LoadPackFile_ParsesDirectory_AndFlagsNonStandardCountAsModified()
    {
        string path = Path.Combine(Path.GetTempPath(), $"qtest_{Guid.NewGuid():N}.pak");
        try
        {
            byte[] payload1 = Encoding.ASCII.GetBytes("hello");
            byte[] payload2 = Encoding.ASCII.GetBytes("world!");
            WritePakFile(path, ("test.txt", payload1), ("other.dat", payload2));

            Pack_t* pack = Com.LoadPackFile(Ansi(path));

            Assert.IsTrue(pack != null);
            Assert.AreEqual(2, pack->NumFiles);
            Assert.AreEqual("test.txt", Marshal.PtrToStringAnsi((nint)pack->Files[0].Name));
            Assert.AreEqual(payload1.Length, pack->Files[0].FileLen);
            Assert.AreEqual("other.dat", Marshal.PtrToStringAnsi((nint)pack->Files[1].Name));
            Assert.AreEqual(payload2.Length, pack->Files[1].FileLen);

            // 2 files != PAK0_COUNT (339), so this must be flagged as a modified pak
            Assert.IsTrue(Com.com_modified);

            Sys.FileClose(pack->Handle);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AddGameDirectory_PackFileTakesPrecedenceOverLooseFileWithSameName()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            File.WriteAllText(Path.Combine(gamedir, "test.txt"), "loose");

            byte[] payload = Encoding.ASCII.GetBytes("frompak");
            WritePakFile(Path.Combine(gamedir, "pak0.pak"), ("test.txt", payload));

            Com.AddGameDirectory(Ansi(gamedir));

            int handle;
            int len = Com.OpenFile(Ansi("test.txt"), &handle);

            Assert.AreEqual(payload.Length, len);

            byte* buf = stackalloc byte[32];
            Sys.FileRead(handle, buf, len);
            Assert.AreEqual("frompak", Marshal.PtrToStringAnsi((nint)buf));
        }
        finally
        {
            for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
                if (s->Pack != null)
                    Sys.FileClose(s->Pack->Handle);
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void FindFile_ReturnsMinusOne_WhenFileIsMissing()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            int handle;
            int len = Com.OpenFile(Ansi("does_not_exist.txt"), &handle);

            Assert.AreEqual(-1, len);
            Assert.AreEqual(-1, handle);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void InitArgv_Then_CheckParm_FindsArgument()
    {
        byte*[] argv = [Ansi("quake"), Ansi("-basedir"), Ansi("C:\\quake")];
        Com.InitArgv(argv.Length, argv);

        Assert.AreEqual(1, Com.CheckParm(Ansi("-basedir")));
        Assert.AreEqual(0, Com.CheckParm(Ansi("-missing")));
    }

    [TestMethod]
    public void InitArgv_SafeFlag_AppendsSafeModeSwitches()
    {
        byte*[] argv = [Ansi("quake"), Ansi("-safe")];
        Com.InitArgv(argv.Length, argv);

        Assert.AreNotEqual(0, Com.CheckParm(Ansi("-nosound")));
        Assert.AreNotEqual(0, Com.CheckParm(Ansi("-nojoy")));
    }

    [TestMethod]
    public void SkipPath_ReturnsFileNameAfterLastSlash()
    {
        byte* path = Ansi("id1/maps/start.bsp");
        byte* result = Com.SkipPath(path);
        Assert.AreEqual("start.bsp", Marshal.PtrToStringAnsi((nint)result));
    }

    [TestMethod]
    public void StripExtension_RemovesTrailingExtension()
    {
        byte* @in = Ansi("start.bsp");
        byte* @out = stackalloc byte[32];
        Com.StripExtension(@in, @out);
        Assert.AreEqual("start", Marshal.PtrToStringAnsi((nint)@out));
    }

    [TestMethod]
    public void FileExtension_ReturnsExtensionWithoutDot()
    {
        byte* result = Com.FileExtension(Ansi("start.bsp"));
        Assert.AreEqual("bsp", Marshal.PtrToStringAnsi((nint)result));
    }

    [TestMethod]
    public void FileExtension_ReturnsEmptyString_WhenNoExtension()
    {
        byte* result = Com.FileExtension(Ansi("start"));
        Assert.AreEqual(string.Empty, Marshal.PtrToStringAnsi((nint)result));
    }

    [TestMethod]
    public void FileBase_ExtractsBaseNameWithoutDirectoryOrExtension()
    {
        byte* @in = Ansi("id1/maps/start.bsp");
        byte* @out = stackalloc byte[32];
        Com.FileBase(@in, @out);
        Assert.AreEqual("start", Marshal.PtrToStringAnsi((nint)@out));
    }

    [TestMethod]
    public void DefaultExtension_AppendsExtension_WhenMissing()
    {
        byte* path = stackalloc byte[32];
        Q.StrCpy(path, Ansi("start"));
        Com.DefaultExtension(path, Ansi(".bsp"));
        Assert.AreEqual("start.bsp", Marshal.PtrToStringAnsi((nint)path));
    }

    [TestMethod]
    public void DefaultExtension_LeavesPathAlone_WhenExtensionPresent()
    {
        byte* path = stackalloc byte[32];
        Q.StrCpy(path, Ansi("start.bsp"));
        Com.DefaultExtension(path, Ansi(".map"));
        Assert.AreEqual("start.bsp", Marshal.PtrToStringAnsi((nint)path));
    }

    [TestMethod]
    public void Parse_ReturnsWordToken_AndAdvancesPointer()
    {
        byte* data = Ansi("hello world");
        byte* rest = Com.Parse(data);
        Assert.AreEqual("hello", Marshal.PtrToStringAnsi((nint)Com.com_token));
        Assert.AreEqual(" world", Marshal.PtrToStringAnsi((nint)rest));
    }

    [TestMethod]
    public void Parse_ReturnsNull_AtEndOfString()
    {
        byte* data = Ansi("   ");
        Assert.IsTrue(Com.Parse(data) == null);
    }

    [TestMethod]
    public void Parse_HandlesQuotedStrings()
    {
        byte* data = Ansi("\"hello world\" rest");
        byte* rest = Com.Parse(data);
        Assert.AreEqual("hello world", Marshal.PtrToStringAnsi((nint)Com.com_token));
        Assert.AreEqual(" rest", Marshal.PtrToStringAnsi((nint)rest));
    }

    [TestMethod]
    public void Parse_SkipsLineCommentsAndBlankLines_LikeAConfigFile()
    {
        // shape of a real .cfg: a leading comment, a blank line, then a token
        byte* data = Ansi("// this line is a comment\n\nsensitivity");
        byte* rest = Com.Parse(data);
        Assert.AreEqual("sensitivity", Marshal.PtrToStringAnsi((nint)Com.com_token));

        // and a comment can also follow a token on the same line
        rest = Com.Parse(rest);
        // (nothing follows "sensitivity" above, so the next parse hits EOF)
        Assert.IsTrue(rest == null);

        data = Ansi("5 // default value\nnextcommand");
        rest = Com.Parse(data);
        Assert.AreEqual("5", Marshal.PtrToStringAnsi((nint)Com.com_token));
        Com.Parse(rest);
        Assert.AreEqual("nextcommand", Marshal.PtrToStringAnsi((nint)Com.com_token));
    }

    [TestMethod]
    public void ByteOrder_Swap_IsInvolutory()
    {
        // BigShort/BigLong/BigFloat swap on a little-endian machine, so
        // applying the same conversion twice must return the original value.
        short s = 0x1234;
        Assert.AreEqual(s, Com.BigShort(Com.BigShort(s)));

        int l = 0x01020304;
        Assert.AreEqual(l, Com.BigLong(Com.BigLong(l)));

        float f = 3.14159f;
        Assert.AreEqual(f, Com.BigFloat(Com.BigFloat(f)));
    }

    [TestMethod]
    public void ByteOrder_Swap_MatchesOriginalByteShuffle()
    {
        // ShortSwap/LongSwap/FloatSwap (common.c): exact byte reversal,
        // including values whose high byte has the sign bit set.
        Assert.AreEqual((short)0x3412, Com.ShortSwap(0x1234));
        Assert.AreEqual(unchecked((short)0x0180), Com.ShortSwap(unchecked((short)0x8001)));
        Assert.AreEqual(unchecked((short)0xFF00), Com.ShortSwap(0x00FF));

        Assert.AreEqual(0x04030201, Com.LongSwap(0x01020304));
        Assert.AreEqual(0x000000F0, Com.LongSwap(unchecked((int)0xF0000000)));
        Assert.AreEqual(unchecked((int)0x80FFFF7F), Com.LongSwap(0x7FFFFF80));

        float f = BitConverter.Int32BitsToSingle(0x40490FDB); // pi
        Assert.AreEqual(unchecked((int)0xDB0F4940), BitConverter.SingleToInt32Bits(Com.FloatSwap(f)));

        // a swapped bit pattern may be a NaN; its payload must survive
        float nan = BitConverter.Int32BitsToSingle(0x7FC00001);
        Assert.AreEqual(0x0100C07F, BitConverter.SingleToInt32Bits(Com.FloatSwap(nan)));
    }

    [TestMethod]
    public void ByteOrder_MatchesRuntimeEndianness()
    {
        short s = 0x1234;
        short little = Com.LittleShort(s);
        Assert.AreEqual(BitConverter.IsLittleEndian, little == s);
    }

    [TestMethod]
    public void SzAndMsg_WriteThenRead_RoundTrips()
    {
        SizeBuf_t sb = default;
        Sz.Alloc(&sb, 256);

        Msg.WriteByte(&sb, 42);
        Msg.WriteShort(&sb, -1234);
        Msg.WriteLong(&sb, 123456789);
        Msg.WriteFloat(&sb, 3.5f);
        Msg.WriteString(&sb, Ansi("hi"));

        Net.net_message->Data = sb.Data; Net.net_message->MaxSize = sb.MaxSize; Msg.net_message->CurSize = sb.CurSize;
        Msg.BeginReading();

        Assert.AreEqual(42, Msg.ReadByte());
        Assert.AreEqual(-1234, Msg.ReadShort());
        Assert.AreEqual(123456789, Msg.ReadLong());
        Assert.AreEqual(3.5f, Msg.ReadFloat());
        Assert.AreEqual("hi", Marshal.PtrToStringAnsi((nint)Msg.ReadString()));
        Assert.IsFalse(Msg.msg_badread);
    }

    [TestMethod]
    public void MemSearch_FindsByteOffset_OrReturnsMinusOne()
    {
        byte* data = stackalloc byte[] { 1, 2, 3, 4, 5 };
        Assert.AreEqual(2, Com.MemSearch(data, 5, 3));
        Assert.AreEqual(-1, Com.MemSearch(data, 5, 9));
    }
}
