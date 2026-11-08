// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: bspfile.h LUMP_* (BspFile.cs).
// Each member's original constant name is given in its trailing comment.
public enum BspLump
{
    Entities = 0, // LUMP_ENTITIES
    Planes = 1, // LUMP_PLANES
    Textures = 2, // LUMP_TEXTURES
    Vertexes = 3, // LUMP_VERTEXES
    Visibility = 4, // LUMP_VISIBILITY
    Nodes = 5, // LUMP_NODES
    TexInfo = 6, // LUMP_TEXINFO
    Faces = 7, // LUMP_FACES
    Lighting = 8, // LUMP_LIGHTING
    ClipNodes = 9, // LUMP_CLIPNODES
    Leafs = 10, // LUMP_LEAFS
    MarkSurfaces = 11, // LUMP_MARKSURFACES
    Edges = 12, // LUMP_EDGES
    SurfEdges = 13, // LUMP_SURFEDGES
    Models = 14, // LUMP_MODELS
}
