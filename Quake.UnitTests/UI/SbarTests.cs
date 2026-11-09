// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// Sbar/Wad/Com/Cvar/Cmd share static state (Sbar_Init loads every HUD pic
// through the WAD loader, matching Host_Init's real W_LoadWadFile("gfx.wad")
// call), so, like WadTests/CvarTests, these can't run concurrently with
// anything else touching those subsystems.
[TestClass]
public sealed unsafe class SbarTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private string? _gamedir;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);
        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Com.com_searchpaths = null;

        _gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_gamedir);
        WriteSbarWadFile(Path.Combine(_gamedir, "gfx.wad"), SbarLumpNames());
        Com.AddGameDirectory(Ansi(_gamedir));
        Wad.LoadWadFile(Ansi("gfx.wad"));

        Com.rogue = false;
        Com.hipnotic = false;
        Array.Clear(Sbar.hsb_weapons);
        Array.Clear(Sbar.hsb_items);
        Array.Clear(Sbar.rsb_invbar);
        Array.Clear(Sbar.rsb_weapons);
        Array.Clear(Sbar.rsb_items);
        Array.Clear(Sbar.rsb_ammo);
        Sbar.rsb_teambord = null;
    }

    [TestCleanup]
    public void Teardown()
    {
        Com.com_searchpaths = null;
        Com.rogue = false;
        Com.hipnotic = false;
        NativeMemory.Free(_buf);
        if (_gamedir != null)
            Directory.Delete(_gamedir, true);
    }

    // Every lump name Sbar.Init() looks up -- id1 base game plus the rogue/
    // hipnotic mission-pack lumps, so a single WAD serves every test
    // regardless of which of Com.rogue/Com.hipnotic is set.
    private static List<string> SbarLumpNames()
    {
        List<string> names = [];

        for (int i = 0; i < 10; i++)
        {
            names.Add($"num_{i}");
            names.Add($"anum_{i}");
        }
        names.Add("num_minus");
        names.Add("anum_minus");
        names.Add("num_colon");
        names.Add("num_slash");

        string[] weapons = ["shotgun", "sshotgun", "nailgun", "snailgun", "rlaunch", "srlaunch", "lightng"];
        foreach (string w in weapons)
            names.Add($"inv_{w}");
        foreach (string w in weapons)
            names.Add($"inv2_{w}");
        for (int i = 1; i <= 5; i++)
            foreach (string w in weapons)
                names.Add($"inva{i}_{w}");

        names.AddRange(["sb_shells", "sb_nails", "sb_rocket", "sb_cells"]);
        names.AddRange(["sb_armor1", "sb_armor2", "sb_armor3"]);
        names.AddRange(["sb_key1", "sb_key2", "sb_invis", "sb_invuln", "sb_suit", "sb_quad"]);
        names.AddRange(["sb_sigil1", "sb_sigil2", "sb_sigil3", "sb_sigil4"]);

        for (int i = 1; i <= 5; i++)
        {
            names.Add($"face{i}");
            names.Add($"face_p{i}");
        }
        names.AddRange(["face_invis", "face_invul2", "face_inv2", "face_quad"]);

        names.AddRange(["sbar", "ibar", "scorebar"]);

        // hipnotic
        string[] hipWeapons = ["laser", "mjolnir", "gren_prox", "prox_gren", "prox"];
        foreach (string w in hipWeapons)
            names.Add($"inv_{w}");
        foreach (string w in hipWeapons)
            names.Add($"inv2_{w}");
        for (int i = 1; i <= 5; i++)
            foreach (string w in hipWeapons)
                names.Add($"inva{i}_{w}");
        names.AddRange(["sb_wsuit", "sb_eshld"]);

        // rogue
        names.AddRange(["r_invbar1", "r_invbar2"]);
        names.AddRange(["r_lava", "r_superlava", "r_gren", "r_multirock", "r_plasma"]);
        names.AddRange(["r_shield1", "r_agrav1", "r_teambord"]);
        names.AddRange(["r_ammolava", "r_ammomulti", "r_ammoplasma"]);

        return names;
    }

    // Builds a minimal WAD2 file with one (int)WadLumpType.QPic lump (8 zero bytes: a
    // width/height pair, both 0) per given name -- mirrors WadTests'
    // WriteWadFile helper, specialized for Sbar_Init's flat name list.
    private static void WriteSbarWadFile(string path, List<string> names)
    {
        const int headerSize = 12;
        byte[] payload = new byte[8]; // width=0, height=0

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        int[] filepositions = new int[names.Count];
        int ofs = headerSize;
        for (int i = 0; i < names.Count; i++)
        {
            filepositions[i] = ofs;
            ofs += payload.Length;
        }

        int infotableofs = ofs;

        bw.Write((byte)'W'); bw.Write((byte)'A'); bw.Write((byte)'D'); bw.Write((byte)'2');
        bw.Write(names.Count);
        bw.Write(infotableofs);

        for (int i = 0; i < names.Count; i++)
            bw.Write(payload);

        for (int i = 0; i < names.Count; i++)
        {
            bw.Write(filepositions[i]);   // filepos
            bw.Write(payload.Length);     // disksize
            bw.Write(payload.Length);     // size
            bw.Write((byte)(int)WadLumpType.QPic); // type
            bw.Write((byte)0);            // compression
            bw.Write((byte)0);            // pad1
            bw.Write((byte)0);            // pad2
            byte[] nameBytes = new byte[16];
            Encoding.ASCII.GetBytes(names[i]).CopyTo(nameBytes, 0);
            bw.Write(nameBytes);
        }
    }

    [TestMethod]
    public void Init_LoadsPicsAndRegistersCommands()
    {
        Sbar.Init();

        Assert.IsTrue(Cmd.Exists(Ansi("+showscores")));
        Assert.IsTrue(Cmd.Exists(Ansi("-showscores")));

        Assert.IsTrue((nint)Sbar.sb_sbar != 0);
        Assert.IsTrue((nint)Sbar.sb_ibar != 0);
        Assert.IsTrue((nint)Sbar.sb_scorebar != 0);
        Assert.IsTrue((nint)Sbar.sb_colon != 0);
        Assert.IsTrue((nint)Sbar.sb_nums[0, 5] != 0);
        Assert.IsTrue((nint)Sbar.sb_weapons[2, 3] != 0);
    }

    [TestMethod]
    public void Init_Hipnotic_LoadsHipnoticWeaponAndItemPics()
    {
        Com.hipnotic = true;
        Sbar.Init();

        Assert.IsTrue((nint)Sbar.hsb_weapons[0, 0] != 0);
        Assert.IsTrue((nint)Sbar.hsb_weapons[6, 4] != 0);
        Assert.IsTrue((nint)Sbar.hsb_items[0] != 0);
        Assert.IsTrue((nint)Sbar.hsb_items[1] != 0);

        // hipweapons (sbar.c) -- maps hipnotic's 4 extra weapon slots to their
        // item bit numbers.
        CollectionAssert.AreEqual(new[] { 23, 7, 4, 16 }, Sbar.hipweapons);
    }

    [TestMethod]
    public void Init_Rogue_LoadsRogueWeaponAndItemPics()
    {
        Com.rogue = true;
        Sbar.Init();

        Assert.IsTrue((nint)Sbar.rsb_invbar[0] != 0);
        Assert.IsTrue((nint)Sbar.rsb_invbar[1] != 0);
        Assert.IsTrue((nint)Sbar.rsb_weapons[4] != 0);
        Assert.IsTrue((nint)Sbar.rsb_items[1] != 0);
        Assert.IsTrue((nint)Sbar.rsb_teambord != 0);
        Assert.IsTrue((nint)Sbar.rsb_ammo[2] != 0);
    }

    [TestMethod]
    public void Init_Stock_DoesNotLoadMissionPackPics()
    {
        Sbar.Init();

        Assert.IsTrue((nint)Sbar.hsb_weapons[0, 0] == 0);
        Assert.IsTrue((nint)Sbar.rsb_invbar[0] == 0);
    }

    [TestMethod]
    public void ShowScores_SetsFlag_DontShowScores_ClearsIt()
    {
        Sbar.sb_showscores = false;
        Sbar.ShowScores();
        Assert.IsTrue(Sbar.sb_showscores);

        Sbar.DontShowScores();
        Assert.IsFalse(Sbar.sb_showscores);
    }

    [TestMethod]
    public void Itoa_PositiveNumber_WritesDigitsAndReturnsLength()
    {
        byte* buf = stackalloc byte[12];
        int len = Sbar.Itoa(42, buf);

        Assert.AreEqual(2, len);
        Assert.AreEqual((byte)'4', buf[0]);
        Assert.AreEqual((byte)'2', buf[1]);
        Assert.AreEqual(0, buf[2]);
    }

    [TestMethod]
    public void Itoa_NegativeNumber_WritesLeadingMinus()
    {
        byte* buf = stackalloc byte[12];
        int len = Sbar.Itoa(-7, buf);

        Assert.AreEqual(2, len);
        Assert.AreEqual((byte)'-', buf[0]);
        Assert.AreEqual((byte)'7', buf[1]);
    }

    [TestMethod]
    public void ColorForMap_MirrorsOriginalFormula()
    {
        Assert.AreEqual(8, Sbar.ColorForMap(0));
        Assert.AreEqual(136, Sbar.ColorForMap(128));
        Assert.AreEqual(263, Sbar.ColorForMap(255));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(5)]
    [DataRow(42)]
    [DataRow(999)]
    [DataRow(1234)]
    [DataRow(-7)]
    [DataRow(-100)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void HandlerAlignment_MatchesPrintfPercent3i(int value)
    {
        // Sbar's "%3i" formatting is the handler's {value,3}.
        Utf8StringHandler text = $"{value,3}";

        Assert.AreEqual(value.ToString().PadLeft(3), Encoding.ASCII.GetString(text.GetWrittenBuffer()));
    }
}
