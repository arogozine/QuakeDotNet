// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using Quake.Engine;
using Quake.Engine.Memory;
using System.Runtime.InteropServices;

namespace Quake.UnitTests;

// SvPhys/SvMove/SvUser share Sv/Progs/World's static state, so, like
// ProgsTests/RealPakFileTests, these can't run concurrently with anything
// else touching that state.
[TestClass]
public sealed unsafe class SvPhysTests
{
    private byte* _buf;
    private const int Size = 4 * 1024 * 1024;
    private List<nint> _allocations = null!;

    [TestInitialize]
    public void Setup()
    {
        _buf = NativeMemory.AllocArray<byte>(Size);
        // Cache.Init() (called by Memory_Init) registers "flush" via
        // Cmd.AddCommand, which walks cmd_functions -- reset it first so
        // that walk doesn't dangle into a previous test's freed Hunk arena.
        Cmd.cmd_functions = null;
        MemoryManager.Memory_Init(_buf, Size);
        _allocations = [];

        Cvar.cvar_vars = null;
        Sv.Init();

        // A minimal synthetic globalvars_t/pr_edict_size big enough to hold
        // just entvars_t (no extra progs.src-only fields, unlike a real
        // progs.dat -- see Progs.cs's pr_edict_size comment).
        Progs.pr_edict_size = sizeof(EDict_t);
        GlobalVars_t* globals = AllocZeroed<GlobalVars_t>(1);
        Progs.pr_global_struct = globals;
        Progs.pr_globals = (float*)globals;

        // Two edicts: [0] the world, [1] a free-falling test entity.
        Sv.edicts = AllocZeroed<EDict_t>(2);
        Sv.num_edicts = 2;
        Sv.max_edicts = 2;

        SetupOpenWorld();

        Host.host_frametime = 0.1f;
        Sv.time = 0;
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (nint p in _allocations)
        {
            NativeMemory.Free((void*)p);
        }

        NativeMemory.Free(_buf);
    }

    private T* AllocZeroed<T>(int count) where T : unmanaged
    {
        T* p = NativeMemory.AllocArrayZeroed<T>(count);
        _allocations.Add((nint)p);
        return p;
    }

    // Builds a worldmodel whose collision hulls report (int)Contents.Empty for
    // every point -- a valid but geometry-free world, so World.Move/
    // World.PointContents can run (and SV_FlyMove/SV_Physics_Toss free-fall
    // without ever hitting anything) without needing a real BSP file.
    private void SetupOpenWorld()
    {
        DClipNode_t* clipnodes = AllocZeroed<DClipNode_t>(1);
        clipnodes[0].PlaneNum = 0;
        clipnodes[0].Children[0] = (int)Contents.Empty;
        clipnodes[0].Children[1] = (int)Contents.Empty;

        MPlane_t* planes = AllocZeroed<MPlane_t>(1);
        planes[0].Normal[0] = 1;
        planes[0].Type = 0;
        planes[0].Dist = 0;

        Model_t* worldmodel = AllocZeroed<Model_t>(1);
        worldmodel->Type = ModType_t.Brush;
        for (int i = 0; i < BspFile.MAX_MAP_HULLS; i++)
        {
            worldmodel->Hulls[i].ClipNodes = clipnodes;
            worldmodel->Hulls[i].Planes = planes;
            worldmodel->Hulls[i].FirstClipNode = 0;
            worldmodel->Hulls[i].LastClipNode = 0;
        }

        Sv.worldmodel = worldmodel;
        Sv.models[0] = worldmodel;

        Sv.edicts[0].V.Solid = (int)SolidType.Bsp;
        Sv.edicts[0].V.MoveType = (int)MoveType.Push;
        Sv.edicts[0].V.ModelIndex = 0;

        World.ClearWorld();
    }

    private EDict_t* TestEnt => Progs.EdictNum(1);

    [TestMethod]
    public void ClipVelocity_OffFloorPlane_SlidesAndReportsFloorBlocked()
    {
        Vec3_t velocity = default;
        velocity[0] = 100;
        velocity[2] = -50;
        Vec3_t normal = default;
        normal[2] = 1; // floor

        Vec3_t outVel = default;
        int blocked = SvPhys.ClipVelocity(velocity, normal, ref outVel, 1);

        Assert.AreEqual(1, blocked & 1); // floor bit set
        Assert.AreEqual(100f, outVel[0], 0.001f); // tangential component unaffected
        Assert.AreEqual(0f, outVel[2], 0.001f);   // normal component removed
    }

