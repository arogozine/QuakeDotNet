// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// world.c -- world query functions: hull-box construction, the area-node
/// BSP used to link/unlink edicts for fast overlap queries, point/line
/// hull tracing, and SV_Move/SV_ClipToLinks. Faithfully ported; the one
/// PR_ExecuteProgram call (SV_TouchLinks' trigger touch dispatch) goes
/// through Progs.ExecuteProgram, whose builtin table (Progs.pr_builtins) is
/// now fully populated (PrCmds.cs's 79 PF_* builtins), so a QuakeC touch
/// function runs its real engine builtin here exactly like the original.
/// </summary>
public static unsafe class World
{
    // server.h



    // world.h

    /*
    ===============================================================================
    HULL BOXES
    ===============================================================================
    */

    // box_hull and its ClipNodes/Planes are returned/dereferenced as raw pointers
    // across the whole RecursiveHullCheck call chain, so all of it needs a
    // permanently stable address (see CLAUDE.md rule 9) — a managed field/array
    // pinned only inside a single `fixed` block is relocatable by the GC
    // afterward, corrupting the stored pointers.
    internal static readonly Hull_t* box_hull_ptr =
        NativeMemory.AllocZeroed<Hull_t>();
    private static ref Hull_t box_hull => ref *box_hull_ptr;
    internal static readonly DClipNode_t* box_clipnodes =
        NativeMemory.AllocArrayZeroed<DClipNode_t>(6);
    internal static readonly MPlane_t* box_planes =
        NativeMemory.AllocArrayZeroed<MPlane_t>(6);

    // SV_InitBoxHull
    public static void InitBoxHull()
    {
        box_hull.ClipNodes = box_clipnodes;
        box_hull.Planes = box_planes;
        box_hull.FirstClipNode = 0;
        box_hull.LastClipNode = 5;

        for (int i = 0; i < 6; i++)
        {
            box_clipnodes[i].PlaneNum = i;

            int side = i & 1;

            box_clipnodes[i].Children[side] = (int)Contents.Empty;
            box_clipnodes[i].Children[side ^ 1] = (short)(i != 5 ? i + 1 : (int)Contents.Solid);

            box_planes[i].Type = (byte)(i >> 1);
            box_planes[i].Normal[i >> 1] = 1;
        }
    }

    // SV_HullForBox
    public static Hull_t* HullForBox(in Vec3_t mins, in Vec3_t maxs)
    {
        box_planes[0].Dist = maxs[0];
        box_planes[1].Dist = mins[0];
        box_planes[2].Dist = maxs[1];
        box_planes[3].Dist = mins[1];
        box_planes[4].Dist = maxs[2];
        box_planes[5].Dist = mins[2];

        return box_hull_ptr;
    }

    // SV_HullForEntity
    public static Hull_t* HullForEntity(EDict_t* ent, in Vec3_t mins, in Vec3_t maxs, Vec3_t* offset)
    {
        Hull_t* hull;

        // decide which clipping hull to use, based on the size
        if (ent->V.Solid == (int)SolidType.Bsp)
        {
            if (ent->V.MoveType != (int)MoveType.Push)
                Sys.Error("(int)SolidType.Bsp without (int)MoveType.Push"u8);

            Model_t* model = Sv.models[(int)ent->V.ModelIndex];

            if (model == null || model->Type != ModType_t.Brush)
                Sys.Error("(int)MoveType.Push with a non bsp model"u8);

            Vec3_t size = maxs - mins;
            hull = size[0] < 3 ? &model->Hulls[0] : size[0] <= 32 ? &model->Hulls[1] : &model->Hulls[2];

            // calculate an offset value to center the origin
            *offset = hull->ClipMins - mins;
            *offset += ent->V.Origin;
        }
        else
        {
            // create a temp hull from bounding box sizes
            Vec3_t hullmins = ent->V.Mins - maxs, hullmaxs = ent->V.Maxs - mins;
            hull = HullForBox(hullmins, hullmaxs);

            *offset = ent->V.Origin;
        }

        return hull;
    }

    /*
    ===============================================================================
    ENTITY AREA CHECKING
    ===============================================================================
    */

