// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.CompilerServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class SndMixTests
{
    private static Dma_t _shm;

    [TestInitialize]
    public void Setup()
    {
        new Span<PortableSamplePair_t>(SndMix.paintbuffer, SndMix.PAINTBUFFER_SIZE).Clear();
        SndDma.paintedtime = 0;
        SndDma.volume->Value = 1.0f; // vol scale = 256 -> (sample*256)>>8 == sample
        _shm = default;
        SndDma.shm = (Dma_t*)Unsafe.AsPointer(ref _shm);
    }

    [TestMethod]
    public void InitScaletable_ScalesSignedByteByRowIndexTimesEight()
    {
        SndMix.InitScaletable();

        // row 2, byte 200 (== sbyte -56): -56 * 2 * 8
        Assert.AreEqual(-56 * 2 * 8, SndMix.snd_scaletable[2 * SndMix.SND_SCALETABLE_COLS + 200]);
        // row 4, byte 127 (== sbyte 127): 127 * 4 * 8
        Assert.AreEqual(127 * 4 * 8, SndMix.snd_scaletable[4 * SndMix.SND_SCALETABLE_COLS + 127]);
        // row 0 is always silent regardless of sample value
        Assert.AreEqual(0, SndMix.snd_scaletable[0 * SndMix.SND_SCALETABLE_COLS + 250]);
    }

    [TestMethod]
    public void PaintChannelFrom8_ScalesEachSampleByLeftAndRightVolumeRows()
    {
        SndMix.InitScaletable();

        int size = sizeof(SfxCache_t) + 3;
        byte* raw = stackalloc byte[size];
        SfxCache_t* sc = (SfxCache_t*)raw;
        *sc = default;
        byte* data = (byte*)(sc + 1);
        data[0] = 192; // sbyte -64

        Channel_t ch = default;
        ch.LeftVol = 64;  // row 64>>3 = 8
        ch.RightVol = 32; // row 32>>3 = 4
        ch.Pos = 0;

        SndMix.PaintChannelFrom8(&ch, sc, 1);

        int expectedLeft = -64 * 8 * 8;
        int expectedRight = -64 * 4 * 8;
        Assert.AreEqual(expectedLeft, SndMix.paintbuffer[0].Left);
        Assert.AreEqual(expectedRight, SndMix.paintbuffer[0].Right);
        Assert.AreEqual(1, ch.Pos);
    }

    [TestMethod]
    public void PaintChannelFrom8_ClampsVolumesAbove255ToRow31()
    {
        SndMix.InitScaletable();

        int size = sizeof(SfxCache_t) + 1;
        byte* raw = stackalloc byte[size];
        SfxCache_t* sc = (SfxCache_t*)raw;
        *sc = default;
        ((byte*)(sc + 1))[0] = 1; // sbyte 1

        Channel_t ch = default;
        ch.LeftVol = 400;  // clamps to 255 -> row 31
        ch.RightVol = 300; // clamps to 255 -> row 31

        SndMix.PaintChannelFrom8(&ch, sc, 1);

        Assert.AreEqual(255, ch.LeftVol);
        Assert.AreEqual(255, ch.RightVol);
        Assert.AreEqual(1 * 31 * 8, SndMix.paintbuffer[0].Left);
        Assert.AreEqual(1 * 31 * 8, SndMix.paintbuffer[0].Right);
    }

    [TestMethod]
    public void PaintChannelFrom16_ScalesSignedSamplesByVolumeAndShifts()
    {
        int count = 2;
        int size = sizeof(SfxCache_t) + count * sizeof(short);
        byte* raw = stackalloc byte[size];
        SfxCache_t* sc = (SfxCache_t*)raw;
        *sc = default;
        short* data = (short*)(sc + 1);
        data[0] = 1000;
        data[1] = -2000;

        Channel_t ch = default;
        ch.LeftVol = 100;
        ch.RightVol = 50;
        ch.Pos = 0;

        SndMix.PaintChannelFrom16(&ch, sc, count);

        Assert.AreEqual((1000 * 100) >> 8, SndMix.paintbuffer[0].Left);
        Assert.AreEqual((1000 * 50) >> 8, SndMix.paintbuffer[0].Right);
        Assert.AreEqual((-2000 * 100) >> 8, SndMix.paintbuffer[1].Left);
        Assert.AreEqual((-2000 * 50) >> 8, SndMix.paintbuffer[1].Right);
        Assert.AreEqual(count, ch.Pos);
    }

    [TestMethod]
    public void TransferPaintBuffer_Mono8Bit_ShiftsAndBiasesTo128WithClamping()
    {
        _shm.SampleBits = 8;
        _shm.Channels = 1;
        _shm.Samples = 8;
        byte* buffer = stackalloc byte[8];
        _shm.Buffer = buffer;

        SndMix.paintbuffer[0].Left = 100;
        SndMix.paintbuffer[1].Left = -200;
        SndMix.paintbuffer[2].Left = 300;
        SndMix.paintbuffer[3].Left = 40000; // exceeds 0x7fff, must clamp

        SndDma.paintedtime = 0;
        SndMix.TransferPaintBuffer(4);

        Assert.AreEqual((byte)128, buffer[0]); // (100>>8)+128
        Assert.AreEqual((byte)127, buffer[1]); // (-200>>8)+128
        Assert.AreEqual((byte)129, buffer[2]); // (300>>8)+128
        Assert.AreEqual((byte)255, buffer[3]); // clamp(40000)=32767 -> (32767>>8)+128
    }

    [TestMethod]
    public void TransferPaintBuffer_Stereo16Bit_RoutesThroughTransferStereo16WithClamping()
    {
        _shm.SampleBits = 16;
        _shm.Channels = 2;
        _shm.Samples = 8; // 4 stereo sample-pairs
        short* buffer = stackalloc short[8];
        _shm.Buffer = (byte*)buffer;

        SndMix.paintbuffer[0].Left = 1000;
        SndMix.paintbuffer[0].Right = -1000;
        SndMix.paintbuffer[1].Left = 40000;  // clamps to 0x7fff
        SndMix.paintbuffer[1].Right = -40000; // clamps to -0x8000

        SndDma.paintedtime = 0;
        SndMix.TransferPaintBuffer(2);

        Assert.AreEqual((short)1000, buffer[0]);
        Assert.AreEqual((short)-1000, buffer[1]);
        Assert.AreEqual((short)0x7fff, buffer[2]);
        Assert.AreEqual(unchecked((short)0x8000), buffer[3]);
    }
}
