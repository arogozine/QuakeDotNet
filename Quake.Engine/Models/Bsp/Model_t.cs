// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// model_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Model_t
{
    public fixed byte Name[QuakeDef.MAX_QPATH];
    public int NeedLoad; // bmodels and sprites don't cache normally (NL_* constants)

    public ModType_t Type;
    public int NumFrames;
    public SyncType_t SyncType;

    public int Flags;

    // volume occupied by the model
    public Vec3_t Mins, Maxs;
    public float Radius;

    // brush model
    public int FirstModelSurface, NumModelSurfaces;

    public int NumSubmodels;
    public DModel_t* Submodels;

    public int NumPlanes;
    public MPlane_t* Planes;

    public int NumLeafs; // number of visible leafs, not counting 0
    public MLeaf_t* Leafs;

    public int NumVertexes;
    public MVertex_t* Vertexes;

    public int NumEdges;
    public MEdge_t* Edges;

    public int NumNodes;
    public MNode_t* Nodes;

    public int NumTexinfo;
    public MTexInfo_t* Texinfo;

    public int NumSurfaces;
    public MSurface_t* Surfaces;

    public int NumSurfedges;
    public int* Surfedges;

    public int NumClipnodes;
    public DClipNode_t* Clipnodes;

    public int NumMarksurfaces;
    public MSurface_t** Marksurfaces;

    public HullArray Hulls;

    public int NumTextures;
    public Texture_t** Textures;

    public byte* VisData;
    public byte* LightData;
    public byte* Entities;

    // additional model data
    public CacheUser Cache; // only access through Mod_Extradata
}
