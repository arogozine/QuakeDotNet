// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

global using Vec3_t = System.Numerics.Vector3;

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class ChaseTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;
    private DClipNode_t* _clipnodes;
    private MPlane_t* _planes;
    private Model_t* _worldmodel;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Cvar.cvar_vars = null;
        SetupOpenWorld();
    }

    [TestCleanup]
    public void Teardown()
    {
        Cl.worldmodel = null;
        NativeMemory.Free(_worldmodel);
        NativeMemory.Free(_planes);
        NativeMemory.Free(_clipnodes);
        NativeMemory.Free(_buf);
    }

    // Geometry-free world for Chase_Update's TraceLine: hull 0 is a single
    // clipnode whose both children are CONTENTS_EMPTY, so the trace never
    // hits anything (same fixture as MainLoopTests.SetupOpenWorld).
    private void SetupOpenWorld()
    {
        _clipnodes = NativeMemory.AllocZeroed<DClipNode_t>();
        _clipnodes[0].Children[0] = (int)Contents.Empty;
        _clipnodes[0].Children[1] = (int)Contents.Empty;

        _planes = NativeMemory.AllocZeroed<MPlane_t>();
        _planes[0].Normal[0] = 1;

        _worldmodel = NativeMemory.AllocZeroed<Model_t>();
        _worldmodel->Type = ModType_t.Brush;
        _worldmodel->Hulls[0].ClipNodes = _clipnodes;
        _worldmodel->Hulls[0].Planes = _planes;

        Cl.worldmodel = _worldmodel;
    }

    [TestMethod]
    public void Init_RegistersChaseCvars()
    {
        Chase.Init();

        Assert.IsTrue(Cvar.FindVar(new Utf8String("chase_back"u8)) != null);
        Assert.AreEqual(100f, Chase.chase_back->Value);
        Assert.AreEqual(16f, Chase.chase_up->Value);
        Assert.AreEqual(0f, Chase.chase_active->Value);
    }

    [TestMethod]
    public void Update_MovesViewOriginToChaseDest()
    {
        Chase.Init();
        Chase.chase_back->Value = 100;
        Chase.chase_up->Value = 16;
        Chase.chase_right->Value = 0;

        Cl.viewangles = default; // facing +X
        RShared.r_refdef->Vieworg = default;
        RShared.r_refdef->Vieworg[0] = 0;
        RShared.r_refdef->Vieworg[1] = 0;
        RShared.r_refdef->Vieworg[2] = 0;

        Chase.Update();

        // forward = (1,0,0) at angle 0, so chase_dest = origin - forward*back
        Assert.AreEqual(-100f, RShared.r_refdef->Vieworg[0], 0.001f);
        Assert.AreEqual(0f, RShared.r_refdef->Vieworg[1], 0.001f);
        Assert.AreEqual(16f, RShared.r_refdef->Vieworg[2], 0.001f);
    }
}
