// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.Engine;

/// <summary>
/// r_part.c: particle system -- spawning, aging, and drawing.
/// </summary>
public static unsafe class RPart
{
    private const int MAX_PARTICLES = 2048;        // default max # of particles at one time
    private const int ABSOLUTE_MIN_PARTICLES = 512; // no fewer than this no matter what's on the command line


    // ramp3 is only initialized with 6 of its 8 elements in the original
    // (`int ramp3[8] = {0x6d, 0x6b, 6, 5, 4, 3};`) -- the trailing two stay
    // 0, matched here rather than declaring a 6-element array.
    public static readonly int[] ramp1 = { 0x6f, 0x6d, 0x6b, 0x69, 0x67, 0x65, 0x63, 0x61 };
    public static readonly int[] ramp2 = { 0x6f, 0x6e, 0x6d, 0x6c, 0x6b, 0x6a, 0x68, 0x66 };
    public static readonly int[] ramp3 = { 0x6d, 0x6b, 6, 5, 4, 3, 0, 0 };

    public static Particle_t* active_particles, free_particles;

    public static Particle_t* particles;
    public static int r_numparticles;

    // r_pright/r_pup/r_ppn already live in DIface.cs -- reused.

    internal const int NUMVERTEXNORMALS = 162;
    internal static readonly Vec3_t* avelocities = NativeMemory.AllocArrayZeroed<Vec3_t>(NUMVERTEXNORMALS);
    private const float beamlength = 16;

    // R_InitParticles
    public static void InitParticles()
    {
        int i = Com.CheckParm("-particles"u8);

        if (i != 0)
        {
            r_numparticles = Q.Atoi(Com.com_argv[i + 1]);
            if (r_numparticles < ABSOLUTE_MIN_PARTICLES)
            {
                r_numparticles = ABSOLUTE_MIN_PARTICLES;
            }
        }
        else
        {
            r_numparticles = MAX_PARTICLES;
        }

        particles = (Particle_t*)Hunk.AllocName(r_numparticles * sizeof(Particle_t), "particles"u8);
    }

    // R_EntityParticles
    public static void EntityParticles(Entity_t* ent)
    {
        const float dist = 64;

        // (rand()&255) * 0.01 seeded once into the flat avelocities buffer
        // (original: `if (!avelocities[0][0]) for(...) avelocities[0][i]=...`
        // -- a flat write across the whole 2D array via its first element's
        // address).
        if (avelocities[0][0] == 0)
        {
            float* flat = (float*)avelocities;
            for (int i = 0; i < NUMVERTEXNORMALS * 3; i++)
            {
                flat[i] = (Q.Rand() & 255) * 0.01f;
            }
        }

        for (int i = 0; i < NUMVERTEXNORMALS; i++)
        {
            float angle = (float)(Cl.time * avelocities[i][0]);
            (float sy, float cy) = MathF.SinCos(angle);
            angle = (float)(Cl.time * avelocities[i][1]);
            (float sp, float cp) = MathF.SinCos(angle);

            Vec3_t forward = default;
            forward[0] = cp * cy;
            forward[1] = cp * sy;
            forward[2] = -sp;

            if (free_particles == null)
            {
                return;
            }

            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            p->Die = (float)(Cl.time + 0.01);
            p->Color = 0x6f;
            p->Type = PType_t.Explode;

            p->Org = ent->Origin + (RAlias.r_avertexnormals[i] * dist) + (forward * beamlength);
        }
    }

    // R_ClearParticles
    public static void ClearParticles()
    {
        free_particles = &particles[0];
        active_particles = null;

        for (int i = 0; i < r_numparticles; i++)
        {
            particles[i].Next = &particles[i + 1];
        }

        particles[r_numparticles - 1].Next = null;
    }

