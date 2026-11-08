// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// cl_tent.c -- client-side temporary entities (spike/explosion particle
/// hits, lightning/grapple beams).
/// </summary>
public static unsafe class ClTEnt
{
    public const int MAX_TEMP_ENTITIES = 256; // client.h -- Mark V MAX_FITZQUAKE_TEMP_ENTITIES (was 64)
    public const int MAX_BEAMS = 32;          // client.h -- Mark V MAX_FITZQUAKE_BEAMS (was 24)

    public static int num_temp_entities;

    // cl_temp_entities[MAX_TEMP_ENTITIES] -- NativeMemory-backed (not a
    // managed array): NewTempEntity hands out raw pointers into this that
    // are stored in ClEntities.cl_visedicts and dereferenced much later
    // during the render pass, so it needs the same pointer-stability
    // guarantee as Cl.cs's cl_entities/cl_static_entities/cl_dlights.
    public static readonly Entity_t* cl_temp_entities =
        NativeMemory.AllocArrayZeroed<Entity_t>(MAX_TEMP_ENTITIES);

    public static readonly Beam_t* cl_beams = NativeMemory.AllocArrayZeroed<Beam_t>(MAX_BEAMS);

    public static Sfx_t* cl_sfx_wizhit;
    public static Sfx_t* cl_sfx_knighthit;
    public static Sfx_t* cl_sfx_tink1;
    public static Sfx_t* cl_sfx_ric1;
    public static Sfx_t* cl_sfx_ric2;
    public static Sfx_t* cl_sfx_ric3;
    public static Sfx_t* cl_sfx_r_exp3;

    /*
    =================
    CL_InitTEnts
    =================
    */
    public static void InitTEnts()
    {
        cl_sfx_wizhit = SndDma.PrecacheSound("wizard/hit.wav"u8);
        cl_sfx_knighthit = SndDma.PrecacheSound("hknight/hit.wav"u8);
        cl_sfx_tink1 = SndDma.PrecacheSound("weapons/tink1.wav"u8);
        cl_sfx_ric1 = SndDma.PrecacheSound("weapons/ric1.wav"u8);
        cl_sfx_ric2 = SndDma.PrecacheSound("weapons/ric2.wav"u8);
        cl_sfx_ric3 = SndDma.PrecacheSound("weapons/ric3.wav"u8);
        cl_sfx_r_exp3 = SndDma.PrecacheSound("weapons/r_exp3.wav"u8);
    }

    /*
    =================
    CL_ParseBeam
    =================
    */
    public static void ParseBeam(Model_t* m)
    {
        int ent = Msg.ReadShort();

        Unsafe.SkipInit(out Vec3_t start);
        start[0] = Msg.ReadCoord();
        start[1] = Msg.ReadCoord();
        start[2] = Msg.ReadCoord();

        Unsafe.SkipInit(out Vec3_t end);
        end[0] = Msg.ReadCoord();
        end[1] = Msg.ReadCoord();
        end[2] = Msg.ReadCoord();

        // override any beam with the same entity
        for (int i = 0; i < MAX_BEAMS; i++)
        {
            {
                Beam_t* b = &cl_beams[i];
                if (b->Entity == ent)
                {
                    b->Entity = ent;
                    b->Model = m;
                    b->Endtime = (float)(Cl.time + 0.2);
                    b->Start = start;
                    b->End = end;
                    return;
                }
            }
        }

        // find a free beam
        for (int i = 0; i < MAX_BEAMS; i++)
        {
            {
                Beam_t* b = &cl_beams[i];
                if (b->Model == null || b->Endtime < Cl.time)
                {
                    b->Entity = ent;
                    b->Model = m;
                    b->Endtime = (float)(Cl.time + 0.2);
                    b->Start = start;
                    b->End = end;
                    return;
                }
            }
        }

        Con.Printf("beam list overflow!\n"u8);
    }

    /*
    =================
    CL_ParseTEnt
    =================
    */
    public static void ParseTEnt()
    {
        int type = Msg.ReadByte();
        switch ((TempEntityType)type)
        {
            case TempEntityType.WizSpike: // spike hitting wall
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.RunParticleEffect(pos, MathLib.vec3_origin, 20, 30);
                SndDma.StartSound(-1, 0, cl_sfx_wizhit, pos, 1, 1);
                break;
            }

            case TempEntityType.KnightSpike: // spike hitting wall
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.RunParticleEffect(pos, MathLib.vec3_origin, 226, 20);
                SndDma.StartSound(-1, 0, cl_sfx_knighthit, pos, 1, 1);
                break;
            }

