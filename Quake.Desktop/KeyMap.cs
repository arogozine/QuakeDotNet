// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using GKeys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using MouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using QKeys = Quake.Engine.Keys;

namespace Quake.Desktop;

/// <summary>
/// vid_win.c's MapKey/scantokey[] -- Win32 scancode -> Quake keynum lookup.
/// GLFW's Keys enum (OpenTK.Windowing.GraphicsLibraryFramework, aliased
/// `GKeys` here since it collides with Quake.Engine.Keys) gives a physical,
/// layout-independent keycode per key, playing the same role scantokey's
/// raw scancode index did; this table maps that to the keynums Key_Event
/// expects (unshifted ASCII for letters/digits/punctuation -- Keys.cs's own
/// keyshift[] table applies Shift, matching the original's division of
/// labor between MapKey and Key_Event/Key_Console).
/// </summary>
public static class KeyMap
{
    public static int ToKeynum(GKeys key) => key switch
    {
        >= GKeys.A and <= GKeys.Z => 'a' + (key - GKeys.A),
        >= GKeys.D0 and <= GKeys.D9 => '0' + (key - GKeys.D0),
        >= GKeys.KeyPad0 and <= GKeys.KeyPad9 => '0' + (key - GKeys.KeyPad0),
        >= GKeys.F1 and <= GKeys.F12 => QKeys.K_F1 + (key - GKeys.F1),

        GKeys.Space => QKeys.K_SPACE,
        GKeys.Enter => QKeys.K_ENTER,
        GKeys.KeyPadEnter => QKeys.K_ENTER,
        GKeys.Escape => QKeys.K_ESCAPE,
        GKeys.Backspace => QKeys.K_BACKSPACE,
        GKeys.Tab => QKeys.K_TAB,
        GKeys.Up => QKeys.K_UPARROW,
        GKeys.Down => QKeys.K_DOWNARROW,
        GKeys.Left => QKeys.K_LEFTARROW,
        GKeys.Right => QKeys.K_RIGHTARROW,
        GKeys.LeftAlt or GKeys.RightAlt => QKeys.K_ALT,
        GKeys.LeftControl or GKeys.RightControl => QKeys.K_CTRL,
        GKeys.LeftShift or GKeys.RightShift => QKeys.K_SHIFT,
        GKeys.Insert => QKeys.K_INS,
        GKeys.Delete => QKeys.K_DEL,
        GKeys.PageDown => QKeys.K_PGDN,
        GKeys.PageUp => QKeys.K_PGUP,
        GKeys.Home => QKeys.K_HOME,
        GKeys.End => QKeys.K_END,
        GKeys.Pause => QKeys.K_PAUSE,

        GKeys.Minus or GKeys.KeyPadSubtract => '-',
        GKeys.Equal or GKeys.KeyPadEqual => '=',
        GKeys.KeyPadAdd => '+',
        GKeys.KeyPadMultiply => '*',
        GKeys.KeyPadDivide => '/',
        GKeys.KeyPadDecimal => '.',
        GKeys.LeftBracket => '[',
        GKeys.RightBracket => ']',
        GKeys.Semicolon => ';',
        GKeys.Apostrophe => '\'',
        GKeys.Comma => ',',
        GKeys.Period => '.',
        GKeys.Slash => '/',
        GKeys.Backslash => '\\',
        GKeys.GraveAccent => '`',

        _ => -1,
    };

    public static int ToKeynum(MouseButton button) => button switch
    {
        MouseButton.Left => QKeys.K_MOUSE1,
        MouseButton.Right => QKeys.K_MOUSE2,
        MouseButton.Middle => QKeys.K_MOUSE3,
        MouseButton.Button4 => QKeys.K_MOUSE4,
        MouseButton.Button5 => QKeys.K_MOUSE5,
        _ => -1,
    };

    // Win32 WM_MOUSEWHEEL -> K_MWHEELUP/K_MWHEELDOWN: the sign of the scroll
    // offset picks the key, -1 for no movement.
    public static int ToWheelKeynum(float offsetY) =>
        offsetY > 0 ? QKeys.K_MWHEELUP : offsetY < 0 ? QKeys.K_MWHEELDOWN : -1;
}
