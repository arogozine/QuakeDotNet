// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// World/Sv share static state (sv_areanodes, box_hull, Sv.worldmodel/edicts),
// so, like SvPhysTests/SvTests, these can't run concurrently with anything
// else touching that state.
[TestClass]
public sealed unsafe class WorldTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private List<nint> _allocations = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        _allocations = [];

        Cvar.cvar_vars = null;

        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        // Two edicts: [0] the world, [1] a generic test entity.
        Sv.edicts = AllocZeroed<EDict_t>(2);
        Sv.num_edicts = 2;
        Sv.max_edicts = 2;

        SetupOpenWorld();
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
        {
            NativeMemory.Free((void*)p);
        }

        NativeMemory.Free(_buf);
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged => TestHelpers.AllocZeroed<T>(_allocations, count);

    // Builds a worldmodel whose collision hulls report (int)Contents.Empty for
    // every point -- see SvPhysTests.cs's own copy of this fixture for the
    // full rationale.
    private void SetupOpenWorld()
    {
        DClipNode_t* clipnodes = AllocZeroed<DClipNode_t>(1);
        clipnodes[0].PlaneNum = 0;
        clipnodes[0].Children[0] = (int)Contents.Empty;
        clipnodes[0].Children[1] = (int)Contents.Empty;

        MPlane_t* planes = AllocZeroed<MPlane_t>(1);
        planes[0].Normal[0] = 1;
        planes[0].Type = 0;
        planes[0].Dist = 0;

        Model_t* worldmodel = AllocZeroed<Model_t>(1);
        worldmodel->Type = ModType_t.Brush;
        for (int i = 0; i < BspFile.MAX_MAP_HULLS; i++)
        {
            worldmodel->Hulls[i].ClipNodes = clipnodes;
            worldmodel->Hulls[i].Planes = planes;
            worldmodel->Hulls[i].FirstClipNode = 0;
            worldmodel->Hulls[i].LastClipNode = 0;
        }

        Sv.worldmodel = worldmodel;
        Sv.models[0] = worldmodel;

        Sv.edicts[0].V.Solid = (int)SolidType.Bsp;
        Sv.edicts[0].V.MoveType = (int)MoveType.Push;
        Sv.edicts[0].V.ModelIndex = 0;

        World.ClearWorld();
    }

    // A single-clipnode hull with a horizontal floor plane at z == floorZ:
    // points above it report (int)Contents.Empty, points below/on it report
    // (int)Contents.Solid -- just enough geometry for Move/DropToFloor to have
    // something to hit, without needing a real BSP file.
    // solidAbove swaps the children, making the plane a ceiling: solid above,
    // empty below, so a trace hits the plane's back side.
    private void SetupFloorWorld(float floorZ, bool solidAbove = false)
    {
        DClipNode_t* clipnodes = AllocZeroed<DClipNode_t>(1);
        clipnodes[0].PlaneNum = 0;
        clipnodes[0].Children[0] = (short)(solidAbove ? Contents.Solid : Contents.Empty);
        clipnodes[0].Children[1] = (short)(solidAbove ? Contents.Empty : Contents.Solid);

        MPlane_t* planes = AllocZeroed<MPlane_t>(1);
        planes[0].Normal[2] = 1;
        planes[0].Type = 2;
        planes[0].Dist = floorZ;

        Model_t* worldmodel = AllocZeroed<Model_t>(1);
        worldmodel->Type = ModType_t.Brush;
        for (int i = 0; i < BspFile.MAX_MAP_HULLS; i++)
        {
            worldmodel->Hulls[i].ClipNodes = clipnodes;
            worldmodel->Hulls[i].Planes = planes;
            worldmodel->Hulls[i].FirstClipNode = 0;
            worldmodel->Hulls[i].LastClipNode = 0;
        }

        Sv.worldmodel = worldmodel;
        Sv.models[0] = worldmodel;

        World.ClearWorld();
    }

    private EDict_t* TestEnt => Progs.EdictNum(1);

    [TestMethod]
    public void HullForBox_SetsPlaneDistancesInAxisOrder()
    {
        Vec3_t mins = default, maxs = default;
        mins[0] = -1; mins[1] = -2; mins[2] = -3;
        maxs[0] = 4; maxs[1] = 5; maxs[2] = 6;

        Hull_t* hull = World.HullForBox(mins, maxs);

        Assert.AreEqual(4f, hull->Planes[0].Dist);  // maxs[0]
        Assert.AreEqual(-1f, hull->Planes[1].Dist);  // mins[0]
        Assert.AreEqual(5f, hull->Planes[2].Dist);  // maxs[1]
        Assert.AreEqual(-2f, hull->Planes[3].Dist);  // mins[1]
        Assert.AreEqual(6f, hull->Planes[4].Dist);  // maxs[2]
        Assert.AreEqual(-3f, hull->Planes[5].Dist);  // mins[2]
    }

    [TestMethod]
    public void HullForEntity_NonBspEntity_BuildsTempHullFromBoundingBoxAndUsesOriginOffset()
    {
        EDict_t* ent = TestEnt;
        ent->V.Solid = 2; // (int)SolidType.BBox, not (int)SolidType.Bsp
        ent->V.Origin = new Vec3_t { [0] = 10, [1] = 20, [2] = 30 };
        ent->V.Mins = new Vec3_t { [0] = -16, [1] = -16, [2] = -24 };
        ent->V.Maxs = new Vec3_t { [0] = 16, [1] = 16, [2] = 32 };

        Vec3_t mins = default, maxs = default;
        mins[0] = -16; mins[1] = -16; mins[2] = -24;
        maxs[0] = 16; maxs[1] = 16; maxs[2] = 32;
        Vec3_t offset = default;

        World.HullForEntity(ent, mins, maxs, &offset);

        Assert.AreEqual(10f, offset[0]);
        Assert.AreEqual(20f, offset[1]);
        Assert.AreEqual(30f, offset[2]);
    }

    [TestMethod]
    public void HullForEntity_SolidBspWithoutMovetypePush_ThrowsSysError()
    {
        EDict_t* ent = TestEnt;
        ent->V.Solid = (int)SolidType.Bsp;
        ent->V.MoveType = 0; // not (int)MoveType.Push

        Assert.ThrowsExactly<QuakeSysErrorException>(() =>
        {
            Vec3_t offset = default;
            World.HullForEntity(ent, default, default, &offset);
        });
    }

    [TestMethod]
    public void ClearWorld_CalledRepeatedly_LeavesTreeUsableForLinkEdict()
    {
        // ClearWorld already ran once in Setup(); re-running it must reset
        // sv_numareanodes back down each time rather than growing the fixed
        // AREA_NODES-sized backing buffer unbounded across calls -- if it
        // didn't, this LinkEdict would eventually write past the buffer.
        World.ClearWorld();
        World.ClearWorld();

        EDict_t* ent = TestEnt;
        ent->V.Solid = (int)SolidType.Trigger;

        World.LinkEdict(ent, false);

        Assert.IsTrue(ent->Area.Prev != null);
    }

    [TestMethod]
    public void LinkEdict_NonFreeTriggerEntity_ComputesExpandedAbsBoundsAndLinks()
    {
        EDict_t* ent = TestEnt;
        ent->V.Origin = new Vec3_t { [0] = 10, [1] = 10, [2] = 10 };
        ent->V.Mins = new Vec3_t { [0] = -8, [1] = -8, [2] = -8 };
        ent->V.Maxs = new Vec3_t { [0] = 8, [1] = 8, [2] = 8 };
        ent->V.Solid = (int)SolidType.Trigger;

        World.LinkEdict(ent, false);

        // default (non-FL_ITEM) expansion is +-1
        Assert.AreEqual(1f, ent->V.AbsMin[0]);
        Assert.AreEqual(1f, ent->V.AbsMin[1]);
        Assert.AreEqual(1f, ent->V.AbsMin[2]);
        Assert.AreEqual(19f, ent->V.AbsMax[0]);
        Assert.IsTrue(ent->Area.Prev != null);
    }

    [TestMethod]
    public void LinkEdict_FlItemEntity_ExpandsAbsBoundsBy15OnXyOnly()
    {
        EDict_t* ent = TestEnt;
        ent->V.Origin = default;
        ent->V.Mins = default;
        ent->V.Maxs = default;
        ent->V.Solid = (int)SolidType.Trigger;
        ent->V.Flags = SvPhys.FL_ITEM;

        World.LinkEdict(ent, false);

        Assert.AreEqual(-15f, ent->V.AbsMin[0]);
        Assert.AreEqual(-15f, ent->V.AbsMin[1]);
        Assert.AreEqual(0f, ent->V.AbsMin[2]); // z is untouched by the FL_ITEM expansion
        Assert.AreEqual(15f, ent->V.AbsMax[0]);
        Assert.AreEqual(15f, ent->V.AbsMax[1]);
    }

    [TestMethod]
    public void LinkEdict_WorldEntity_ReturnsWithoutLinking()
    {
        EDict_t* world = Progs.EdictNum(0);

        World.LinkEdict(world, false);

        Assert.IsTrue(world->Area.Prev == null);
    }

    [TestMethod]
    public void LinkEdict_FreeEntity_ReturnsWithoutLinking()
    {
        EDict_t* ent = TestEnt;
        ent->Free = 1;
        ent->V.Solid = (int)SolidType.Trigger;

        World.LinkEdict(ent, false);

        Assert.IsTrue(ent->Area.Prev == null);
    }

    [TestMethod]
    public void LinkThenUnlinkEdict_ClearsAreaLinksAndIsIdempotent()
    {
        EDict_t* ent = TestEnt;
        ent->V.Solid = (int)SolidType.Trigger;

        World.LinkEdict(ent, false);
        Assert.IsTrue(ent->Area.Prev != null);

        World.UnlinkEdict(ent);
        Assert.IsTrue(ent->Area.Prev == null);
        Assert.IsTrue(ent->Area.Next == null);

        // a second unlink must be a safe no-op (early return on Area.Prev == null)
        World.UnlinkEdict(ent);
        Assert.IsTrue(ent->Area.Prev == null);
    }

    [TestMethod]
    public void PointContents_EmptyWorld_ReturnsContentsEmptyEverywhere()
    {
        Vec3_t p = default;
        p[0] = 5; p[1] = -5; p[2] = 100;

        Assert.AreEqual((int)Contents.Empty, World.PointContents(p));
        Assert.AreEqual((int)Contents.Empty, World.TruePointContents(p));
    }

    [TestMethod]
    public void PointContents_BelowFloorPlane_ReturnsContentsSolid()
    {
        SetupFloorWorld(floorZ: 0);
        Vec3_t below = default;
        below[2] = -10;

        Assert.AreEqual((int)Contents.Solid, World.PointContents(below));
        Assert.AreEqual((int)Contents.Solid, World.TruePointContents(below));
    }

    [TestMethod]
    public void Move_EmptyWorldStraightLine_TraversesFullyWithFractionOne()
    {
        Vec3_t start = default, end = default;
        end[0] = 100;

        Trace_t trace = World.Move(start, default, default, end, (int)ClipMoveType.Normal, TestEnt);

        Assert.AreEqual(1f, trace.Fraction);
        Assert.IsTrue(trace.InOpen);
        Assert.IsTrue(trace.Ent == null);
    }

    [TestMethod]
    public void Move_HitsFloor_ReturnsPartialFractionAndImpactPlane()
    {
        SetupFloorWorld(floorZ: 0);
        Vec3_t start = default, end = default;
        start[2] = 50;
        end[2] = -50;

        Trace_t trace = World.Move(start, default, default, end, (int)ClipMoveType.Normal, TestEnt);

        Assert.IsLessThan(1f, trace.Fraction);
        Assert.AreEqual(1f, trace.Plane.Normal[2], 0.001f);
        Assert.IsTrue(trace.EndPos[2] >= -0.5f && trace.EndPos[2] <= 0.5f);
    }

    // SV_RecursiveHullCheck (world.c): a back-side hit flips the plane with
    // VectorSubtract (vec3_origin, plane->normal, ...), so the zero
    // components of the flipped normal are +0, not -0.
    [TestMethod]
    public void Move_HitsCeilingBackSide_FlipsNormalWithPositiveZeroComponents()
    {
        SetupFloorWorld(floorZ: 0, solidAbove: true);
        Vec3_t start = default, end = default;
        start[2] = -50;
        end[2] = 50;

        Trace_t trace = World.Move(start, default, default, end, (int)ClipMoveType.Normal, TestEnt);

        Assert.IsLessThan(1f, trace.Fraction);
        Assert.AreEqual(0, BitConverter.SingleToInt32Bits(trace.Plane.Normal[0]));
        Assert.AreEqual(0, BitConverter.SingleToInt32Bits(trace.Plane.Normal[1]));
        Assert.AreEqual(-1f, trace.Plane.Normal[2]);
        Assert.AreEqual(0f, trace.Plane.Dist);
    }

    [TestMethod]
    public void Move_StartPointInsideSolid_ReportsStartSolid()
    {
        SetupFloorWorld(floorZ: 0);
        Vec3_t start = default, end = default;
        start[2] = -10;
        end[2] = -20;

        Trace_t trace = World.Move(start, default, default, end, (int)ClipMoveType.Normal, TestEnt);

        Assert.IsTrue(trace.StartSolid);
    }

    [TestMethod]
    public void DropToFloor_FloorWithinRange_SnapsOriginAndSetsOnGround()
    {
        SetupFloorWorld(floorZ: 0);
        EDict_t* ent = TestEnt;
        ent->V.Origin = new Vec3_t { [0] = 0, [1] = 0, [2] = 100 };
        ent->V.Mins = default;
        ent->V.Maxs = default;

        bool result = World.DropToFloor(ent);

        Assert.IsTrue(result);
        Assert.IsTrue(ent->V.Origin[2] >= -0.5f && ent->V.Origin[2] <= 0.5f);
        Assert.AreEqual((float)SvPhys.FL_ONGROUND, ent->V.Flags);
    }

    [TestMethod]
    public void DropToFloor_NoFloorWithin256Units_ReturnsFalseAndLeavesOriginUntouched()
    {
        EDict_t* ent = TestEnt; // SetupOpenWorld's fixture never reports solid
        ent->V.Origin = new Vec3_t { [0] = 0, [1] = 0, [2] = 500 };

        bool result = World.DropToFloor(ent);

        Assert.IsFalse(result);
        Assert.AreEqual(500f, ent->V.Origin[2]);
    }

    [TestMethod]
    public void MoveBounds_ForwardMove_BracketsStartAndEndWithUnitPadding()
    {
        Vec3_t start = default, end = default, mins = default, maxs = default;
        start[0] = 0; end[0] = 10;
        mins[0] = -2; maxs[0] = 2;
        Vec3_t boxmins = default, boxmaxs = default;

        World.MoveBounds(start, mins, maxs, end, ref boxmins, ref boxmaxs);

        Assert.AreEqual(-3f, boxmins[0]); // start + mins - 1
        Assert.AreEqual(13f, boxmaxs[0]); // end + maxs + 1
    }

    [TestMethod]
    public void MoveBounds_BackwardMove_BracketsStartAndEndWithUnitPadding()
    {
        Vec3_t start = default, end = default, mins = default, maxs = default;
        start[0] = 10; end[0] = 0;
        mins[0] = -2; maxs[0] = 2;
        Vec3_t boxmins = default, boxmaxs = default;

        World.MoveBounds(start, mins, maxs, end, ref boxmins, ref boxmaxs);

        Assert.AreEqual(-3f, boxmins[0]); // end + mins - 1
        Assert.AreEqual(13f, boxmaxs[0]); // start + maxs + 1
    }

    // SV_MoveBounds (world.c) reference: the original per-axis branch
    private static void MoveBoundsReference(Vec3_t start, Vec3_t mins, Vec3_t maxs, Vec3_t end, out Vec3_t boxmins, out Vec3_t boxmaxs)
    {
        boxmins = default;
        boxmaxs = default;
        for (int i = 0; i < 3; i++)
        {
            if (end[i] > start[i])
            {
                boxmins[i] = start[i] + mins[i] - 1;
                boxmaxs[i] = end[i] + maxs[i] + 1;
            }
            else
            {
                boxmins[i] = end[i] + mins[i] - 1;
                boxmaxs[i] = start[i] + maxs[i] + 1;
            }
        }
    }

    // Edge values for the per-axis compare: signed zeros, ties, NaN
    private static readonly float[] MoveBoundsSpecials = [0f, -0f, 1f, -1f, 0.5f, float.NaN, 4096f, -4096f];

    private static float MoveBoundsValue(Random rng) =>
        rng.Next(2) == 0 ? MoveBoundsSpecials[rng.Next(MoveBoundsSpecials.Length)] : (float)((rng.NextDouble() * 8192) - 4096);

    [TestMethod]
    public void MoveBounds_RandomAndEdgeInputs_MatchOriginalBitwise()
    {
        Random rng = new(97531);

        for (int iter = 0; iter < 20_000; iter++)
        {
            Vec3_t start = new(MoveBoundsValue(rng), MoveBoundsValue(rng), MoveBoundsValue(rng));
            Vec3_t end = new(MoveBoundsValue(rng), MoveBoundsValue(rng), MoveBoundsValue(rng));
            Vec3_t mins = new(MoveBoundsValue(rng), MoveBoundsValue(rng), MoveBoundsValue(rng));
            Vec3_t maxs = new(MoveBoundsValue(rng), MoveBoundsValue(rng), MoveBoundsValue(rng));
            if (rng.Next(4) == 0)
            {
                end = start; // ties take the else branch
            }

            MoveBoundsReference(start, mins, maxs, end, out Vec3_t expectedMins, out Vec3_t expectedMaxs);
            Vec3_t actualMins = default, actualMaxs = default;
            World.MoveBounds(start, mins, maxs, end, ref actualMins, ref actualMaxs);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(BitConverter.SingleToInt32Bits(expectedMins[i]), BitConverter.SingleToInt32Bits(actualMins[i]), $"iteration {iter}: boxmins[{i}]");
                Assert.AreEqual(BitConverter.SingleToInt32Bits(expectedMaxs[i]), BitConverter.SingleToInt32Bits(actualMaxs[i]), $"iteration {iter}: boxmaxs[{i}]");
            }
        }
    }
}