    private const int AREA_DEPTH = 4;
    internal const int AREA_NODES = 32;

    // sv_areanodes is walked via raw Link_t* pointers embedded in its elements
    // (see AreaNode_t.SolidEdicts/TriggerEdicts), which must stay valid across
    // calls; a managed array only has a stable address inside a `fixed` block,
    // so it is backed by unmanaged memory instead, like the rest of this codebase.
    internal static readonly AreaNode_t* sv_areanodes =
        NativeMemory.AllocArrayZeroed<AreaNode_t>(AREA_NODES);
    private static int sv_numareanodes;

    // SV_CreateAreaNode
    public static AreaNode_t* CreateAreaNode(int depth, Vec3_t mins, Vec3_t maxs)
    {
        AreaNode_t* anode = &sv_areanodes[sv_numareanodes];
        sv_numareanodes++;

        Link.ClearLink((Link_t*)&anode->TriggerEdicts);
        Link.ClearLink((Link_t*)&anode->SolidEdicts);

        if (depth == AREA_DEPTH)
        {
            anode->Axis = -1;
            anode->Children0 = anode->Children1 = null;
            return anode;
        }

        Vec3_t size = maxs - mins;
        anode->Axis = size[0] > size[1] ? 0 : 1;

        anode->Dist = 0.5f * (maxs[anode->Axis] + mins[anode->Axis]);
        Vec3_t mins1 = mins, mins2 = mins, maxs1 = maxs, maxs2 = maxs;

        maxs1[anode->Axis] = mins2[anode->Axis] = anode->Dist;

        anode->Children0 = CreateAreaNode(depth + 1, mins2, maxs2);
        anode->Children1 = CreateAreaNode(depth + 1, mins1, maxs1);

        return anode;
    }

    // SV_ClearWorld
    public static void ClearWorld()
    {
        InitBoxHull();

        NativeMemory.Clear(sv_areanodes, AREA_NODES);
        sv_numareanodes = 0;
        CreateAreaNode(0, Sv.worldmodel->Mins, Sv.worldmodel->Maxs);
    }

    // SV_UnlinkEdict
    public static void UnlinkEdict(EDict_t* ent)
    {
        if (ent->Area.Prev == null)
            return; // not linked in anywhere
        Link.RemoveLink((Link_t*)&ent->Area);
        ent->Area.Prev = ent->Area.Next = null;
    }

    // SV_TouchLinks
    public static void TouchLinks(EDict_t* ent, AreaNode_t* node)
    {
        for (Link_t* l = node->TriggerEdicts.Next; l != (Link_t*)&node->TriggerEdicts;)
        {
            // johnfitz -- fixes a crash when a touch function deletes an entity which comes later in the list
            if (l == null)
            {
                Con.Printf("SV_TouchLinks: null link\n"u8);
                break;
            }
            // johnfitz

            Link_t* next = l->Next;
            EDict_t* touch = EdictFromArea(l);
            if (touch != ent && touch->V.Touch != 0 && touch->V.Solid == (int)SolidType.Trigger
                && !(ent->V.AbsMin[0] > touch->V.AbsMax[0]
                     || ent->V.AbsMin[1] > touch->V.AbsMax[1]
                     || ent->V.AbsMin[2] > touch->V.AbsMax[2]
                     || ent->V.AbsMax[0] < touch->V.AbsMin[0]
                     || ent->V.AbsMax[1] < touch->V.AbsMin[1]
                     || ent->V.AbsMax[2] < touch->V.AbsMin[2]))
            {
                int old_self = Progs.pr_global_struct->Self;
                int old_other = Progs.pr_global_struct->Other;

                Progs.pr_global_struct->Self = Progs.EdictToProg(touch);
                Progs.pr_global_struct->Other = Progs.EdictToProg(ent);
                Progs.pr_global_struct->Time = (float)Sv.time;
                Progs.ExecuteProgram(touch->V.Touch);

                // johnfitz -- the PR_ExecuteProgram above can alter the linked edicts -- fix from tyrquake
                if (next != l->Next && l->Next != null)
                {
                    Con.Printf("SV_TouchLinks: next != l->next\n"u8);
                    next = l->Next;
                }
                // johnfitz

                Progs.pr_global_struct->Self = old_self;
                Progs.pr_global_struct->Other = old_other;
            }
            l = next;
        }

        // recurse down both sides
        if (node->Axis == -1)
            return;

        if (ent->V.AbsMax[node->Axis] > node->Dist)
            TouchLinks(ent, node->Children0);
        if (ent->V.AbsMin[node->Axis] < node->Dist)
            TouchLinks(ent, node->Children1);
    }