    [TestMethod]
    public void ClipVelocity_VerticalPlane_ReportsStepBlocked()
    {
        Vec3_t velocity = default;
        velocity[0] = 50;
        Vec3_t normal = default; // normal[2] == 0 -> a wall/step plane
        normal[0] = 1;

        Vec3_t outVel = default;
        int blocked = SvPhys.ClipVelocity(velocity, normal, ref outVel, 1);

        Assert.AreEqual(2, blocked & 2);
    }

    // SV_ClipVelocity (sv_phys.c) reference: the original per-axis loop
    private static int ClipVelocityReference(Vec3_t @in, Vec3_t normal, out Vec3_t @out, float overbounce)
    {
        const float STOP_EPSILON = 0.1f;
        int blocked = 0;
        if (normal[2] > 0)
        {
            blocked |= 1;
        }

        if (normal[2] == 0)
        {
            blocked |= 2;
        }

        float backoff = ((@in[0] * normal[0]) + (@in[1] * normal[1]) + (@in[2] * normal[2])) * overbounce;

        @out = default;
        for (int i = 0; i < 3; i++)
        {
            float change = normal[i] * backoff;
            @out[i] = @in[i] - change;
            if (@out[i] > -STOP_EPSILON && @out[i] < STOP_EPSILON)
            {
                @out[i] = 0;
            }
        }
        return blocked;
    }

    // Edge values around the STOP_EPSILON window, signed zeros and NaN
    private static readonly float[] ClipSpecials = [0f, -0f, 0.1f, -0.1f, 0.09999999f, -0.09999999f, 0.10000001f, -0.10000001f, 1f, -1f, float.NaN];

    private static float ClipValue(Random rng) =>
        rng.Next(3) == 0 ? ClipSpecials[rng.Next(ClipSpecials.Length)] : (float)((rng.NextDouble() * 1000) - 500);

