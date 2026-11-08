// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Quake.Engine;

namespace Quake.Desktop;

/// <summary>
/// in_win.c's IN_Init/IN_Commands/IN_Move -- OS-agnostic subset: routes
/// OpenTK window events into Keys.Event (Key_Event's real entry point,
/// keys.c) and accumulates mouse deltas for ClInput.Move (IN_MouseMove's
/// port, cl_input.c/in_win.c). Raw hardware polling is this class's whole
/// job; everything it hands off to (Keys.cs/ClInput.cs) is otherwise
/// OS-agnostic, per open_tk.md.
/// </summary>
public static class InputHost
{
    private static NativeWindow window = null!;

    // Attach -- IN_Init's event-source half (VID_Init creates the window
    // this subscribes to, so it's called from VidWindow.Init rather than
    // duplicating window creation here).
    public static void Attach(NativeWindow w)
    {
        window = w;
        window.KeyDown += OnKeyDown;
        window.KeyUp += OnKeyUp;
        window.MouseDown += OnMouseDown;
        window.MouseUp += OnMouseUp;
        window.MouseMove += OnMouseMove;
        window.MouseWheel += OnMouseWheel;
        window.FocusedChanged += OnFocusedChanged;

        // Console/chat text comes from the OS text event so non-US layouts
        // type correctly; KeyDown/KeyUp stay physical for bindings.
        window.TextInput += OnTextInput;
        Quake.Engine.Keys.TextInputActive = true;
    }

    private static void OnTextInput(TextInputEventArgs e)
    {
        Quake.Engine.Keys.CharEvent(e.Unicode);
    }

    private static void OnKeyDown(KeyboardKeyEventArgs e)
    {
        // Repeats are forwarded: Keys.Event does the filtering itself
        // (key_repeats), letting only K_BACKSPACE/K_PAUSE autorepeat.
        int keynum = KeyMap.ToKeynum(e.Key);
        if (keynum != -1)
            Quake.Engine.Keys.Event(keynum, true);
    }

    private static void OnKeyUp(KeyboardKeyEventArgs e)
    {
        int keynum = KeyMap.ToKeynum(e.Key);
        if (keynum != -1)
            Quake.Engine.Keys.Event(keynum, false);
    }

    private static void OnMouseDown(MouseButtonEventArgs e)
    {
        int keynum = KeyMap.ToKeynum(e.Button);
        if (keynum != -1)
            Quake.Engine.Keys.Event(keynum, true);
    }

    private static void OnMouseUp(MouseButtonEventArgs e)
    {
        int keynum = KeyMap.ToKeynum(e.Button);
        if (keynum != -1)
            Quake.Engine.Keys.Event(keynum, false);
    }

    // WM_MOUSEWHEEL -- one down+up pair per notch (the wheel has no held state).
    private static void OnMouseWheel(MouseWheelEventArgs e)
    {
        int keynum = KeyMap.ToWheelKeynum(e.OffsetY);
        if (keynum == -1)
            return;

        int notches = Math.Max(1, (int)MathF.Round(MathF.Abs(e.OffsetY)));
        for (int i = 0; i < notches; i++)
        {
            Quake.Engine.Keys.Event(keynum, true);
            Quake.Engine.Keys.Event(keynum, false);
        }
    }

    // vid_win.c AppActivate/ClearAllStates -- Key_ClearStates on activation
    // changes; here also releases held keys (Keys.ReleaseAll) so buttons
    // don't stick when the window loses focus mid-press.
    private static void OnFocusedChanged(FocusedChangedEventArgs e)
    {
        if (!e.IsFocused)
        {
            Quake.Engine.Keys.ReleaseAll();
            pendingDx = pendingDy = 0;
        }
    }

    private static float pendingDx, pendingDy;

    private static void OnMouseMove(MouseMoveEventArgs e)
    {
        // ActiveApp && !Minimized gate (IN_Move's own condition, in_win.c) --
        // this port's stand-in is "only accumulate while focused".
        if (!window.IsFocused)
            return;

        pendingDx += e.DeltaX;
        pendingDy += e.DeltaY;
    }

    /*
    ============
    IN_Commands

    Flushes this frame's queued mouse delta into ClInput's accumulator.
    Called once per frame from Program.cs's main loop, after polling window
    events and before Host.Frame -- the exact position _Host_Frame's own
    comment (Host.cs) says IN_Commands originally ran.
    ============
    */
    public static void Commands()
    {
        if (pendingDx != 0 || pendingDy != 0)
        {
            ClInput.AccumulateMouse(pendingDx, pendingDy);
            pendingDx = 0;
            pendingDy = 0;
        }
    }

    /*
    ===============
    Cursor capture

    Relative/hidden mouse while key_dest == KeyDest.Game, released otherwise --
    call once per frame (or on key_dest transitions) from Program.cs.
    ===============
    */
    public static void UpdateCursorState()
    {
        CursorState desired = Quake.Engine.Keys.key_dest == Quake.Engine.KeyDest.Game
            ? CursorState.Grabbed
            : CursorState.Normal;

        // The setter calls into GLFW every time; only touch it on a change.
        if (window.CursorState != desired)
            window.CursorState = desired;
    }
}
