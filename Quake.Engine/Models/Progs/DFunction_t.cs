// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

// dfunction_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DFunction_t
{
    public int First_Statement; // negative numbers are builtins
    public int Parm_Start;
    public int Locals; // total ints of parms + locals

    public int Profile; // runtime

    public int S_Name;
    public int S_File; // source file defined in

    public int NumParms;
    public fixed byte Parm_Size[8]; // MAX_PARMS
}
