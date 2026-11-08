// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class REdgeTests
{
    private Surf_t* _surfaces;
    private ESpan_t* _spanBuf;

    [TestInitialize]
    public void Setup()
    {
        // surfaces[0] dummy, [1] background, [2] one real surface.
        _surfaces = (Surf_t*)NativeMemory.AllocZeroed((nuint)(3 * sizeof(Surf_t)));
        RShared.surfaces = _surfaces;
        RShared.surface_p = &_surfaces[3];
        RShared.surf_max = &_surfaces[3];

        _spanBuf = (ESpan_t*)NativeMemory.AllocZeroed((nuint)(64 * sizeof(ESpan_t)));
        REdge.span_p = _spanBuf;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_surfaces);
        NativeMemory.Free(_spanBuf);
    }

    [TestMethod]
    public void InsertNewEdges_InsertsBeforeFirstEdgeWithGreaterOrEqualU()
    {
        Edge_t head = default, tail = default, existing = default;
        head.U = int.MinValue;
        head.Next = &existing;
        existing.U = 30 << 20;
        existing.Prev = &head;
        existing.Next = &tail;
        tail.U = int.MaxValue;
        tail.Prev = &existing;

        Edge_t toAdd = default;
        toAdd.U = 20 << 20;
        toAdd.Next = null;

        REdge.InsertNewEdges(&toAdd, &existing);

        // toAdd (u=20) should land between head and existing (u=30)
        Assert.IsTrue(head.Next == &toAdd);
        Assert.IsTrue(toAdd.Prev == &head);
        Assert.IsTrue(toAdd.Next == &existing);
        Assert.IsTrue(existing.Prev == &toAdd);
    }

    [TestMethod]
    public void RemoveEdges_UnlinksSingleEdgeFromChain()
    {
        Edge_t a = default, b = default, c = default;
        a.Next = &b;
        b.Prev = &a;
        b.Next = &c;
        c.Prev = &b;
        b.NextRemove = null;

        REdge.RemoveEdges(&b);

        Assert.IsTrue(a.Next == &c);
        Assert.IsTrue(c.Prev == &a);
    }

    [TestMethod]
    public void StepActiveU_ReordersEdgeThatStepsBelowItsPredecessor()
    {
        *RShared.edge_head = default;
        *RShared.edge_tail = default;
        *RShared.edge_aftertail = default;

        Edge_t* edgeHead = RShared.edge_head;
        Edge_t* edgeTail = RShared.edge_tail;
        Edge_t* edgeAfterTail = RShared.edge_aftertail;

        edgeHead->U = 0;

        Edge_t a = default, b = default;
        a.U = 10 << 20;
        a.UStep = 5 << 20; // advances forward -- no pushback
        b.U = 20 << 20;
        b.UStep = -8 << 20; // steps back below 'a' -- triggers pushback

        // edge_head -> a -> b -> edge_tail -> edge_aftertail
        edgeHead->Next = &a;
        a.Prev = edgeHead;
        a.Next = &b;
        b.Prev = &a;
        b.Next = edgeTail;
        edgeTail->Prev = &b;
        edgeTail->Next = edgeAfterTail;
        edgeAfterTail->Prev = edgeTail;

        REdge.StepActiveU(edgeHead->Next);

        // a.u = 10+5 = 15; b.u = 20-8 = 12 -- b now sorts before a.
        Assert.AreEqual(15, a.U >> 20);
        Assert.AreEqual(12, b.U >> 20);

        Assert.IsTrue(edgeHead->Next == &b);
        Assert.IsTrue(b.Prev == edgeHead);
        Assert.IsTrue(b.Next == &a);
        Assert.IsTrue(a.Prev == &b);
        Assert.IsTrue(a.Next == edgeTail);
        Assert.IsTrue(edgeTail->Prev == &a);
    }

    [TestMethod]
    public void StepActiveU_KeepsOrderPastX2047()
    {
        // Regression: with a 32-bit 12.20 U, a 2560-wide screen's edge_tail
        // U ((2560 << 20) + 0xFFFFF) wrapped negative, so StepActiveU pushed
        // edge_tail back past edge_head and dereferenced its null Prev.
        *RShared.edge_head = default;
        *RShared.edge_tail = default;
        *RShared.edge_aftertail = default;

        Edge_t* edgeHead = RShared.edge_head;
        Edge_t* edgeTail = RShared.edge_tail;
        Edge_t* edgeAfterTail = RShared.edge_aftertail;

        // R_ScanEdges's background edge setup for a 2560-wide vrect.
        edgeHead->U = 0;
        edgeHead->Prev = null;
        edgeTail->U = (2560L << 20) + 0xFFFFF;
        edgeAfterTail->U = -1;

        Edge_t a = default;
        a.U = 2400L << 20;
        a.UStep = 1 << 20;

        // edge_head -> a -> edge_tail -> edge_aftertail
        edgeHead->Next = &a;
        a.Prev = edgeHead;
        a.Next = edgeTail;
        edgeTail->Prev = &a;
        edgeTail->Next = edgeAfterTail;
        edgeAfterTail->Prev = edgeTail;

        REdge.StepActiveU(edgeHead->Next);

        Assert.AreEqual(2401L, a.U >> 20);
        Assert.AreEqual(2560L, edgeTail->U >> 20);

        Assert.IsTrue(edgeHead->Next == &a);
        Assert.IsTrue(a.Next == edgeTail);
        Assert.IsTrue(edgeTail->Prev == &a);
        Assert.IsTrue(edgeTail->Next == edgeAfterTail);
    }

    [TestMethod]
    public void GenerateSpans_SingleSurfaceObscuresAndReleasesBackground()
    {
        // Background (surfaces[1]): huge key so any real surface sorts in
        // front of it in front-to-back (R_GenerateSpans) mode.
        _surfaces[1].Key = 0x7FFFFFFF;
        _surfaces[1].Next = _surfaces[1].Prev = &_surfaces[1];

        // Our one real surface: key smaller than background -> drawn on top.
        _surfaces[2].Key = 100;

        REdge.edge_head_u_shift20 = 0;   // screen left edge (x=0)
        REdge.edge_tail_u_shift20 = 30;  // screen right edge (x=30)
        REdge.current_iv = 5;
        REdge.fv = 5;

        // Active edge list for this scanline: edge_head -> left -> right -> edge_tail
        Edge_t* edgeHead = RShared.edge_head;
        Edge_t* edgeTail = RShared.edge_tail;
        *RShared.edge_head = default;
        *RShared.edge_tail = default;

        Edge_t left = default, right = default;
        left.U = 10 << 20;
        left.Surfs[0] = 0;
        left.Surfs[1] = 2; // opens surface index 2

        right.U = 20 << 20;
        right.Surfs[0] = 2; // closes surface index 2
        right.Surfs[1] = 0;

        edgeHead->Next = &left;
        left.Prev = edgeHead;
        left.Next = &right;
        right.Prev = &left;
        right.Next = edgeTail;
        edgeTail->Prev = &right;

        REdge.GenerateSpans();

        // The real surface got exactly one span covering [10, 20).
        ESpan_t* surfSpan = _surfaces[2].Spans;
        Assert.IsTrue(surfSpan != null);
        Assert.AreEqual(10, surfSpan->U);
        Assert.AreEqual(10, surfSpan->Count);
        Assert.AreEqual(5, surfSpan->V);
        Assert.IsTrue(surfSpan->PNext == null);

        // Background got two spans: [0,10) emitted when the surface opened,
        // and [20,30) emitted by CleanupSpan after the surface closed.
        ESpan_t* bgSpan1 = _surfaces[1].Spans; // most recently prepended: [20,30)
        Assert.AreEqual(20, bgSpan1->U);
        Assert.AreEqual(10, bgSpan1->Count);
        ESpan_t* bgSpan2 = bgSpan1->PNext; // [0,10)
        Assert.AreEqual(0, bgSpan2->U);
        Assert.AreEqual(10, bgSpan2->Count);
        Assert.IsTrue(bgSpan2->PNext == null);

        // Active stack collapsed back to just the background, spanstate reset.
        Assert.AreEqual(0, _surfaces[1].SpanState);
        Assert.AreEqual(0, _surfaces[2].SpanState);
    }
}