    // EDICT_FROM_AREA(l) == STRUCT_FROM_LINK(l, edict_t, area): l points at
    // the `Area` field inside an EDict_t; this returns the offset of that
    // field so the pointer can be walked back to the containing struct.
    private static readonly int _areaOffset = ComputeAreaOffset();
    private static int AreaOffset() => _areaOffset;
    private static int ComputeAreaOffset()
    {
        EDict_t dummy = default;
        return (int)((byte*)&dummy.Area - (byte*)&dummy);
    }

    private static EDict_t* EdictFromArea(Link_t* l) => (EDict_t*)((byte*)l - AreaOffset());

    // SV_FindTouchedLeafs
    public static void FindTouchedLeafs(EDict_t* ent, MNode_t* node)
    {
        if (node->Contents == (int)Contents.Solid)
            return;

        if (node->Contents < 0)
        {
            if (ent->NumLeafs == EDict_t.MAX_ENT_LEAFS)
                return;

            MLeaf_t* leaf = (MLeaf_t*)node;
            int leafnum = (int)(leaf - Sv.worldmodel->Leafs) - 1;

            ent->LeafNums[ent->NumLeafs] = (short)leafnum;
            ent->NumLeafs++;
            return;
        }

        // NODE_MIXED
        MPlane_t* splitplane = node->Plane;
        int sides = MathLib.BoxOnPlaneSideFast(ent->V.AbsMin, ent->V.AbsMax, splitplane);

        if ((sides & 1) != 0)
            FindTouchedLeafs(ent, node->Children[0]);
        if ((sides & 2) != 0)
            FindTouchedLeafs(ent, node->Children[1]);
    }

    // SV_LinkEdict
    public static void LinkEdict(EDict_t* ent, bool touch_triggers)
    {
        if (ent->Area.Prev != null)
            UnlinkEdict(ent); // unlink from old position

        {
            AreaNode_t* rootNode = sv_areanodes;

            if (ent == Sv.edicts)
                return; // don't add the world

            if (ent->Free != 0)
                return;

            // set the abs box
            ent->V.AbsMin = ent->V.Origin + ent->V.Mins;
            ent->V.AbsMax = ent->V.Origin + ent->V.Maxs;

            // to make items easier to pick up and allow them to be grabbed off
            // of shelves, the abs sizes are expanded
            if (((int)ent->V.Flags & SvPhys.FL_ITEM) != 0)
            {
                ent->V.AbsMin[0] -= 15;
                ent->V.AbsMin[1] -= 15;
                ent->V.AbsMax[0] += 15;
                ent->V.AbsMax[1] += 15;
            }
            else
            {
                // because movement is clipped an epsilon away from an actual edge,
                // we must fully check even when bounding boxes don't quite touch
                ent->V.AbsMin[0] -= 1;
                ent->V.AbsMin[1] -= 1;
                ent->V.AbsMin[2] -= 1;
                ent->V.AbsMax[0] += 1;
                ent->V.AbsMax[1] += 1;
                ent->V.AbsMax[2] += 1;
            }

            // link to PVS leafs
            ent->NumLeafs = 0;
            if (ent->V.ModelIndex != 0)
                FindTouchedLeafs(ent, Sv.worldmodel->Nodes);

            if (ent->V.Solid == (int)SolidType.Not)
                return;

            // find the first node that the ent's box crosses
            AreaNode_t* node = rootNode;
            while (true)
            {
                if (node->Axis == -1)
                    break;
                if (ent->V.AbsMin[node->Axis] > node->Dist)
                    node = node->Children0;
                else if (ent->V.AbsMax[node->Axis] < node->Dist)
                    node = node->Children1;
                else
                    break; // crosses the node
            }

            // link it in
            if (ent->V.Solid == (int)SolidType.Trigger)
                Link.InsertLinkBefore((Link_t*)&ent->Area, (Link_t*)&node->TriggerEdicts);
            else
                Link.InsertLinkBefore((Link_t*)&ent->Area, (Link_t*)&node->SolidEdicts);

            // if touch_triggers, touch all entities at this node and decend for more
            if (touch_triggers)
                TouchLinks(ent, rootNode);
        }
    }

