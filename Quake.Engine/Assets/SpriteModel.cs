// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;

namespace Quake.Engine;

// spriteframetype_t
public enum SpriteFrameType_t
{
    Single = 0,
    Group,
}

/// <summary>
/// model.c -- Mod_LoadSpriteModel and friends (.spr sprite model loading).
/// Unlike alias models, sprites are stored directly on the Hunk (mod->cache.data
/// is set to a raw Hunk_AllocName pointer -- no Cache_Alloc call, no Hunk
/// low-mark scratch/relocate dance), matching the original exactly.
/// </summary>
public static unsafe class SpriteModel
{
    public const int SPRITE_VERSION = 1;


    // Mod_LoadSpriteFrame
    // 8-bit only: this port has no software-renderer palette expansion path,
    // so the original's r_pixbytes==2 (16bpp, d_8to16table) branch is
    // dropped -- consistent with Mod_LoadAliasSkin.
    public static void* LoadSpriteFrame(void* pin, MSpriteFrame_t** ppframe)
    {
        DSpriteFrame_t* pinframe = (DSpriteFrame_t*)pin;

        int width = Com.LittleLong(pinframe->Width);
        int height = Com.LittleLong(pinframe->Height);
        int size = width * height;

        MSpriteFrame_t* pspriteframe = (MSpriteFrame_t*)Hunk.AllocName(sizeof(MSpriteFrame_t) + size, Model.loadname);

        Q.MemSet(pspriteframe, 0, sizeof(MSpriteFrame_t) + size);
        *ppframe = pspriteframe;

        pspriteframe->Width = width;
        pspriteframe->Height = height;
        int origin0 = Com.LittleLong(pinframe->Origin[0]);
        int origin1 = Com.LittleLong(pinframe->Origin[1]);

        pspriteframe->Up = origin1;
        pspriteframe->Down = origin1 - height;
        pspriteframe->Left = origin0;
        pspriteframe->Right = width + origin0;

        Q.MemCpy(&pspriteframe->Pixels[0], (byte*)(pinframe + 1), size);

        return (byte*)(pinframe + 1) + size;
    }

    // Mod_LoadSpriteGroup
    public static void* LoadSpriteGroup(void* pin, MSpriteFrame_t** ppframe)
    {
        DSpriteGroup_t* pingroup = (DSpriteGroup_t*)pin;

        int numframes = Com.LittleLong(pingroup->NumFrames);

        MSpriteGroup_t* pspritegroup = (MSpriteGroup_t*)Hunk.AllocName(
            sizeof(MSpriteGroup_t) + (numframes - 1) * sizeof(MSpriteFrame_t*), Model.loadname);

        pspritegroup->NumFrames = numframes;

        // frames[i] for a group frame stores the *group's* address (cast to
        // mspriteframe_t*) -- the renderer re-casts it back using the type
        // tag. Kept exactly as the original, however odd it looks.
        *ppframe = (MSpriteFrame_t*)pspritegroup;

        DSpriteInterval_t* pin_intervals = (DSpriteInterval_t*)(pingroup + 1);

        float* poutintervals = (float*)Hunk.AllocName(numframes * sizeof(float), Model.loadname);

        pspritegroup->Intervals = poutintervals;

        for (int i = 0; i < numframes; i++)
        {
            *poutintervals = Com.LittleFloat(pin_intervals->Interval);
            if (*poutintervals <= 0.0f)
                Sys.Error("Mod_LoadSpriteGroup: interval<=0"u8);

            poutintervals++;
            pin_intervals++;
        }

        void* ptemp = pin_intervals;

        MSpriteFrame_t** frames = &pspritegroup->Frames;
        for (int i = 0; i < numframes; i++)
            ptemp = LoadSpriteFrame(ptemp, &frames[i]);

        return ptemp;
    }

    // Mod_LoadSpriteModel
    public static void LoadSpriteModel(Model_t* mod, void* buffer)
    {
        DSprite_t* pin = (DSprite_t*)buffer;

        int version = Com.LittleLong(pin->Version);
        if (version != SPRITE_VERSION)
            Sys.Error($"{mod->Name} has wrong version number ({version} should be {SPRITE_VERSION})");

        int numframes = Com.LittleLong(pin->NumFrames);

        int size = sizeof(MSprite_t) + (numframes - 1) * sizeof(MSpriteFrameDesc_t);

        MSprite_t* psprite = (MSprite_t*)Hunk.AllocName(size, Model.loadname);

        mod->Cache.Data = psprite;

        psprite->Type = (SpriteType)Com.LittleLong((int)pin->Type);
        psprite->MaxWidth = Com.LittleLong(pin->Width);
        psprite->MaxHeight = Com.LittleLong(pin->Height);
        psprite->BeamLength = Com.LittleFloat(pin->BeamLength);
        mod->SyncType = (SyncType_t)Com.LittleLong((int)pin->SyncType);
        psprite->NumFrames = numframes;

        mod->Mins[0] = mod->Mins[1] = -(psprite->MaxWidth / 2);
        mod->Maxs[0] = mod->Maxs[1] = psprite->MaxWidth / 2;
        mod->Mins[2] = -(psprite->MaxHeight / 2);
        mod->Maxs[2] = psprite->MaxHeight / 2;

        // load the frames
        if (numframes < 1)
            Sys.Error($"Mod_LoadSpriteModel: Invalid # of frames: {numframes}");

        mod->NumFrames = numframes;
        mod->Flags = 0;

        DSpriteFrameType_t* pframetype = (DSpriteFrameType_t*)(pin + 1);

        MSpriteFrameDesc_t* frames = &psprite->Frames;
        for (int i = 0; i < numframes; i++)
        {
            SpriteFrameType_t frametype = (SpriteFrameType_t)Com.LittleLong((int)pframetype->Type);
            frames[i].Type = frametype;

            pframetype = frametype == SpriteFrameType_t.Single
                ? (DSpriteFrameType_t*)LoadSpriteFrame(pframetype + 1, &frames[i].FramePtr)
                : (DSpriteFrameType_t*)LoadSpriteGroup(pframetype + 1, &frames[i].FramePtr);
        }

        mod->Type = ModType_t.Sprite;
    }
}
