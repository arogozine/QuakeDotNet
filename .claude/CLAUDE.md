# Project Summary

Quake (WinQuake) source (with improvements from Mark V Quake) converted to .NET C#.

# .NET Port Directory

Repository root — `QuakeDotNet.slnx` solution with the `Quake.Engine`, `Quake.UnitTests`, `Quake.Benchmarks` and `Quake.Desktop` projects (unsafe blocks enabled).

# Rules
1. Prefer pointers and unsafe code.
2. Keep original C behavior where possible.
3. Ported code must reference name and file of the original in a comment.
4. Add unit tests where possible to ensure behavior matches original code.
5. OS agnostic code only. OpenTK/OpenAL for presentation and audio.
6. Files ported from the C++ code should have the original copyright - Copyright (C) 1996-1997 Id Software, Inc. (GNU General Public License).
7. Code that doesn't follow these rules should be justified.
8. Prefer NativeMemory allocation for pointer stability.