// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class REfragTests
{
    [TestInitialize]
    public void Setup()
    {
        RMain.r_pefragtopnode = null;
        RMain.r_emins = default;
        RMain.r_emaxs = default;
        Cl.free_efrags = null;
    }

    [TestMethod]
    public void SplitEntityOnNode_SolidNode_ReturnsImmediately()
    {
        MNode_t node = default;
        node.Contents = (int)Contents.Solid;

        // Should not touch r_pefragtopnode nor dereference node.Plane
        // (which is null) -- proves the solid check short-circuits first.
        REfrag.SplitEntityOnNode(&node);

        Assert.IsTrue(RMain.r_pefragtopnode == null);
    }

    [TestMethod]
    public void SplitEntityOnNode_LeafWithNoFreeEfrags_PrintsAndReturns()
    {
        MLeaf_t leaf = default;
        leaf.Contents = -1; // non-solid leaf

        Cl.free_efrags = null;

        REfrag.SplitEntityOnNode((MNode_t*)&leaf);

        // r_pefragtopnode still gets set before the free-list check (matches
        // original: "if (!r_pefragtopnode) r_pefragtopnode = node;" runs
        // before the free-efrag grab).
        Assert.IsTrue(RMain.r_pefragtopnode == (MNode_t*)&leaf);
        Assert.IsTrue(leaf.EFrags == null); // nothing linked -- no free efrag available
    }

    [TestMethod]
    public void SplitEntityOnNode_LeafWithFreeEfrag_LinksIntoLeafAndEntityChains()
    {
        EFrag_t efragStorage = default;
        Cl.free_efrags = &efragStorage;

        Entity_t entity = default;

        MLeaf_t leaf = default;
        leaf.Contents = -1;

        // Drive R_AddEfrags-equivalent linkage manually since lastlink/r_addent
        // are private to REfrag -- use AddEfrags itself via a minimal single-leaf world.
        Model_t model = default;
        model.Nodes = (MNode_t*)&leaf;
        Cl.worldmodel = &model;

        Model_t entModel = default; // Mins/Maxs default to 0
        entity.Model = &entModel;
        entity.Origin = default;

        // ent must not equal ClEntities.cl_entities (the world) to be added.
        Assert.IsTrue(&entity != ClEntities.cl_entities);

        REfrag.AddEfrags(&entity);

        Assert.IsTrue(entity.Efrag == &efragStorage);
        Assert.IsTrue(efragStorage.Entity == &entity);
        Assert.IsTrue(leaf.EFrags == &efragStorage);
        Assert.IsTrue(Cl.free_efrags == null); // consumed
    }

    [TestMethod]
    public void AddEfrags_NullModel_ReturnsImmediately()
    {
        Entity_t entity = default;
        entity.Model = null;

        REfrag.AddEfrags(&entity);

        Assert.IsTrue(entity.Efrag == null);
    }

    [TestMethod]
    public void AddEfrags_WorldEntity_NeverAdded()
    {
        // ClEntities.cl_entities[0] represents the world; passing that exact
        // pointer must be a no-op regardless of its Model field.
        Model_t model = default;
        ClEntities.cl_entities->Model = &model;

        REfrag.AddEfrags(ClEntities.cl_entities);

        Assert.IsTrue(ClEntities.cl_entities->Efrag == null);
    }

    [TestMethod]
    public void SplitEntityOnNode2_StaleVisframe_ReturnsImmediately()
    {
        RMain.r_visframecount = 5;

        MNode_t node = default;
        node.VisFrame = 4; // stale

        REfrag.SplitEntityOnNode2(&node);

        Assert.IsTrue(RMain.r_pefragtopnode == null);
    }

    [TestMethod]
    public void SplitEntityOnNode2_NonSolidLeaf_SetsTopNode()
    {
        RMain.r_visframecount = 5;

        MLeaf_t leaf = default;
        leaf.VisFrame = 5;
        leaf.Contents = -1; // non-solid (not (int)Contents.Solid = -2)

        REfrag.SplitEntityOnNode2((MNode_t*)&leaf);

        Assert.IsTrue(RMain.r_pefragtopnode == (MNode_t*)&leaf);
    }

    [TestMethod]
    public void SplitEntityOnNode2_SolidLeaf_LeavesTopNodeUnset()
    {
        RMain.r_visframecount = 5;

        MLeaf_t leaf = default;
        leaf.VisFrame = 5;
        leaf.Contents = (int)Contents.Solid;

        REfrag.SplitEntityOnNode2((MNode_t*)&leaf);

        Assert.IsTrue(RMain.r_pefragtopnode == null);
    }

    [TestMethod]
    public void StoreEfrags_UnvisitedEntity_AddsToVisedictsAndStampsFrame()
    {
        ClEntities.cl_numvisedicts = 0;
        DIface.r_framecount = 10;

        Model_t model = default;
        model.Type = ModType_t.Brush;

        Entity_t entity = default;
        entity.Model = &model;
        entity.VisFrame = 9; // stale -- not this frame yet

        EFrag_t frag = default;
        frag.Entity = &entity;
        frag.LeafNext = null;

        EFrag_t* head = &frag;
        REfrag.StoreEfrags(&head);

        Assert.AreEqual(1, ClEntities.cl_numvisedicts);
        Assert.IsTrue(ClEntities.cl_visedicts[0] == &entity);
        Assert.AreEqual(10, entity.VisFrame);
    }

    [TestMethod]
    public void StoreEfrags_AlreadyVisitedThisFrame_SkipsButAdvancesChain()
    {
        ClEntities.cl_numvisedicts = 0;
        DIface.r_framecount = 10;

        Model_t model = default;
        model.Type = ModType_t.Brush;

        Entity_t entity = default;
        entity.Model = &model;
        entity.VisFrame = 10; // already recorded this frame

        EFrag_t frag = default;
        frag.Entity = &entity;
        frag.LeafNext = null;

        EFrag_t* head = &frag;
        REfrag.StoreEfrags(&head);

        Assert.AreEqual(0, ClEntities.cl_numvisedicts);
    }

    [TestMethod]
    public void StoreEfrags_BadEntityType_Throws()
    {
        Model_t model = default;
        model.Type = (ModType_t)99;

        Entity_t entity = default;
        entity.Model = &model;

        EFrag_t frag = default;
        frag.Entity = &entity;

        EFrag_t* fragPtr = &frag;
        EFrag_t** head = &fragPtr;
        Assert.ThrowsExactly<QuakeSysErrorException>(() => REfrag.StoreEfrags(head));
    }
}
