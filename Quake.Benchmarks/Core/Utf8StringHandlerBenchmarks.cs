// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Quake.Engine;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Benchmarks;

// Compares Utf8StringHandler (formats straight into one static, native,
// 4096-byte ASCII buffer -- the replacement for the sprintf-into-char[] idiom,
// e.g. Con_Printf/va in common.c) against standard string interpolation.
//
// The scenarios are the shapes the engine actually formats, taken from its
// Con.Printf / Host.Error / CStr.Write / SaveGame / Sbar call sites:
//   OneInt      - short status line ("Sound sampling rate: 11025\n")
//   StatLine    - long fixed literal + one int (net stats: "packetsSent   = N")
//   TwoCStrings - two C strings ("PackFile: %s : %s\n")
//   ModelName   - tiny "*N" brush-model name written into a native buffer
//   Mixed       - map line: string + ints + float
//   Hex         - zone dump: int + pointer in hex
//   Aligned     - printf field widths ("%5d %-16s")
//   Hud         - per-frame status bar numbers ("%3d")
//   Vector      - savegame/edict dump: three floats
//   Truncated   - message that overflows the buffer (the "..." path)
//
// The consumer of a Quake format string needs NUL-terminated ASCII bytes, so
// each scenario has two string baselines:
//   String        - just the interpolated string (allocation + formatting)
//   String+Encode - interpolated string, then ASCII-encoded into a native
//                   NUL-terminated buffer (what the handler produces)
// The handler variants return the written length so the work cannot be
// elided. Handler benchmarks are Release-only meaningful: DEBUG builds add
// the stale-handler assert to every append.
[MemoryDiagnoser]
[DisassemblyDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class Utf8StringHandlerBenchmarks
{
    #region Setup / Teardown

    private const int BufSize = 4096;
    // Instance fields so the JIT cannot constant-fold the arguments.
    private int _int = 12345;
    private int _small = 42;
    private int _health = 100;
    private int _index = 7;
    private float _float = 123.456f;
    private float _x = 512.5f;
    private float _y = -128.25f;
    private float _z = 24f;
    private nint _zone;
    private string _name = "maps/e1m1.bsp";
    private string _long = new('x', 6000);
    private byte* _cname;
    private byte* _cpak;
    private byte* _cfile;
    private byte* _dst;
    private string CName => Marshal.PtrToStringAnsi((nint)_cname)!;
    private string CPak => Marshal.PtrToStringAnsi((nint)_cpak)!;
    private string CFile => Marshal.PtrToStringAnsi((nint)_cfile)!;

    [GlobalSetup]
    public void Setup()
    {
        _dst = NativeMemory.AllocArrayZeroed<byte>(BufSize);
        _zone = (nint)_dst;

        _cname = AllocCString(_name);
        _cpak = AllocCString("id1/pak0.pak");
        _cfile = AllocCString("progs/player.mdl");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.Free(_dst);
        NativeMemory.Free(_cname);
        NativeMemory.Free(_cpak);
        NativeMemory.Free(_cfile);
    }

    #endregion

    private static byte* AllocCString(string s)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        byte* p = NativeMemory.AllocArrayZeroed<byte>(bytes.Length + 1);
        bytes.AsSpan().CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    // Encodes s into the native buffer and NUL-terminates it, like the
    // handler's output; returns the byte count.
    private int Encode(string s)
    {
        int n = Encoding.ASCII.GetBytes(s, new Span<byte>(_dst, BufSize - 1));
        _dst[n] = 0;
        return n;
    }

    private static int Length(Utf8StringHandler h) => h.GetWrittenBuffer().Length;

    // ---- One int: "Sound sampling rate: 12345\n" ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OneInt")]
    public string OneInt_String() => $"Sound sampling rate: {_int}\n";

    [Benchmark]
    [BenchmarkCategory("OneInt")]
    public int OneInt_StringEncode() => Encode($"Sound sampling rate: {_int}\n");

    [Benchmark]
    [BenchmarkCategory("OneInt")]
    public int OneInt_Handler() => Length($"Sound sampling rate: {_int}\n");

    // ---- Long literal + int: "reliable messages sent     = 12345\n" ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("StatLine")]
    public string StatLine_String() => $"reliable messages sent     = {_int}\n";

    [Benchmark]
    [BenchmarkCategory("StatLine")]
    public int StatLine_StringEncode() => Encode($"reliable messages sent     = {_int}\n");

    [Benchmark]
    [BenchmarkCategory("StatLine")]
    public int StatLine_Handler() => Length($"reliable messages sent     = {_int}\n");

    // ---- Two C strings: "PackFile: id1/pak0.pak : progs/player.mdl\n" ----
    // The string baseline has to decode each C string into a managed string.

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TwoCStrings")]
    public string TwoCStrings_String() => $"PackFile: {CPak} : {CFile}\n";

    [Benchmark]
    [BenchmarkCategory("TwoCStrings")]
    public int TwoCStrings_StringEncode() => Encode($"PackFile: {CPak} : {CFile}\n");

    [Benchmark]
    [BenchmarkCategory("TwoCStrings")]
    public int TwoCStrings_Handler() => Length($"PackFile: {_cpak} : {_cfile}\n");

    // ---- Tiny brush-model name: "*7" ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ModelName")]
    public string ModelName_String() => $"*{_index + 1}";

    [Benchmark]
    [BenchmarkCategory("ModelName")]
    public int ModelName_StringEncode() => Encode($"*{_index + 1}");

    [Benchmark]
    [BenchmarkCategory("ModelName")]
    public int ModelName_Handler() => Length($"*{_index + 1}");

    // ---- Several mixed values: map name + ints + float ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Mixed")]
    public string Mixed_String() =>
        $"{_name}: {_int} edicts, {_small} clients, {_float:F2}s";

    [Benchmark]
    [BenchmarkCategory("Mixed")]
    public int Mixed_StringEncode() =>
        Encode($"{_name}: {_int} edicts, {_small} clients, {_float:F2}s");

    [Benchmark]
    [BenchmarkCategory("Mixed")]
    public int Mixed_Handler() =>
        Length($"{_name}: {_int} edicts, {_small} clients, {_float:F2}s");

    // ---- Int + pointer in hex: "zone size: 12345  location: 1A2B3C\n" ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Hex")]
    public string Hex_String() => $"zone size: {_int}  location: {_zone:X}\n";

    [Benchmark]
    [BenchmarkCategory("Hex")]
    public int Hex_StringEncode() => Encode($"zone size: {_int}  location: {_zone:X}\n");

    [Benchmark]
    [BenchmarkCategory("Hex")]
    public int Hex_Handler() => Length($"zone size: {_int}  location: {_zone:X}\n");

    // ---- printf field widths: "%5d %-16s" ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Aligned")]
    public string Aligned_String() => $"{_small,5} {_name,-16}|";

    [Benchmark]
    [BenchmarkCategory("Aligned")]
    public int Aligned_StringEncode() => Encode($"{_small,5} {_name,-16}|");

    [Benchmark]
    [BenchmarkCategory("Aligned")]
    public int Aligned_Handler() => Length($"{_small,5} {_name,-16}|");

    // ---- Per-frame HUD numbers: "%3d" for health, a C string label ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Hud")]
    public string Hud_String() => $"{_health,3}";

    [Benchmark]
    [BenchmarkCategory("Hud")]
    public int Hud_StringEncode() => Encode($"{_health,3}");

    [Benchmark]
    [BenchmarkCategory("Hud")]
    public int Hud_Handler() => Length($"{_health,3}");

    // ---- Vector / edict dump: three floats ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Vector")]
    public string Vector_String() => $"\"{_name}\" \"{_x} {_y} {_z}\"\n";

    [Benchmark]
    [BenchmarkCategory("Vector")]
    public int Vector_StringEncode() => Encode($"\"{_name}\" \"{_x} {_y} {_z}\"\n");

    [Benchmark]
    [BenchmarkCategory("Vector")]
    public int Vector_Handler() => Length($"\"{_name}\" \"{_x} {_y} {_z}\"\n");

    // ---- Overflow: a message longer than the buffer ("..." path) ----
    // The string baselines build and encode all 6000 chars; the handler stops
    // at capacity.

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Truncated")]
    public string Truncated_String() => $"Error: {_long}";

    [Benchmark]
    [BenchmarkCategory("Truncated")]
    public int Truncated_StringEncode()
    {
        string s = $"Error: {_long}";
        return Encode(s.Length < BufSize - 1 ? s : s[..(BufSize - 1)]);
    }

    [Benchmark]
    [BenchmarkCategory("Truncated")]
    public int Truncated_Handler() => Length($"Error: {_long}");
}
