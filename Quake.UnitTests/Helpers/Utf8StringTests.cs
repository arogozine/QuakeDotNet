// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

// Utf8String stands in for a C string literal: the pointer must be stable,
// NUL-terminated and identical for identical literals.
[TestClass]
public sealed unsafe class Utf8StringTests
{
    [TestMethod]
    public void Ctor_ReturnsStableNullTerminatedPointer()
    {
        byte* p1 = new Utf8String("abc"u8);
        byte* p2 = new Utf8String("abc"u8);

        Assert.AreEqual(3, Q.StrLen(p1));
        Assert.AreEqual("abc", CStr.ToString(p1));

        // RVA static data: the same literal always resolves to the same,
        // never-moving address, even across GCs.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.AreEqual((nint)p1, (nint)p2);
        Assert.AreEqual("abc", CStr.ToString(p1));
    }

    [TestMethod]
    public void Equality_ComparesContents()
    {
        byte* heap = System.Runtime.InteropServices.NativeMemory.AllocArrayZeroed<byte>(8);
        heap[0] = (byte)'a'; heap[1] = (byte)'b';
        Utf8String a = "ab"u8;
        Utf8String b = new(heap);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.IsTrue(a != (Utf8String)"abc"u8);
        Assert.IsTrue(a == (Utf8String)"ab"u8);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        System.Runtime.InteropServices.NativeMemory.Free(heap);
    }
}