            case TempEntityType.Spike: // spike hitting wall
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.RunParticleEffect(pos, MathLib.vec3_origin, 0, 10);
                if (Q.Rand() % 5 != 0)
                    SndDma.StartSound(-1, 0, cl_sfx_tink1, pos, 1, 1);
                else
                {
                    int rnd = Q.Rand() & 3;
                    if (rnd == 1)
                        SndDma.StartSound(-1, 0, cl_sfx_ric1, pos, 1, 1);
                    else if (rnd == 2)
                        SndDma.StartSound(-1, 0, cl_sfx_ric2, pos, 1, 1);
                    else
                        SndDma.StartSound(-1, 0, cl_sfx_ric3, pos, 1, 1);
                }
                break;
            }

            case TempEntityType.SuperSpike: // super spike hitting wall
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.RunParticleEffect(pos, MathLib.vec3_origin, 0, 20);

                if (Q.Rand() % 5 != 0)
                    SndDma.StartSound(-1, 0, cl_sfx_tink1, pos, 1, 1);
                else
                {
                    int rnd = Q.Rand() & 3;
                    if (rnd == 1)
                        SndDma.StartSound(-1, 0, cl_sfx_ric1, pos, 1, 1);
                    else if (rnd == 2)
                        SndDma.StartSound(-1, 0, cl_sfx_ric2, pos, 1, 1);
                    else
                        SndDma.StartSound(-1, 0, cl_sfx_ric3, pos, 1, 1);
                }
                break;
            }

            case TempEntityType.Gunshot: // bullet hitting wall
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.RunParticleEffect(pos, MathLib.vec3_origin, 0, 20);
                break;
            }

            case TempEntityType.Explosion: // rocket explosion
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.ParticleExplosion(pos);
                DLight_t* dl = ClMain.AllocDlight(0);
                dl->Origin = pos;
                dl->Radius = 350;
                dl->Die = (float)(Cl.time + 0.5);
                dl->Decay = 300;
                SndDma.StartSound(-1, 0, cl_sfx_r_exp3, pos, 1, 1);
                break;
            }

            case TempEntityType.TarExplosion: // tarbaby explosion
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.BlobExplosion(pos);
                SndDma.StartSound(-1, 0, cl_sfx_r_exp3, pos, 1, 1);
                break;
            }

            case TempEntityType.Lightning1: // lightning bolts
                ParseBeam(Model.ForName("progs/bolt.mdl"u8, true));
                break;

            case TempEntityType.Lightning2: // lightning bolts
                ParseBeam(Model.ForName("progs/bolt2.mdl"u8, true));
                break;

            case TempEntityType.Lightning3: // lightning bolts
                ParseBeam(Model.ForName("progs/bolt3.mdl"u8, true));
                break;

            case TempEntityType.Beam: // grappling hook beam
                ParseBeam(Model.ForName("progs/beam.mdl"u8, true));
                break;

            case TempEntityType.LavaSplash:
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.LavaSplash(pos);
                break;
            }

            case TempEntityType.Teleport:
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                RPart.TeleportSplash(pos);
                break;
            }

            case TempEntityType.Explosion2: // color mapped explosion
            {
                Vec3_t pos = default;
                pos[0] = Msg.ReadCoord();
                pos[1] = Msg.ReadCoord();
                pos[2] = Msg.ReadCoord();
                int colorStart = Msg.ReadByte();
                int colorLength = Msg.ReadByte();
                RPart.ParticleExplosion2(pos, colorStart, colorLength);
                DLight_t* dl = ClMain.AllocDlight(0);
                dl->Origin = pos;
                dl->Radius = 350;
                dl->Die = (float)(Cl.time + 0.5);
                dl->Decay = 300;
                SndDma.StartSound(-1, 0, cl_sfx_r_exp3, pos, 1, 1);
                break;
            }

            default:
                Sys.Error("CL_ParseTEnt: bad type"u8); // the original genuinely calls Sys_Error directly here, not Host_Error
                break;
        }
    }

    /*
    =================
    CL_NewTempEntity
    =================
    */
    public static Entity_t* NewTempEntity()
    {
        if (ClEntities.cl_numvisedicts == ClEntities.MAX_VISEDICTS)
            return null;
        if (num_temp_entities == MAX_TEMP_ENTITIES)
            return null;

        Entity_t* ent = &cl_temp_entities[num_temp_entities];
        *ent = default;
        num_temp_entities++;
        ClEntities.cl_visedicts[ClEntities.cl_numvisedicts] = ent;
        ClEntities.cl_numvisedicts++;

        ent->ColorMap = Vid.vid.Colormap;
        return ent;
    }

    /*
    =================
    CL_UpdateTEnts
    =================
    */
    public static void UpdateTEnts()
    {
        num_temp_entities = 0;

        // update lightning
        for (int i = 0; i < MAX_BEAMS; i++)
        {
            {
                Beam_t* b = &cl_beams[i];
                if (b->Model == null || b->Endtime < Cl.time)
                    continue;

                // if coming from the player, update the start position
                if (b->Entity == Cl.viewentity)
                    b->Start = ClEntities.cl_entities[Cl.viewentity].Origin;

                // calculate pitch and yaw
                Vec3_t dist = b->End - b->Start;

                float yaw, pitch;
                if (dist[1] == 0 && dist[0] == 0)
                {
                    yaw = 0;
                    pitch = dist[2] > 0 ? 90 : 270;
                }
                else
                {
                    yaw = (int)(Math.Atan2(dist[1], dist[0]) * 180 / Math.PI);
                    if (yaw < 0)
                        yaw += 360;

                    float forward = MathF.Sqrt(dist[0] * dist[0] + dist[1] * dist[1]);
                    pitch = (int)(Math.Atan2(dist[2], forward) * 180 / Math.PI);
                    if (pitch < 0)
                        pitch += 360;
                }

                // add new entities for the lightning
                Vec3_t org = b->Start;
                float d = MathLib.VectorNormalize(&dist);
                while (d > 0)
                {
                    Entity_t* ent = NewTempEntity();
                    if (ent == null)
                        return;
                    ent->Origin = org;
                    ent->Model = b->Model;
                    ent->Angles[0] = pitch;
                    ent->Angles[1] = yaw;
                    ent->Angles[2] = Q.Rand() % 360;

                    for (int j = 0; j < 3; j++)
                        org[j] += dist[j] * 30;
                    d -= 30;
                }
            }
        }
    }
}