    // R_ReadPointFile_f
    public static void ReadPointFile_f()
    {
        byte* name = stackalloc byte[QuakeDef.MAX_OSPATH];
        Q.StrCpy(name, "maps/"u8);
        Q.StrCat(name, Sv.Name);
        Q.StrCat(name, ".pts"u8);

        nint fileHandle;
        int len = Com.FOpenFile(name, &fileHandle);
        if (fileHandle == -1)
        {
            Con.Printf($"couldn't open {name}\n");
            return;
        }

        Con.Printf($"Reading {name}...\n");

        byte* buf = NativeMemory.AllocArray<byte>(len);
        Sys.FileRead((int)fileHandle, buf, len);
        Sys.FileClose((int)fileHandle);

        // fscanf(f, "%f %f %f\n", ...) in a loop, stopping at the first line
        // that doesn't parse as three floats -- reimplemented as whitespace
        // tokenization over the whole buffer (migrated off CRT stdio per
        // CLAUDE.md rule 5; the original's FILE* interface doesn't exist in
        // this port).
        string text = Encoding.Latin1.GetString(buf, len);
        NativeMemory.Free(buf);
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        int c = 0;
        int tok = 0;
        while (tok + 3 <= tokens.Length)
        {
            if (!float.TryParse(tokens[tok], out float x) ||
                !float.TryParse(tokens[tok + 1], out float y) ||
                !float.TryParse(tokens[tok + 2], out float z))
            {
                break;
            }

            tok += 3;
            c++;

            if (free_particles == null)
            {
                Con.Printf("Not enough free particles\n"u8);
                break;
            }
            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            p->Die = 99999;
            p->Color = (-c) & 15;
            p->Type = PType_t.Static;
            p->Vel = MathLib.vec3_origin;
            Vec3_t org = default;
            org[0] = x; org[1] = y; org[2] = z;
            p->Org = org;
        }

        Con.Printf($"{c} points read\n");
    }

    // R_ParseParticleEffect
    // Parse an effect out of the server message
    public static void ParseParticleEffect()
    {
        Vec3_t org = default, dir = default;
        for (int i = 0; i < 3; i++)
        {
            org[i] = Msg.ReadCoord();
        }

        for (int i = 0; i < 3; i++)
        {
            dir[i] = Msg.ReadChar() * (1.0f / 16);
        }

        int msgcount = Msg.ReadByte();
        int color = Msg.ReadByte();

        int count = msgcount == 255 ? 1024 : msgcount;

        RunParticleEffect(org, dir, color, count);
    }

    // R_ParticleExplosion
    public static void ParticleExplosion(Vec3_t org)
    {
        for (int i = 0; i < 1024; i++)
        {
            if (free_particles == null)
            {
                return;
            }

            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            p->Die = (float)(Cl.time + 5);
            p->Color = ramp1[0];
            p->Ramp = Q.Rand() & 3;
            p->Type = (i & 1) != 0 ? PType_t.Explode : PType_t.Explode2;
            for (int j = 0; j < 3; j++)
            {
                p->Org[j] = org[j] + ((Q.Rand() % 32) - 16);
                p->Vel[j] = (Q.Rand() % 512) - 256;
            }
        }
    }

    // R_ParticleExplosion2
    public static void ParticleExplosion2(Vec3_t org, int colorStart, int colorLength)
    {
        int colorMod = 0;

        for (int i = 0; i < 512; i++)
        {
            if (free_particles == null)
            {
                return;
            }

            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            p->Die = (float)(Cl.time + 0.3);
            p->Color = colorStart + (colorMod % colorLength);
            colorMod++;

            p->Type = PType_t.Blob;
            for (int j = 0; j < 3; j++)
            {
                p->Org[j] = org[j] + ((Q.Rand() % 32) - 16);
                p->Vel[j] = (Q.Rand() % 512) - 256;
            }
        }
    }

    // R_BlobExplosion
    public static void BlobExplosion(Vec3_t org)
    {
        for (int i = 0; i < 1024; i++)
        {
            if (free_particles == null)
            {
                return;
            }

            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            p->Die = (float)(Cl.time + 1 + ((Q.Rand() & 8) * 0.05));

            if ((i & 1) != 0)
            {
                p->Type = PType_t.Blob;
                p->Color = 66 + (Q.Rand() % 6);
            }
            else
            {
                p->Type = PType_t.Blob2;
                p->Color = 150 + (Q.Rand() % 6);
            }
            for (int j = 0; j < 3; j++)
            {
                p->Org[j] = org[j] + ((Q.Rand() % 32) - 16);
                p->Vel[j] = (Q.Rand() % 512) - 256;
            }
        }
    }

