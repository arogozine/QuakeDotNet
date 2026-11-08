// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// ClDemo/Com/Cmd share static state, so, like the other loader tests, these
// can't run concurrently with anything else touching them.
[TestClass]
public sealed unsafe class ClDemoTests
{
    private byte* _buf;
    private const int Size = 2 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;

        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cmd.cmd_source = CmdSource_t.SrcCommand;
        Cvar.cvar_vars = null;

        ClDemo.demofile = -1;
        ClDemo.demoplayback = false;
        ClDemo.demorecording = false;
        ClDemo.forcetrack = 0;
        Sz.Alloc(Net.net_message, Net.NET_MAXMESSAGE); // Net_Init allocates the one net_message
        Cl.viewangles = default;

        // Live-path GetMessage tests (below) need a real loopback
        // connection -- same reset list as SvSendTests' Setup.
        Cls.netcon = null;
        Svs.maxclients = 1;
        Svs.maxclientslimit = 1;
        Net.net_activeSockets = null;
        Net.net_freeSockets = null;
        Net.net_numsockets = 0;
        Net.net_activeconnections = 0;
        Net.net_driverlevel = 0;
        NetLoop.loop_client = null;
        NetLoop.loop_server = null;
        NetLoop.localconnectpending = false;
    }

    [TestCleanup]
    public void Teardown()
    {
        if (ClDemo.demofile >= 0)
        {
            try { Sys.FileClose(ClDemo.demofile); } catch { /* already closed */ }
            ClDemo.demofile = -1;
        }

        NativeMemory.Free(_buf);
    }

    private static string CaptureOutput(Action action)
    {
        TextWriter original = Console.Out;
        StringWriter sw = new();
        Console.SetOut(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    // Wires up the in-process loopback pair, with Cls.netcon set to the
    // client end -- ClDemo.GetMessage's live-network branch reads through it
    // exactly like the real client would.
    private static void ConnectLoopback(out QSocket_t* clientSock, out QSocket_t* serverSock)
    {
        Net.Init();
        clientSock = Net.Connect(Ansi("local"));
        Assert.IsTrue(clientSock != null);
        serverSock = Net.CheckNewConnections();
        Assert.IsTrue(serverSock != null);
        Cls.netcon = clientSock;
    }

    private static string TempGameDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void WriteDemoMessage_ThenGetMessage_RoundTripsPayloadAndViewangles()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            // build a small payload via Msg.cs, matching a real net_message
            SizeBuf_t* pMsg = Net.net_message;
            {
                Sz.Clear(pMsg);
                Msg.WriteByte(pMsg, 7);
                Msg.WriteByte(pMsg, 42);
            }
            Cl.viewangles[0] = 10f;
            Cl.viewangles[1] = -20f;
            Cl.viewangles[2] = 30f;

            ClDemo.demofile = Sys.FileOpenWrite(Ansi(path));
            ClDemo.WriteDemoMessage();
            Sys.FileClose(ClDemo.demofile);

            // verify the on-disk layout matches the original: length(4) + 3 angle floats(4 each) + payload
            byte[] onDisk = File.ReadAllBytes(path);
            Assert.HasCount(4 + 12 + 2, onDisk);
            Assert.AreEqual(2, BitConverter.ToInt32(onDisk, 0));
            Assert.AreEqual(10f, BitConverter.ToSingle(onDisk, 4));
            Assert.AreEqual(-20f, BitConverter.ToSingle(onDisk, 8));
            Assert.AreEqual(30f, BitConverter.ToSingle(onDisk, 12));
            Assert.AreEqual((byte)7, onDisk[16]);
            Assert.AreEqual((byte)42, onDisk[17]);

            // now read it back through GetMessage's demo-playback branch
            Net.net_message->CurSize = 0;
            int handle;
            Sys.FileOpenRead(Ansi(path), &handle);
            ClDemo.demofile = handle;
            ClDemo.demoplayback = true;

            int result = ClDemo.GetMessage();

            Assert.AreEqual(1, result);
            Assert.AreEqual(2, Net.net_message->CurSize);
            Assert.AreEqual((byte)7, Net.net_message->Data[0]);
            Assert.AreEqual((byte)42, Net.net_message->Data[1]);
            Assert.AreEqual(10f, Cl.mviewangles[0][0]);
            Assert.AreEqual(-20f, Cl.mviewangles[0][1]);
            Assert.AreEqual(30f, Cl.mviewangles[0][2]);
        }
        finally
        {
            if (ClDemo.demofile >= 0)
            {
                Sys.FileClose(ClDemo.demofile);
                ClDemo.demofile = -1;
            }
            File.Delete(path);
        }
    }

    [TestMethod]
    public void GetMessage_ShortRead_StopsPlaybackAndReturnsZero()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            // a length prefix + angles claiming 10 bytes of payload, but no payload bytes follow
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(10);
                bw.Write(0f);
                bw.Write(0f);
                bw.Write(0f);
            }

            int handle;
            Sys.FileOpenRead(Ansi(path), &handle);
            ClDemo.demofile = handle;
            ClDemo.demoplayback = true;

            int result = ClDemo.GetMessage();

            Assert.AreEqual(0, result);
            Assert.IsFalse(ClDemo.demoplayback);
            Assert.AreEqual(-1, ClDemo.demofile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void GetMessage_OverMaxMsgLen_ThrowsSysError()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(QuakeDef.MAX_MSGLEN + 1); // > MAX_MSGLEN
                bw.Write(0f);
                bw.Write(0f);
                bw.Write(0f);
            }

            int handle;
            Sys.FileOpenRead(Ansi(path), &handle);
            ClDemo.demofile = handle;
            ClDemo.demoplayback = true;

            Assert.ThrowsExactly<QuakeSysErrorException>(() => ClDemo.GetMessage());
        }
        finally
        {
            if (ClDemo.demofile >= 0)
            {
                Sys.FileClose(ClDemo.demofile);
                ClDemo.demofile = -1;
            }
            File.Delete(path);
        }
    }

    [TestMethod]
    public void PlayDemoF_ParsesTrackLine_AndOpensForPlayback()
    {
        string gamedir = TempGameDir();
        try
        {
            // "1\n" track line + one record: length(4) + 3 angles(4 each) + 1-byte payload
            using (var fs = new FileStream(Path.Combine(gamedir, "test.dem"), FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("1\n"));
                bw.Write(1);
                bw.Write(0f);
                bw.Write(0f);
                bw.Write(0f);
                bw.Write((byte)99);
            }

            Com.AddGameDirectory(Ansi(gamedir));

            fixed (byte* text = Encoding.ASCII.GetBytes("play test\0"))
                Cmd.TokenizeString(text);

            ClDemo.PlayDemo_f();

            Assert.IsTrue(ClDemo.demoplayback);
            Assert.AreEqual(1, ClDemo.forcetrack);

            int result = ClDemo.GetMessage();
            Assert.AreEqual(1, result);
            Assert.AreEqual(1, Net.net_message->CurSize);
            Assert.AreEqual((byte)99, Net.net_message->Data[0]);
        }
        finally
        {
            if (ClDemo.demofile >= 0)
            {
                Sys.FileClose(ClDemo.demofile);
                ClDemo.demofile = -1;
            }
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void PlayDemoF_NegativeTrackNumber_ParsesCorrectly()
    {
        string gamedir = TempGameDir();
        try
        {
            using (var fs = new FileStream(Path.Combine(gamedir, "test.dem"), FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("-3\n"));
            }

            Com.AddGameDirectory(Ansi(gamedir));

            fixed (byte* text = Encoding.ASCII.GetBytes("play test\0"))
                Cmd.TokenizeString(text);

            ClDemo.PlayDemo_f();

            Assert.AreEqual(-3, ClDemo.forcetrack);
        }
        finally
        {
            if (ClDemo.demofile >= 0)
            {
                Sys.FileClose(ClDemo.demofile);
                ClDemo.demofile = -1;
            }
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void RecordF_ThenStopF_WritesTrackLineAndDisconnectMessage()
    {
        string gamedir = TempGameDir();
        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            fixed (byte* text = Encoding.ASCII.GetBytes("record demo1\0"))
                Cmd.TokenizeString(text);

            ClDemo.Record_f();

            Assert.IsTrue(ClDemo.demorecording);
            Assert.AreEqual(-1, ClDemo.forcetrack);

            // write one message, then stop
            SizeBuf_t* pMsg = Net.net_message;
            {
                Sz.Clear(pMsg);
                Msg.WriteByte(pMsg, 5);
            }
            ClDemo.WriteDemoMessage();

            ClDemo.Stop_f();

            Assert.IsFalse(ClDemo.demorecording);
            Assert.AreEqual(-1, ClDemo.demofile);

            byte[] onDisk = File.ReadAllBytes(Path.Combine(gamedir, "demo1.dem"));
            string trackLine = Encoding.ASCII.GetString(onDisk, 0, 3);
            Assert.AreEqual("-1\n", trackLine);

            // first record: length(4)+angles(12)+1 byte payload (0x05)
            int offset = 3;
            Assert.AreEqual(1, BitConverter.ToInt32(onDisk, offset));
            offset += 4 + 12;
            Assert.AreEqual((byte)5, onDisk[offset]);
            offset += 1;

            // second record: the (int)Svc.Disconnect (2) byte written by Stop_f
            Assert.AreEqual(1, BitConverter.ToInt32(onDisk, offset));
            offset += 4 + 12;
            Assert.AreEqual((byte)2, onDisk[offset]);
        }
        finally
        {
            Com.com_searchpaths = null;
            Directory.Delete(gamedir, true);
        }
    }

    [TestMethod]
    public void RecordF_RejectsRelativePathnames()
    {
        fixed (byte* text = Encoding.ASCII.GetBytes("record ../evil\0"))
            Cmd.TokenizeString(text);

        ClDemo.Record_f();

        Assert.IsFalse(ClDemo.demorecording);
        Assert.AreEqual(-1, ClDemo.demofile);
    }

    [TestMethod]
    public void StopF_WhenNotRecording_DoesNothing()
    {
        ClDemo.Stop_f();

        Assert.IsFalse(ClDemo.demorecording);
    }

    [TestMethod]
    public void GetMessage_LiveConnection_NotPlayingBack_DeliversIntoNetMessage()
    {
        ConnectLoopback(out _, out QSocket_t* serverSock);

        byte* buf = stackalloc byte[4];
        SizeBuf_t msg = default;
        msg.Data = buf;
        msg.MaxSize = 4;
        Msg.WriteByte(&msg, 55);
        Net.SendMessage(serverSock, &msg);

        int result = ClDemo.GetMessage();

        Assert.AreEqual(1, result);
        Assert.AreEqual(1, Net.net_message->CurSize);
        Assert.AreEqual((byte)55, Net.net_message->Data[0]);
    }

    [TestMethod]
    public void GetMessage_LiveConnection_DiscardsNopKeepalive_ThenReturnsRealMessage()
    {
        ConnectLoopback(out _, out QSocket_t* serverSock);

        byte* nopBuf = stackalloc byte[1];
        SizeBuf_t nopMsg = default;
        nopMsg.Data = nopBuf;
        nopMsg.MaxSize = 1;
        Msg.WriteByte(&nopMsg, (int)Svc.Nop);
        Net.SendUnreliableMessage(serverSock, &nopMsg);

        byte* realBuf = stackalloc byte[4];
        SizeBuf_t realMsg = default;
        realMsg.Data = realBuf;
        realMsg.MaxSize = 4;
        Msg.WriteByte(&realMsg, 77);
        Net.SendMessage(serverSock, &realMsg);

        int result = 0;
        string output = CaptureOutput(() => result = ClDemo.GetMessage());

        Assert.AreEqual(1, result);
        Assert.AreEqual(1, Net.net_message->CurSize);
        Assert.AreEqual((byte)77, Net.net_message->Data[0]);
        StringAssert.Contains(output, "keepalive");
    }

    [TestMethod]
    public void GetMessage_LiveConnection_NoQueuedMessage_ReturnsZero()
    {
        ConnectLoopback(out _, out _);

        int result = ClDemo.GetMessage();

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void GetMessage_LiveConnection_WhileRecording_WritesToDemoFile()
    {
        ConnectLoopback(out _, out QSocket_t* serverSock);

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            ClDemo.demofile = Sys.FileOpenWrite(Ansi(path));
            ClDemo.demorecording = true;
            Cl.viewangles = default;

            byte* buf = stackalloc byte[4];
            SizeBuf_t msg = default;
            msg.Data = buf;
            msg.MaxSize = 4;
            Msg.WriteByte(&msg, 9);
            Net.SendMessage(serverSock, &msg);

            int result = ClDemo.GetMessage();
            Assert.AreEqual(1, result);

            Sys.FileClose(ClDemo.demofile);
            ClDemo.demofile = -1;

            // length(4) + 3 angle floats(4 each) + 1-byte payload
            byte[] onDisk = File.ReadAllBytes(path);
            Assert.HasCount(4 + 12 + 1, onDisk);
            Assert.AreEqual(1, BitConverter.ToInt32(onDisk, 0));
            Assert.AreEqual((byte)9, onDisk[16]);
        }
        finally
        {
            if (ClDemo.demofile >= 0)
            {
                Sys.FileClose(ClDemo.demofile);
                ClDemo.demofile = -1;
            }
            File.Delete(path);
        }
    }

    [TestMethod]
    public void GetMessage_LiveConnection_NotRecording_DoesNotTouchDemofile()
    {
        ConnectLoopback(out _, out QSocket_t* serverSock);

        byte* buf = stackalloc byte[4];
        SizeBuf_t msg = default;
        msg.Data = buf;
        msg.MaxSize = 4;
        Msg.WriteByte(&msg, 1);
        Net.SendMessage(serverSock, &msg);

        ClDemo.GetMessage();

        Assert.AreEqual(-1, ClDemo.demofile); // never opened -- WriteDemoMessage was never called
    }
}
