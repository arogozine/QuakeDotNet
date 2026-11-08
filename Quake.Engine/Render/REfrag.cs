// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// r_efrag.c: entity fragment linking -- attaches each entity to every leaf
/// its bounding box touches (so R_StoreEfrags can find "what's visible in
/// this leaf" without walking every entity every frame).
/// </summary>
public static unsafe class REfrag
{
    // r_pefragtopnode/r_emins/r_emaxs already live in RMain.cs (r_main.c's
    // own globals, step 2b, since R_DrawBEntitiesOnList already needed
    // them for the R_SplitEntityOnNode2 call site) -- reused, not
    // redeclared.

    // lastlink -- points at the "next" slot to write when linking a new
    // efrag onto an entity's chain (either &ent->efrag for the first, or
    // &ef->entnext for subsequent links).
    private static EFrag_t** lastlink;

    // r_addent -- entity currently being split into the BSP by R_AddEfrags.
    private static Entity_t* r_addent;

    // R_RemoveEfrags
    // Call when removing an object from the world or moving it to another position
    public static void RemoveEfrags(Entity_t* ent)
    {
        EFrag_t* ef = ent->Efrag;

        while (ef != null)
        {
            EFrag_t** prev = &ef->Leaf->EFrags;
            while (true)
            {
                EFrag_t* walk = *prev;
                if (walk == null)
                    break;
                if (walk == ef)
                {
                    // remove this fragment
                    *prev = ef->LeafNext;
                    break;
                }
                else
                {
                    prev = &walk->LeafNext;
                }
            }

            EFrag_t* old = ef;
            ef = ef->EntNext;

            // put it on the free list
            old->EntNext = Cl.free_efrags;
            Cl.free_efrags = old;
        }

        ent->Efrag = null;
    }

    // R_SplitEntityOnNode
    public static void SplitEntityOnNode(MNode_t* node)
    {
        if (node->Contents == (int)Contents.Solid)
            return;

        // add an efrag if the node is a leaf
        if (node->Contents < 0)
        {
            if (RMain.r_pefragtopnode == null)
                RMain.r_pefragtopnode = node;

            MLeaf_t* leaf = (MLeaf_t*)node;

            // grab an efrag off the free list
            EFrag_t* ef = Cl.free_efrags;
            if (ef == null)
            {
                Con.Printf("Too many efrags!\n"u8);
                return; // no free fragments...
            }
            Cl.free_efrags = Cl.free_efrags->EntNext;

            ef->Entity = r_addent;

            // add the entity link
            *lastlink = ef;
            lastlink = &ef->EntNext;
            ef->EntNext = null;

            // set the leaf links
            ef->Leaf = leaf;
            ef->LeafNext = leaf->EFrags;
            leaf->EFrags = ef;

            return;
        }

        // NODE_MIXED
        MPlane_t* splitplane = node->Plane;
        int sides = MathLib.BoxOnPlaneSideFast(RMain.r_emins, RMain.r_emaxs, splitplane);

        if (sides == 3)
        {
            // split on this plane
            // if this is the first splitter of this bmodel, remember it
            if (RMain.r_pefragtopnode == null)
                RMain.r_pefragtopnode = node;
        }

        // recurse down the contacted sides
        if ((sides & 1) != 0)
            SplitEntityOnNode(node->Children[0]);

        if ((sides & 2) != 0)
            SplitEntityOnNode(node->Children[1]);
    }

    // R_SplitEntityOnNode2
    public static void SplitEntityOnNode2(MNode_t* node)
    {
        if (node->VisFrame != RMain.r_visframecount)
            return;

        if (node->Contents < 0)
        {
            if (node->Contents != (int)Contents.Solid)
                RMain.r_pefragtopnode = node; // we've reached a non-solid leaf, so it's
                                                //  visible and not BSP clipped
            return;
        }

        MPlane_t* splitplane = node->Plane;
        int sides = MathLib.BoxOnPlaneSideFast(RMain.r_emins, RMain.r_emaxs, splitplane);

        if (sides == 3)
        {
            // remember first splitter
            RMain.r_pefragtopnode = node;
            return;
        }

        // not split yet; recurse down the contacted side
        if ((sides & 1) != 0)
            SplitEntityOnNode2(node->Children[0]);
        else
            SplitEntityOnNode2(node->Children[1]);
    }

    // R_AddEfrags
    public static void AddEfrags(Entity_t* ent)
    {
        if (ent->Model == null)
            return;

        if (ent == ClEntities.cl_entities)
            return; // never add the world

        r_addent = ent;

        lastlink = &ent->Efrag;
        RMain.r_pefragtopnode = null;

        Model_t* entmodel = ent->Model;

        RMain.r_emins = ent->Origin + entmodel->Mins;
        RMain.r_emaxs = ent->Origin + entmodel->Maxs;

        SplitEntityOnNode(Cl.worldmodel->Nodes);

        ent->TopNode = RMain.r_pefragtopnode;
    }

    // R_StoreEfrags
    // FIXME (original comment): a lot of this goes away with edge-based
    public static void StoreEfrags(EFrag_t** ppefrag)
    {
        EFrag_t* pefrag;

        while ((pefrag = *ppefrag) != null)
        {
            Entity_t* pent = pefrag->Entity;
            Model_t* clmodel = pent->Model;

            switch (clmodel->Type)
            {
                case ModType_t.Alias:
                case ModType_t.Brush:
                case ModType_t.Sprite:
                    if (pent->VisFrame != DIface.r_framecount && ClEntities.cl_numvisedicts < ClEntities.MAX_VISEDICTS)
                    {
                        ClEntities.cl_visedicts[ClEntities.cl_numvisedicts++] = pent;

                        // mark that we've recorded this entity for this frame
                        pent->VisFrame = DIface.r_framecount;
                    }

                    ppefrag = &pefrag->LeafNext;
                    break;

                default:
                    Sys.Error($"R_StoreEfrags: Bad entity type {(int)clmodel->Type}\n");
                    break;
            }
        }
    }

}
