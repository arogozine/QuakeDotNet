// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Desktop;
using Quake.Engine;
using GKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using MouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace Quake.UnitTests;

// vid_win.c's MapKey/scantokey[] equivalent (Quake.Desktop/KeyMap.cs): pure
// GLFW keycode -> Quake keynum lookup, no window needed.
[TestClass]
public sealed unsafe class KeyMapTests
{
    [TestMethod]
    public void Letters_MapToLowercaseAscii()
    {
        Assert.AreEqual('a', KeyMap.ToKeynum(GKeys.A));
        Assert.AreEqual('z', KeyMap.ToKeynum(GKeys.Z));
    }

    [TestMethod]
    public void Digits_AndKeypadDigits_MapToAsciiDigits()
    {
        Assert.AreEqual('0', KeyMap.ToKeynum(GKeys.D0));
        Assert.AreEqual('9', KeyMap.ToKeynum(GKeys.D9));
        Assert.AreEqual('5', KeyMap.ToKeynum(GKeys.KeyPad5));
    }

    [TestMethod]
    public void FunctionKeys_MapToConsecutiveKeynums()
    {
        Assert.AreEqual(Keys.K_F1, KeyMap.ToKeynum(GKeys.F1));
        Assert.AreEqual(Keys.K_F12, KeyMap.ToKeynum(GKeys.F12));
    }

    [TestMethod]
    public void LeftAndRightModifiers_MapToSameKeynum()
    {
        Assert.AreEqual(Keys.K_SHIFT, KeyMap.ToKeynum(GKeys.LeftShift));
        Assert.AreEqual(Keys.K_SHIFT, KeyMap.ToKeynum(GKeys.RightShift));
        Assert.AreEqual(Keys.K_CTRL, KeyMap.ToKeynum(GKeys.LeftControl));
        Assert.AreEqual(Keys.K_CTRL, KeyMap.ToKeynum(GKeys.RightControl));
        Assert.AreEqual(Keys.K_ALT, KeyMap.ToKeynum(GKeys.LeftAlt));
        Assert.AreEqual(Keys.K_ALT, KeyMap.ToKeynum(GKeys.RightAlt));
    }

    [TestMethod]
    public void SpecialKeys_MapToQuakeKeynums()
    {
        Assert.AreEqual(Keys.K_ENTER, KeyMap.ToKeynum(GKeys.Enter));
        Assert.AreEqual(Keys.K_ENTER, KeyMap.ToKeynum(GKeys.KeyPadEnter));
        Assert.AreEqual(Keys.K_ESCAPE, KeyMap.ToKeynum(GKeys.Escape));
        Assert.AreEqual(Keys.K_BACKSPACE, KeyMap.ToKeynum(GKeys.Backspace));
        Assert.AreEqual(Keys.K_PAUSE, KeyMap.ToKeynum(GKeys.Pause));
        Assert.AreEqual('`', KeyMap.ToKeynum(GKeys.GraveAccent));
    }

    [TestMethod]
    public void UnmappedKey_ReturnsMinusOne()
    {
        Assert.AreEqual(-1, KeyMap.ToKeynum(GKeys.Unknown));
        Assert.AreEqual(-1, KeyMap.ToKeynum(GKeys.CapsLock));
    }

    [TestMethod]
    public void EveryMappedKeynum_IsWithinKeyTableRange()
    {
        foreach (GKeys k in Enum.GetValues<GKeys>())
        {
            int n = KeyMap.ToKeynum(k);
            Assert.IsTrue(n == -1 || (n >= 0 && n < 256), $"{k} -> {n}");
        }
    }

    [TestMethod]
    public void MouseButtons_MapIncludingButton4And5()
    {
        Assert.AreEqual(Keys.K_MOUSE1, KeyMap.ToKeynum(MouseButton.Left));
        Assert.AreEqual(Keys.K_MOUSE2, KeyMap.ToKeynum(MouseButton.Right));
        Assert.AreEqual(Keys.K_MOUSE3, KeyMap.ToKeynum(MouseButton.Middle));
        Assert.AreEqual(Keys.K_MOUSE4, KeyMap.ToKeynum(MouseButton.Button4));
        Assert.AreEqual(Keys.K_MOUSE5, KeyMap.ToKeynum(MouseButton.Button5));
        Assert.AreEqual(-1, KeyMap.ToKeynum(MouseButton.Button6));
    }

    [TestMethod]
    public void Wheel_SignSelectsKey()
    {
        Assert.AreEqual(Keys.K_MWHEELUP, KeyMap.ToWheelKeynum(1f));
        Assert.AreEqual(Keys.K_MWHEELUP, KeyMap.ToWheelKeynum(0.1f));
        Assert.AreEqual(Keys.K_MWHEELDOWN, KeyMap.ToWheelKeynum(-1f));
        Assert.AreEqual(-1, KeyMap.ToWheelKeynum(0f));
    }
}