    // R_RunParticleEffect
    public static void RunParticleEffect(Vec3_t org, Vec3_t dir, int color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (free_particles == null)
            {
                return;
            }

            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            if (count == 1024)
            {
                // rocket explosion
                p->Die = (float)(Cl.time + 5);
                p->Color = ramp1[0];
                p->Ramp = Q.Rand() & 3;
                p->Type = (i & 1) != 0 ? PType_t.Explode : PType_t.Explode2;
                for (int j = 0; j < 3; j++)
                {
                    p->Org[j] = org[j] + ((Q.Rand() % 32) - 16);
                    p->Vel[j] = (Q.Rand() % 512) - 256;
                }
            }
            else
            {
                p->Die = (float)(Cl.time + (0.1 * (Q.Rand() % 5)));
                p->Color = (color & ~7) + (Q.Rand() & 7);
                p->Type = PType_t.SlowGrav;
                for (int j = 0; j < 3; j++)
                {
                    p->Org[j] = org[j] + (Q.Rand() & 15) - 8;
                    p->Vel[j] = dir[j] * 15; // + (rand()%300)-150;
                }
            }
        }
    }

    // R_LavaSplash
    public static void LavaSplash(Vec3_t org)
    {
        for (int i = -16; i < 16; i++)
        {
            for (int j = -16; j < 16; j++)
            {
                if (free_particles == null)
                {
                    return;
                }

                Particle_t* p = free_particles;
                free_particles = p->Next;
                p->Next = active_particles;
                active_particles = p;

                p->Die = (float)(Cl.time + 2 + ((Q.Rand() & 31) * 0.02));
                p->Color = 224 + (Q.Rand() & 7);
                p->Type = PType_t.SlowGrav;

                Vec3_t dir = default;
                dir[0] = (j * 8) + (Q.Rand() & 7);
                dir[1] = (i * 8) + (Q.Rand() & 7);
                dir[2] = 256;

                p->Org[0] = org[0] + dir[0];
                p->Org[1] = org[1] + dir[1];
                p->Org[2] = org[2] + (Q.Rand() & 63);

                float vel = 50 + (Q.Rand() & 63);
                MathLib.VectorNormalize(&dir);
                p->Vel = dir * vel;
            }
        }
    }

    // R_TeleportSplash
    public static void TeleportSplash(Vec3_t org)
    {
        for (int i = -16; i < 16; i += 4)
        {
            for (int j = -16; j < 16; j += 4)
            {
                for (int k = -24; k < 32; k += 4)
                {
                    if (free_particles == null)
                    {
                        return;
                    }

                    Particle_t* p = free_particles;
                    free_particles = p->Next;
                    p->Next = active_particles;
                    active_particles = p;

                    p->Die = (float)(Cl.time + 0.2 + ((Q.Rand() & 7) * 0.02));
                    p->Color = 7 + (Q.Rand() & 7);
                    p->Type = PType_t.SlowGrav;

                    Vec3_t dir = default;
                    dir[0] = j * 8;
                    dir[1] = i * 8;
                    dir[2] = k * 8;

                    p->Org[0] = org[0] + i + (Q.Rand() & 3);
                    p->Org[1] = org[1] + j + (Q.Rand() & 3);
                    p->Org[2] = org[2] + k + (Q.Rand() & 3);

                    float vel = 50 + (Q.Rand() & 63);
                    MathLib.VectorNormalize(&dir);
                    p->Vel = dir * vel;
                }
            }
        }
    }

    // R_RocketTrail
    private static int tracercount;

    public static void RocketTrail(Vec3_t start, Vec3_t end, int type)
    {
        Vec3_t vec = end - start;
        float len = MathLib.VectorNormalize(&vec);

        int dec;
        if (type < 128)
        {
            dec = 3;
        }
        else
        {
            dec = 1;
            type -= 128;
        }

        while (len > 0)
        {
            len -= dec;

            if (free_particles == null)
            {
                return;
            }

            Particle_t* p = free_particles;
            free_particles = p->Next;
            p->Next = active_particles;
            active_particles = p;

            p->Vel = MathLib.vec3_origin;
            p->Die = (float)(Cl.time + 2);

            switch (type)
            {
                case 0: // rocket trail
                    p->Ramp = Q.Rand() & 3;
                    p->Color = ramp3[(int)p->Ramp];
                    p->Type = PType_t.Fire;
                    for (int j = 0; j < 3; j++)
                    {
                        p->Org[j] = start[j] + ((Q.Rand() % 6) - 3);
                    }

                    break;

                case 1: // smoke smoke
                    p->Ramp = (Q.Rand() & 3) + 2;
                    p->Color = ramp3[(int)p->Ramp];
                    p->Type = PType_t.Fire;
                    for (int j = 0; j < 3; j++)
                    {
                        p->Org[j] = start[j] + ((Q.Rand() % 6) - 3);
                    }

                    break;

                case 2: // blood
                    p->Type = PType_t.Grav;
                    p->Color = 67 + (Q.Rand() & 3);
                    for (int j = 0; j < 3; j++)
                    {
                        p->Org[j] = start[j] + ((Q.Rand() % 6) - 3);
                    }

                    break;

                case 3:
                case 5: // tracer
                    p->Die = (float)(Cl.time + 0.5);
                    p->Type = PType_t.Static;
                    p->Color = type == 3
                        ? 52 + ((tracercount & 4) << 1)
                        : 230 + ((tracercount & 4) << 1);

                    tracercount++;

                    p->Org = start;
                    if ((tracercount & 1) != 0)
                    {
                        p->Vel[0] = 30 * vec[1];
                        p->Vel[1] = 30 * -vec[0];
                    }
                    else
                    {
                        p->Vel[0] = 30 * -vec[1];
                        p->Vel[1] = 30 * vec[0];
                    }
                    break;

                case 4: // slight blood
                    p->Type = PType_t.Grav;
                    p->Color = 67 + (Q.Rand() & 3);
                    for (int j = 0; j < 3; j++)
                    {
                        p->Org[j] = start[j] + ((Q.Rand() % 6) - 3);
                    }

                    len -= 3;
                    break;

                case 6: // voor trail
                    p->Color = (9 * 16) + 8 + (Q.Rand() & 3);
                    p->Type = PType_t.Static;
                    p->Die = (float)(Cl.time + 0.3);
                    for (int j = 0; j < 3; j++)
                    {
                        p->Org[j] = start[j] + (Q.Rand() & (15 - 8));
                    }

                    break;
            }

            start = start + vec;
        }
    }

    // R_DrawParticles
    public static void DrawParticles()
    {
        DPart.StartParticles();

        DIface.r_pright = RShared.vright * RShared.xscaleshrink;
        DIface.r_pup = RShared.vup * RShared.yscaleshrink;
        DIface.r_ppn = RShared.vpn;

        float frametime = (float)(Cl.time - Cl.oldtime);
        float time3 = frametime * 15;
        float time2 = frametime * 10; // 15;
        float time1 = frametime * 5;
        float grav = frametime * Sv.sv_gravity->Value * 0.05f; // extern cvar_t sv_gravity (sv_phys.c, registered by SV_Init)
        float dvel = 4 * frametime;

        for (; ; )
        {
            Particle_t* kill = active_particles;
            if (kill != null && kill->Die < Cl.time)
            {
                active_particles = kill->Next;
                kill->Next = free_particles;
                free_particles = kill;
                continue;
            }
            break;
        }

        for (Particle_t* p = active_particles; p != null; p = p->Next)
        {
            for (; ; )
            {
                Particle_t* kill = p->Next;
                if (kill != null && kill->Die < Cl.time)
                {
                    p->Next = kill->Next;
                    kill->Next = free_particles;
                    free_particles = kill;
                    continue;
                }
                break;
            }

            DPart.DrawParticle(p);

            // per lane org[i] + vel[i] * frametime, as in the original
            p->Org += p->Vel * frametime;

            switch (p->Type)
            {
                case PType_t.Static:
                    break;
                case PType_t.Fire:
                    p->Ramp += time1;
                    if (p->Ramp >= 6)
                    {
                        p->Die = -1;
                    }
                    else
                    {
                        p->Color = ramp3[(int)p->Ramp];
                    }

                    p->Vel[2] += grav;
                    break;

                case PType_t.Explode:
                    p->Ramp += time2;
                    if (p->Ramp >= 8)
                    {
                        p->Die = -1;
                    }
                    else
                    {
                        p->Color = ramp1[(int)p->Ramp];
                    }

                    p->Vel += p->Vel * dvel;
                    p->Vel[2] -= grav;
                    break;

                case PType_t.Explode2:
                    p->Ramp += time3;
                    if (p->Ramp >= 8)
                    {
                        p->Die = -1;
                    }
                    else
                    {
                        p->Color = ramp2[(int)p->Ramp];
                    }

                    p->Vel -= p->Vel * frametime;
                    p->Vel[2] -= grav;
                    break;

                case PType_t.Blob:
                    p->Vel += p->Vel * dvel;
                    p->Vel[2] -= grav;
                    break;

                case PType_t.Blob2:
                    for (int i = 0; i < 2; i++)
                    {
                        p->Vel[i] -= p->Vel[i] * dvel;
                    }

                    p->Vel[2] -= grav;
                    break;

                case PType_t.Grav:
                case PType_t.SlowGrav:
                    p->Vel[2] -= grav;
                    break;
            }
        }

        DPart.EndParticles();
    }

}
