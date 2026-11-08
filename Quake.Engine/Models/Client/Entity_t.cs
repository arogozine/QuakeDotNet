// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// entity_t (refresh.h/client.h)
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Entity_t
{
    public bool ForceLink;      // model changed
    public int UpdateType;

    public EntityState_t Baseline; // to fill in defaults in updates -- cl_parse.c's ClParse.ParseBaseline is the writer

    public double MsgTime;      // time of last update
    public Vec3Array2 MsgOrigins; // last two updates (0 is newest)
    public Vec3_t Origin;
    public Vec3Array2 MsgAngles;  // last two updates (0 is newest)
    public Vec3_t Angles;
    public Model_t* Model;      // null = no model
    public EFrag_t* Efrag;      // linked list of efrags
    public int Frame;
    public float SyncBase;      // for client-side animations
    public byte* ColorMap;
    public int Effects;         // light, particals, etc
    public int SkinNum;         // for Alias models
    public byte Alpha;          // johnfitz -- alpha (ENTALPHA_*), PROTOCOL_FITZQUAKE
    public float LerpFinish;    // johnfitz -- server-driven lerp end time (U_LERPFINISH)
    public int VisFrame;        // last frame this entity was found in an active leaf

    // Interpolation state (render.h entity_t, johnfitz -- lerping; Mark_V r_common.c)
    public byte LerpFlags;      // LERP_* below
    public float LerpStart;     // animation lerp start time
    public float LerpTime;      // animation lerp interval
    public int PreviousPose;    // not Fitz's pose index: byte offset of the pose's verts from the aliashdr_t
    public int CurrentPose;     //   (0 = none yet); only meaningful for LerpModel
    public Model_t* LerpModel;  // model PreviousPose/CurrentPose belong to (offsets are meaningless on another model)
    public float MoveLerpStart; // transform lerp start time
    public Vec3_t PreviousOrigin;
    public Vec3_t CurrentOrigin;
    public Vec3_t PreviousAngles;
    public Vec3_t CurrentAngles;

    public int DLightFrame;     // dynamic lighting
    public int DLightBits;

    // FIXME: could turn these into a union
    public int TrivialAccept;
    public MNode_t* TopNode;    // for bmodels, first world node that splits
                                 // bmodel, or null if not split
}
