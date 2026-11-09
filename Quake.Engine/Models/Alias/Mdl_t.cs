// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// mdl_t
[StructLayout(LayoutKind.Sequential)]
public struct Mdl_t
{
    public int Ident;
    public int Version;
    public Vec3_t Scale;
    public Vec3_t ScaleOrigin;
    public float BoundingRadius;
    public Vec3_t EyePosition;
    public int NumSkins;
    public int SkinWidth;
    public int SkinHeight;
    public int NumVerts;
    public int NumTris;
    public int NumFrames;
    public SyncType_t SyncType;
    public int Flags;
    public float Size;
}
