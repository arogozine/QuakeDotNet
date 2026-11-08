// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine;
using Quake.Engine.Memory;
using static Quake.UnitTests.TestHelpers;

namespace Quake.UnitTests;

// ResampleSfx/LoadSound go through Cache (Hunk-backed) and, for LoadSound,
// Com's search paths -- shared static state, so this needs the same
// [DoNotParallelize] + Memory_Init isolation as the other loader tests.
[TestClass]
public sealed unsafe class WavTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;
    private static Dma_t _shm;

    [TestInitialize]
    public void Setup()
    {
        _buf = (byte*)NativeMemory.Alloc(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        Com.com_searchpaths = null;

        _shm = default;
        _shm.Speed = 11025;
        SndDma.shm = (Dma_t*)Unsafe.AsPointer(ref _shm);
        SndDma.loadas8bit->Value = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        for (SearchPath_t* s = Com.com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null)
                Sys.FileClose(s->Pack->Handle);

        NativeMemory.Free(_buf);
    }

    private static void WriteChunkId(BinaryWriter bw, string id) =>
        bw.Write(Encoding.ASCII.GetBytes(id));

    // Builds a minimal 8-bit mono PCM WAV: RIFF/WAVE, fmt , data (no cue/LIST).
    private static byte[] BuildMinimalWav(byte[] samples, int rate = 11025, short bitsPerSample = 8, short channels = 1)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int byteRate = rate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));

        // fmt chunk body: format(2) channels(2) rate(4) byterate(4) blockalign(2) bits(2) = 16 bytes
        int fmtChunkSize = 16;
        int fmtTotal = 8 + fmtChunkSize + (fmtChunkSize & 1);

        int dataChunkSize = samples.Length;
        int dataTotal = 8 + dataChunkSize + (dataChunkSize & 1);

        int riffSize = 4 /*WAVE*/ + fmtTotal + dataTotal;

        WriteChunkId(bw, "RIFF");
        bw.Write(riffSize);
        WriteChunkId(bw, "WAVE");

        WriteChunkId(bw, "fmt ");
        bw.Write(fmtChunkSize);
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(rate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);

        WriteChunkId(bw, "data");
        bw.Write(dataChunkSize);
        bw.Write(samples);
        if ((dataChunkSize & 1) != 0)
            bw.Write((byte)0);

        return ms.ToArray();
    }

    // Builds a WAV with a "cue " chunk (a single cue point at loopstart) plus
    // a following "LIST" chunk whose payload starts with a fake "mark"
    // sub-chunk id at offset 28 (matching GetWavinfo's non-standard parse),
    // encoding a loop-length sample count at offset 24 within that chunk.
    private static byte[] BuildLoopingWav(byte[] samples, int loopStart, int loopSamples, int rate = 11025)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int fmtChunkSize = 16;
        int fmtTotal = 8 + fmtChunkSize;

        // GetWavinfo does `data_p += 32` from the "cue " chunk's id (i.e. 8
        // bytes of id+size, then 24 bytes into the payload) to reach
        // dwSampleOffset of the first cue point -- so the loopstart long
        // lives at payload offset 24.
        int cuePayloadSize = 32;
        byte[] cuePayload = new byte[cuePayloadSize];
        BitConverter.GetBytes(loopStart).CopyTo(cuePayload, 24);
        int cueTotal = 8 + cuePayloadSize;

        // GetWavinfo checks for "mark" at (LIST id + 28) -- payload offset
        // 20 -- and, if found, reads the loop sample count as a long at
        // (LIST id + 24) -- payload offset 16.
        byte[] listPayload = new byte[32];
        BitConverter.GetBytes(loopSamples).CopyTo(listPayload, 16);
        Encoding.ASCII.GetBytes("mark").CopyTo(listPayload, 20);
        int listTotal = 8 + listPayload.Length;

        int dataChunkSize = samples.Length;
        int dataTotal = 8 + dataChunkSize + (dataChunkSize & 1);

        int riffSize = 4 + fmtTotal + cueTotal + listTotal + dataTotal;

        WriteChunkId(bw, "RIFF");
        bw.Write(riffSize);
        WriteChunkId(bw, "WAVE");

        WriteChunkId(bw, "fmt ");
        bw.Write(fmtChunkSize);
        bw.Write((short)1);
        bw.Write((short)1); // mono
        bw.Write(rate);
        bw.Write(rate * 1); // byte rate (8-bit mono)
        bw.Write((short)1); // block align
        bw.Write((short)8); // bits per sample

        WriteChunkId(bw, "cue ");
        bw.Write(cuePayloadSize);
        bw.Write(cuePayload);

        WriteChunkId(bw, "LIST");
        bw.Write(listPayload.Length);
        bw.Write(listPayload);

        WriteChunkId(bw, "data");
        bw.Write(dataChunkSize);
        bw.Write(samples);
        if ((dataChunkSize & 1) != 0)
            bw.Write((byte)0);

        return ms.ToArray();
    }

    [TestMethod]
    public void GetWavinfo_ParsesMinimal8BitMonoPcm()
    {
        byte[] samples = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] wav = BuildMinimalWav(samples, rate: 11025, bitsPerSample: 8, channels: 1);

        fixed (byte* p = wav)
        {
            WavInfo_t info = Wav.GetWavinfo(Ansi("test.wav"), p, wav.Length);

            Assert.AreEqual(11025, info.Rate);
            Assert.AreEqual(1, info.Width);
            Assert.AreEqual(1, info.Channels);
            Assert.AreEqual(-1, info.LoopStart);
            Assert.AreEqual(samples.Length, info.Samples);

            byte* dataStart = p + info.DataOfs;
            for (int i = 0; i < samples.Length; i++)
                Assert.AreEqual(samples[i], dataStart[i]);
        }
    }

    [TestMethod]
    public void GetWavinfo_Parses16BitStereoFormat()
    {
        // 4 stereo 16-bit sample frames = 16 bytes of sample data.
        byte[] samples = new byte[16];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (byte)(i + 1);

        byte[] wav = BuildMinimalWav(samples, rate: 22050, bitsPerSample: 16, channels: 2);

        fixed (byte* p = wav)
        {
            WavInfo_t info = Wav.GetWavinfo(Ansi("stereo.wav"), p, wav.Length);

            Assert.AreEqual(22050, info.Rate);
            Assert.AreEqual(2, info.Width);
            Assert.AreEqual(2, info.Channels);
            Assert.AreEqual(samples.Length / info.Width, info.Samples);
        }
    }

    [TestMethod]
    public void GetWavinfo_ParsesLoopPointsFromCueAndListChunks()
    {
        byte[] samples = new byte[20];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (byte)i;

        const int loopStart = 5;
        const int loopSamples = 15;
        byte[] wav = BuildLoopingWav(samples, loopStart, loopSamples);

        fixed (byte* p = wav)
        {
            WavInfo_t info = Wav.GetWavinfo(Ansi("loop.wav"), p, wav.Length);

            Assert.AreEqual(loopStart, info.LoopStart);
            Assert.AreEqual(loopStart + loopSamples, info.Samples);
        }
    }

    [TestMethod]
    public void GetWavinfo_NullData_ReturnsZeroedInfo()
    {
        WavInfo_t info = Wav.GetWavinfo(Ansi("missing.wav"), null, 0);

        Assert.AreEqual(0, info.Rate);
        Assert.AreEqual(0, info.Width);
        Assert.AreEqual(0, info.Channels);
        Assert.AreEqual(0, info.Samples);
        Assert.AreEqual(0, info.DataOfs);
    }

    [TestMethod]
    public void GetWavinfo_MissingDataChunk_ReturnsInfoWithoutSamples()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int fmtChunkSize = 16;
        int riffSize = 4 + 8 + fmtChunkSize;

        WriteChunkId(bw, "RIFF");
        bw.Write(riffSize);
        WriteChunkId(bw, "WAVE");

        WriteChunkId(bw, "fmt ");
        bw.Write(fmtChunkSize);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(11025);
        bw.Write(11025);
        bw.Write((short)1);
        bw.Write((short)8);

        byte[] wav = ms.ToArray();

        fixed (byte* p = wav)
        {
            WavInfo_t info = Wav.GetWavinfo(Ansi("nodata.wav"), p, wav.Length);

            Assert.AreEqual(11025, info.Rate);
            Assert.AreEqual(0, info.Samples);
            Assert.AreEqual(0, info.DataOfs);
        }
    }

    [TestMethod]
    public void ResampleSfx_MatchingRateAnd8BitWidth_TakesFastPathUnsignedToSigned()
    {
        Sfx_t sfx = default;
        int size = sizeof(SfxCache_t) + 4;
        SfxCache_t* sc = (SfxCache_t*)Cache.Alloc(&sfx.Cache, size, "test"u8);
        sc->Length = 4;   // pre-resample sample count (== outcount since stepscale is 1)
        sc->LoopStart = 2; // unchanged when stepscale == 1

        byte[] input = [0, 128, 255, 64];
        fixed (byte* data = input)
        {
            Wav.ResampleSfx(&sfx, inrate: SndDma.shm->Speed, inwidth: 1, data);
        }

        Assert.AreEqual(4, sc->Length);
        Assert.AreEqual(2, sc->LoopStart);
        Assert.AreEqual(11025, sc->Speed);
        Assert.AreEqual(1, sc->Width);
        Assert.AreEqual(0, sc->Stereo);

        sbyte* outData = (sbyte*)(sc + 1);
        Assert.AreEqual((sbyte)-128, outData[0]);
        Assert.AreEqual((sbyte)0, outData[1]);
        Assert.AreEqual((sbyte)127, outData[2]);
        Assert.AreEqual((sbyte)-64, outData[3]);
    }

    [TestMethod]
    public void ResampleSfx_HalvesRate_DecimatesEveryOtherSample()
    {
        Sfx_t sfx = default;
        int size = sizeof(SfxCache_t) + 2 * sizeof(short);
        SfxCache_t* sc = (SfxCache_t*)Cache.Alloc(&sfx.Cache, size, "test"u8);
        sc->Length = 4;    // pre-resample count; stepscale = 2 -> outcount = 2
        sc->LoopStart = 4; // halved to 2 below

        short[] input = [1000, 2000, 3000, 4000];
        fixed (short* data = input)
        {
            // inrate is double shm.Speed -> stepscale = inrate/shm.Speed = 2
            Wav.ResampleSfx(&sfx, inrate: SndDma.shm->Speed * 2, inwidth: 2, (byte*)data);
        }

        Assert.AreEqual(2, sc->Length);
        Assert.AreEqual(2, sc->LoopStart);
        Assert.AreEqual(2, sc->Width);

        short* outData = (short*)(sc + 1);
        Assert.AreEqual((short)1000, outData[0]); // srcsample 0
        Assert.AreEqual((short)3000, outData[1]); // srcsample 2
    }

    [TestMethod]
    public void ResampleSfx_LoadAs8BitSet_ForcesOutputWidthTo1()
    {
        SndDma.loadas8bit->Value = 1;

        Sfx_t sfx = default;
        int size = sizeof(SfxCache_t) + 2;
        SfxCache_t* sc = (SfxCache_t*)Cache.Alloc(&sfx.Cache, size, "test"u8);
        sc->Length = 2;
        sc->LoopStart = -1;

        short[] input = [1000, -2000];
        fixed (short* data = input)
        {
            Wav.ResampleSfx(&sfx, inrate: SndDma.shm->Speed, inwidth: 2, (byte*)data);
        }

        Assert.AreEqual(1, sc->Width);

        sbyte* outData = (sbyte*)(sc + 1);
        Assert.AreEqual((sbyte)(1000 >> 8), outData[0]);
        Assert.AreEqual((sbyte)(-2000 >> 8), outData[1]);
    }

    [TestMethod]
    public void LoadSound_LoadsWavFromDiskAndResamplesIntoCache()
    {
        byte[] samples = [10, 20, 30, 40, 50, 60, 70, 80];
        byte[] wav = BuildMinimalWav(samples, rate: 11025, bitsPerSample: 8, channels: 1);

        string gamedir = Path.Combine(Path.GetTempPath(), $"qtest_gamedir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gamedir, "sound"));
        File.WriteAllBytes(Path.Combine(gamedir, "sound", "test.wav"), wav);

        try
        {
            Com.AddGameDirectory(Ansi(gamedir));

            Sfx_t sfx = default;
            Sfx_t* pSfx = &sfx;
            Q.StrCpy(pSfx->Name, Ansi("test.wav"));

            SfxCache_t* sc = Wav.LoadSound(pSfx);

            Assert.IsTrue((nint)sc != 0);
            Assert.AreEqual(samples.Length, sc->Length);
            Assert.AreEqual(-1, sc->LoopStart);
            Assert.AreEqual(11025, sc->Speed);
            Assert.AreEqual(1, sc->Width);
            Assert.AreEqual(0, sc->Stereo);

            sbyte* outData = (sbyte*)(sc + 1);
            for (int i = 0; i < samples.Length; i++)
                Assert.AreEqual((sbyte)(samples[i] - 128), outData[i]);

            // a second load should hit the cache and return the same entry
            SfxCache_t* again = Wav.LoadSound(pSfx);
            Assert.IsTrue(again == sc);
        }
        finally
        {
            Directory.Delete(gamedir, recursive: true);
        }
    }
}
