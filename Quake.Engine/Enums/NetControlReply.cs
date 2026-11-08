// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: net_dgrm.c CCREP_* (NetDgrm.cs).
// Each member's original constant name is given in its trailing comment.
public enum NetControlReply
{
    Accept = 129, // CCREP_ACCEPT
    Reject = 130, // CCREP_REJECT
    ServerInfo = 131, // CCREP_SERVER_INFO
    PlayerInfo = 132, // CCREP_PLAYER_INFO
    RuleInfo = 133, // CCREP_RULE_INFO
}
