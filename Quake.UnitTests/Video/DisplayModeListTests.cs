// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class DisplayModeListTests
{
    [TestMethod]
    public void BuildList_DedupesSameResolutionFromMultipleRefreshRates()
    {
        // Candidates simulate 1920x1080 reported at several refresh rates
        // by OpenTK's SupportedVideoModes (VideoMode carries refresh rate,
        // DisplayMode does not) -- these must collapse to one entry.
        List<DisplayMode> candidates =
        [
            new(1920, 1080),
            new(1920, 1080),
            new(1920, 1080),
        ];

        List<DisplayMode> result = DisplayModeList.BuildList(candidates, 1920, 1080);

        Assert.AreEqual(1, result.Count(m => m.Width == 1920 && m.Height == 1080));
    }

    [TestMethod]
    public void BuildList_ExcludesCandidatesAboveNativeResolution()
    {
        List<DisplayMode> candidates = [new(3840, 2160)];

        List<DisplayMode> result = DisplayModeList.BuildList(candidates, 1920, 1080);

        Assert.IsFalse(result.Any(m => m.Width == 3840 && m.Height == 2160));
    }

    [TestMethod]
    public void BuildList_IncludesBaselineResolutionsUpToNative()
    {
        List<DisplayMode> result = DisplayModeList.BuildList([], 3840, 2160);

        Assert.Contains(new DisplayMode(1920, 1080), result);
        Assert.Contains(new DisplayMode(3840, 2160), result);
    }

    [TestMethod]
    public void BuildList_OmitsBaselineResolutionsAboveNative()
    {
        // 1600x900 is above the minimum in both dimensions, so the forced
        // MinWidth/MinHeight inclusion doesn't itself exceed native here.
        List<DisplayMode> result = DisplayModeList.BuildList([], 1600, 900);

        Assert.IsFalse(result.Any(m => m.Width > 1600 || m.Height > 900));
        Assert.Contains(new DisplayMode(1600, 900), result);
        Assert.IsFalse(result.Any(m => m.Width == 1920 && m.Height == 1080));
    }

    [TestMethod]
    public void BuildList_ExcludesCandidatesBelowMinimumResolution()
    {
        List<DisplayMode> candidates = [new(640, 480), new(800, 600)];

        List<DisplayMode> result = DisplayModeList.BuildList(candidates, 1920, 1080);

        Assert.IsFalse(result.Any(m => m.Width < DisplayModeList.MinWidth || m.Height < DisplayModeList.MinHeight));
    }

    [TestMethod]
    public void BuildList_AlwaysIncludesMinimumResolutionEvenBelowNative()
    {
        // A monitor smaller than the supported minimum is unusual, but the
        // minimum must still be offered rather than leaving the list empty.
        List<DisplayMode> result = DisplayModeList.BuildList([], 800, 600);

        Assert.Contains(new DisplayMode(DisplayModeList.MinWidth, DisplayModeList.MinHeight), result);
    }

    [TestMethod]
    public void BuildList_SortsAscendingByWidthThenHeight()
    {
        List<DisplayMode> candidates = [new(1920, 1080), new(640, 480), new(1280, 720)];

        List<DisplayMode> result = DisplayModeList.BuildList(candidates, 1920, 1080);

        for (int i = 1; i < result.Count; i++)
        {
            bool ordered = result[i - 1].Width < result[i].Width ||
                (result[i - 1].Width == result[i].Width && result[i - 1].Height <= result[i].Height);
            Assert.IsTrue(ordered, $"Out of order at index {i}: {result[i - 1]} then {result[i]}");
        }
    }

    [TestMethod]
    public void BuildList_EmptyCandidatesStillReturnsBaselineList()
    {
        List<DisplayMode> result = DisplayModeList.BuildList([], 1920, 1080);

        Assert.IsNotEmpty(result);
    }

    [TestMethod]
    public void BuildList_NativeResolutionItselfIsIncludedOnce()
    {
        List<DisplayMode> candidates = [new(1920, 1080)];

        List<DisplayMode> result = DisplayModeList.BuildList(candidates, 1920, 1080);

        Assert.AreEqual(1, result.Count(m => m.Width == 1920 && m.Height == 1080));
    }

    [TestMethod]
    public void BuildList_SingleCandidateBelowNativeIsIncluded()
    {
        List<DisplayMode> candidates = [new(1024, 768)];

        List<DisplayMode> result = DisplayModeList.BuildList(candidates, 1920, 1080);

        Assert.Contains(new DisplayMode(1024, 768), result);
    }

    private static unsafe string Str(in Utf8StringHandler h) => CStr.ToString(h.GetWrittenBufferPtr());

    [TestMethod]
    public void Format_Windowed_MatchesVidGetModeDescription2()
    {
        DisplayMode mode = new(1280, 1024);
        bool fullscreen = false;
        Assert.AreEqual("1280x1024 windowed", Str($"{mode} {(fullscreen ? "fullscreen"u8 : "windowed"u8)}"));
    }

    [TestMethod]
    public void Format_Fullscreen_MatchesVidGetModeDescription2()
    {
        DisplayMode mode = new(3440, 1440);
        bool fullscreen = true;
        Assert.AreEqual("3440x1440 fullscreen", Str($"{mode} {(fullscreen ? "fullscreen"u8 : "windowed"u8)}"));
    }

    [TestMethod]
    public void Format_InsideLargerString_KeepsPrefix()
    {
        DisplayMode mode = new(1920, 1080);
        int i = 3;
        Assert.AreEqual(" 3: 1920x1080 windowed", Str($"{i,2}: {mode} windowed"));
    }

    [TestMethod]
    public void TryFormat_ExactFit_Succeeds()
    {
        Span<byte> dest = stackalloc byte[8];
        Assert.IsTrue(new DisplayMode(1024, 768).TryFormat(dest, out int n, default, null));
        Assert.AreEqual(8, n);
        Assert.IsTrue(dest.SequenceEqual("1024x768"u8));
    }

    [TestMethod]
    public void TryFormat_TooSmall_FailsWithZeroBytes()
    {
        Span<byte> dest = stackalloc byte[3];
        Assert.IsFalse(new DisplayMode(1024, 768).TryFormat(dest, out int n, default, null)); // width does not fit
        Assert.AreEqual(0, n);

        dest = stackalloc byte[4];
        Assert.IsFalse(new DisplayMode(1024, 768).TryFormat(dest, out n, default, null)); // no room for 'x'
        Assert.AreEqual(0, n);

        dest = stackalloc byte[7];
        Assert.IsFalse(new DisplayMode(1024, 768).TryFormat(dest, out n, default, null)); // height does not fit
        Assert.AreEqual(0, n);
    }
}
