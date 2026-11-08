// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

namespace Quake.UnitTests;

using System.Text.RegularExpressions;

/// <summary>Every declared Cvar_t* is registered somewhere (Cvar_RegisterVariable), so console changes reach it.</summary>
[TestClass]
public sealed unsafe class CvarRegistrationTests
{
    [TestMethod]
    public void EveryDeclaredCvarIsRegistered()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Quake.Engine"))) dir = Path.GetDirectoryName(dir)!;
        Assert.IsNotNull(dir, "Quake.Engine source directory not found");

        var files = Directory.EnumerateFiles(Path.Combine(dir, "Quake.Engine"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText).ToList();
        string all = string.Join("\n", files);

        var decl = new Regex(@"^\s*(?:public |private |internal )?static (?:readonly )?Cvar_t\* (\w+(?:\s*,\s*\w+)*)\s*[;=]", RegexOptions.Multiline);
        var names = files.SelectMany(t => decl.Matches(t))
            .SelectMany(m => m.Groups[1].Value.Split(',').Select(n => n.Trim())).Where(n => n != "cvar_vars").Distinct().ToList(); // cvar_vars is the list head, not a cvar
        Assert.IsGreaterThan(50, names.Count, "cvar declaration scan found too few fields");

        var unregistered = names.Where(n => !Regex.IsMatch(all, @"Register(?:Cvar|Variable)\(\s*(?:\w+\.)?" + n + @"\b")).ToList();
        Assert.IsEmpty(unregistered, "Cvar_t* fields never passed to RegisterVariable/RegisterCvar: " + string.Join(", ", unregistered));
    }
}
