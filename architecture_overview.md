# Quake Engine Architecture Overview

This document describes the original WinQuake engine's design, the changes made while
porting it to .NET/C#, and a lookup table mapping each ported `.cs` file back to the
original `.c`/`.h` source it came from.

Original source: Copyright (C) 1996-1997 Id Software, Inc. (GNU General Public License).

The original source is not included in this repository. References below link to:

- **Quake / WinQuake** — <https://github.com/id-Software/Quake>
- **Mark V Quake** — <https://quakeone.com/markv/> (source mirror:
  <https://github.com/QuakeEngines/Mark_V-clone>)

---

## 1. Quake Engine Design Summary

Quake is split into a few large subsystems that communicate through global state and a
small number of well-defined entry points (`Host_Frame`, `SV_Frame`, `CL_ReadFromServer`,
`R_RenderView`, etc.). At a high level:

- **Host (`host.c`/[Host.cs](Quake.Engine/Core/Host.cs))** — the top-level driver. Runs the
  main loop (`Host_Frame`), advancing time, pumping input, running the client and/or
  server frame, and issuing a render frame. A single executable can run client-only,
  server-only (dedicated), or both (listen server) at once, since client and server code
  share one process and communicate through in-memory network loopback
  ([NetLoop.cs](Quake.Engine/Network/NetLoop.cs)) rather than sockets when not networked.

- **Server (`sv_*.c`/[Sv.cs](Quake.Engine/Server/Sv.cs), [SvMove.cs](Quake.Engine/Server/SvMove.cs),
  [SvPhys.cs](Quake.Engine/Server/SvPhys.cs), [SvUser.cs](Quake.Engine/Server/SvUser.cs))** — owns
  authoritative game state: entities (edicts), physics/movement, and the QuakeC ("progs")
  virtual machine that runs game logic. Every server tick it moves entities, runs think
  functions, and serializes a delta of relevant state to each connected client.

- **QuakeC VM (`pr_*.c`/[Progs.cs](Quake.Engine/Progs/Progs.cs), [PrCmds.cs](Quake.Engine/Progs/PrCmds.cs))**
  — a small bytecode interpreter that executes the game's `.dat` progs file (compiled
  QuakeC). Game rules (monsters, weapons, doors, etc.) are data, not engine code; the
  engine just supplies builtins (`PF_*` in `pr_cmds.c`) that the game script calls into.

- **Client (`cl_*.c`/[Cl.cs](Quake.Engine/Client/Cl.cs), [ClParse.cs](Quake.Engine/Client/ClParse.cs),
  [ClInput.cs](Quake.Engine/Client/ClInput.cs), [ClMain.cs](Quake.Engine/Client/ClMain.cs),
  [ClTEnt.cs](Quake.Engine/Client/ClTEnt.cs), [ClDemo.cs](Quake.Engine/Client/ClDemo.cs))** — receives
  the server's state deltas, predicts local movement between updates, and drives the
  view/camera and temporary entities (explosions, particles, beams).

- **Networking (`net_*.c`/[Net.cs](Quake.Engine/Network/Net.cs), [NetDgrm.cs](Quake.Engine/Network/NetDgrm.cs),
  [NetIp.cs](Quake.Engine/Network/NetIp.cs), [NetLoop.cs](Quake.Engine/Network/NetLoop.cs))** — a
  driver-based abstraction (`net_landriver_t`/`net_driver_t`) over transports: loopback
  (single player), IPX (dropped), and UDP/IP. Messages are packed with
  [Msg.cs](Quake.Engine/Core/Msg.cs)'s `MSG_*` helpers and reliable delivery is layered on top
  in `net_dgrm.c`.

