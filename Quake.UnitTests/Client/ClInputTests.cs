// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.InteropServices;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// cl_input.c's ClInput.cs -- open_tk.md's Section 2/5: CL_BaseMove/button-
// bit accumulation is pure state (given synthetic key-down/up command
// sequences, assert the resulting usercmd_t), no OpenTK dependency. Shares
// Cmd/Cvar/Cl/Cls's static state with the other DoNotParallelize classes.
[TestClass]
public sealed unsafe class ClInputTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        Cmd.cmd_functions = null; // Cache.Init() registers "flush"; reset before it walks a stale list
        MemoryManager.Memory_Init(_buf, Size);

        Cvar.cvar_vars = null;
        Cmd.cmd_functions = null;
        Cmd.cmd_alias = null;
        Cmd.cmd_argc = 0;
        Cmd.BufInit();

        Cl.viewangles = default;
        Cls.signon = Cls.SIGNONS;
        Host.host_frametime = 0.1f;

        *ClInput.in_forward = default;
        *ClInput.in_back = default;
        *ClInput.in_left = default;
        *ClInput.in_right = default;
        *ClInput.in_moveleft = default;
        *ClInput.in_moveright = default;
        *ClInput.in_strafe = default;
        *ClInput.in_speed = default;
        *ClInput.in_up = default;
        *ClInput.in_down = default;
        *ClInput.in_klook = default;
        *ClInput.in_mlook = default;
        *ClInput.in_attack = default;
        *ClInput.in_jump = default;
        *ClInput.in_use = default;
        ClInput.in_impulse = 0;

        // View.cl_forwardspeed is the one original cl_forwardspeed global
        // (ClInput.cs reuses View's registration rather than declaring its
        // own -- see ClInput.cs's own comment); View.Init() isn't called in
        // this test, so it's never routed through Cvar.RegisterVariable
        // here, and setting .Value directly is enough for BaseMove to read.
        *View.cl_forwardspeed = new() { Value = 200f };

        // ClInput.Init()'s Cvar.RegisterVariable calls mutate each Cvar_t's
        // String to a Zone-allocated copy (freed with this test's arena in
        // Teardown); re-seed every cvar back to its field-initializer
        // default before re-registering against the fresh arena Setup() just
        // created above, or the next test's RegisterVariable call reads a
        // dangling String pointer from this test's already-freed Zone.
        *ClInput.cl_upspeed = new() { Name = new Utf8String("cl_upspeed"u8), String = new Utf8String("200"u8) };
        *ClInput.cl_backspeed = new() { Name = new Utf8String("cl_backspeed"u8), String = new Utf8String("200"u8), Archive = true };
        *ClInput.cl_sidespeed = new() { Name = new Utf8String("cl_sidespeed"u8), String = new Utf8String("350"u8) };
        *ClInput.cl_movespeedkey = new() { Name = new Utf8String("cl_movespeedkey"u8), String = new Utf8String("2.0"u8) };
        *ClInput.cl_yawspeed = new() { Name = new Utf8String("cl_yawspeed"u8), String = new Utf8String("140"u8) };
        *ClInput.cl_pitchspeed = new() { Name = new Utf8String("cl_pitchspeed"u8), String = new Utf8String("150"u8) };
        *ClInput.cl_anglespeedkey = new() { Name = new Utf8String("cl_anglespeedkey"u8), String = new Utf8String("1.5"u8) };
        *ClInput.lookspring = new() { Name = new Utf8String("lookspring"u8), String = new Utf8String("0"u8), Archive = true };
        *ClInput.lookstrafe = new() { Name = new Utf8String("lookstrafe"u8), String = new Utf8String("0"u8), Archive = true };
        *ClInput.sensitivity = new() { Name = new Utf8String("sensitivity"u8), String = new Utf8String("3"u8), Archive = true };
        *ClInput.m_pitch = new() { Name = new Utf8String("m_pitch"u8), String = new Utf8String("0.022"u8), Archive = true };
        *ClInput.m_yaw = new() { Name = new Utf8String("m_yaw"u8), String = new Utf8String("0.022"u8), Archive = true };
        *ClInput.m_forward = new() { Name = new Utf8String("m_forward"u8), String = new Utf8String("1"u8), Archive = true };
        *ClInput.m_side = new() { Name = new Utf8String("m_side"u8), String = new Utf8String("0.8"u8), Archive = true };
        *ClInput.m_filter = new() { Name = new Utf8String("m_filter"u8), String = new Utf8String("0"u8), Archive = true };

        ClInput.Init();
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_buf);

        // Leave ClInput's cvars pointing at their permanent Utf8String literal defaults
        // rather than a Zone-allocated String about to dangle -- otherwise
        // the next test class in this process to reach ClInput.Init()
        // (HostTests's real Host.Init() call chain does, via ClMain.Init())
        // reads a freed pointer in Cvar.RegisterVariable's dedup-copy step.
        // Same reasoning as HostTests/ConsoleTests's own Teardown comments.
        *ClInput.cl_upspeed = new() { Name = new Utf8String("cl_upspeed"u8), String = new Utf8String("200"u8) };
        *ClInput.cl_backspeed = new() { Name = new Utf8String("cl_backspeed"u8), String = new Utf8String("200"u8), Archive = true };
        *ClInput.cl_sidespeed = new() { Name = new Utf8String("cl_sidespeed"u8), String = new Utf8String("350"u8) };
        *ClInput.cl_movespeedkey = new() { Name = new Utf8String("cl_movespeedkey"u8), String = new Utf8String("2.0"u8) };
        *ClInput.cl_yawspeed = new() { Name = new Utf8String("cl_yawspeed"u8), String = new Utf8String("140"u8) };
        *ClInput.cl_pitchspeed = new() { Name = new Utf8String("cl_pitchspeed"u8), String = new Utf8String("150"u8) };
        *ClInput.cl_anglespeedkey = new() { Name = new Utf8String("cl_anglespeedkey"u8), String = new Utf8String("1.5"u8) };
        *ClInput.lookspring = new() { Name = new Utf8String("lookspring"u8), String = new Utf8String("0"u8), Archive = true };
        *ClInput.lookstrafe = new() { Name = new Utf8String("lookstrafe"u8), String = new Utf8String("0"u8), Archive = true };
        *ClInput.sensitivity = new() { Name = new Utf8String("sensitivity"u8), String = new Utf8String("3"u8), Archive = true };
        *ClInput.m_pitch = new() { Name = new Utf8String("m_pitch"u8), String = new Utf8String("0.022"u8), Archive = true };
        *ClInput.m_yaw = new() { Name = new Utf8String("m_yaw"u8), String = new Utf8String("0.022"u8), Archive = true };
        *ClInput.m_forward = new() { Name = new Utf8String("m_forward"u8), String = new Utf8String("1"u8), Archive = true };
        *ClInput.m_side = new() { Name = new Utf8String("m_side"u8), String = new Utf8String("0.8"u8), Archive = true };
        *ClInput.m_filter = new() { Name = new Utf8String("m_filter"u8), String = new Utf8String("0"u8), Archive = true };
    }

    [TestMethod]
    public void BaseMove_BeforeSignon_LeavesCmdUntouched()
    {
        Cls.signon = 0;
        UserCmd_t cmd;
        cmd = default;
        cmd.ForwardMove = 999; // sentinel: CL_BaseMove returns early, mustn't zero it

        ClInput.BaseMove(&cmd);

        Assert.AreEqual(999f, cmd.ForwardMove);
    }

    [TestMethod]
    public void BaseMove_NoKeysHeld_ProducesZeroedCmd()
    {
        UserCmd_t cmd;

        ClInput.BaseMove(&cmd);

        Assert.AreEqual(0f, cmd.ForwardMove);
        Assert.AreEqual(0f, cmd.SideMove);
        Assert.AreEqual(0f, cmd.UpMove);
    }

    [TestMethod]
    public void KeyDown_ForwardHeld_ProducesHalfSpeedForwardMoveOnFirstFrame()
    {
        // +forward with no argv(1) is the "typed manually at the console for
        // continuous down" path (k = -1), matching a real keydown binding's
        // console command with no keynum appended.
        Cmd.ExecuteString(Ansi("+forward"), CmdSource_t.SrcCommand);

        UserCmd_t cmd;
        ClInput.BaseMove(&cmd);

        // CL_KeyState: pressed-and-held-this-frame (impulse down, no impulse
        // up, currently down) == 0.5.
        Assert.AreEqual(0.5f * View.cl_forwardspeed->Value, cmd.ForwardMove);
    }

    [TestMethod]
    public void KeyDown_ForwardHeldAcrossTwoFrames_ProducesFullSpeedOnSecondFrame()
    {
        Cmd.ExecuteString(Ansi("+forward"), CmdSource_t.SrcCommand);

        UserCmd_t cmd;
        ClInput.BaseMove(&cmd); // consumes the impulse-down edge

        ClInput.BaseMove(&cmd); // held the entire second frame -> 1.0

        Assert.AreEqual(View.cl_forwardspeed->Value, cmd.ForwardMove);
    }

    [TestMethod]
    public void KeyUp_ForwardReleased_StopsProducingForwardMove()
    {
        Cmd.ExecuteString(Ansi("+forward"), CmdSource_t.SrcCommand);
        UserCmd_t cmd;
        ClInput.BaseMove(&cmd);

        Cmd.ExecuteString(Ansi("-forward"), CmdSource_t.SrcCommand);
        ClInput.BaseMove(&cmd);

        Assert.AreEqual(0f, cmd.ForwardMove);
    }

    [TestMethod]
    public void KeyDown_Back_ProducesNegativeForwardMove()
    {
        Cmd.ExecuteString(Ansi("+back"), CmdSource_t.SrcCommand);

        UserCmd_t cmd;
        ClInput.BaseMove(&cmd);

        Assert.AreEqual(-0.5f * ClInput.cl_backspeed->Value, cmd.ForwardMove);
    }

    [TestMethod]
    public void KeyDown_MoveRight_ProducesPositiveSideMove()
    {
        Cmd.ExecuteString(Ansi("+moveright"), CmdSource_t.SrcCommand);

        UserCmd_t cmd;
        ClInput.BaseMove(&cmd);

        Assert.AreEqual(0.5f * ClInput.cl_sidespeed->Value, cmd.SideMove);
    }

    [TestMethod]
    public void KeyDown_SpeedKeyHeld_ScalesMovement()
    {
        Cmd.ExecuteString(Ansi("+forward"), CmdSource_t.SrcCommand);
        Cmd.ExecuteString(Ansi("+speed"), CmdSource_t.SrcCommand);

        UserCmd_t cmd;
        ClInput.BaseMove(&cmd);

        Assert.AreEqual(0.5f * View.cl_forwardspeed->Value * ClInput.cl_movespeedkey->Value, cmd.ForwardMove);
    }

    [TestMethod]
    public void KeyDown_Attack_SetsDownAndImpulseBits()
    {
        Cmd.ExecuteString(Ansi("+attack"), CmdSource_t.SrcCommand);

        Assert.AreEqual(3, ClInput.in_attack->State); // bit 0 (down) + bit 1 (impulse down)
    }

    [TestMethod]
    public void KeyUp_AttackTypedAtConsole_ClearsDownAndSetsImpulseUp()
    {
        Cmd.ExecuteString(Ansi("+attack"), CmdSource_t.SrcCommand);

        Cmd.ExecuteString(Ansi("-attack"), CmdSource_t.SrcCommand);

        Assert.AreEqual(4, ClInput.in_attack->State); // bit 2 (impulse up), no longer down
    }

    [TestMethod]
    public void KeyDownUp_WithMatchingKeynum_TracksPerKeyState()
    {
        // two different physical keys (200 = K_MOUSE1, 201 = K_MOUSE2) both
        // holding the same button command, keys.c's own "should only be
        // released when both of the pressing keys have been released".
        Cmd.ExecuteString(Ansi("+jump 200"), CmdSource_t.SrcCommand);
        Cmd.ExecuteString(Ansi("+jump 201"), CmdSource_t.SrcCommand);

        Cmd.ExecuteString(Ansi("-jump 200"), CmdSource_t.SrcCommand);
        Assert.AreNotEqual(0, ClInput.in_jump->State & 1); // still down: 201 still held

        Cmd.ExecuteString(Ansi("-jump 201"), CmdSource_t.SrcCommand);
        Assert.AreEqual(0, ClInput.in_jump->State & 1); // now fully released
    }

    [TestMethod]
    public void Impulse_SetsInImpulseFromArgv()
    {
        Cmd.ExecuteString(Ansi("impulse 9"), CmdSource_t.SrcCommand);

        Assert.AreEqual(9, ClInput.in_impulse);
    }
}
