// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

global using Vec3_t = System.Numerics.Vector3;
// auxvert_t
global using AuxVert_t = System.Numerics.Vector3;
// dvertex_t (bspfile.h) -- three little-endian floats, identical layout to Vector3
global using DVertex_t = System.Numerics.Vector3;
// mvertex_t (model.h) -- a single Vector3 position
// !!! if this is changed, it must be changed in asm_draw.h too !!!
global using MVertex_t = System.Numerics.Vector3;