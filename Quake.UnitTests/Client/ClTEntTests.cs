// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// cl_tent.c's ClTEnt.cs ((int)Svc.TempEntity). Shares Cl/ClEntities' static
// state with ClParseTests, so it follows the same
// DoNotParallelize/reset-everything-in-Setup pattern.
[TestClass]
public sealed unsafe class ClTEntTests
{
    private byte* _buf;
    private const int Size = 1 * 1024 * 1024;
    private byte* _colormap;
    private byte* _netMessageBuf;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);

        Cl.time = 10.0;
        Cl.viewentity = 0;

        _colormap = NativeMemory.AllocArrayZeroed<byte>(Vid.VID_GRADES * 256);
        Vid.vid.Colormap = _colormap;

        new Span<Entity_t>(ClEntities.cl_entities, QuakeDef.MAX_EDICTS).Clear();
        NativeMemory.ClearPointerArray(ClEntities.cl_visedicts, ClEntities.MAX_VISEDICTS);

        ClEntities.cl_numvisedicts = 0;
        new Span<DLight_t>(ClEntities.cl_dlights, ClEntities.MAX_DLIGHTS).Clear();

        new Span<Beam_t>(ClTEnt.cl_beams, ClTEnt.MAX_BEAMS).Clear();
        new Span<Entity_t>(ClTEnt.cl_temp_entities, ClTEnt.MAX_TEMP_ENTITIES).Clear();
        ClTEnt.num_temp_entities = 0;

        // No real audio device (SndDma.sound_started stays false) and no
        // real .wav files loaded -- SndDma.StartSound early-returns on
        // !sound_started before touching cl_sfx_*, so leaving them null is
        // safe (matches RealSoundTests' own note on needing real pak data
        // for anything beyond that).
        ClTEnt.cl_sfx_wizhit = null;
        ClTEnt.cl_sfx_knighthit = null;
        ClTEnt.cl_sfx_tink1 = null;
        ClTEnt.cl_sfx_ric1 = null;
        ClTEnt.cl_sfx_ric2 = null;
        ClTEnt.cl_sfx_ric3 = null;
        ClTEnt.cl_sfx_r_exp3 = null;

        _netMessageBuf = NativeMemory.AllocArrayZeroed<byte>(2048);
        Net.net_message->Data = _netMessageBuf; Net.net_message->MaxSize = 2048;
    }

    [TestCleanup]
    public void Teardown()
    {
        NativeMemory.Free(_colormap);
        NativeMemory.Free(_netMessageBuf);
        NativeMemory.Free(_buf);
    }

    private static string CaptureOutput(Action action)
    {
        TextWriter original = Console.Out;
        StringWriter sw = new();
        Console.SetOut(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    // Writes a TE_* payload directly into Msg.net_message ((int)Svc.TempEntity's
    // type byte is already consumed by ClParse.cs before ParseTEnt runs, so
    // tests calling ParseTEnt directly write it too, matching the real wire
    // format).
    private static void SetTEntMessage(int teType, params ReadOnlySpan<float> coords)
    {
        SizeBuf_t* msg = Msg.net_message;
        {
            Sz.Clear(msg);
            Msg.WriteByte(msg, teType);
            foreach (float c in coords)
            {
                Msg.WriteCoord(msg, c);
            }
        }
        Msg.BeginReading();
    }

    private static Beam_t* Beam(int i) => &ClTEnt.cl_beams[i];

    [TestMethod]
    public void ParseBeam_FreeSlot_PopulatesEntityModelAndEndpoints()
    {
        SizeBuf_t* msg = Msg.net_message;
        {
            Sz.Clear(msg);
            Msg.WriteShort(msg, 5); // entity
            Msg.WriteCoord(msg, 1); Msg.WriteCoord(msg, 2); Msg.WriteCoord(msg, 3);
            Msg.WriteCoord(msg, 4); Msg.WriteCoord(msg, 5); Msg.WriteCoord(msg, 6);
        }
        Msg.BeginReading();

        Model_t* model = NativeMemory.AllocZeroed<Model_t>();
        try
        {
            ClTEnt.ParseBeam(model);

            Beam_t* b = Beam(0);
            Assert.AreEqual(5, b->Entity);
            Assert.IsTrue(b->Model == model);
            Assert.AreEqual(1f, b->Start[0]);
            Assert.AreEqual(6f, b->End[2]);
            Assert.AreEqual((float)(Cl.time + 0.2), b->Endtime, 0.0001f);
        }
        finally
        {
            NativeMemory.Free(model);
        }
    }

    [TestMethod]
    public void ParseBeam_SameEntityAsExistingBeam_OverridesThatSlotInstead()
    {
        Model_t* model1 = NativeMemory.AllocZeroed<Model_t>();
        Model_t* model2 = NativeMemory.AllocZeroed<Model_t>();
        try
        {
            SizeBuf_t* msg = Msg.net_message;
            {
                Sz.Clear(msg);
                Msg.WriteShort(msg, 7);
                for (int i = 0; i < 6; i++)
                {
                    Msg.WriteCoord(msg, 0);
                }
            }
            Msg.BeginReading();
            ClTEnt.ParseBeam(model1);

            msg = Msg.net_message;
            {
                Sz.Clear(msg);
                Msg.WriteShort(msg, 7); // same entity
                for (int i = 0; i < 6; i++)
                {
                    Msg.WriteCoord(msg, 9);
                }
            }
            Msg.BeginReading();
            ClTEnt.ParseBeam(model2);

            Assert.AreEqual(7, Beam(0)->Entity);
            Assert.IsTrue(Beam(0)->Model == model2);
            Assert.AreEqual(9f, Beam(0)->Start[0]);
            // no second beam was allocated for the same entity
            Assert.IsTrue(Beam(1)->Model == null);
        }
        finally
        {
            NativeMemory.Free(model1);
            NativeMemory.Free(model2);
        }
    }

    [TestMethod]
    public void ParseBeam_NoFreeSlot_PrintsOverflowAndDoesNotThrow()
    {
        Model_t* model = NativeMemory.AllocZeroed<Model_t>();
        try
        {
            // fill every slot with a live (non-expired), distinct-entity beam
            for (int i = 0; i < ClTEnt.MAX_BEAMS; i++)
            {
                Beam_t* b = Beam(i);
                b->Entity = 100 + i;
                b->Model = model;
                b->Endtime = (float)(Cl.time + 10);
            }

            SizeBuf_t* msg = Msg.net_message;
            {
                Sz.Clear(msg);
                Msg.WriteShort(msg, 999); // new entity, no free/matching slot
                for (int i = 0; i < 6; i++)
                {
                    Msg.WriteCoord(msg, 0);
                }
            }
            Msg.BeginReading();

            string output = CaptureOutput(() => ClTEnt.ParseBeam(model));

            StringAssert.Contains(output, "beam list overflow");
        }
        finally
        {
            NativeMemory.Free(model);
        }
    }

    [TestMethod]
    public void ParseTEnt_TE_EXPLOSION_AllocatesDlightWithExpectedFields()
    {
        SetTEntMessage((int)TempEntityType.Explosion, 10, 20, 30);

        ClTEnt.ParseTEnt();

        DLight_t* dl = &ClEntities.cl_dlights[0];
        Assert.AreEqual(10f, dl->Origin[0]);
        Assert.AreEqual(30f, dl->Origin[2]);
        Assert.AreEqual(350f, dl->Radius);
        Assert.AreEqual(300f, dl->Decay);
        Assert.AreEqual((float)(Cl.time + 0.5), dl->Die, 0.0001f);
    }

    [TestMethod]
    public void ParseTEnt_TE_EXPLOSION2_AllocatesDlightAndConsumesColorBytes()
    {
        SizeBuf_t* msg = Msg.net_message;
        {
            Sz.Clear(msg);
            Msg.WriteByte(msg, (int)TempEntityType.Explosion2);
            Msg.WriteCoord(msg, 1); Msg.WriteCoord(msg, 2); Msg.WriteCoord(msg, 3);
            Msg.WriteByte(msg, 64);  // colorStart
            Msg.WriteByte(msg, 8);   // colorLength
        }
        Msg.BeginReading();

        ClTEnt.ParseTEnt();

        DLight_t* dl = &ClEntities.cl_dlights[0];
        Assert.AreEqual(350f, dl->Radius);
    }

    [TestMethod]
    public void ParseTEnt_UnknownType_ThrowsHostErrorSubstitute()
    {
        SetTEntMessage(99);

        Assert.ThrowsExactly<QuakeSysErrorException>(() => ClTEnt.ParseTEnt());
    }

    [TestMethod]
    public void NewTempEntity_AtCap_ReturnsNull()
    {
        ClTEnt.num_temp_entities = ClTEnt.MAX_TEMP_ENTITIES;

        Entity_t* ent = ClTEnt.NewTempEntity();

        Assert.IsTrue(ent == null);
    }

    [TestMethod]
    public void NewTempEntity_VisedictsFull_ReturnsNull()
    {
        ClEntities.cl_numvisedicts = ClEntities.MAX_VISEDICTS;

        Entity_t* ent = ClTEnt.NewTempEntity();

        Assert.IsTrue(ent == null);
    }

    [TestMethod]
    public void NewTempEntity_FreeSlot_AddsToVisedictsAndSetsColormap()
    {
        Entity_t* ent = ClTEnt.NewTempEntity();

        Assert.IsTrue(ent != null);
        Assert.AreEqual(1, ClTEnt.num_temp_entities);
        Assert.AreEqual(1, ClEntities.cl_numvisedicts);
        Assert.IsTrue(ClEntities.cl_visedicts[0] == ent);
        Assert.IsTrue(ent->ColorMap == _colormap);
    }

    [TestMethod]
    public void UpdateTEnts_ExpiredBeam_IsSkipped()
    {
        Model_t* model = NativeMemory.AllocZeroed<Model_t>();
        try
        {
            Beam_t* b = Beam(0);
            b->Entity = 1;
            b->Model = model;
            b->Endtime = (float)(Cl.time - 1); // already expired

            ClTEnt.UpdateTEnts();

            Assert.AreEqual(0, ClTEnt.num_temp_entities);
        }
        finally
        {
            NativeMemory.Free(model);
        }
    }

    [TestMethod]
    public void UpdateTEnts_ActiveBeam_PopulatesTempEntitiesAlongItsLength()
    {
        Model_t* model = NativeMemory.AllocZeroed<Model_t>();
        try
        {
            Beam_t* b = Beam(0);
            b->Entity = 1;
            b->Model = model;
            b->Endtime = (float)(Cl.time + 1);
            b->Start[0] = 0; b->Start[1] = 0; b->Start[2] = 0;
            b->End[0] = 90; b->End[1] = 0; b->End[2] = 0; // 90 units -> 3 segments of 30

            ClTEnt.UpdateTEnts();

            Assert.IsGreaterThan(0, ClTEnt.num_temp_entities);
            Assert.IsTrue(ClTEnt.cl_temp_entities[0].Model == model);
        }
        finally
        {
            NativeMemory.Free(model);
        }
    }

    [TestMethod]
    public void UpdateTEnts_ResetsNumTempEntitiesEachCall()
    {
        ClTEnt.num_temp_entities = 5;

        ClTEnt.UpdateTEnts(); // no live beams -- should reset to 0 and stay there

        Assert.AreEqual(0, ClTEnt.num_temp_entities);
    }
}
