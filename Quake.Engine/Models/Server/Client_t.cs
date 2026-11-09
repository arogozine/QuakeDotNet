// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

// client_t (server.h) -- wishdir (write-only, never read even in the
// original) is left out as genuinely unused by any ported code; everything
// else SV_SendClientMessages/SV_DropClient/SV_ConnectClient/SV_ReadClientMessage/
// SV_ReadClientMove need is here. privileged is always false (IDGODS-only in
// the original, dead code even there) -- kept as a real field since
// SV_ReadClientMessage's ret==2 branch reads it, even though that branch is
// unreachable in this port.
public unsafe struct Client_t
{
    public const int NUM_PING_TIMES = 16; // server.h

    public bool Active;
    public bool Spawned;    // false = don't send datagrams
    public bool DropAsap;   // has been told to go to another level
    public bool SendSignon; // only valid before spawned
    public bool Privileged; // can execute any host command

    public double LastMessage; // reliable messages must be sent periodically

    public QSocket_t* NetConnection;

    public UserCmd_t Cmd;

    public SizeBuf_t Message; // can be added to at any time, copied and cleared once per frame
    public fixed byte MsgBuf[MAX_MSGLEN];
    public EDict_t* Edict;    // EDICT_NUM(clientnum+1)
    public fixed byte Name[32]; // for printing to other people
    public int Colors;

    public fixed float PingTimes[NUM_PING_TIMES];
    public int NumPings; // ping_times[num_pings % NUM_PING_TIMES]

    public fixed float spawn_parms[Svs.NUM_SPAWN_PARMS]; // carried from level to level

    // client known data for deltas
    public int OldFrags;
}