    /*
    ===============================================================================
    POINT TESTING IN HULLS
    ===============================================================================
    */

    // SV_HullPointContents
    public static int HullPointContents(Hull_t* hull, int num, in Vec3_t p)
    {
        while (num >= 0)
        {
            if (num < hull->FirstClipNode || num > hull->LastClipNode)
                Sys.Error("SV_HullPointContents: bad node number"u8);

            DClipNode_t* node = hull->ClipNodes + num;
            MPlane_t* plane = hull->Planes + node->PlaneNum;

            float d = plane->Type < 3 ? p[plane->Type] - plane->Dist : Vector3.Dot(plane->Normal, p) - plane->Dist;
            num = node->Children[d < 0 ? 1 : 0];
        }

        return num;
    }

    // SV_PointContents
    public static int PointContents(in Vec3_t p)
    {
        int cont = HullPointContents(&Sv.worldmodel->Hulls[0], 0, p);
        if (cont <= (int)Contents.Current0 && cont >= (int)Contents.CurrentDown)
            cont = (int)Contents.Water;
        return cont;
    }

    public static int TruePointContents(in Vec3_t p) => HullPointContents(&Sv.worldmodel->Hulls[0], 0, p);

    //===========================================================================

    // SV_TestEntityPosition
    public static EDict_t* TestEntityPosition(EDict_t* ent)
    {
        Trace_t trace = Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, ent->V.Origin, (int)ClipMoveType.Normal, ent);

        if (trace.StartSolid)
            return Sv.edicts;

