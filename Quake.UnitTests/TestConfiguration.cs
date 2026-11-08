// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Text.Json;

namespace Quake.UnitTests;

// Reads configuration.json (copied next to the test assembly) for machine-local
// settings that can't be checked into source control, such as the location of a
// real Quake installation used by tests that load real PAK files. A
// configuration.local.json next to it (git-ignored) overrides per key, so a
// machine-specific path never needs to be edited into the tracked file.
//
// Every key is optional: "PakFilesDirectory" is the id1 directory, and
// "RogueDirectory" / "HipnoticDirectory" are the mission pack directories
// (-rogue / -hipnotic). A missing or blank key yields null and the tests that
// need it report Inconclusive.
internal static class TestConfiguration
{
    private static readonly Lazy<Dictionary<string, string>> _values = new(Load);

    public static string? PakFilesDirectory => Get("PakFilesDirectory");
    public static string? RogueDirectory => Get("RogueDirectory");
    public static string? HipnoticDirectory => Get("HipnoticDirectory");

    private static string? Get(string key) => _values.Value.GetValueOrDefault(key);

    private static Dictionary<string, string> Load()
    {
        Dictionary<string, string> values = new();
        ReadInto(values, Path.Combine(AppContext.BaseDirectory, "configuration.json"));
        ReadInto(values, Path.Combine(AppContext.BaseDirectory, "configuration.local.json"));
        return values;
    }

    private static void ReadInto(Dictionary<string, string> values, string path)
    {
        if (!File.Exists(path))
            return;

        using FileStream fs = File.OpenRead(path);
        using JsonDocument doc = JsonDocument.Parse(fs);

        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
        {
            string? value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(value))
                values[property.Name] = value;
        }
    }
}
