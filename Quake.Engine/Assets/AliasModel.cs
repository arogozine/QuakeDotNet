// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;

namespace Quake.Engine;

// aliasframetype_t
public enum AliasFrameType_t
{
    Single = 0,
    Group,
}

// aliasskintype_t
public enum AliasSkinType_t
{
    Single = 0,
    Group,
}

/// <summary>
/// model.c -- Mod_LoadAliasModel and friends (.mdl alias model loading).
/// </summary>
public static unsafe class AliasModel
{
    public const int ALIAS_VERSION = 6;

    // Offset -- readable pointer arithmetic for the byte-offset relocation
    // scheme described on AliasHdr_t.
    public static void* Offset(AliasHdr_t* hdr, int ofs) => (byte*)hdr + ofs;

    // Mod_LoadAliasFrame
    public static void* LoadAliasFrame(void* pin, int* pframeindex, int numv,
        TriVertX_t* pbboxmin, TriVertX_t* pbboxmax, AliasHdr_t* pheader, byte* name)
    {
        DAliasFrame_t* pdaliasframe = (DAliasFrame_t*)pin;

        Q.StrCpy(name, pdaliasframe->Name);

        for (int i = 0; i < 3; i++)
        {
            // these are byte values, so we don't have to worry about endianness
            pbboxmin->V[i] = pdaliasframe->BboxMin.V[i];
            pbboxmax->V[i] = pdaliasframe->BboxMax.V[i];
        }

        TriVertX_t* pinframe = (TriVertX_t*)(pdaliasframe + 1);
        TriVertX_t* pframe = (TriVertX_t*)Hunk.AllocName(numv * sizeof(TriVertX_t), Model.loadname);

        *pframeindex = (int)((byte*)pframe - (byte*)pheader);

        for (int j = 0; j < numv; j++)
        {
            // these are all byte values, so no need to deal with endianness
            pframe[j].LightNormalIndex = pinframe[j].LightNormalIndex;

            for (int k = 0; k < 3; k++)
            {
                pframe[j].V[k] = pinframe[j].V[k];
            }
        }

        pinframe += numv;

        return pinframe;
    }

    // Mod_LoadAliasGroup
    public static void* LoadAliasGroup(void* pin, int* pframeindex, int numv,
        TriVertX_t* pbboxmin, TriVertX_t* pbboxmax, AliasHdr_t* pheader, byte* name)
    {
        DAliasGroup_t* pingroup = (DAliasGroup_t*)pin;

        int numframes = Com.LittleLong(pingroup->NumFrames);

        MAliasGroup_t* paliasgroup = (MAliasGroup_t*)Hunk.AllocName(
            sizeof(MAliasGroup_t) + ((numframes - 1) * sizeof(MAliasGroupFrameDesc_t)), Model.loadname);

        paliasgroup->NumFrames = numframes;

        for (int i = 0; i < 3; i++)
        {
            // these are byte values, so we don't have to worry about endianness
            pbboxmin->V[i] = pingroup->BboxMin.V[i];
            pbboxmax->V[i] = pingroup->BboxMax.V[i];
        }

        *pframeindex = (int)((byte*)paliasgroup - (byte*)pheader);

        DAliasInterval_t* pin_intervals = (DAliasInterval_t*)(pingroup + 1);

        float* poutintervals = (float*)Hunk.AllocName(numframes * sizeof(float), Model.loadname);

        paliasgroup->Intervals = (int)((byte*)poutintervals - (byte*)pheader);

        for (int i = 0; i < numframes; i++)
        {
            *poutintervals = Com.LittleFloat(pin_intervals->Interval);
            if (*poutintervals <= 0.0f)
            {
                Sys.Error("Mod_LoadAliasGroup: interval<=0"u8);
            }

            poutintervals++;
            pin_intervals++;
        }

        void* ptemp = pin_intervals;

        MAliasGroupFrameDesc_t* frames = &paliasgroup->Frames;
        for (int i = 0; i < numframes; i++)
        {
            ptemp = LoadAliasFrame(ptemp, &frames[i].Frame, numv,
                &frames[i].BboxMin, &frames[i].BboxMax, pheader, name);
        }

        return ptemp;
    }

    // Mod_LoadAliasSkin
    // 8-bit only: the r_pixbytes==2 (16bpp) branch is dropped -- this port has
    // no software-renderer palette expansion path.
    public static void* LoadAliasSkin(void* pin, int* pskinindex, int skinsize, AliasHdr_t* pheader)
    {
        byte* pskin = (byte*)Hunk.AllocName(skinsize, Model.loadname);
        byte* pinskin = (byte*)pin;
        *pskinindex = (int)(pskin - (byte*)pheader);

        Q.MemCpy(pskin, pinskin, skinsize);

        pinskin += skinsize;

        return pinskin;
    }

