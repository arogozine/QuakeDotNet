// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Numerics;
namespace Quake.Engine;

/// <summary>
/// d_part.c: software driver module for drawing particles.
/// Only the !id386 C fallback is ported.
/// </summary>
public static unsafe class DPart
{
    // D_EndParticles -- not used by the software driver
    public static void EndParticles() { }

    // D_StartParticles -- not used by the software driver
    public static void StartParticles() { }

    // D_DrawParticle
    public static void DrawParticle(Particle_t* pparticle)
    {
        Vec3_t local = pparticle->Org - RShared.r_origin;

        Vec3_t transformed = new(
            Vector3.Dot(local, DIface.r_pright),
            Vector3.Dot(local, DIface.r_pup),
            Vector3.Dot(local, DIface.r_ppn));

        if (transformed.Z < DIface.PARTICLE_Z_CLIP)
        {
            return;
        }

        // project the point
        float zi = 1.0f / transformed.Z;
        int u = float.ConvertToIntegerNative<int>(RShared.xcenter + (zi * transformed.X) + 0.5f);
        int v = float.ConvertToIntegerNative<int>(RShared.ycenter - (zi * transformed.Y) + 0.5f);

        if (v > DLocal.d_vrectbottom_particle ||
            u > DLocal.d_vrectright_particle ||
            v < DLocal.d_vrecty ||
            u < DLocal.d_vrectx)
        {
            return;
        }

        short* pz = DLocal.d_pzbuffer + (DLocal.d_zwidth * v) + u;
        byte* pdest = DLocal.d_viewbuffer + DLocal.d_scantable[v] + u;
        int izi = float.ConvertToIntegerNative<int>(zi * 0x8000);

        // pix = izi >> d_pix_shift -- linear scale instead of a shift, see
        // DModech.ViewChanged's own comment.
        int pix = float.ConvertToIntegerNative<int>(izi * DLocal.d_pix_scale);

        if (pix < DLocal.d_pix_min)
        {
            pix = DLocal.d_pix_min;
        }
        else if (pix > DLocal.d_pix_max)
        {
            pix = DLocal.d_pix_max;
        }

        int count = pix << DLocal.d_y_aspect_shift;

        for (; count > 0; count--, pz += DLocal.d_zwidth, pdest += RShared.screenwidth)
        {
            for (int i = 0; i < pix; i++)
            {
                if (pz[i] <= izi)
                {
                    pz[i] = (short)izi;
                    pdest[i] = (byte)pparticle->Color;
                }
            }
        }
    }
}