- **Renderer (`r_*.c` "refresh" layer + `d_*.c` software rasterizer,
  [RMain.cs](Quake.Engine/Render/RMain.cs), [RBsp.cs](Quake.Engine/Render/RBsp.cs),
  [REdge.cs](Quake.Engine/Render/REdge.cs), [RAlias.cs](Quake.Engine/Render/RAlias.cs),
  [DEdge.cs](Quake.Engine/Rasterizer/DEdge.cs), [DPolyse.cs](Quake.Engine/Rasterizer/DPolyse.cs),
  [DScan.cs](Quake.Engine/Rasterizer/DScan.cs), [DSurf.cs](Quake.Engine/Rasterizer/DSurf.cs))** — a pure
  software renderer (this port targets the software path, not `gl_*.c`/OpenGL). The
  `r_*` "refresh" layer walks the BSP tree, clips and projects polygons, builds an
  edge/span list; the `d_*` "draw" layer scan-converts spans into the 8-bit framebuffer
  using affine texture mapping, with a dedicated fixed-point path for near-vertical
  polygon edges ([DZpoint.cs](Quake.Engine/DZpoint.cs)) and dedicated sprite/particle
  rasterizers ([DSprite.cs](Quake.Engine/Rasterizer/DSprite.cs), [RPart.cs](Quake.Engine/Render/RPart.cs)).
  The original x86 assembly fast paths (`d_*a.s`, `r_*a.s`) were not ported — only their
  C fallback implementations were, per the porting rules below.

- **World/BSP (`world.c`/[World.cs](Quake.Engine/Server/World.cs), `model.c`/
  [Model.cs](Quake.Engine/Assets/Model.cs), `bspfile.h`/[BspFile.cs](Quake.Engine/Assets/BspFile.cs))**
  — the static level geometry (BSP tree, planes, surfaces, visibility PVS) plus loaded
  models (brush models = the level and movable brush entities; alias models = MDL
  skeletal-less vertex-animated models; sprites = billboards).

- **Memory management (`zone.c`/[Memory/Zone.cs](Quake.Engine/Memory/Zone.cs),
  [Memory/Hunk.cs](Quake.Engine/Memory/Hunk.cs), [Memory/Cache.cs](Quake.Engine/Memory/Cache.cs))**
  — three custom allocators instead of malloc/free: the **hunk** is a big stack-like
  arena freed in LIFO order (used for level data that lives for a whole map); the
  **zone** is a general-purpose heap with free-list coalescing (small, long-lived
  allocations); the **cache** is an LRU-evictable region carved out of the hunk (used for
  models/sounds that can be discarded and reloaded under memory pressure).

- **Console/Cvar/Cmd (`console.c`, `cvar.c`, `cmd.c`/[Console.cs](Quake.Engine/UI/Console.cs),
  [Cvar.cs](Quake.Engine/Core/Cvar.cs), [Cmd.cs](Quake.Engine/Core/Cmd.cs))** — the in-game
  console, the `cvar` variable system (settings, many networked/archived), and the
  command system (`cmd` text buffer, aliases, key bindings) that ties console input,
  config files, and the menu together.

- **Sound (`snd_*.c`/[SndDma.cs](Quake.Engine/Sound/SndDma.cs), [SndMix.cs](Quake.Engine/Sound/SndMix.cs),
  [SndHost.cs](Quake.Engine/Sound/SndHost.cs), `cd_*.c`/[CdAudio.cs](Quake.Engine/Sound/CdAudio.cs))**
  — a software mixer that mixes active `channel_t` voices into a ring buffer normally
  handed to a DirectSound/OSS/ALSA DMA buffer; CD audio for the redbook soundtrack.

- **Video/Input (`vid_*.c`, `in_*.c`, `d_iface.h`)** — thin OS-specific layers that give
  the renderer a framebuffer to draw into and feed raw keyboard/mouse events into the
  input system; these are the layers most rewritten for the .NET port (see below).

---

## 2. Changes Made for the .NET Port