        return null;
    }

    /*
    ===============================================================================
    LINE TESTING IN HULLS
    ===============================================================================
    */

    private const float DIST_EPSILON = 0.03125f; // 1/32 epsilon to keep floating point happy

    // SV_RecursiveHullCheck
    public static bool RecursiveHullCheck(Hull_t* hull, int num, float p1f, float p2f, in Vec3_t p1, in Vec3_t p2, ref Trace_t trace)
    {
        // check for empty
        if (num < 0)
        {
            if (num != (int)Contents.Solid)
            {
                trace.AllSolid = false;
                if (num == (int)Contents.Empty)
                    trace.InOpen = true;
                else
                    trace.InWater = true;
            }
            else
                trace.StartSolid = true;
            return true; // empty
        }

        if (num < hull->FirstClipNode || num > hull->LastClipNode)
            Sys.Error("SV_RecursiveHullCheck: bad node number"u8);

        // find the point distances
        DClipNode_t* node = hull->ClipNodes + num;
        MPlane_t* plane = hull->Planes + node->PlaneNum;

        float t1, t2;
        if (plane->Type < 3)
        {
            t1 = p1[plane->Type] - plane->Dist;
            t2 = p2[plane->Type] - plane->Dist;
        }
        else
        {
            Vec3_t normal = plane->Normal;
            t1 = Vector3.Dot(normal, p1) - plane->Dist;
            t2 = Vector3.Dot(normal, p2) - plane->Dist;
        }

        if (t1 >= 0 && t2 >= 0)
            return RecursiveHullCheck(hull, node->Children[0], p1f, p2f, p1, p2, ref trace);
        if (t1 < 0 && t2 < 0)
            return RecursiveHullCheck(hull, node->Children[1], p1f, p2f, p1, p2, ref trace);

        // put the crosspoint DIST_EPSILON pixels on the near side
        float frac = t1 < 0 ? (t1 + DIST_EPSILON) / (t1 - t2) : (t1 - DIST_EPSILON) / (t1 - t2);
        if (frac < 0)
            frac = 0;
        if (frac > 1)
            frac = 1;

        float midf = p1f + (p2f - p1f) * frac;
        Vec3_t mid = p1 + frac * (p2 - p1);

        int side = t1 < 0 ? 1 : 0;

        // move up to the node
        if (!RecursiveHullCheck(hull, node->Children[side], p1f, midf, p1, mid, ref trace))
            return false;

        if (HullPointContents(hull, node->Children[side ^ 1], mid) != (int)Contents.Solid)
            // go past the node
            return RecursiveHullCheck(hull, node->Children[side ^ 1], midf, p2f, mid, p2, ref trace);

        if (trace.AllSolid)
            return false; // never got out of the solid area

        //==================
        // the other side of the node is solid, this is the impact point
        //==================
        if (side == 0)
        {
            trace.Plane.Normal = plane->Normal;
            trace.Plane.Dist = plane->Dist;
        }
        else
        {
            // VectorSubtract (vec3_origin, ...): 0 - n, not -n, so a zero
            // component stays +0 as in the original
            trace.Plane.Normal = Vec3_t.Zero - plane->Normal;
            trace.Plane.Dist = -plane->Dist;
        }

        while (HullPointContents(hull, hull->FirstClipNode, mid) == (int)Contents.Solid)
        { // shouldn't really happen, but does occasionally
            frac -= 0.1f;
            if (frac < 0)
            {
                trace.Fraction = midf;
                trace.EndPos = mid;
                Con.DPrintf("backup past 0\n"u8);
                return false;
            }
            midf = p1f + (p2f - p1f) * frac;
            mid = p1 + frac * (p2 - p1);
        }

        trace.Fraction = midf;
        trace.EndPos = mid;

        return false;
    }

    // SV_ClipMoveToEntity
    public static Trace_t ClipMoveToEntity(EDict_t* ent, in Vec3_t start, in Vec3_t mins, in Vec3_t maxs, in Vec3_t end)
    {
        Trace_t trace = default;
        trace.Fraction = 1;
        trace.AllSolid = true;
        trace.EndPos = end;

        Vec3_t offset = default;
        Hull_t* hull = HullForEntity(ent, mins, maxs, &offset);

        Vec3_t start_l = start - offset, end_l = end - offset;

        RecursiveHullCheck(hull, hull->FirstClipNode, 0, 1, start_l, end_l, ref trace);

        // fix trace up by the offset
        if (trace.Fraction != 1)
            trace.EndPos = trace.EndPos + offset;

        // did we clip the move?
        if (trace.Fraction < 1 || trace.StartSolid)
            trace.Ent = ent;

        return trace;
    }

    //===========================================================================

    // SV_ClipToLinks
    public static void ClipToLinks(AreaNode_t* node, ref MoveClip_t clip)
    {
        for (Link_t* l = node->SolidEdicts.Next; l != (Link_t*)&node->SolidEdicts;)
        {
            Link_t* next = l->Next;
            EDict_t* touch = EdictFromArea(l);
            l = next;

            if (touch->V.Solid == (int)SolidType.Not)
                continue;
            if (touch == clip.PassEdict)
                continue;
            if (touch->V.Solid == (int)SolidType.Trigger)
                Sys.Error("Trigger in clipping list"u8);

            if (clip.Type == (int)ClipMoveType.NoMonsters && touch->V.Solid != (int)SolidType.Bsp)
                continue;

            if (clip.BoxMins[0] > touch->V.AbsMax[0]
                || clip.BoxMins[1] > touch->V.AbsMax[1]
                || clip.BoxMins[2] > touch->V.AbsMax[2]
                || clip.BoxMaxs[0] < touch->V.AbsMin[0]
                || clip.BoxMaxs[1] < touch->V.AbsMin[1]
                || clip.BoxMaxs[2] < touch->V.AbsMin[2])
                continue;

            if (clip.PassEdict != null && clip.PassEdict->V.Size[0] != 0 && touch->V.Size[0] == 0)
                continue; // points never interact

            // might intersect, so do an exact clip
            if (clip.Trace.AllSolid)
                return;
            if (clip.PassEdict != null)
            {
                if (Progs.ProgToEdict(touch->V.Owner) == clip.PassEdict)
                    continue; // don't clip against own missiles
                if (Progs.ProgToEdict(clip.PassEdict->V.Owner) == touch)
                    continue; // don't clip against owner
            }

            Trace_t trace = ((int)touch->V.Flags & SvPhys.FL_MONSTER) != 0
                ? ClipMoveToEntity(touch, clip.Start, clip.Mins2, clip.Maxs2, clip.End)
                : ClipMoveToEntity(touch, clip.Start, clip.Mins, clip.Maxs, clip.End);

            if (trace.AllSolid || trace.StartSolid || trace.Fraction < clip.Trace.Fraction)
            {
                trace.Ent = touch;
                if (clip.Trace.StartSolid)
                {
                    clip.Trace = trace;
                    clip.Trace.StartSolid = true;
                }
                else
                    clip.Trace = trace;
            }
            else if (trace.StartSolid)
                clip.Trace.StartSolid = true;
        }

        // recurse down both sides
        if (node->Axis == -1)
            return;

        if (clip.BoxMaxs[node->Axis] > node->Dist)
            ClipToLinks(node->Children0, ref clip);
        if (clip.BoxMins[node->Axis] < node->Dist)
            ClipToLinks(node->Children1, ref clip);
    }

    // SV_MoveBounds
    public static void MoveBounds(in Vec3_t start, in Vec3_t mins, in Vec3_t maxs, in Vec3_t end, ref Vec3_t boxmins, ref Vec3_t boxmaxs)
    {
        // per lane, end[i] > start[i] selects (start, end) else (end, start):
        // the original's branch, done branch-free for all three axes
        Vector3 endGreater = Vector3.GreaterThan(end, start);
        Vector3 lo = Vector3.ConditionalSelect(endGreater, start, end);
        Vector3 hi = Vector3.ConditionalSelect(endGreater, end, start);

        boxmins = lo + mins - Vector3.One;
        boxmaxs = hi + maxs + Vector3.One;
    }

    // SV_DropToFloor (pr_cmds.c's PF_droptofloor) -- traces 256 units down
    // from ent's current origin and snaps to the floor if one is found.
    // Returns true (and relinks ent) if the entity is now on solid ground.
    public static bool DropToFloor(EDict_t* ent)
    {
        Vec3_t end = ent->V.Origin;
        end[2] -= 256;

        Trace_t trace = Move(ent->V.Origin, ent->V.Mins, ent->V.Maxs, end, (int)ClipMoveType.Normal, ent);

        if (trace.Fraction == 1 || trace.AllSolid)
            return false;

        ent->V.Origin = trace.EndPos;
        LinkEdict(ent, false);
        ent->V.Flags = (int)ent->V.Flags | SvPhys.FL_ONGROUND;
        ent->V.GroundEntity = Progs.EdictToProg(trace.Ent);
        return true;
    }

    // SV_Move
    public static Trace_t Move(in Vec3_t start, in Vec3_t mins, in Vec3_t maxs, in Vec3_t end, int type, EDict_t* passedict)
    {
        MoveClip_t clip = default;

        // clip to world
        clip.Trace = ClipMoveToEntity(Sv.edicts, start, mins, maxs, end);

        clip.Start = start;
        clip.End = end;
        clip.Mins = mins;
        clip.Maxs = maxs;
        clip.Type = type;
        clip.PassEdict = passedict;

        if (type == (int)ClipMoveType.Missile)
        {
            for (int i = 0; i < 3; i++)
            {
                clip.Mins2[i] = -15;
                clip.Maxs2[i] = 15;
            }
        }
        else
        {
            clip.Mins2 = mins;
            clip.Maxs2 = maxs;
        }

        // create the bounding box of the entire move
        MoveBounds(start, clip.Mins2, clip.Maxs2, end, ref clip.BoxMins, ref clip.BoxMaxs);

        // clip to entities
        ClipToLinks(sv_areanodes, ref clip);

        return clip.Trace;
    }
}

