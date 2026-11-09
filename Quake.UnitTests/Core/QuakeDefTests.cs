// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

namespace Quake.UnitTests;

using Quake.Engine;
using System.Text.RegularExpressions;

/// <summary>quakedef.h constants (QuakeDef.cs) and the single-definition rule for them.</summary>
[TestClass]
public sealed unsafe class QuakeDefTests
{
    [TestMethod]
    public void ItemFlagsMatchQuakedefH()
    {
        Assert.AreEqual(1, QuakeDef.IT_SHOTGUN);
        Assert.AreEqual(4, QuakeDef.IT_NAILGUN);
        Assert.AreEqual(131072, QuakeDef.IT_KEY1);   // Sbar used to declare 4 (IT_NAILGUN) here
        Assert.AreEqual(262144, QuakeDef.IT_KEY2);   // ... and 8 (IT_SUPER_NAILGUN) here
        Assert.AreEqual(524288, QuakeDef.IT_INVISIBILITY);
        Assert.AreEqual(1048576, QuakeDef.IT_INVULNERABILITY);
        Assert.AreEqual(2097152, QuakeDef.IT_SUIT);
        Assert.AreEqual(4194304, QuakeDef.IT_QUAD);
        Assert.AreEqual(1 << 31, QuakeDef.IT_SIGIL4);
        Assert.AreEqual(8388608, QuakeDef.RIT_ARMOR1);
        Assert.AreEqual(int.MinValue, QuakeDef.RIT_SUPERHEALTH);
        Assert.AreEqual(1 << 16, QuakeDef.HIT_PROXIMITY_GUN);
        Assert.AreEqual(1 << 7, QuakeDef.HIT_MJOLNIR);
        Assert.AreEqual(1 << 23, QuakeDef.HIT_LASER_CANNON);
    }

    [TestMethod]
    public void LimitsMatchQuakedefH()
    {
        Assert.AreEqual(64, QuakeDef.MAX_QPATH);
        Assert.AreEqual(128, QuakeDef.MAX_OSPATH);
        Assert.AreEqual(50, QuakeDef.MAX_NUM_ARGVS);
        Assert.AreEqual(1024, QuakeDef.MAX_DATAGRAM);
        Assert.AreEqual(64, QuakeDef.MAX_LIGHTSTYLES);
        Assert.AreEqual(39, QuakeDef.SAVEGAME_COMMENT_LENGTH);
        Assert.AreEqual(16, QuakeDef.MAX_SCOREBOARD);
        Assert.AreEqual(32, QuakeDef.MAX_SCOREBOARDNAME);
        Assert.AreEqual(1.09f, QuakeDef.VERSION);
    }

    [TestMethod]
    public void QuakedefConstantsAreDeclaredOnlyInQuakeDef()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Quake.Engine"))) dir = Path.GetDirectoryName(dir)!;
        Assert.IsNotNull(dir, "Quake.Engine source directory not found");

        var decl = new Regex(@"^\s*(?:public |private |internal )?(?:static )?const (?:int|float) (?:MAX_(?:QPATH|OSPATH|NUM_ARGVS|MSGLEN|DATAGRAM|EDICTS|LIGHTSTYLES|MODELS|SOUNDS|STYLESTRING|CL_STATS|SCOREBOARDNAME|SCOREBOARD)|VERSION|PITCH|YAW|ROLL|ON_EPSILON|SAVEGAME_COMMENT_LENGTH|R?IT_\w+|HIT_\w+)\b", RegexOptions.Multiline);
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(dir, "Quake.Engine"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || Path.GetFileName(file) == "QuakeDef.cs") continue;
            if (decl.IsMatch(File.ReadAllText(file))) offenders.Add(Path.GetFileName(file));
        }
        Assert.AreEqual(0, offenders.Count, "quakedef.h constants re-declared in: " + string.Join(", ", offenders));
    }
}
