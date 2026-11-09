// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// Quake.Engine is one big bag of mutable statics (the original's C globals),
// and tests point them at test-owned memory: stackalloc'd buffers, locals,
// NativeMemory blocks freed in [TestCleanup]. Whatever a test leaves behind
// dangles into the *next* test -- e.g. RShared.currententity or
// Sv.datagram->Data still aiming at a dead stack frame -- and the next engine
// call that writes through it silently corrupts the process (observed as an
// intermittent "Internal CLR error 0x80131506" test-host crash, several
// classes after the one that leaked the pointer).
//
// Chasing every such field per test class doesn't scale, so before the first
// test this snapshots
//   1. every non-readonly static field in Quake.Engine, and
//   2. the contents of every fixed-size native table the engine allocates
//      once into a `static readonly T*` (Model.mod_known, Keys.keybindings,
//      SndDma.channels, every cvar, ...) -- the field itself can't change, but what's in
//      the table carries pointers and state across tests just the same,
// and restores both before every test. Each test therefore starts from
// process-fresh engine state, and its own [TestInitialize] builds on top of it.
//
// EngineStateGuardTests.AllReadonlyNativeTablesAreClassified fails when a new
// `static readonly T*` shows up in the engine without being added to either
// RestoredTables or ScratchTables below.
[TestClass]
public static unsafe class EngineStateGuard
{
    private static List<(FieldInfo Field, object? Value)>? _fieldSnapshot;
    private static List<(nint Ptr, byte[] Bytes)>? _tableSnapshot;

    [GlobalTestInitialize]
    public static void RestoreEngineState(TestContext _)
    {
        if (_fieldSnapshot == null)
        {
            _fieldSnapshot = SnapshotFields();
            _tableSnapshot = RestoredTables().Select(t => (t.Ptr, new Span<byte>((void*)t.Ptr, t.Bytes).ToArray())).ToList();
        }

        foreach ((FieldInfo field, object? initial) in _fieldSnapshot)
        {
            object? current = field.GetValue(null);

            // anything disposable a test opened through the engine (demo/log
            // file streams, ...) would otherwise stay open and could lock
            // files the next test wants.
            if (current is IDisposable disposable && !ReferenceEquals(current, initial))
                disposable.Dispose();

            field.SetValue(null, initial);
        }

        foreach ((nint ptr, byte[] bytes) in _tableSnapshot!)
            bytes.CopyTo(new Span<byte>((void*)ptr, bytes.Length));
    }

