// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// host_parms (quakedef.h) -- the quakeparms_t that WinMain (sys_win.c) fills before
/// Host_Init. The port has no WinMain, so Host.host_parms keeps defaults (basedir ".",
/// no cachedir) that COM_InitFilesystem reads; -basedir/-cachedir still override them.
/// </summary>
public static partial class Host
{
    // host_parms
    public static HostParms_t host_parms = new()
    {
        BaseDir = new Utf8String("."u8),
        CacheDir = Utf8String.Empty,
    };

    // realtime, host_frametime -- host.c's frame-timing globals, updated
    // once per frame by Host.Frame (Host_Frame, now ported -- see Host.cs).
    // Declared here (host.c's own file) as plain zero-initialized data.
    public static double realtime;
    public static float host_frametime;

    // oldrealtime -- host.c's own global, Host_FilterTime's previous-frame
    // snapshot of realtime (host_frametime = realtime - oldrealtime).
    public static double oldrealtime;

    // host_framecount -- host.c's own global, incremented once per frame at
    // the end of _Host_Frame.
    public static int host_framecount;

    // host_time -- host.c's own global (server.h's `extern double host_time`),
    // set to 1.0 in Host_InitLocal (so a think at time 0 won't get called)
    // and advanced once per frame by Host.Frame (Host_Frame, now ported).
    // menu.c's blinking-cursor/animated-icon code was the earliest ported
    // consumer.
    public static double host_time = 1.0;
}