    [TestMethod]
    public void ClipVelocity_RandomAndEdgeInputs_MatchOriginalBitwise()
    {
        Random rng = new(24680);

        for (int iter = 0; iter < 20_000; iter++)
        {
            Vec3_t velocity = new(ClipValue(rng), ClipValue(rng), ClipValue(rng));
            Vec3_t normal = rng.Next(2) == 0
                ? Vec3_t.Normalize(new Vec3_t(ClipValue(rng), ClipValue(rng), ClipValue(rng)))
                : new Vec3_t(rng.Next(3) - 1, rng.Next(3) - 1, rng.Next(3) - 1); // axial and zero normals
            float overbounce = rng.Next(2) == 0 ? 1 : 1.5f;

            int expectedBlocked = ClipVelocityReference(velocity, normal, out Vec3_t expected, overbounce);
            Vec3_t actual = default;
            int actualBlocked = SvPhys.ClipVelocity(velocity, normal, ref actual, overbounce);

            Assert.AreEqual(expectedBlocked, actualBlocked, $"iteration {iter}: blocked");
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(actual[i]),
                    $"iteration {iter}: out[{i}] in={velocity} normal={normal}");
            }
        }
    }

    [TestMethod]
    public void ClipVelocity_InAndOutAliased_MatchesNonAliased()
    {
        Vec3_t velocity = new(123.5f, -0.05f, -40f);
        Vec3_t normal = Vec3_t.Normalize(new Vec3_t(0.3f, 0.2f, 1f));

        Vec3_t separate = default;
        int separateBlocked = SvPhys.ClipVelocity(velocity, normal, ref separate, 1.5f);
        int aliasedBlocked = SvPhys.ClipVelocity(velocity, normal, ref velocity, 1.5f);

        Assert.AreEqual(separateBlocked, aliasedBlocked);
        Assert.AreEqual(separate, velocity);
    }

    [TestMethod]
    public void CheckVelocity_ClampsToMaxVelocity()
    {
        EDict_t* ent = TestEnt;
        ent->V.Velocity[0] = Sv.sv_maxvelocity->Value + 500;
        ent->V.Velocity[1] = -(Sv.sv_maxvelocity->Value + 500);

        SvPhys.CheckVelocity(ent);

        Assert.AreEqual(Sv.sv_maxvelocity->Value, ent->V.Velocity[0]);
        Assert.AreEqual(-Sv.sv_maxvelocity->Value, ent->V.Velocity[1]);
    }

    [TestMethod]
    public void CheckVelocity_ZeroesOutNaN()
    {
        EDict_t* ent = TestEnt;
        float nan = BitConverter.Int32BitsToSingle(0x7fc00000);
        ent->V.Velocity[0] = nan;
        ent->V.Origin[1] = nan;

        SvPhys.CheckVelocity(ent);

        Assert.AreEqual(0f, ent->V.Velocity[0]);
        Assert.AreEqual(0f, ent->V.Origin[1]);
    }

    [TestMethod]
    public void AddGravity_ReducesVerticalVelocityBySvGravityTimesFrametime()
    {
        EDict_t* ent = TestEnt;
        ent->V.Velocity[2] = 0;

        SvPhys.AddGravity(ent);

        Assert.AreEqual(-Sv.sv_gravity->Value * Host.host_frametime, ent->V.Velocity[2], 0.001f);
    }

    [TestMethod]
    public void RunThink_NoPendingThink_ReturnsTrueWithoutExecutingProgram()
    {
        EDict_t* ent = TestEnt;
        ent->V.NextThink = 0; // <= 0 means nothing scheduled

        bool result = SvPhys.RunThink(ent);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void RunThink_ThinkFarInFuture_ReturnsTrueWithoutRunning()
    {
        EDict_t* ent = TestEnt;
        Sv.time = 0;
        Host.host_frametime = 0.1f;
        ent->V.NextThink = 100; // far beyond sv.time + host_frametime

        bool result = SvPhys.RunThink(ent);

        Assert.IsTrue(result);
        Assert.AreEqual(100f, ent->V.NextThink); // untouched
    }

    [TestMethod]
    public void Physics_Client_InactiveSlot_ReturnsWithoutTouchingEntity()
    {
        EDict_t* ent = TestEnt;
        Vec3_t before = ent->V.Origin;
        Svs.clients[0].Active = false;

        SvPhys.Physics_Client(ent, 1);

        Assert.AreEqual(before[0], ent->V.Origin[0]);
        Assert.AreEqual(before[1], ent->V.Origin[1]);
        Assert.AreEqual(before[2], ent->V.Origin[2]);
    }

    [TestMethod]
    public void Physics_Toss_FreeFallsUnderGravity_InAnEmptyWorld()
    {
        EDict_t* ent = TestEnt;
        ent->V.MoveType = (int)MoveType.Toss;
        ent->V.Solid = 2; // (int)SolidType.BBox (server.h)
        ent->V.Origin[2] = 1000; // high up, nothing below in this empty world

        SvPhys.Physics_Toss(ent);

        // fell under gravity for one host_frametime tick, and nothing solid
        // was hit (trace.fraction == 1), so origin[2] decreased and velocity
        // became negative.
        Assert.IsLessThan(0f, ent->V.Velocity[2]);
        Assert.IsLessThan(1000f, ent->V.Origin[2]);
    }

    [TestMethod]
    public void CloseEnough_WithinDistance_ReturnsTrue()
    {
        EDict_t* a = Progs.EdictNum(0);
        EDict_t* b = TestEnt;

        a->V.AbsMin = default; a->V.AbsMax = default;
        b->V.AbsMin = default; b->V.AbsMax = default;
        b->V.AbsMin[0] = 5; b->V.AbsMax[0] = 5;

        Assert.IsTrue(SvMove.CloseEnough(a, b, 10));
        Assert.IsFalse(SvMove.CloseEnough(a, b, 1));
    }

    [TestMethod]
    public void ChangeYaw_TurnsTowardIdealAtYawSpeed_WithoutOvershooting()
    {
        EDict_t* ent = TestEnt;
        ent->V.Angles[1] = 0;
        ent->V.IdealYaw = 90;
        ent->V.YawSpeed = 10;
        Progs.pr_global_struct->Self = Progs.EdictToProg(ent);

        SvMove.ChangeYaw();

        // anglemod quantizes to 360/65536-degree steps, same as the original.
        Assert.AreEqual(10f, ent->V.Angles[1], 0.01f);
    }

    [TestMethod]
    public void ChangeYaw_AlreadyAtIdeal_LeavesAngleUnchanged()
    {
        EDict_t* ent = TestEnt;
        ent->V.Angles[1] = 45;
        ent->V.IdealYaw = 45;
        ent->V.YawSpeed = 10;
        Progs.pr_global_struct->Self = Progs.EdictToProg(ent);

        SvMove.ChangeYaw();

        Assert.AreEqual(45f, ent->V.Angles[1], 0.001f);
    }

    [TestMethod]
    public void StartSound_LargeSoundNumber_Fitz_SetsLargeSoundAndWritesShort()
    {
        byte* dataBuf = stackalloc byte[1024];
        SizeBuf_t dg = default;
        dg.Data = dataBuf;
        dg.MaxSize = 1024;
        *Sv.datagram = dg;
        Sv.current_protocol = Protocol.PROTOCOL_FITZQUAKE;

        byte* sample = new Utf8String("weapons/r_exp3.wav"u8);
        for (int i = 1; i < 300; i++)
        {
            Sv.sound_precache[i] = new Utf8String("dummy"u8);
        }

        Sv.sound_precache[300] = sample;

        EDict_t* ent = TestEnt;
        SvPhys.StartSound(ent, 0, sample, 255, 1);

        Assert.AreEqual((byte)(int)Svc.Sound, dataBuf[0]);
        Assert.AreEqual(Protocol.SND_LARGESOUND, dataBuf[1]);
        Assert.AreEqual(1 << 3, dataBuf[2] | (dataBuf[3] << 8)); // ent 1, channel 0
        Assert.AreEqual(300, dataBuf[4] | (dataBuf[5] << 8));
    }

    [TestMethod]
    public void StartSound_LargeSoundNumber_Netquake_IsDropped()
    {
        byte* dataBuf = stackalloc byte[1024];
        SizeBuf_t dg = default;
        dg.Data = dataBuf;
        dg.MaxSize = 1024;
        *Sv.datagram = dg;
        Sv.current_protocol = Protocol.PROTOCOL_NETQUAKE;

        byte* sample = new Utf8String("weapons/r_exp3.wav"u8);
        for (int i = 1; i < 300; i++)
        {
            Sv.sound_precache[i] = new Utf8String("dummy"u8);
        }

        Sv.sound_precache[300] = sample;

        SvPhys.StartSound(TestEnt, 0, sample, 255, 1);

        Assert.AreEqual(0, Sv.datagram->CurSize);
    }

    [TestMethod]
    public void StartSound_Precached_WritesSvcSoundToDatagram()
    {
        byte* dataBuf = stackalloc byte[1024];
        SizeBuf_t dg = default;
        dg.Data = dataBuf;
        dg.MaxSize = 1024;
        *Sv.datagram = dg;

        byte* sample = new Utf8String("weapons/r_exp3.wav"u8);
        Sv.sound_precache[1] = sample;

        EDict_t* ent = TestEnt;
        ent->V.Origin = new Vec3_t { [0] = 1, [1] = 2, [2] = 3 };

        SvPhys.StartSound(ent, 0, sample, 255, 1);

        Assert.AreEqual((byte)(int)Svc.Sound, Sv.datagram->Data[0]);

        byte* readBuf = stackalloc byte[1024];
        new Span<byte>(Sv.datagram->Data, Sv.datagram->CurSize).CopyTo(new Span<byte>(readBuf, Sv.datagram->CurSize));
        Net.net_message->Data = readBuf; Net.net_message->MaxSize = Sv.datagram->CurSize; Msg.net_message->CurSize = Sv.datagram->CurSize;
        Msg.BeginReading();

        Assert.AreEqual((int)Svc.Sound, Msg.ReadByte());
        int field_mask = Msg.ReadByte();
        Assert.AreEqual(0, field_mask); // default volume (255) and attenuation (1) need no extra bytes
        int channel = Msg.ReadShort();
        Assert.AreEqual(1, channel >> 3); // entity 1 (TestEnt)
        Assert.AreEqual(0, channel & 7);
        Assert.AreEqual(1, Msg.ReadByte()); // sound_num
        Assert.AreEqual(1f, Msg.ReadCoord());
        Assert.AreEqual(2f, Msg.ReadCoord());
        Assert.AreEqual(3f, Msg.ReadCoord());
    }

    [TestMethod]
    public void StartSound_NotPrecached_PrintsErrorAndWritesNothing()
    {
        byte* dataBuf = stackalloc byte[1024];
        SizeBuf_t dg = default;
        dg.Data = dataBuf;
        dg.MaxSize = 1024;
        *Sv.datagram = dg;

        byte* sample = new Utf8String("missing.wav"u8);

        SvPhys.StartSound(TestEnt, 0, sample, 255, 1);

        Assert.AreEqual(0, Sv.datagram->CurSize);
    }
}
