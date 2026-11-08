// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

// Empty-string inputs to SZ_Write / MSG_WriteString (common.c). The original
// C code handles a zero-length write as a no-op and "" as a lone NUL byte;
// the span/handler overloads used to throw (MemoryMarshal.AsRef on an empty span).
[TestClass]
public sealed unsafe class EmptyStringTests
{
    private byte* _data;
    private SizeBuf_t* _sb;

    [TestInitialize]
    public void Setup()
    {
        _data = (byte*)NativeMemory.AllocZeroed(64);
        _sb = Sz.Wrap(_data, 64);
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_sb);
        NativeMemory.Free(_data);
    }

    [TestMethod]
    public void SzWrite_EmptySpan_IsNoOp()
    {
        Sz.Write(_sb, ReadOnlySpan<byte>.Empty);

        Assert.AreEqual(0, _sb->CurSize);
        Assert.IsFalse(_sb->Overflowed);
    }

    [TestMethod]
    public void SzWrite_EmptySpan_AfterData_LeavesSizeUnchanged()
    {
        Sz.Write(_sb, "abc"u8);
        Sz.Write(_sb, ""u8);

        Assert.AreEqual(3, _sb->CurSize);
        Assert.AreEqual((byte)'c', _data[2]);
    }

    [TestMethod]
    public void SzWrite_NonEmptySpan_StillCopies()
    {
        Sz.Write(_sb, "abc"u8);

        Assert.AreEqual(3, _sb->CurSize);
        Assert.AreEqual((byte)'a', _data[0]);
        Assert.AreEqual((byte)'c', _data[2]);
    }

    [TestMethod]
    public void MsgWriteString_EmptyHandler_WritesLoneNul()
    {
        string empty = "";
        Msg.WriteString(_sb, $"{empty}");

        Assert.AreEqual(1, _sb->CurSize);
        Assert.AreEqual(0, _data[0]);
    }

    [TestMethod]
    public void MsgWriteString_EmptyLiteralHandler_WritesLoneNul()
    {
        Msg.WriteString(_sb, $"");

        Assert.AreEqual(1, _sb->CurSize);
        Assert.AreEqual(0, _data[0]);
    }

    [TestMethod]
    public void MsgWriteString_NonEmptyHandler_WritesTextAndNul()
    {
        int n = 42;
        Msg.WriteString(_sb, $"hi {n}");

        Assert.AreEqual(6, _sb->CurSize);
        Assert.AreEqual("hi 42", System.Text.Encoding.ASCII.GetString(_data, 5));
        Assert.AreEqual(0, _data[5]);
    }
}
