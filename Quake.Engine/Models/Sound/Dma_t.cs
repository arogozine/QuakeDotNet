// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quake.Engine.Memory;

namespace Quake.Engine;

// dma_t
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Dma_t
{
    public bool GameAlive;
    public bool SoundAlive;
    public bool SplitBuffer;
    public int Channels;
    public int Samples;           // mono samples in buffer
    public int SubmissionChunk;   // don't mix less than this #
    public int SamplePos;         // in mono samples
    public int SampleBits;
    public int Speed;
    public byte* Buffer;
}
