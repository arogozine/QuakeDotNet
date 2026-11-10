// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// protocol.h -- communications protocols. Pure constants; QUAKE2-only
/// values (TE_IMPLOSION/TE_RAILTRAIL) are dropped (QUAKE2 is never defined
/// for WinQuake, matching this port's existing convention elsewhere).
/// </summary>
public static class Protocol
{
    public const int PROTOCOL_VERSION = 15;

    // protocol.h (Mark V) -- the protocol enum
    public const int PROTOCOL_NETQUAKE = 15;
    public const int PROTOCOL_FITZQUAKE = 666;

    // Wire-format limits of protocol 15. Storage (QuakeDef.MAX_MODELS etc.) is sized to
    // Mark V's limits, but model/sound indices are sent as a byte, and an entity's
    // sound channel as (ent << 3) | chan in a short. Mark V applies the same
    // WinQuake limits (MAX_WINQUAKE_MODELS/SOUNDS) when running protocol 15.
    public const int MAX_P15_MODELS = 256;
    public const int MAX_P15_SOUNDS = 256;
    public const int MAX_P15_SOUND_ENTITIES = 8192;

    // if the high bit of the servercmd is set, the low bits are fast update flags:
    public const int U_MOREBITS = 1 << 0;
    public const int U_ORIGIN1 = 1 << 1;
    public const int U_ORIGIN2 = 1 << 2;
    public const int U_ORIGIN3 = 1 << 3;
    public const int U_ANGLE2 = 1 << 4;
    public const int U_NOLERP = 1 << 5; // don't interpolate movement
    public const int U_FRAME = 1 << 6;
    public const int U_SIGNAL = 1 << 7; // just differentiates from other updates

    // svc_update can pass all of the fast update bits, plus more
    public const int U_ANGLE1 = 1 << 8;
    public const int U_ANGLE3 = 1 << 9;
    public const int U_MODEL = 1 << 10;
    public const int U_COLORMAP = 1 << 11;
    public const int U_SKIN = 1 << 12;
    public const int U_EFFECTS = 1 << 13;
    public const int U_LONGENTITY = 1 << 14;

    // PROTOCOL_FITZQUAKE entity update bits (Mark V protocol.h)
    public const int U_STEP = U_NOLERP;     // Fitz name for bit 5
    public const int U_EXTEND1 = 1 << 15;   // a third bits byte (16-23) follows
    public const int U_ALPHA = 1 << 16;     // 1 byte, ENTALPHA_ENCODE'd; assumed to be 1.0 if not present
    public const int U_FRAME2 = 1 << 17;    // 1 byte, this is .frame & 0xFF00 (second byte)
    public const int U_MODEL2 = 1 << 18;    // 1 byte, this is .modelindex & 0xFF00 (second byte)
    public const int U_LERPFINISH = 1 << 19; // 1 byte, (nextthink - time) * 255
    // bits 20-22 unused
    public const int U_EXTEND2 = 1 << 23;   // another bits byte (24-31) follows

    // PROTOCOL_FITZQUAKE baseline/static bits (svc_spawnbaseline2, svc_spawnstatic2)
    public const int B_LARGEMODEL = 1 << 0; // modelindex is short instead of byte
    public const int B_LARGEFRAME = 1 << 1; // frame is short instead of byte
    public const int B_ALPHA = 1 << 2;      // 1 byte, ENTALPHA_ENCODE'd

    // ENTALPHA_* (Mark V protocol.h) -- entity alpha encoding
    public const int ENTALPHA_DEFAULT = 0;  // entity's alpha is "default" (i.e. water obeys r_wateralpha)
    public const int ENTALPHA_ZERO = 1;     // entity is invisible (lowest possible alpha)
    public const int ENTALPHA_ONE = 255;    // entity is fully opaque (highest possible alpha)