The port is split into three projects: **`Quake.Engine`** (the platform-agnostic engine,
this document's main subject), **`Quake.Desktop`** (the thin OS/windowing/input host),
and **`Quake.UnitTests`** (behavioral tests asserting parity with the original C).

Key changes from the original WinQuake source:

- **Software renderer kept, OS layer replaced.** `r_*.c`/`d_*.c` rasterization logic is
  ported essentially 1:1 (same fixed-point math, same span/edge structures). What changed
  is how the resulting framebuffer reaches the screen: instead of `vid_win.c`'s GDI/
  DirectDraw surface, the engine renders into an 8-bit indexed CPU buffer, expands it to
  RGBA32 via the palette on the CPU each frame, and uploads that as a texture that
  [Quake.Desktop/VidWindow.cs](Quake.Desktop/VidWindow.cs) draws with **OpenTK**
  (`OpenTK.Windowing.Desktop`/OpenGL) instead of native Win32/DirectX APIs.
- **Input** (`in_win.c`) is replaced by [Quake.Desktop/InputHost.cs](Quake.Desktop/InputHost.cs)
  and [KeyMap.cs](Quake.Desktop/KeyMap.cs), translating OpenTK's cross-platform keyboard/
  mouse events into Quake's original `K_*` key codes and input event queue, so the engine
  side ([Keys.cs](Quake.Engine/Client/Keys.cs), [ClInput.cs](Quake.Engine/Client/ClInput.cs)) is
  unchanged.
- **Audio** (`snd_win.c`'s DirectSound + `cd_win.c`'s MCI redbook audio) is replaced with
  **OpenTK's OpenAL bindings**. [SndHost.cs](Quake.Engine/Sound/SndHost.cs) owns the OpenAL
  device/context and streams the mixer's output (still produced by the original
  `snd_dma.c`/`snd_mix.c` mixing logic, unchanged) into OpenAL buffers instead of a
  DirectSound DMA buffer. CdAudio (`cd_win.c`) is similarly reduced to its OS-agnostic
  shape in [CdAudio.cs](Quake.Engine/Sound/CdAudio.cs).
- **Networking, file I/O, threading**: raw Winsock (`net_win.c`, `net_wins.c`,
  `net_wipx.c`) and DOS-era file/process APIs (`sys_win.c`, `conproc.c`) are replaced with
  .NET's `System.Net.Sockets`, `System.IO`, and `System.Threading` equivalents, keeping
  the same driver abstractions (`net_driver_t`/`net_landriver_t`) so the rest of the
  networking code (`net_dgrm.c`, `net_main.c`) didn't need structural changes.
- **Memory**: the hunk/zone/cache allocators are reimplemented in
  [Memory/Hunk.cs](Quake.Engine/Memory/Hunk.cs), [Memory/Zone.cs](Quake.Engine/Memory/Zone.cs),
  and [Memory/Cache.cs](Quake.Engine/Memory/Cache.cs) on top of
  `NativeMemory.AllocZeroed`/`NativeMemory.Alloc`, so pointers taken into these regions
  (e.g. `Link_t` chains, cached model data) remain stable across GC — the managed heap is
  deliberately avoided for anything addressed by a long-lived raw pointer, matching
  patterns like `Model.mod_known` and `Sv.clients`.
- **C idioms preserved deliberately**: `unsafe` pointers (not `ref`/`Span<T>`) are used
  throughout to mirror the original C pointer arithmetic exactly; structs use
  `[StructLayout(LayoutKind.Sequential)]` to match original memory layout; original
  struct/function names and comments are preserved as C# comments alongside the ported
  code, and static classes carry a doc comment naming the original file(s) they
  aggregate (see the lookup table below).
- **x86 assembly dropped, C fallback ported.** The original shipped hand-written x86
  assembly fast paths for the innermost rasterizer/mixer loops (`d_polysa.s`, `d_scana.s`,
  `r_edgea.s`, `snd_mixa.s`, etc.). Only the corresponding portable C implementations
  (`d_polyse.c`, `d_scan.c`, `r_edge.c`, `snd_mix.c`, ...) were ported, since the target
  isn't x86-specific.
