// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.Benchmarks;

public unsafe class MarkLeavesBenchmarks
{
    #region Setup / Teardown

    [Params(1000, 4000)] public int NumLeafs;
    [Params(0.05, 0.3)] public double Density;
    private MLeaf_t* _leaves;
    private MNode_t* _nodes;
    private byte* _vis;
    private int _frame;

    [GlobalSetup]
    public void Setup()
    {
        int n = NumLeafs;
        _leaves = NativeMemory.AllocArrayZeroed<MLeaf_t>(n + 1);
        _nodes = NativeMemory.AllocArrayZeroed<MNode_t>(n + 1);
        for (int i = 0; i <= n; i++) _leaves[i].Parent = &_nodes[1 + i / 4 % n];
        for (int k = 1; k <= n; k++) _nodes[k].Parent = k > 1 ? &_nodes[k / 2] : null;
        _vis = NativeMemory.AllocArrayZeroed<byte>(((n + 7) >> 3) + 16);
        Random r = new(7);
        for (int i = 0; i < n; i++) if (r.NextDouble() < Density) _vis[i >> 3] |= (byte)(1 << (i & 7));
        _vis[(n - 1) >> 3] |= (byte)(0xFF << ((n - 1) & 7) << 1);  // garbage padding bits beyond NumLeafs
    }

    [GlobalCleanup] public void Cleanup() { NativeMemory.Free(_leaves); NativeMemory.Free(_nodes); NativeMemory.Free(_vis); }

    #endregion

    [Benchmark(Baseline = true)]
    public void Original()
    {
        int vf = ++_frame;
        for (int i = 0; i < NumLeafs; i++)
            if ((_vis[i >> 3] & (1 << (i & 7))) != 0)
            {
                MNode_t* node = (MNode_t*)&_leaves[i + 1];
                do { if (node->VisFrame == vf) break; node->VisFrame = vf; node = node->Parent; } while (node != null);
            }
    }

    [Benchmark]
    public void WordScan() => RMain.MarkVisibleLeaves(_vis, NumLeafs, _leaves, ++_frame);
}
