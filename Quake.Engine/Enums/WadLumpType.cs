// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: wad.h TYP_* (Wad.cs; Lumpy and Palette share value 64 as in the original).
// Each member's original constant name is given in its trailing comment.
public enum WadLumpType
{
    None = 0, // TYP_NONE
    Label = 1, // TYP_LABEL
    Lumpy = 64, // TYP_LUMPY
    Palette = 64, // TYP_PALETTE
    QTex = 65, // TYP_QTEX
    QPic = 66, // TYP_QPIC
    Sound = 67, // TYP_SOUND
    MipTex = 68, // TYP_MIPTEX
}