    // Mod_LoadAliasSkinGroup
    public static void* LoadAliasSkinGroup(void* pin, int* pskinindex, int skinsize, AliasHdr_t* pheader)
    {
        DAliasSkinGroup_t* pinskingroup = (DAliasSkinGroup_t*)pin;

        int numskins = Com.LittleLong(pinskingroup->NumSkins);

        MAliasSkinGroup_t* paliasskingroup = (MAliasSkinGroup_t*)Hunk.AllocName(
            sizeof(MAliasSkinGroup_t) + ((numskins - 1) * sizeof(MAliasSkinDesc_t)), Model.loadname);

        paliasskingroup->NumSkins = numskins;

        *pskinindex = (int)((byte*)paliasskingroup - (byte*)pheader);

        DAliasSkinInterval_t* pinskinintervals = (DAliasSkinInterval_t*)(pinskingroup + 1);

        float* poutskinintervals = (float*)Hunk.AllocName(numskins * sizeof(float), Model.loadname);

        paliasskingroup->Intervals = (int)((byte*)poutskinintervals - (byte*)pheader);

        for (int i = 0; i < numskins; i++)
        {
            *poutskinintervals = Com.LittleFloat(pinskinintervals->Interval);
            if (*poutskinintervals <= 0)
            {
                Sys.Error("Mod_LoadAliasSkinGroup: interval<=0"u8);
            }

            poutskinintervals++;
            pinskinintervals++;
        }

        void* ptemp = pinskinintervals;

        MAliasSkinDesc_t* skindescs = &paliasskingroup->SkinDescs;
        for (int i = 0; i < numskins; i++)
        {
            ptemp = LoadAliasSkin(ptemp, &skindescs[i].Skin, skinsize, pheader);
        }

        return ptemp;
    }

