// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

// dprograms_t
[StructLayout(LayoutKind.Sequential)]
public struct DPrograms_t
{
    public int Version;
    public int Crc; // check of header file

    public int Ofs_Statements;
    public int NumStatements; // statement 0 is an error

    public int Ofs_GlobalDefs;
    public int NumGlobalDefs;

    public int Ofs_FieldDefs;
    public int NumFieldDefs;

    public int Ofs_Functions;
    public int NumFunctions; // function 0 is an empty

    public int Ofs_Strings;
    public int NumStrings; // first string is a null string

    public int Ofs_Globals;
    public int NumGlobals;

    public int EntityFields;
}
