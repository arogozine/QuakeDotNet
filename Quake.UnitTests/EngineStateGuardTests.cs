// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

namespace Quake.UnitTests;

// Keeps EngineStateGuard's table inventory honest as the engine grows.
[TestClass]
public sealed unsafe class EngineStateGuardTests
{
    [TestMethod]
    public void AllReadonlyNativeTablesAreClassified()
    {
        List<string> unclassified = EngineStateGuard.UnclassifiedReadonlyTables();

        Assert.IsEmpty(unclassified,
            "Add these to EngineStateGuard.RestoredTables (state-carrying) or ScratchTables: " + string.Join(", ", unclassified));
    }

    [TestMethod]
    public void RestoredTablesDoNotOverlap()
    {
        List<EngineStateGuard.Table> tables = [.. EngineStateGuard.RestoredTables().OrderBy(t => t.Ptr)];

        for (int i = 1; i < tables.Count; i++)
            Assert.IsLessThanOrEqualTo(tables[i].Ptr, tables[i - 1].Ptr + tables[i - 1].Bytes,
                $"{tables[i - 1].Name} overlaps {tables[i].Name} -- wrong size?");
    }
}