- **Mark V Quake enhancements copied in.** Beyond the WinQuake baseline, some engine
  behavior was ported from the [Mark V Quake](https://quakeone.com/markv/) source ([`Mark_V/`](https://github.com/QuakeEngines/Mark_V-clone/tree/HEAD/Mark_V),
  itself FitzQuake-derived) rather than id's original code:
  - **Raised engine limits.** Values follow Mark V's `quakedef.h`/`bspfile.h`, not WinQuake's:
    2048 models/sounds, 32000 edicts, 128 dlights, 4096 visible edicts, 8192 efrags,
    512 static entities, 256 temp entities, 32 beams, 32 leaves per entity,
    `MAX_MAP_LEAFS` of 131071, 64 KB message buffers (`MAX_MSGLEN`/`NET_MAXMESSAGE`),
    a 256 KB command buffer, and a datagram size chosen per protocol as Mark V does
    (`Sv.host_protocol_datagram_maxsize`: 1024 for protocol 15, 65527 for single-player
    protocol 666, 1400 otherwise). Protocol 15 keeps WinQuake's wire limits
    (`MAX_P15_*` in [Protocol.cs](Quake.Engine/Network/Protocol.cs)). The software renderer's
    resolution caps are also raised, and its surface-extents limit is still 256.
  - **Frame interpolation (lerping).** Animation and movement interpolation between server
    updates (`LerpFlags`/`LerpData_t` in [RLerp.cs](Quake.Engine/Render/RLerp.cs), the
    `Lerp*` fields of `Entity_t`, and the lerp handling in `ClParse`/`RAlias`) comes from
    Mark V's `johnfitz -- lerping` code. `LerpFlags.FINISH` (server-driven lerp time) is
    parsed but not yet consumed by the renderer.
  - **`PROTOCOL_FITZQUAKE` (666).** The extended network protocol (large model/sound/frame
    indices, entity alpha, `svc_spawnbaseline2` etc., 16-bit `clc_move` angles) is also
    taken from Mark V. `sv_protocol` selects 15 or 666 and defaults to 666.
- **Unit tests** (`Quake.UnitTests`) were added per file/subsystem where behavior could
  be pinned down (math, parsing, protocol/message packing, BSP/model loading, memory
  managers, etc.), asserting numeric/byte-for-byte parity with the original algorithms.

---

## 3. File Lookup: `.cs` → original `.c`/`.h`