    // ENTALPHA_ENCODE -- a == 0 ? 0 : rint(clamp(a * 254 + 1, 1, 255))
    public static byte EntAlphaEncode(float a)
    {
        if (a == 0)
        {
            return ENTALPHA_DEFAULT;
        }

        float v = MathF.Round((a * 254) + 1, MidpointRounding.ToEven);
        return (byte)Math.Clamp(v, 1, 255);
    }

    // ENTALPHA_DECODE -- (a == ENTALPHA_DEFAULT) ? 1.0 : (a - 1) / 254.0
    public static float EntAlphaDecode(int a)
    {
        return a == ENTALPHA_DEFAULT ? 1.0f : (a - 1) / 254.0f;
    }

    public const int SU_VIEWHEIGHT = 1 << 0;
    public const int SU_IDEALPITCH = 1 << 1;
    public const int SU_PUNCH1 = 1 << 2;
    public const int SU_PUNCH2 = 1 << 3;
    public const int SU_PUNCH3 = 1 << 4;
    public const int SU_VELOCITY1 = 1 << 5;
    public const int SU_VELOCITY2 = 1 << 6;
    public const int SU_VELOCITY3 = 1 << 7;
    // SU_AIMENT (1<<8) -- AVAILABLE BIT in the original, never used.
    public const int SU_ITEMS = 1 << 9;
    public const int SU_ONGROUND = 1 << 10;  // no data follows, the bit is it
    public const int SU_INWATER = 1 << 11;   // no data follows, the bit is it
    public const int SU_WEAPONFRAME = 1 << 12;
    public const int SU_ARMOR = 1 << 13;
    public const int SU_WEAPON = 1 << 14;

    // PROTOCOL_FITZQUAKE clientdata bits (Mark V protocol.h)
    public const int SU_EXTEND1 = 1 << 15;      // another byte of bits (16-23) follows
    public const int SU_WEAPON2 = 1 << 16;      // 1 byte, this is .weaponmodel & 0xFF00 (second byte)
    public const int SU_ARMOR2 = 1 << 17;       // 1 byte, this is .armorvalue & 0xFF00 (second byte)
    public const int SU_AMMO2 = 1 << 18;        // 1 byte, this is .currentammo & 0xFF00 (second byte)
    public const int SU_SHELLS2 = 1 << 19;      // 1 byte, this is .ammo_shells & 0xFF00 (second byte)
    public const int SU_NAILS2 = 1 << 20;       // 1 byte, this is .ammo_nails & 0xFF00 (second byte)
    public const int SU_ROCKETS2 = 1 << 21;     // 1 byte, this is .ammo_rockets & 0xFF00 (second byte)
    public const int SU_CELLS2 = 1 << 22;       // 1 byte, this is .ammo_cells & 0xFF00 (second byte)
    public const int SU_EXTEND2 = 1 << 23;      // another byte of bits (24-31) follows
    public const int SU_WEAPONFRAME2 = 1 << 24; // 1 byte, this is .weaponframe & 0xFF00 (second byte)
    public const int SU_WEAPONALPHA = 1 << 25;  // 1 byte, this is alpha for weaponmodel, uses ENTALPHA_ENCODE, not sent if ENTALPHA_DEFAULT

    // a sound with no channel is a local only sound
    public const int SND_VOLUME = 1 << 0;      // a byte
    public const int SND_ATTENUATION = 1 << 1; // a byte
    public const int SND_LOOPING = 1 << 2;     // a long
    public const int SND_LARGEENTITY = 1 << 3; // a short + a byte (instead of a short)
    public const int SND_LARGESOUND = 1 << 4;  // a short soundindex (instead of a byte)

    // defaults for clientinfo messages
    public const int DEFAULT_VIEWHEIGHT = 22;

    // game types sent by serverinfo -- these determine which intermission
    // screen plays

    // server to client


    // svc_spawnbinary = 21 -- never implemented even in the original.









    // client to server

    // temp entity events
}
