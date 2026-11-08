// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

namespace Quake.UnitTests;

using System.Reflection;
using Quake.Engine;

/// <summary>
/// Guardrail for duplicate_consolidation.md: a static field/const/method name that WinQuake C
/// defines once must have one definition in Quake.Engine. Same-named statics in different
/// static classes are listed in <see cref="Allowed"/> with the reason they are legitimate.
/// </summary>
[TestClass]
public sealed unsafe class DuplicateSymbolTests
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> Allowed =
    [
        // Per-module entry points, one per C file
        "Init",
        "Shutdown",
        "Update",
        "Reset",
        "Close",
        "Connect",
        "Listen",
        "Play",
        "Print",
        "Printf",
        "Error",
        "Write",
        "Alloc",
        "Free",
        "Check",
        "Clear",
        "Move",
        "Set",
        // cl_*/sv_* and cls/svs pairs of the same field
        "cmd",
        "paused",
        "time",
        "state",
        "signon",
        "maxclients",
        "worldmodel",
        "model_precache",
        "sound_precache",
        "onground",
        // net driver vtable functions (net_main.c, net_dgrm.c, net_loop.c, ...)
        "CanSendMessage",
        "CheckNewConnections",
        "GetMessage",
        "MakeDriver",
        "SearchForHosts",
        "SendMessage",
        "SendUnreliableMessage",
        // distinct C functions that share a suffix (V_/SCR_, Con_/SCR_, M_/Sbar_, R_/D_, Host_/Sys_, CDAudio_/S_, Mod_/S_, ...)
        "CalcRefdef",
        "CheckParm",
        "DrawCharacter",
        "DrawConsole",
        "DrawPic",
        "DrawSprite",
        "DrawTransPic",
        "Draw_",
        "FindName",
        "RenderView",
        "SetupFrame",
        "StartSound",
        "TransformVector",
        "ViewChanged",
        "RegisterCvar",
        // file-scope statics that C also declares in more than one file (view.c/sv_user.c forward/right/up, d_sprite.c/r_sprite.c sprite_height, r_aclip.c/r_edge.c fv, r_alias.c/r_part.c NUMVERTEXNORMALS)
        "forward",
        "right",
        "up",
        "sprite_height",
        "fv",
        "NUMVERTEXNORMALS",
        // port-only helpers that stand in for fscanf/fprintf and similar libc calls
        "ReadFloat",
        "WriteText",
        // OpenAL objects: not in C; CdAudio streams music on its own source next to SndHost's SFX source
        "alBuffers",
        "initialized",
        "source",
    ];

    private static IEnumerable<Type> StaticClasses() =>
        typeof(QuakeDef).Assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed && !t.IsNested
            && !t.Name.StartsWith('<') && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() == null);

    internal static List<string> Collisions()
    {
        var byName = new Dictionary<string, List<string>>();
        foreach (Type t in StaticClasses())
        {
            var names = t.GetFields(Flags).Where(f => !f.Name.Contains('<')).Select(f => f.Name)
                .Concat(t.GetMethods(Flags).Where(m => !m.IsSpecialName && !m.Name.Contains('<') && !m.Name.Contains('|')).Select(m => m.Name))
                .Distinct();
            foreach (string n in names)
            {
                if (!byName.TryGetValue(n, out var l)) byName[n] = l = [];
                l.Add(t.Name);
            }
        }
        return byName.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}").OrderBy(s => s).ToList();
    }

    [TestMethod]
    public void StaticSymbolNames_AreDefinedInOneClass_UnlessAllowed()
    {
        var offenders = Collisions().Where(c => !Allowed.Contains(c[..c.IndexOf(':')])).ToList();
        Assert.IsEmpty(offenders, "Duplicate static names (add to Allowed with a reason if C also has them twice):\n" + string.Join("\n", offenders));
    }

    [TestMethod]
    public void TypeNames_AreUnique()
    {
        var dupes = typeof(QuakeDef).Assembly.GetTypes().Where(t => !t.IsNested && !t.Name.StartsWith('<'))
            .GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}").ToList();
        Assert.IsEmpty(dupes, "Same type name defined twice:\n" + string.Join("\n", dupes));
    }
}
