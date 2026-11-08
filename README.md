# QuakeDotNet

A port of the WinQuake engine to .NET C#, using OpenTK/OpenAL for presentation and audio.

See [architecture_overview.md](architecture_overview.md) for the code layout.

## Attribution and Legal

QuakeDotNet is a derivative work of the Quake source code released by id Software, and
it consults the Mark V engine for reference. It is therefore distributed under the terms
of the **GNU General Public License, version 2 or (at your option) any later version**
(GPL-2.0-or-later); see [LICENSE](LICENSE). If you
distribute binaries of this project, you must make the corresponding source code available
under the same license. Copyright (C) 2026 Alexandre Rogozine for the .NET port. Third-party
packages are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The original sources are not included in this repository; they are linked below.

### Quake

- Original work: Quake / WinQuake / GLQuake / QuakeWorld source code, available at
  <https://github.com/id-Software/Quake>.
- Copyright (C) 1996-1997 Id Software, Inc.
- Released by id Software under the GNU General Public License, version 2 (see
  [`gnu.txt`](https://github.com/id-Software/Quake/blob/HEAD/gnu.txt) and
  [`readme.txt`](https://github.com/id-Software/Quake/blob/HEAD/readme.txt)).
- The ported files in `Quake.Engine` each reference the name and file of the original
  C code they are derived from.

### Mark V

- Original work: Mark V Quake engine (v1099 Revision 4), a derivative of id Software's
  Quake engine. Official site: <https://quakeone.com/markv/>. Unofficial source mirror:
  <https://github.com/QuakeEngines/Mark_V-clone>.
- Copyright (C) 1996-2001 Id Software, Inc.; Copyright (C) 2002-2014 John Fitzgibbons
  and others; Copyright (C) 2009-2018 Baker and others; Copyright (C) 2007-2008
  Kristian Duske (and other contributors credited in the individual source files).
- Licensed under the GNU General Public License, version 2 (see
  [`gnu.txt`](https://github.com/QuakeEngines/Mark_V-clone/blob/HEAD/gnu.txt)). Per-file
  copyright notices in that source take precedence over the summary above.

### Game data

The Quake game data files (`pak0.pak`, `pak1.pak`, maps, models, sounds, textures, etc.)
remain copyrighted by id Software under their original license terms. They are **not**
covered by the GPL, are not included in this repository, and may not be redistributed.
You must provide your own legally obtained copy of the game data.

### Trademarks

Quake is a registered trademark of id Software LLC / ZeniMax Media Inc. This project is
not affiliated with or endorsed by id Software, ZeniMax Media, Microsoft, or the Mark V
authors.

### Warranty

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
See the GNU General Public License for more details.
