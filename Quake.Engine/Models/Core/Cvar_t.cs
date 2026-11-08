// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.InteropServices;

namespace Quake.Engine;

// cvar_s / cvar_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Cvar_t
{
    public Utf8String Name;
    public Utf8String String;
    public bool Archive; // set to true to cause it to be saved to vars.rc
    public bool Server;  // notifies players when changed
    public float Value;
    public Cvar_t* Next;
}
