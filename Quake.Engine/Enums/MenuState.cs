// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

// Originally numeric constants: menu.c anonymous enum m_* (Menu.cs); original enum name unnamed.
// Each member's original constant name is given in its trailing comment.
public enum MenuState
{
    None = 0, // m_none
    Main = 1, // m_main
    SinglePlayer = 2, // m_singleplayer
    Load = 3, // m_load
    Save = 4, // m_save
    MultiPlayer = 5, // m_multiplayer
    Setup = 6, // m_setup
    Net = 7, // m_net
    Options = 8, // m_options
    Video = 9, // m_video
    Keys = 10, // m_keys
    Help = 11, // m_help
    Quit = 12, // m_quit
    SerialConfig = 13, // m_serialconfig
    ModemConfig = 14, // m_modemconfig
    LanConfig = 15, // m_lanconfig
    GameOptions = 16, // m_gameoptions
    Search = 17, // m_search
    SList = 18, // m_slist
}