Original sources live under [`WinQuake/`](https://github.com/id-Software/Quake/tree/HEAD/WinQuake)
in the id Software Quake repository; code taken from Mark V Quake lives under
[`Mark_V/`](https://github.com/QuakeEngines/Mark_V-clone/tree/HEAD/Mark_V) in the Mark V
mirror. `.cs` paths below are relative to this file.

Quake.Engine folders group files by original subsystem (C file prefix): `Core/`
(host, cmd, cvar, common, sys), `Client/` (cl_*, chase, view, keys), `Server/` (sv_*,
world), `Progs/` (pr_*), `Network/` (net_*), `Sound/` (snd_*, cd_*), `Assets/` (model,
bspfile, wad), `Render/` (r_*), `Rasterizer/` (d_*), `UI/` (draw, screen, sbar, menu,
console), `Video/` (vid_*), `Memory/` (zone), plus `Enums/`, `Helpers/` and `Models/<subsystem>/`
for struct definitions. The folders don't set namespaces: everything stays in the flat
`Quake.Engine` namespace, the same way the C code shares one global namespace. The one
exception is `Quake.Engine.Memory`.

### Quake.Engine

| .cs file | Original source(s) | Notes |
|---|---|---|
| [AliasModel.cs](Quake.Engine/Assets/AliasModel.cs) | [model.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/model.c), [r_local.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_local.h), [asm_draw.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/asm_draw.h) | MDL (alias model) structures and loading |
| [BspFile.cs](Quake.Engine/Assets/BspFile.cs) | [bspfile.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/bspfile.h), [asm_i386.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/asm_i386.h) | On-disk BSP format structs |
| [CdAudio.cs](Quake.Engine/Sound/CdAudio.cs) | [cd_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cd_win.c), [cd_linux.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cd_linux.c), [cd_null.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cd_null.c) | Redbook CD audio, OS-agnostic shell |
| [Chase.cs](Quake.Engine/Client/Chase.cs) | [chase.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/chase.c) | Chase-cam |
| [Cl.cs](Quake.Engine/Client/Cl.cs) | [client.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/client.h), [cl_main.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_main.c), [cl_input.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_input.c), [cl_parse.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_parse.c), [cl_demo.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_demo.c) | Core client state (`client_state_t` etc.) |
| [ClDemo.cs](Quake.Engine/Client/ClDemo.cs) | [cl_demo.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_demo.c) | Demo record/playback |
| [ClInput.cs](Quake.Engine/Client/ClInput.cs) | [cl_input.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_input.c) | Movement/usercmd input building |
| [ClMain.cs](Quake.Engine/Client/ClMain.cs) | [cl_main.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_main.c) | Client frame/connect/disconnect |
| [ClParse.cs](Quake.Engine/Client/ClParse.cs) | [cl_parse.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_parse.c) | Server message parsing |
| [ClTEnt.cs](Quake.Engine/Client/ClTEnt.cs) | [cl_tent.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_tent.c) | Temp entities (explosions, beams, muzzle flashes) |
| [Cmd.cs](Quake.Engine/Core/Cmd.cs) | [cmd.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cmd.c) | Console command buffer/dispatch/aliases |
| [Common.cs](Quake.Engine/Core/Common.cs) | [common.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/common.c) | Shared utilities (COM_*, argv, byte order) |
| [Console.cs](Quake.Engine/UI/Console.cs) | [console.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/console.c) | In-game/dev console |
| [Crc.cs](Quake.Engine/Core/Crc.cs) | [crc.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/crc.c) | CRC-16 checksum |
| [Cvar.cs](Quake.Engine/Core/Cvar.cs) | [cvar.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cvar.c) | Console variable system |
| [DEdge.cs](Quake.Engine/Rasterizer/DEdge.cs) | [d_edge.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_edge.c) | Edge table management for scan conversion |
| [DFill.cs](Quake.Engine/DFill.cs) | [d_fill.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_fill.c) | Solid rectangle fill (console background, etc.) |
| [DIface.cs](Quake.Engine/Rasterizer/DIface.cs) | [d_iface.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_iface.h), [r_shared.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_shared.h) | Interface between refresh (`r_*`) and draw (`d_*`) layers |
| [DInit.cs](Quake.Engine/Rasterizer/DInit.cs) | [d_init.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_init.c) | Software renderer init/resolution setup |
| [DLocal.cs](Quake.Engine/Rasterizer/DLocal.cs) | [d_local.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_local.h) | Shared internal draw-layer declarations |
| [DModech.cs](Quake.Engine/Rasterizer/DModech.cs) | [d_modech.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_modech.c) | Render mode change checks |
| [DPart.cs](Quake.Engine/Rasterizer/DPart.cs) | [d_part.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_part.c) | Particle rasterization |
| [DPolyse.cs](Quake.Engine/Rasterizer/DPolyse.cs) | [d_polyse.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_polyse.c), [adivtab.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/adivtab.h) | Polygon (alias model) scan conversion |
| [Draw.cs](Quake.Engine/UI/Draw.cs) | [draw.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/draw.c) | 2D UI drawing (pics, conchars, crosshair) |
| [DScan.cs](Quake.Engine/Rasterizer/DScan.cs) | [d_scan.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_scan.c) | Span scan-conversion (world surfaces) |
| [DSprite.cs](Quake.Engine/Rasterizer/DSprite.cs) | [d_sprite.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_sprite.c) | Sprite rasterization |
| [DSurf.cs](Quake.Engine/Rasterizer/DSurf.cs) | [d_surf.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_surf.c) | World surface (lightmap) drawing |
| [DZpoint.cs](Quake.Engine/DZpoint.cs) | [d_zpoint.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/d_zpoint.c) | Near-vertical edge fixed-point handling |
| [EdictSerialization.cs](Quake.Engine/Progs/EdictSerialization.cs) | [pr_edict.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/pr_edict.c), [pr_comp.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/pr_comp.h) | Save/load of QuakeC entity field data |
| [Host.cs](Quake.Engine/Core/Host.cs) | [host.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/host.c) | Main loop, frame driver, init/shutdown |
| [HostCmd.cs](Quake.Engine/Core/HostCmd.cs) | [host_cmd.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/host_cmd.c) | Host console commands (map, kill, give, etc.) |
| [HostParms.cs](Quake.Engine/Core/HostParms.cs) | [host.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/host.c) (`quakeparms_t`) | Startup parameters |
| [Keys.cs](Quake.Engine/Client/Keys.cs) | [keys.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/keys.c), [keys.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/keys.h) | Key binding/event dispatch (`K_*` codes) |
| [Link.cs](Quake.Engine/Core/Link.cs) | [common.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/common.c) (`link_t`) | Doubly-linked list helper used by entity/edict lists |
| [MathLib.cs](Quake.Engine/Core/MathLib.cs) | [mathlib.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/mathlib.c), [mathlib.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/mathlib.h) | Vector/matrix/trig math |
| [Menu.cs](Quake.Engine/UI/Menu.cs) | [menu.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/menu.c) | In-game menu system |
| [Model.cs](Quake.Engine/Assets/Model.cs) | [model.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/model.c), [model.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/model.h), [modelgen.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/modelgen.h), [spritegn.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/spritegn.h) | Model loading (brush/alias/sprite), `mod_known` |
| [Msg.cs](Quake.Engine/Core/Msg.cs) | [common.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/common.c) (`MSG_*`) | Network message read/write helpers |
| [Net.cs](Quake.Engine/Network/Net.cs) | [net.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net.h), [net_main.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_main.c) | Network driver abstraction, connection table |
| [NetDgrm.cs](Quake.Engine/Network/NetDgrm.cs) | [net_dgrm.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_dgrm.c), [net_dgrm.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_dgrm.h) | Reliable datagram protocol layer |
| [NetIp.cs](Quake.Engine/Network/NetIp.cs) | [net_udp.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_udp.c), [net_bsd.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_bsd.c), [net_wins.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_wins.c) | UDP/IP transport (.NET `Socket`-based) |
| [NetLoop.cs](Quake.Engine/Network/NetLoop.cs) | [net_loop.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_loop.c), [net_loop.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/net_loop.h) | Loopback transport for single player |
| [PrCmds.cs](Quake.Engine/Progs/PrCmds.cs) | [pr_cmds.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/pr_cmds.c) | QuakeC builtin functions (`PF_*`) |
| [Progs.cs](Quake.Engine/Progs/Progs.cs) | [pr_exec.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/pr_exec.c), [pr_edict.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/pr_edict.c), [progs.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/progs.h), [progdefs.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/progdefs.h) | QuakeC VM interpreter/edict management |
| [Protocol.cs](Quake.Engine/Network/Protocol.cs) | [protocol.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/protocol.h), Mark V [protocol.h](https://github.com/QuakeEngines/Mark_V-clone/blob/HEAD/Mark_V/protocol.h) | Network protocol message/entity flag constants; `PROTOCOL_FITZQUAKE` additions are from Mark V |
| [Q.cs](Quake.Engine/Core/Q.cs) | [common.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/common.c) (`Q_*` string/memory helpers) | Portable libc-style helpers |
| [RAclip.cs](Quake.Engine/Render/RAclip.cs) | [r_aclip.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_aclip.c) | Alias model polygon clipping |
| [RAlias.cs](Quake.Engine/Render/RAlias.cs) | [r_alias.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_alias.c) | Alias model setup/transform for rendering |
| [RBsp.cs](Quake.Engine/Render/RBsp.cs) | [r_bsp.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_bsp.c) | BSP tree traversal for rendering (world surfaces) |
| [RDraw.cs](Quake.Engine/Render/RDraw.cs) | [r_draw.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_draw.c) | Refresh-layer draw dispatch |
| [REdge.cs](Quake.Engine/Render/REdge.cs) | [r_edge.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_edge.c) | Edge-based scan conversion setup (refresh side) |
| [REfrag.cs](Quake.Engine/Render/REfrag.cs) | [r_efrag.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_efrag.c) | Entity fragment linking into world leaves |
| [RLerp.cs](Quake.Engine/Render/RLerp.cs) | Mark V [render.h](https://github.com/QuakeEngines/Mark_V-clone/blob/HEAD/Mark_V/render.h), [model.h](https://github.com/QuakeEngines/Mark_V-clone/blob/HEAD/Mark_V/model.h) (`lerpflags`, `lerpdata_t`) | Frame interpolation flags/data — not in WinQuake |
| [RLight.cs](Quake.Engine/Render/RLight.cs) | [r_light.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_light.c) | Dynamic lighting, lightmaps |
| [RLocal.cs](Quake.Engine/Render/RLocal.cs) | [r_local.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_local.h) | Shared internal refresh-layer declarations |
| [RMain.cs](Quake.Engine/Render/RMain.cs) | [r_main.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_main.c), [render.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/render.h) | Top-level `R_RenderView`, frustum/visibility |
| [RMisc.cs](Quake.Engine/Render/RMisc.cs) | [r_misc.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_misc.c) | Misc refresh utilities/console commands |
| [RPart.cs](Quake.Engine/Render/RPart.cs) | [r_part.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_part.c) | Particle system simulation |
| [RShared.cs](Quake.Engine/Render/RShared.cs) | [r_shared.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_shared.h) | Types shared between refresh and draw layers |
| [RSky.cs](Quake.Engine/Render/RSky.cs) | [r_sky.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_sky.c) | Sky surface warp/rendering |
| [RSprite.cs](Quake.Engine/Render/RSprite.cs) | [r_sprite.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_sprite.c) | Sprite setup for rendering |
| [RSurf.cs](Quake.Engine/Render/RSurf.cs) | [r_surf.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/r_surf.c) | World surface rendering (span building) |
| [SaveGame.cs](Quake.Engine/Server/SaveGame.cs) | [host_cmd.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/host_cmd.c) (`Host_Savegame_f`/`Host_Loadgame_f`) | Save/load game state |
| [Sbar.cs](Quake.Engine/UI/Sbar.cs) | [sbar.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sbar.c), [sbar.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sbar.h) | Status bar / HUD |
| [Screen.cs](Quake.Engine/UI/Screen.cs) | [screen.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/screen.c) | Screen refresh orchestration, console/menu overlay |
| [SizeBuf.cs](Quake.Engine/Core/SizeBuf.cs) | [common.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/common.c) (`sizebuf_t`) | Growable message buffer |
| [SndDma.cs](Quake.Engine/Sound/SndDma.cs) | [snd_dma.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/snd_dma.c) | Sound system init, channel management |
| [SndHost.cs](Quake.Engine/Sound/SndHost.cs) | [snd_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/snd_win.c) | OS sound output — replaced with OpenTK/OpenAL |
| [SndMix.cs](Quake.Engine/Sound/SndMix.cs) | [snd_mix.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/snd_mix.c) | Channel mixing into the output buffer |
| [SpriteModel.cs](Quake.Engine/Assets/SpriteModel.cs) | [model.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/model.c) (sprite loading) | Sprite model structures/loading |
| [Sv.cs](Quake.Engine/Server/Sv.cs) | [server.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/server.h), [sv_main.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sv_main.c) | Server state (`server_t`/`server_static_t`), main loop |
| [SvMove.cs](Quake.Engine/Server/SvMove.cs) | [sv_move.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sv_move.c) | Monster/entity movement helpers |
| [SvPhys.cs](Quake.Engine/Server/SvPhys.cs) | [sv_phys.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sv_phys.c) | Physics simulation (gravity, pushmove, toss/step) |
| [SvUser.cs](Quake.Engine/Server/SvUser.cs) | [sv_user.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sv_user.c) | Per-client command execution |
| [Sys.cs](Quake.Engine/Core/Sys.cs) | [sys_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sys_win.c) | System layer (timing, error handling) — made OS-agnostic |
| [Vid.cs](Quake.Engine/Video/Vid.cs) | [vid.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/vid.h), [vid_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/vid_win.c) | Video mode / framebuffer abstraction, palette |
| [View.cs](Quake.Engine/Client/View.cs) | [view.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/view.c) | Player view (bob, kick, gamma, camera angles) |
| [Wad.cs](Quake.Engine/Assets/Wad.cs) | [wad.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/wad.c), [wad.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/wad.h) | WAD archive (gfx.wad) loading |
| [Wav.cs](Quake.Engine/Sound/Wav.cs) | [snd_mem.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/snd_mem.c), [sound.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sound.h) | WAV file loading into sound cache |
| [World.cs](Quake.Engine/Server/World.cs) | [world.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/world.c), [world.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/world.h) | Collision/hull tracing, entity area grid |

#### Quake.Engine/Memory

| .cs file | Original source(s) | Notes |
|---|---|---|
| [Memory/Zone.cs](Quake.Engine/Memory/Zone.cs) | [zone.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.c), [zone.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.h) | `Z_Malloc`/`Z_Free` general heap allocator |
| [Memory/Hunk.cs](Quake.Engine/Memory/Hunk.cs) | [zone.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.c), [zone.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.h) | `Hunk_Alloc`/`Hunk_FreeToLowMark` stack allocator |
| [Memory/Cache.cs](Quake.Engine/Memory/Cache.cs) | [zone.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.c), [zone.h](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.h) | `Cache_Alloc`/LRU-evictable region |
| [Memory/CStr.cs](Quake.Engine/Memory/CStr.cs) | [common.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/common.c) | Native/C-string helpers for interop with `NativeMemory`-backed buffers |
| [Memory/MemoryManager.cs](Quake.Engine/Memory/MemoryManager.cs) | [zone.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/zone.c), [host.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/host.c), [sys_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sys_win.c) | `Memory_Init`-equivalent startup wiring of hunk/zone/cache |

### Quake.Desktop

The host executable — replaces the OS-specific `sys_win.c`/`vid_win.c`/`in_win.c` glue
with an OpenTK-based windowing/input/present loop.

| .cs file | Original source(s) | Notes |
|---|---|---|
| [Program.cs](Quake.Desktop/Program.cs) | [sys_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/sys_win.c) (`WinMain`) | Executable entry point, engine bootstrap |
| [VidWindow.cs](Quake.Desktop/VidWindow.cs) | [vid_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/vid_win.c), [gl_vidnt.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/gl_vidnt.c) | OpenTK window; palette→RGBA32 expansion and texture present |
| [VidMenu.cs](Quake.Desktop/VidMenu.cs) | [vid_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/vid_win.c) (video options menu) | Video mode selection menu glue |
| [InputHost.cs](Quake.Desktop/InputHost.cs) | [in_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/in_win.c), [cl_input.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/cl_input.c) | OpenTK keyboard/mouse → Quake input event queue |
| [KeyMap.cs](Quake.Desktop/KeyMap.cs) | [vid_win.c](https://github.com/id-Software/Quake/blob/HEAD/WinQuake/vid_win.c) (Win32 VK_* → `K_*` mapping) | OpenTK `Keys` → Quake `K_*` key code mapping |

---

## Notes / Caveats

- Files under `WinQuake/gl_*.c` (the OpenGL renderer) and `*a.s` (x86 assembly) were
  **not** ported — this project targets the software renderer only, per project rules.
- Several `.cs` files intentionally aggregate multiple original files where the original
  split was purely a compilation-unit/platform concern (e.g. `net_*.c`'s per-OS driver
  variants collapse into fewer `.cs` files backed by .NET's own cross-platform sockets).
- The lookup table reflects references found in each file's own header/inline comments;
  see each file's top-of-file comment for the authoritative original-source note (rule 4/5
  of the project's porting guidelines).
