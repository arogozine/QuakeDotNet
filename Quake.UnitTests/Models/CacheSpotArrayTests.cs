// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class CacheSpotArrayTests
{
    private MSurface_t* _surface;

    [TestInitialize]
    public void Setup()
    {
        _surface = NativeMemory.AllocZeroed<MSurface_t>();
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_surface);
        _surface = null;
    }

    [TestMethod]
    public void CacheSpotArray_Size_IsMiplevelsPointers()
    {
        Assert.AreEqual(BspFile.MIPLEVELS * sizeof(nint), sizeof(CacheSpotArray));
    }

    [TestMethod]
    public void CacheSpotArray_ZeroInit_AllSlotsNull()
    {
        for (int i = 0; i < BspFile.MIPLEVELS; i++)
            Assert.IsTrue(_surface->CacheSpots[i] == null, $"slot {i}");
    }

    [TestMethod]
    public void CacheSpotArray_SetGet_RoundTripsEachSlotIndependently()
    {
        for (int i = 0; i < BspFile.MIPLEVELS; i++)
            _surface->CacheSpots[i] = (SurfCache_t*)(nint)(0x1000 * (i + 1));

        for (int i = 0; i < BspFile.MIPLEVELS; i++)
            Assert.AreEqual((nint)(0x1000 * (i + 1)), (nint)_surface->CacheSpots[i], $"slot {i}");
    }

    [TestMethod]
    public void AddressOf_EachSlot_PointsAtSlotAndWritesThrough()
    {
        for (int i = 0; i < BspFile.MIPLEVELS; i++)
        {
            SurfCache_t** p = _surface->CacheSpots.AddressOf(i);

            Assert.AreEqual((nint)((byte*)&_surface->CacheSpots + i * sizeof(nint)), (nint)p, $"slot {i}");

            *p = (SurfCache_t*)(nint)0x2000;
            Assert.AreEqual((nint)0x2000, (nint)_surface->CacheSpots[i], $"slot {i}");
            *p = null;
        }
    }
}
