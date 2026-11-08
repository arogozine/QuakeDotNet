// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: bspfile.h CONTENTS_* (BspFile.cs).
// Each member's original constant name is given in its trailing comment.
public enum Contents
{
    Empty = -1, // CONTENTS_EMPTY
    Solid = -2, // CONTENTS_SOLID
    Water = -3, // CONTENTS_WATER
    Slime = -4, // CONTENTS_SLIME
    Lava = -5, // CONTENTS_LAVA
    Sky = -6, // CONTENTS_SKY
    Origin = -7, // CONTENTS_ORIGIN
    Clip = -8, // CONTENTS_CLIP
    Current0 = -9, // CONTENTS_CURRENT_0
    Current90 = -10, // CONTENTS_CURRENT_90
    Current180 = -11, // CONTENTS_CURRENT_180
    Current270 = -12, // CONTENTS_CURRENT_270
    CurrentUp = -13, // CONTENTS_CURRENT_UP
    CurrentDown = -14, // CONTENTS_CURRENT_DOWN
}