    [GlobalTestCleanup]
    public static void ReleaseOsResources(TestContext _)
    {
        // NetIp's socket table and Sys's file-handle table are static
        // readonly managed arrays, so neither is touched by the restore
        // above -- close every real OS socket/file a test left open.
        NetIp.ResetForTests();

        FileStream?[] handles = Sys.sys_handles;
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i]?.Dispose();
            handles[i] = null;
        }
    }

    private static List<(FieldInfo, object?)> SnapshotFields()
    {
        List<(FieldInfo, object?)> snapshot = [];
        foreach (FieldInfo field in EngineStaticFields())
        {
            if (field.IsInitOnly)
                continue;
            snapshot.Add((field, field.GetValue(null)));
        }
        return snapshot;
    }

    internal static IEnumerable<FieldInfo> EngineStaticFields()
    {
        foreach (Type type in typeof(Host).Assembly.GetTypes())
        {
            if (type.ContainsGenericParameters || type.IsDefined(typeof(CompilerGeneratedAttribute)))
                continue;

            // so the snapshot captures post-static-constructor values
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);

            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral || field.IsDefined(typeof(ThreadStaticAttribute)))
                    continue;
                yield return field;
            }
        }
    }

    // ---- readonly native tables ------------------------------------------

    internal readonly record struct Table(string Name, nint Ptr, int Bytes);

    private static Table Tab<T>(T* p, int count, [CallerArgumentExpression(nameof(p))] string name = "") where T : unmanaged
        => new(name, (nint)p, count * sizeof(T));

    private static Table PtrTab<T>(T** p, int count, [CallerArgumentExpression(nameof(p))] string name = "") where T : unmanaged
        => new(name, (nint)p, count * sizeof(nint));

    // Tables whose contents carry state from one engine call to the next.
    internal static List<Table> RestoredTables() =>
    [
        Tab(SndHost.sn, 1),
        Tab(Cache.cache_head, 1),
        Tab(CdAudio.remap, CdAudio.MAX_TRACKS),
        Tab(Cl.stats, QuakeDef.MAX_CL_STATS),
        Tab(Cl.cshifts, Cl.NUM_CSHIFTS),
        Tab(Cl.prev_cshifts, Cl.NUM_CSHIFTS),
        PtrTab(Cl.model_precache, QuakeDef.MAX_MODELS),
        Tab(Cl.item_gettime, 32),
        Tab(Cl.levelname, Cl.LEVELNAME_LENGTH),
        Tab(Cl.scores, QuakeDef.MAX_SCOREBOARD),
        PtrTab(Cl.sound_precache, QuakeDef.MAX_SOUNDS),
        Tab(Cl.mtime, 2),
        Tab(Cl.mvelocity, 2),
        Tab(ClLightStyle.cl_lightstyle, QuakeDef.MAX_LIGHTSTYLES),
        Tab(ClEntities.cl_entities, QuakeDef.MAX_EDICTS),
        Tab(ClEntities.cl_static_entities, ClEntities.MAX_STATIC_ENTITIES),
        Tab(ClEntities.cl_efrags, ClEntities.MAX_EFRAGS),
        Tab(ClEntities.cl_dlights, ClEntities.MAX_DLIGHTS),
        PtrTab(ClEntities.cl_visedicts, ClEntities.MAX_VISEDICTS),
        Tab(Cls.messageBuffer, Cls.MESSAGE_BUFSIZE),
        Tab(Cls.spawnparms, 1),
        Tab(Cls.mapstring, QuakeDef.MAX_QPATH),
        Tab(ClDemo.demos, ClDemo.MAX_DEMOS * ClDemo.MAX_DEMONAME),
        Tab(Cl.mviewangles, 2),
        Tab(ClTEnt.cl_temp_entities, ClTEnt.MAX_TEMP_ENTITIES),
        Tab(ClTEnt.cl_beams, ClTEnt.MAX_BEAMS),
        Tab(Cmd.cmd_text, 1),
        Tab(Com.com_token, 1024),
        Tab(Com.com_cmdline, Com.CMDLINE_LENGTH),
        Tab(Com.com_cachedir, QuakeDef.MAX_OSPATH),
        Tab(Com.com_gamedir, QuakeDef.MAX_OSPATH),
        Tab(Con.con_times, Con.NUM_CON_TIMES),
        Tab(DLocal.d_scantable, RShared.MAXHEIGHT),
        PtrTab(DLocal.zspantable, RShared.MAXHEIGHT),
        Tab(DLocal.d_scalemip, 3),
        Tab(Draw.menu_cachepics, Draw.MAX_CACHED_PICS),
        PtrTab(Keys.keybindings, 256),
        Tab(Keys.consolekeys, 256),
        Tab(Keys.menubound, 256),
        Tab(Keys.keyshift, 256),
        Tab(Keys.key_repeats, 256),
        Tab(Keys.keydown, 256),
        Tab(Keys.key_lines, 32 * Keys.MAXCMDLINE),
        Tab(Keys.chat_buffer, 32),
        Tab(Menu.m_return_reason, Menu.M_RETURN_REASON_LENGTH),
        Tab(Menu.identityTable, 256),
        Tab(Menu.translationTable, 256),
        Tab(Menu.loadable, Menu.MAX_SAVEGAMES),
        Tab(Menu.m_filenames, Menu.MAX_SAVEGAMES * Menu.M_FILENAME_SIZE),
        Tab(Menu.setup_hostname, 16),
        Tab(Menu.setup_myname, 16),
        Tab(Menu.serialConfig_phone, 16),
        Tab(Menu.modemConfig_clear, 16),
        Tab(Menu.modemConfig_init, 32),
        Tab(Menu.modemConfig_hangup, 16),
        Tab(Menu.lanConfig_portname, 6),
        Tab(Menu.lanConfig_joinname, 22),
        Tab(Model.mod_known, Model.MAX_MOD_KNOWN),
        Tab(Model.mod_novis, Model.MOD_NOVIS_SIZE),
        Tab(Net.net_drivers, Net.MAX_NET_DRIVERS),
        Tab(Progs.pr_stack, Progs.MAX_STACK_DEPTH),
        Tab(Progs.localstack, Progs.LOCALSTACK_SIZE),
        Tab(RAlias.aliastransform, 3 * 4),
        Tab(RBsp.entity_rotation, 9),
        Tab(RDraw.world_clipplanes, 16),
        Tab(RMain.screenedge, 4),
        Tab(RMain.view_clipplanes, 4),
        Tab(RMain.r_frustum_indexes, 4 * 6),
        PtrTab(RMain.pfrustum_indexes, 4),
        Tab(RMisc.r_timings, RMisc.MAX_TIMINGS),
        Tab(RPart.avelocities, RPart.NUMVERTEXNORMALS),
        Tab(RShared.sintable, RShared.SIN_BUFFER_SIZE),
        Tab(RShared.intsintable, RShared.SIN_BUFFER_SIZE),
        Tab(RShared.d_lightstylevalue, 256),
        Tab(RShared.edge_head, 1),
        Tab(RShared.edge_tail, 1),
        Tab(RShared.edge_aftertail, 1),
        Tab(REdge.edge_sentinel, 1),
        Tab(RSky.bottomsky, 128 * 131),
        Tab(RSky.bottommask, 128 * 131),
        Tab(RSky.newsky, 128 * 256),
        Tab(Sbar.fragsort, QuakeDef.MAX_SCOREBOARD),
        Tab(Sbar.scoreboardtext, QuakeDef.MAX_SCOREBOARD * Sbar.SCOREBOARDTEXT_COLS),
        Tab(Sbar.scoreboardtop, QuakeDef.MAX_SCOREBOARD),
        Tab(Sbar.scoreboardbottom, QuakeDef.MAX_SCOREBOARD),
        Tab(Screen.scr_centerstring, 1024),
        Tab(SndDma.channels, SndDma.MAX_CHANNELS),
        Tab(SndMix.snd_scaletable, 32 * SndMix.SND_SCALETABLE_COLS),
        PtrTab(Sv.model_precache, QuakeDef.MAX_MODELS),
        PtrTab(Sv.models, QuakeDef.MAX_MODELS),
        PtrTab(Sv.sound_precache, QuakeDef.MAX_SOUNDS),
        PtrTab(Sv.lightstyles, QuakeDef.MAX_LIGHTSTYLES),
        Tab(Sys.consoleInputText, 256),
        Tab(Vid.d_8to16table, 256),
        Tab(Vid.d_8to24table, 256),
        Tab(View.gammatable, 256),
        Tab(World.box_hull_ptr, 1),
        Tab(RShared.r_refdef, 1),
        Tab(Screen.scr_vrect, 1),
        Tab(RMain.r_viewlighting, 1),
        Tab(Cl.viewent, 1),
        Tab(Cls.message, 1),
        Tab(Sv.datagram, 1),
        Tab(Sv.reliable_datagram, 1),
        Tab(Sv.signon, 1),
        Tab(Net.net_message, 1),
        Tab(Net.hostcache, Net.HOSTCACHESIZE),
        Tab(World.box_clipnodes, 6),
        Tab(World.box_planes, 6),
        Tab(World.sv_areanodes, World.AREA_NODES),
        .. CvarTables(),
        .. KButtonTables(),
    ];

    // Every cvar is a `static readonly Cvar_t*` to its own NativeMemory block
    // (Cvar.Alloc), and its String/Value/Next carry state across tests just
    // like the tables above -- found by type so a new cvar needs no entry here.
    private static IEnumerable<Table> CvarTables() =>
        EngineStaticFields()
            .Where(f => f.IsInitOnly && f.FieldType == typeof(Cvar_t*))
            .Select(f => new Table($"{f.DeclaringType!.Name}.{f.Name}", (nint)Pointer.Unbox(f.GetValue(null)!), sizeof(Cvar_t)));

    // ClInput's kbutton_t globals (in_forward, in_attack, ...) are each a
    // `static readonly KButton_t*` to their own NativeMemory block holding
    // key-down state -- found by type so a new button needs no entry here.
    private static IEnumerable<Table> KButtonTables() =>
        EngineStaticFields()
            .Where(f => f.IsInitOnly && f.FieldType == typeof(KButton_t*))
            .Select(f => new Table($"{f.DeclaringType!.Name}.{f.Name}", (nint)Pointer.Unbox(f.GetValue(null)!), sizeof(KButton_t)));

    // Scratch buffers: fully rewritten before every read within a single
    // engine call, so nothing a previous test left in them is ever observed.
    // Also constant tables built once by a static initializer.
    internal static readonly HashSet<string> ScratchTables =
    [
        .. Names(nameof(Com), nameof(Com.va_string), nameof(Com.extenBuf)),
        .. Names(nameof(Keys), nameof(Keys.tinystr)),
        .. Names(nameof(Msg), nameof(Msg.readStringBuffer)),
        .. Names(nameof(PrCmds), nameof(PrCmds.varStringBuf), nameof(PrCmds.checkpvs)),
        .. Names(nameof(Sv), nameof(Sv.fatpvs)),
        .. Names(nameof(ClParse), nameof(ClParse.precacheModelNames), nameof(ClParse.precacheSoundNames)),
        .. Names(nameof(SvPhys), nameof(SvPhys.pushMovedEdict), nameof(SvPhys.pushMovedFrom)),
        .. Names(nameof(Model), nameof(Model.decompressed), nameof(Model.loadname)),
        .. Names(nameof(DPolyse), nameof(DPolyse.r_p0), nameof(DPolyse.r_p1), nameof(DPolyse.r_p2), nameof(DPolyse.a_spans), nameof(DPolyse.skintable)),
        .. Names(nameof(DScan), nameof(DScan.warp_rowptr), nameof(DScan.warp_column)),
        .. Names(nameof(DSprite), nameof(DSprite.sprite_spans)),
        .. Names(nameof(RAclip), nameof(RAclip.fv), nameof(RAclip.av)),
        .. Names(nameof(RAlias), nameof(RAlias.finalverts), nameof(RAlias.auxverts)),
        .. Names(nameof(RBsp), nameof(RBsp.bverts), nameof(RBsp.bedges)),
        .. Names(nameof(RDraw), nameof(RDraw._clipverts), nameof(RDraw.r_leftenter), nameof(RDraw.r_leftexit), nameof(RDraw.r_rightenter), nameof(RDraw.r_rightexit)),
        .. Names(nameof(RDraw), nameof(RDraw.renderpoly_verts0), nameof(RDraw.renderpoly_verts1), nameof(RDraw.renderpoly_pverts)),
        .. Names(nameof(RSprite), nameof(RSprite.clip_verts)),
        .. Names(nameof(RSurf), nameof(RSurf.blocklights)),
        .. Names(nameof(SndMix), nameof(SndMix.paintbuffer)),
        .. Names(nameof(CdAudio), nameof(CdAudio.decodeBuf), nameof(CdAudio.pcmBuf)),
        .. Names(nameof(Vid), nameof(Vid.backingbuf)),
        .. Names(nameof(RAlias), nameof(RAlias.r_avertexnormals)), // constant: filled once from anorms.h
        .. Names(nameof(Net), "slistSendProcedure", "slistPollProcedure"), // Procedure/NextTime rewritten by every NET_Slist_f
        .. Names(nameof(NetIp), "broadcastaddr"), // reset to default by NetIp.Init before use
        .. Names(nameof(NetDgrm), "packetBuffer"), // header/payload rewritten by every send/read before use
        .. Names(nameof(Utf8StringHandler), nameof(Utf8StringHandler._buffer)),
        .. Names(nameof(Cmd), "cmd_argv"), // entries rewritten by every TokenizeString; cmd_argc is reset separately
        .. Names(nameof(RShared), nameof(RShared.newedges), nameof(RShared.removeedges)) // R_ScanEdges clears them every frame
    ];

    // "Type.field" keys, matching UnclassifiedReadonlyTables' naming.
    private static IEnumerable<string> Names(string type, params string[] fields)
        => fields.Select(f => $"{type}.{f}");

    // `static readonly T*` fields that point at neither a restored nor a
    // scratch table -- should always be empty. Utf8String string literals are
    // excluded: they point into Quake.Engine's own mapped image (read-only
    // RVA data), not at a mutable table.
    internal static List<string> UnclassifiedReadonlyTables()
    {
        nint imageBase = Marshal.GetHINSTANCE(typeof(Host).Module);
        int peHeader = *(int*)(imageBase + 0x3C);
        int sizeOfImage = *(int*)(imageBase + peHeader + 0x50); // IMAGE_OPTIONAL_HEADER.SizeOfImage
        List<Table> restored = RestoredTables();

        List<string> unclassified = [];
        foreach (FieldInfo field in EngineStaticFields())
        {
            if (!field.IsInitOnly || !field.FieldType.IsPointer)
                continue;

            string name = $"{field.DeclaringType!.Name}.{field.Name}";
            nint p = (nint)Pointer.Unbox(field.GetValue(null)!);
            if (p == 0 || ScratchTables.Contains(name))
                continue;
            if (p >= imageBase && p < imageBase + sizeOfImage)
                continue;
            if (restored.Any(t => t.Ptr == p))
                continue;
            unclassified.Add(name);
        }
        return unclassified;
    }
}
