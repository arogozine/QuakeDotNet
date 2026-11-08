// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

// S_BlockSound / S_UnblockSound (snd_win.c)
[TestClass]
public sealed unsafe class SndBlockTests
{
    [TestMethod]
    public void BlockUnblock_NestsSndBlockedCounter()
    {
        int saved = SndDma.snd_blocked;
        try
        {
            SndDma.snd_blocked = 0;
            SndDma.BlockSound();
            SndDma.BlockSound();
            Assert.AreEqual(2, SndDma.snd_blocked);
            SndDma.UnblockSound();
            Assert.AreEqual(1, SndDma.snd_blocked);
            SndDma.UnblockSound();
            Assert.AreEqual(0, SndDma.snd_blocked);
        }
        finally { SndDma.snd_blocked = saved; }
    }
}