    // Mod_LoadAliasModel
    public static void LoadAliasModel(Model_t* mod, void* buffer)
    {
        int start = Hunk.LowMark();

        Mdl_t* pinmodel = (Mdl_t*)buffer;

        int version = Com.LittleLong(pinmodel->Version);
        if (version != ALIAS_VERSION)
        {
            Sys.Error($"{mod->Name} has wrong version number ({version} should be {ALIAS_VERSION})");
        }

        // allocate space for a working header, plus all the data except the
        // frames, skin and group info
        int numframes0 = Com.LittleLong(pinmodel->NumFrames);
        int size = sizeof(AliasHdr_t) + ((numframes0 - 1) * sizeof(MAliasFrameDesc_t))
                 + sizeof(Mdl_t)
                 + (Com.LittleLong(pinmodel->NumVerts) * sizeof(StVert_t))
                 + (Com.LittleLong(pinmodel->NumTris) * sizeof(MTriangle_t));

        AliasHdr_t* pheader = (AliasHdr_t*)Hunk.AllocName(size, Model.loadname);
        Mdl_t* pmodel = (Mdl_t*)((byte*)(pheader + 1) + ((numframes0 - 1) * sizeof(MAliasFrameDesc_t)));

        mod->Flags = Com.LittleLong(pinmodel->Flags);
        if (ModelFlags.IsNoLerp(mod->Name)) // Mod_Flags_Refresh_f (model.c, Mark_V)
        {
            mod->Flags |= ModelFlags.MOD_NOLERP;
        }

        // endian-adjust and copy the data, starting with the alias model header
        pmodel->BoundingRadius = Com.LittleFloat(pinmodel->BoundingRadius);
        pmodel->NumSkins = Com.LittleLong(pinmodel->NumSkins);
        pmodel->SkinWidth = Com.LittleLong(pinmodel->SkinWidth);
        pmodel->SkinHeight = Com.LittleLong(pinmodel->SkinHeight);

        if (pmodel->SkinHeight > DIface.MAX_LBM_HEIGHT)
        {
            Sys.Error($"model {mod->Name} has a skin taller than {DIface.MAX_LBM_HEIGHT}");
        }

        pmodel->NumVerts = Com.LittleLong(pinmodel->NumVerts);

        if (pmodel->NumVerts <= 0)
        {
            Sys.Error($"model {mod->Name} has no vertices");
        }

        if (pmodel->NumVerts > RLocal.MAXALIASVERTS)
        {
            Sys.Error($"model {mod->Name} has too many vertices");
        }

        pmodel->NumTris = Com.LittleLong(pinmodel->NumTris);

        if (pmodel->NumTris <= 0)
        {
            Sys.Error($"model {mod->Name} has no triangles");
        }

        pmodel->NumFrames = Com.LittleLong(pinmodel->NumFrames);
        pmodel->Size = Com.LittleFloat(pinmodel->Size) * RLocal.ALIAS_BASE_SIZE_RATIO;
        mod->SyncType = (SyncType_t)Com.LittleLong((int)pinmodel->SyncType);
        mod->NumFrames = pmodel->NumFrames;

        for (int i = 0; i < 3; i++)
        {
            pmodel->Scale[i] = Com.LittleFloat(pinmodel->Scale[i]);
            pmodel->ScaleOrigin[i] = Com.LittleFloat(pinmodel->ScaleOrigin[i]);
            pmodel->EyePosition[i] = Com.LittleFloat(pinmodel->EyePosition[i]);
        }

        int numskins = pmodel->NumSkins;
        int numframes = pmodel->NumFrames;

        if ((pmodel->SkinWidth & 0x03) != 0)
        {
            Sys.Error("Mod_LoadAliasModel: skinwidth not multiple of 4"u8);
        }

        pheader->Model = (int)((byte*)pmodel - (byte*)pheader);

        // load the skins
        int skinsize = pmodel->SkinHeight * pmodel->SkinWidth;

        if (numskins < 1)
        {
            Sys.Error($"Mod_LoadAliasModel: Invalid # of skins: {numskins}");
        }

        DAliasSkinType_t* pskintype = (DAliasSkinType_t*)(pinmodel + 1);

        MAliasSkinDesc_t* pskindesc = (MAliasSkinDesc_t*)Hunk.AllocName(numskins * sizeof(MAliasSkinDesc_t), Model.loadname);

        pheader->SkinDesc = (int)((byte*)pskindesc - (byte*)pheader);

        for (int i = 0; i < numskins; i++)
        {
            AliasSkinType_t skintype = (AliasSkinType_t)Com.LittleLong((int)pskintype->Type);
            pskindesc[i].Type = skintype;

            pskintype = skintype == AliasSkinType_t.Single
                ? (DAliasSkinType_t*)LoadAliasSkin(pskintype + 1, &pskindesc[i].Skin, skinsize, pheader)
                : (DAliasSkinType_t*)LoadAliasSkinGroup(pskintype + 1, &pskindesc[i].Skin, skinsize, pheader);
        }

        // set base s and t vertices
        StVert_t* pstverts = (StVert_t*)(pmodel + 1);
        StVert_t* pinstverts = (StVert_t*)pskintype;

        pheader->StVerts = (int)((byte*)pstverts - (byte*)pheader);

        for (int i = 0; i < pmodel->NumVerts; i++)
        {
            pstverts[i].OnSeam = Com.LittleLong(pinstverts[i].OnSeam);
            // put s and t in 16.16 format
            pstverts[i].S = Com.LittleLong(pinstverts[i].S) << 16;
            pstverts[i].T = Com.LittleLong(pinstverts[i].T) << 16;
        }

        // set up the triangles
        MTriangle_t* ptri = (MTriangle_t*)(pstverts + pmodel->NumVerts);
        DTriangle_t* pintriangles = (DTriangle_t*)(pinstverts + pmodel->NumVerts);

        pheader->Triangles = (int)((byte*)ptri - (byte*)pheader);

        for (int i = 0; i < pmodel->NumTris; i++)
        {
            ptri[i].FacesFront = Com.LittleLong(pintriangles[i].FacesFront);

            for (int j = 0; j < 3; j++)
            {
                ptri[i].VertIndex[j] = Com.LittleLong(pintriangles[i].VertIndex[j]);
            }
        }

        // load the frames
        if (numframes < 1)
        {
            Sys.Error($"Mod_LoadAliasModel: Invalid # of frames: {numframes}");
        }

        DAliasFrameType_t* pframetype = (DAliasFrameType_t*)(pintriangles + pmodel->NumTris);

        MAliasFrameDesc_t* frames = &pheader->Frames;
        for (int i = 0; i < numframes; i++)
        {
            AliasFrameType_t frametype = (AliasFrameType_t)Com.LittleLong((int)pframetype->Type);
            frames[i].Type = frametype;

            pframetype = frametype == AliasFrameType_t.Single
                ? (DAliasFrameType_t*)LoadAliasFrame(pframetype + 1, &frames[i].Frame,
                    pmodel->NumVerts, &frames[i].BboxMin, &frames[i].BboxMax, pheader, frames[i].Name)
                : (DAliasFrameType_t*)LoadAliasGroup(pframetype + 1, &frames[i].Frame,
                    pmodel->NumVerts, &frames[i].BboxMin, &frames[i].BboxMax, pheader, frames[i].Name);
        }

        mod->Type = ModType_t.Alias;

        // FIXME: do this right
        mod->Mins[0] = mod->Mins[1] = mod->Mins[2] = -16;
        mod->Maxs[0] = mod->Maxs[1] = mod->Maxs[2] = 16;

        // move the complete, relocatable alias model to the cache
        int end = Hunk.LowMark();
        int total = end - start;

        Cache.Alloc(&mod->Cache, total, Model.loadname);
        if (mod->Cache.Data == null)
        {
            return;
        }

        Q.MemCpy(mod->Cache.Data, pheader, total);

        Hunk.FreeToLowMark(start);
    }
}
