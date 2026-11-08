// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: client.h cactive_t (Cl.cs Cls.CA_*; original enumerators ca_dedicated/ca_disconnected/ca_connected).
// Each member's original constant name is given in its trailing comment.
public enum CActive
{
    Dedicated = 0, // CA_DEDICATED
    Disconnected = 1, // CA_DISCONNECTED
    Connected = 2, // CA_CONNECTED
}
