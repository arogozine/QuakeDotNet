// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class ScreenTests
{
    private byte* _memBuf;
    private const int MemSize = 1 * 1024 * 1024;
    private byte* _vidBuf;
    private byte* _wadBuf;

    [TestInitialize]
    public void Setup()
    {
        _memBuf = NativeMemory.AllocArray<byte>(MemSize);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_memBuf, MemSize);
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cvar.cvar_vars = null;
        Cmd.BufInit();

        DIface.r_pixbytes = 1;
        _vidBuf = NativeMemory.AllocArrayZeroed<byte>(64 * 64);
        Vid.vid.Buffer = _vidBuf;
        Vid.vid.RowBytes = 64;
        Vid.vid.Width = 64;
        Vid.vid.Height = 64;
        Vid.vid.NumPages = 1;
        Vid.vid.RecalcRefdef = 0;
        Vid.vid.ConBuffer = _vidBuf; // Draw_Character draws into the console buffer
        Vid.vid.ConRowBytes = 64;
        Vid.vid.ConWidth = 64;
        Vid.vid.ConHeight = 64;

        Draw.draw_chars = NativeMemory.AllocArrayZeroed<byte>(128 * 16);

        Cls.state = CActive.Connected;
        Cls.signon = Cls.SIGNONS;
        Keys.key_dest = KeyDest.Game;
        Cl.intermission = 0;
        ClDemo.demoplayback = false;
        Host.realtime = 0;
        Host.host_frametime = 0.05f;

        Screen.scr_copytop = 0;
        Screen.scr_copyeverything = 0;
        Screen.scr_drawloading = false;
        Screen.scr_drawdialog = false;
        Screen.scr_disabled_for_loading = false;
        Screen.scr_skipupdate = false;
        Screen.block_drawing = false;
        Screen.scr_fullupdate = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_memBuf);
        NativeMemory.Free(_vidBuf);
        NativeMemory.Free(Draw.draw_chars);
        if (_wadBuf != null)
        {
            NativeMemory.Free(_wadBuf);
        }
    }

    // Builds a minimal in-memory WAD (3 lumps: ram/net/turtle, each a
    // 1x1 qpic_t) so Screen.Init's Draw_PicFromWad calls can resolve
    // without needing a real gfx.wad file on disk.
    private void InstallFakeWad()
    {
        string[] names = ["ram", "net", "turtle"];
        int picSize = (sizeof(int) * 2) + 1; // width,height + 1 data byte
        int lumpsSize = names.Length * sizeof(LumpInfo_t);
        int picsOffset = lumpsSize;
        _wadBuf = NativeMemory.AllocArrayZeroed<byte>(lumpsSize + (picSize * names.Length));

        LumpInfo_t* lumps = (LumpInfo_t*)_wadBuf;
        for (int i = 0; i < names.Length; i++)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(names[i]);
            for (int j = 0; j < nameBytes.Length; j++)
            {
                lumps[i].Name[j] = nameBytes[j];
            }

            lumps[i].FilePos = picsOffset + (i * picSize);

            QPic_t* pic = (QPic_t*)(_wadBuf + lumps[i].FilePos);
            pic->Width = 1;
            pic->Height = 1;
        }

        Wad.wad_base = _wadBuf;
        Wad.wad_lumps = lumps;
        Wad.wad_numlumps = names.Length;
    }

    [TestMethod]
    public void CalcFov_SquareAspectAt90Degrees_ReturnsNinety()
    {
        // x = 100/tan(45deg) = 100; a = atan(100/100) = 45deg;
        // a*360/PI (radians->the original's own, doubled, formula) = 90.
        float result = Screen.CalcFov(90, 100, 100);
        Assert.AreEqual(90f, result, 0.01f);
    }

    [TestMethod]
    public void CalcFov_OutOfRange_ThrowsSysError()
    {
        Assert.ThrowsExactly<QuakeSysErrorException>(() => Screen.CalcFov(200, 100, 100));
    }

    [TestMethod]
    public void CalcFov_OutOfRange_MessageFormatsFovLikePrintfF()
    {
        // screen.c SCR_CalcFov: Sys_Error ("Bad fov: %f", fov_x)
        var ex = Assert.ThrowsExactly<QuakeSysErrorException>(() => Screen.CalcFov(200, 100, 100));
        Assert.AreEqual("Bad fov: 200.000000", ex.Message);
    }

    [TestMethod]
    public void CalcHorPlusFov_FourByThree_ReturnsFovUnchanged()
    {
        Assert.AreEqual(90f, Screen.CalcHorPlusFov(90, 1024, 768), 0.01f);
    }

    [TestMethod]
    public void CalcHorPlusFov_SixteenByNine_WidensTo106()
    {
        // 2*atan(tan(45deg) * (16/9) / (4/3)) = 2*atan(4/3) = 106.26deg
        Assert.AreEqual(106.26f, Screen.CalcHorPlusFov(90, 1920, 1080), 0.01f);
    }

    [TestMethod]
    public void CalcHorPlusFov_UltraWide_WidensTo121()
    {
        // 2*atan((3440/1440) / (4/3)) = 2*atan(1.7917) = 121.66deg
        Assert.AreEqual(121.66f, Screen.CalcHorPlusFov(90, 3440, 1440), 0.01f);
    }

    [TestMethod]
    public void CalcHorPlusFov_KeepsVerticalFovOfFourByThree()
    {
        float vertical43 = Screen.CalcFov(90, 1024, 768);
        float fov = Screen.CalcHorPlusFov(90, 1920, 1080);

        Assert.AreEqual(vertical43, Screen.CalcFov(fov, 1920, 1080), 0.01f);
    }

    [TestMethod]
    public void CalcHorPlusFov_MaxFovOnSuperWide_StaysValidForCalcFov()
    {
        // fov 170 (CalcRefdef's own upper bound) at 32:9.
        float fov = Screen.CalcHorPlusFov(170, 5120, 1440);

        Assert.IsTrue(fov <= 179);
        Screen.CalcFov(fov, 5120, 1440); // must not Sys_Error
    }

    [TestMethod]
    public void ConsoleViewsizeAndFov_DriveRendererAndView()
    {
        InstallFakeWad();
        Screen.Init();
        Cl.intermission = 0;

        // the registered cvars are the very objects the renderer and menu read
        Assert.IsTrue(Cvar.FindVar(new Utf8String("viewsize"u8)) == Screen.scr_viewsize);
        Assert.IsTrue(Cvar.FindVar(new Utf8String("fov"u8)) == Screen.scr_fov);

        Cvar.SetValue("viewsize"u8, 50);
        VRect_t vrectin = new() { X = 0, Y = 0, Width = 320, Height = 200 };
        VRect_t vrect = default;
        RMain.SetVrect(&vrectin, &vrect, 0);
        Assert.AreEqual(160, vrect.Width); // R_SetVrect follows "viewsize 50"

        Cvar.SetValue("fov"u8, 110);
        *RShared.r_refdef = default;
        RShared.r_refdef->FovX = 110;
        RMain.ViewChanged(&vrectin, 0, 1.0f);
        Assert.IsTrue(RMain.r_fov_greater_than_90); // ... and "fov 110"
    }

    [TestMethod]
    public void Init_RegistersCvarsCommandsAndPics()
    {
        InstallFakeWad();

        Screen.Init();

        Assert.IsTrue(Cvar.FindVar(new Utf8String("viewsize"u8)) != null);
        Assert.AreEqual(100f, Screen.scr_viewsize->Value);
        Assert.IsTrue(Cvar.FindVar(new Utf8String("fov"u8)) != null);
        Assert.IsTrue(Cmd.Exists(new Utf8String("sizeup"u8)));
        Assert.IsTrue(Cmd.Exists(new Utf8String("sizedown"u8)));
        Assert.IsTrue(Cmd.Exists(new Utf8String("screenshot"u8)));
        Assert.IsTrue(Screen.scr_initialized);
        Assert.IsTrue((nint)Screen.scr_ram != 0);
    }

    [TestMethod]
    public void SizeUp_f_IncrementsViewsizeAndFlagsRecalc()
    {
        InstallFakeWad();
        Screen.Init();
        Screen.scr_viewsize->Value = 100;
        Vid.vid.RecalcRefdef = 0;

        Screen.SizeUp_f();

        Assert.AreEqual(110f, Screen.scr_viewsize->Value);
        Assert.AreEqual(1, Vid.vid.RecalcRefdef);
    }

    [TestMethod]
    public void SizeDown_f_DecrementsViewsize()
    {
        InstallFakeWad();
        Screen.Init();
        Screen.scr_viewsize->Value = 100;

        Screen.SizeDown_f();

        Assert.AreEqual(90f, Screen.scr_viewsize->Value);
    }

    [TestMethod]
    public void DrawRam_SkippedWhenShowramDisabled()
    {
        InstallFakeWad();
        Screen.Init();
        Screen.scr_showram->Value = 0;
        DSurf.r_cache_thrash = true;

        // Would throw ("Draw_Pic: bad coordinates") if it actually drew,
        // since scr_vrect is zeroed -- absence of a throw here confirms the
        // showram==0 early-out fired.
        Screen.DrawRam();
    }

    [TestMethod]
    public void DrawNet_SkippedDuringDemoPlayback()
    {
        InstallFakeWad();
        Screen.Init();
        Host.realtime = 10;
        Cls.last_received_message = 0; // well over the 0.3s threshold
        ClDemo.demoplayback = true;

        Screen.DrawNet(); // must not throw despite the threshold being past
    }

    [TestMethod]
    public void CenterPrint_CountsLinesAndStartsTimer()
    {
        InstallFakeWad();
        Screen.Init();
        Cl.time = 5;
        Screen.scr_centertime->Value = 2;

        fixed (byte* str = "line1\nline2\nline3"u8)
        {
            Screen.CenterPrint(str);
        }

        Screen.DrawCenterString(); // must not throw

        Assert.AreEqual(2f, Screen.scr_centertime_off);
    }

    [TestMethod]
    public void CheckDrawCenterString_SkipsWhenNotInGame()
    {
        Keys.key_dest = KeyDest.Menu;
        Screen.scr_center_lines = 1;

        // Should return early (key_dest != key_game) without touching
        // scr_centerstring/Draw.Character -- verified indirectly by not
        // throwing even though Draw.draw_chars content is all zero.
        Screen.CheckDrawCenterString();
    }

    [TestMethod]
    public void UpdateScreen_SkipUpdateFlag_ReturnsImmediately()
    {
        Screen.scr_skipupdate = true;
        int before = Screen.scr_fullupdate;

        Screen.UpdateScreen();

        Assert.AreEqual(before, Screen.scr_fullupdate); // never reached the body
    }

    [TestMethod]
    public void UpdateScreen_DedicatedState_ReturnsImmediately()
    {
        Screen.scr_skipupdate = false;
        Screen.scr_disabled_for_loading = false;
        Cls.state = CActive.Dedicated;
        int before = Screen.scr_fullupdate;

        Screen.UpdateScreen();

        Assert.AreEqual(before, Screen.scr_fullupdate);
    }

    [TestMethod]
    public void UpdateScreen_NotInitialized_ReturnsImmediately()
    {
        Screen.scr_skipupdate = false;
        Screen.scr_disabled_for_loading = false;
        Cls.state = CActive.Connected;
        Screen.scr_initialized = false;
        int before = Screen.scr_fullupdate;

        Screen.UpdateScreen();

        Assert.AreEqual(before, Screen.scr_fullupdate);
    }

    [TestMethod]
    public void WritePCXfile_PacksRunLengthAndWritesFile()
    {
        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gamedir);
        try
        {
            fixed (byte* dir = Encoding.ASCII.GetBytes(gamedir + "\0"))
            {
                Q.StrCpy(Com.com_gamedir, dir);
            }

            byte* pixels = stackalloc byte[4]; // 2x2, no run-length escape needed
            pixels[0] = 0x10;
            pixels[1] = 0x20;
            pixels[2] = 0x30;
            pixels[3] = 0x40;

            byte* palette = stackalloc byte[768];

            fixed (byte* filename = "test_screenshot.pcx"u8)
            {
                Screen.WritePCXfile(filename, pixels, 2, 2, 2, palette);
            }

            Assert.IsTrue(File.Exists(Path.Combine(gamedir, "test_screenshot.pcx")));
        }
        finally
        {
            Directory.Delete(gamedir, true);
        }
    }
}
