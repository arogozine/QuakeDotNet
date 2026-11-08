// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;

namespace Quake.UnitTests;

[TestClass]
public sealed unsafe class CrcTests
{
    // Independent bitwise reference implementation of the same algorithm
    // (CRC-16/CCITT, poly 0x1021, init 0xffff, non-reflected) that crc.c's
    // table is a precomputed shortcut for. Used to catch transcription errors
    // in the ported 256-entry table.
    private static ushort ReferenceCrc(byte[] data)
    {
        ushort crc = 0xffff;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    private static ushort PortCrc(byte[] data)
    {
        Crc.Init(out ushort crc);
        foreach (byte b in data)
            Crc.ProcessByte(ref crc, b);
        return Crc.Value(crc);
    }

    [TestMethod]
    public void Init_SetsStartingValue()
    {
        Crc.Init(out ushort crc);
        Assert.AreEqual((ushort)0xffff, crc);
    }

    [TestMethod]
    public void EmptyInput_ReturnsInitValue()
    {
        Assert.AreEqual((ushort)0xffff, PortCrc([]));
    }

    [TestMethod]
    public void MatchesBitwiseReferenceImplementation_ForVariousInputs()
    {
        byte[][] cases =
        [
            [0x00],
            [0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39], // "123456789"
            [0xff, 0x00, 0xab, 0xcd],
        ];

        foreach (byte[] data in cases)
            Assert.AreEqual(ReferenceCrc(data), PortCrc(data));
    }

    [TestMethod]
    public void ProcessByte_IsOrderSensitive()
    {
        Assert.AreNotEqual(PortCrc([1, 2, 3]), PortCrc([3, 2, 1]));
    }
}
