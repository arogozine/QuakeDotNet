// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

// mnode_t / mleaf_t share their first 4 fields (contents, visframe, minmaxs,
// parent) so a node's children[] can point at either a node or a leaf,
// discriminated purely by contents<0 -- keep that prefix identical between
// the two structs.

// modtype_t
public enum ModType_t
{
    Brush,
    Sprite,
    Alias,
}

// synctype_t (modelgen.h)
public enum SyncType_t
{
    Sync,
    Rand,
}

// model.h's EF_* -- model_t.flags bits set by qcc/the alias model compiler
// (renders as a trail/rotation in cl_main.c's ClMain.RelinkEntities), not to
// be confused with entvars_t/entity_t.effects' EF_BRIGHTFIELD/EF_MUZZLEFLASH/
// EF_BRIGHTLIGHT/EF_DIMLIGHT bits (Sv.cs) -- distinct bit spaces in the
// original too.
public static unsafe class ModelFlags
{
    public const int EF_ROCKET = 1;   // leave a trail
    public const int EF_GRENADE = 2;  // leave a trail
    public const int EF_GIB = 4;      // leave a trail
    public const int EF_ROTATE = 8;   // rotate (bonus items)
    public const int EF_TRACER = 16;  // green split trail
    public const int EF_ZOMGIB = 32;  // small blood trail
    public const int EF_TRACER2 = 64; // orange split trail + rotate
    public const int EF_TRACER3 = 128; // purple trail

    // model.h MOD_NOLERP (johnfitz, Mark_V): don't blend animation poses of this model.
    // Outside the mdl file's own flag bits (those only use 0..255).
    public const int MOD_NOLERP = 256;

    // Mark_V cvar_list_sheet.h r_nolerp_list default, which Mod_Flags_Refresh_f matches
    // against the model name. Fixed list here (the r_nolerp_list cvar isn't ported).
    private static readonly Utf8String[] NoLerpList =
    [
        "progs/flame.mdl"u8, "progs/flame2.mdl"u8, "progs/braztall.mdl"u8, "progs/brazshrt.mdl"u8,
        "progs/longtrch.mdl"u8, "progs/flame_pyre.mdl"u8, "progs/v_saw.mdl"u8, "progs/v_xfist.mdl"u8,
        "progs/h2stuff/newfire.mdl"u8,
    ];

    // COM_ListMatch(r_nolerp_list.string, mod->name) from Mod_Flags_Refresh_f
    public static bool IsNoLerp(byte* name)
    {
        foreach (byte* n in NoLerpList)
            if (Q.StrCaseCmp(n, name) == 0)
                return true;
        return false;
    }

}

/// <summary>
/// model.c -- model loading and caching. Brush-model loading lives here;
/// alias (.mdl) and sprite (.spr) loading are ported in AliasModel.cs/
/// SpriteModel.cs and dispatched from Mod_LoadModel below by magic number.
/// </summary>
public static unsafe class Model
{
    // IDPOLYHEADER / IDSPRITEHEADER (modelgen.h / spritegn.h) -- Mod_LoadModel's
    // magic-number dispatch to AliasModel.LoadAliasModel/SpriteModel.LoadSpriteModel.
    public const int IDPOLYHEADER = ('O' << 24) + ('P' << 16) + ('D' << 8) + 'I';
    public const int IDSPRITEHEADER = ('P' << 24) + ('S' << 16) + ('D' << 8) + 'I';

    // values for model_t's needload

    internal const int MAX_MOD_KNOWN = 2048; // model.c -- johnfitz/Mark V (was 256)

    // mod_known
    public static readonly Model_t* mod_known = NativeMemory.AllocArrayZeroed<Model_t>(MAX_MOD_KNOWN);

    // mod_numknown
    public static int mod_numknown;

    // mod_novis
    internal const int MOD_NOVIS_SIZE = BspFile.MAX_MAP_LEAFS / 8;
    public static readonly byte* mod_novis = NativeMemory.AllocArrayZeroed<byte>(MOD_NOVIS_SIZE);

    // loadmodel
    public static Model_t* loadmodel;

    // loadname -- for hunk tags
    // internal so AliasModel.cs / SpriteModel.cs (which share this file-scope
    // state with model.c's brush loader in the original) can use it too.
    // char loadname[32] -- a native buffer (rule 9), read as a C string by
    // Hunk.AllocName/Cache.Alloc.
    internal static readonly byte* loadname = NativeMemory.AllocArrayZeroed<byte>(32);

    // mod_base
    internal static byte* mod_base;

    private static readonly Utf8String skyName = "sky"u8;
    private static readonly Utf8String turbName = "*"u8;

    // Mod_Init
    public static void Init()
    {
        Q.MemSet(mod_novis, 0xff, MOD_NOVIS_SIZE);
    }

    // Mod_Print
    public static void Print()
    {
        Con.Printf("Cached models:\n"u8);
        for (int i = 0; i < mod_numknown; i++)
        {
            Model_t* mod = mod_known + i;
            Con.Printf($"{(nint)mod->Cache.Data:x8} : {mod->Name}");
            if ((mod->NeedLoad & (int)NeedLoad.Unreferenced) != 0)
                Con.Printf(" (!R)"u8);
            if ((mod->NeedLoad & (int)NeedLoad.NeedsLoaded) != 0)
                Con.Printf(" (!P)"u8);
            Con.Printf("\n"u8);
        }
    }

    // Mod_Extradata
    public static void* Extradata(Model_t* mod)
    {
        void* r = Cache.Check(&mod->Cache);
        if (r != null)
            return r;

        LoadModel(mod, true);

        if (mod->Cache.Data == null)
            Sys.Error("Mod_Extradata: caching failed"u8);
        return mod->Cache.Data;
    }

    // Mod_PointInLeaf
    public static MLeaf_t* PointInLeaf(in Vec3_t p, Model_t* model)
    {
        if (model == null || model->Nodes == null)
            Sys.Error("Mod_PointInLeaf: bad model"u8);

        MNode_t* node = model->Nodes;
        while (true)
        {
            if (node->Contents < 0)
                return (MLeaf_t*)node;
            MPlane_t* plane = node->Plane;

            Vec3_t normal = plane->Normal;
            float d = Vector3.Dot(p, normal) - plane->Dist;
            node = d > 0 ? node->Children[0] : node->Children[1];
        }
    }

    // Mod_DecompressVis
    internal static readonly byte* decompressed = NativeMemory.AllocArrayZeroed<byte>(MOD_NOVIS_SIZE);

    public static byte* DecompressVis(byte* @in, Model_t* model)
    {
        int row = (model->NumLeafs + 7) >> 3;
        byte* @out = decompressed;

        if (@in == null)
        {
            // no vis info, so make all visible
            for (int i = 0; i < row; i++)
                *@out++ = 0xff;
            return decompressed;
        }

        do
        {
            if (*@in != 0)
            {
                *@out++ = *@in++;
                continue;
            }

            int c = @in[1];
            @in += 2;
            if (c != 0)
            {
                NativeMemory.Clear(@out, c);
                @out += c;
            }
        } while (@out - decompressed < row);

        return decompressed;
    }

    // Mod_LeafPVS
    public static byte* LeafPVS(MLeaf_t* leaf, Model_t* model)
    {
        if (leaf == model->Leafs)
            return mod_novis;
        return DecompressVis(leaf->CompressedVis, model);
    }

    // Mod_ClearAll
    public static void ClearAll()
    {
        for (int i = 0; i < mod_numknown; i++)
        {
            Model_t* mod = mod_known + i;
            mod->NeedLoad = (int)NeedLoad.Unreferenced;
            // FIX FOR CACHE_ALLOC ERRORS:
            if (mod->Type == ModType_t.Sprite)
                mod->Cache.Data = null;
        }
    }

    // Mod_FindName
    public static Model_t* FindName(byte* name)
    {
        if (*name == 0)
            Sys.Error("Mod_ForName: NULL name"u8);

        Model_t* avail = null;
        int i;
        Model_t* mod = mod_known;
        for (i = 0; i < mod_numknown; i++, mod++)
        {
            if (Q.StrCmp(mod->Name, name) == 0)
                break;
            if (mod->NeedLoad == (int)NeedLoad.Unreferenced)
                if (avail == null || mod->Type != ModType_t.Alias)
                    avail = mod;
        }

        if (i == mod_numknown)
        {
            if (mod_numknown == MAX_MOD_KNOWN)
            {
                if (avail != null)
                {
                    mod = avail;
                    if (mod->Type == ModType_t.Alias)
                        if (Cache.Check(&mod->Cache) != null)
                            Cache.Free(&mod->Cache);
                }
                else
                {
                    Sys.Error("mod_numknown == MAX_MOD_KNOWN"u8);
                }
            }
            else
            {
                mod_numknown++;
            }
            Q.StrCpy(mod->Name, name);
            mod->NeedLoad = (int)NeedLoad.NeedsLoaded;
        }

        return mod;
    }

    // Mod_TouchModel
    public static void TouchModel(byte* name)
    {
        Model_t* mod = FindName(name);

        if (mod->NeedLoad == (int)NeedLoad.Present)
            if (mod->Type == ModType_t.Alias)
                Cache.Check(&mod->Cache);
    }

    // Mod_LoadModel
    [SkipLocalsInit]
    public static Model_t* LoadModel(Model_t* mod, bool crash)
    {
        if (mod->Type == ModType_t.Alias)
        {
            if (Cache.Check(&mod->Cache) != null)
            {
                mod->NeedLoad = (int)NeedLoad.Present;
                return mod;
            }
        }
        else
        {
            if (mod->NeedLoad == (int)NeedLoad.Present)
                return mod;
        }

        // because the world is so huge, load it one piece at a time

        // load the file
        byte* stackbuf = stackalloc byte[1024];
        byte* buf = Com.LoadStackFile(mod->Name, stackbuf, 1024);
        if (buf == null)
        {
            if (crash)
                Sys.Error($"Mod_NumForName: {mod->Name} not found");
            return null;
        }

        // allocate a new model
        byte* baseNameBuf = stackalloc byte[32];
        Com.FileBase(mod->Name, baseNameBuf);
        Q.StrCpy(loadname, baseNameBuf);

        loadmodel = mod;

        // fill it in

        // call the apropriate loader
        mod->NeedLoad = (int)NeedLoad.Present;

        switch (Com.LittleLong(*(int*)buf))
        {
            case IDPOLYHEADER:
                AliasModel.LoadAliasModel(mod, buf);
                break;

            case IDSPRITEHEADER:
                SpriteModel.LoadSpriteModel(mod, buf);
                break;

            default:
                LoadBrushModel(mod, buf);
                break;
        }

        return mod;
    }

    // Mod_ForName
    public static Model_t* ForName(Utf8String name, bool crash)
    {
        Model_t* mod = FindName(name);

        return LoadModel(mod, crash);
    }

    /*
    ===============================================================================

                        BRUSHMODEL LOADING

    ===============================================================================
    */

    // Mod_LoadTextures
    public static void LoadTextures(Lump_t* l)
    {
        if (l->FileLen == 0)
        {
            loadmodel->Textures = null;
            return;
        }

        DMipTexLump_t* m = (DMipTexLump_t*)(mod_base + l->FileOfs);

        m->NumMipTex = Com.LittleLong(m->NumMipTex);

        loadmodel->NumTextures = m->NumMipTex;
        loadmodel->Textures = (Texture_t**)Hunk.AllocName(m->NumMipTex * sizeof(Texture_t*), loadname);

        Texture_t** anims = stackalloc Texture_t*[10];
        Texture_t** altanims = stackalloc Texture_t*[10];

        for (int i = 0; i < m->NumMipTex; i++)
        {
            m->DataOfs[i] = Com.LittleLong(m->DataOfs[i]);
            if (m->DataOfs[i] == -1)
                continue;

            MipTex_t* mt = (MipTex_t*)((byte*)m + m->DataOfs[i]);
            mt->Width = (uint)Com.LittleLong((int)mt->Width);
            mt->Height = (uint)Com.LittleLong((int)mt->Height);
            for (int j = 0; j < BspFile.MIPLEVELS; j++)
                mt->Offsets[j] = (uint)Com.LittleLong((int)mt->Offsets[j]);

            if ((mt->Width & 15) != 0 || (mt->Height & 15) != 0)
                Sys.Error($"Texture {mt->Name} is not 16 aligned");

            int pixels = (int)(mt->Width * mt->Height / 64 * 85);
            Texture_t* tx = (Texture_t*)Hunk.AllocName(sizeof(Texture_t) + pixels, loadname);
            loadmodel->Textures[i] = tx;

            Q.MemCpy(tx->Name, mt->Name, 16);
            tx->Width = mt->Width;
            tx->Height = mt->Height;
            for (int j = 0; j < BspFile.MIPLEVELS; j++)
                tx->Offsets[j] = mt->Offsets[j] + (uint)(sizeof(Texture_t) - sizeof(MipTex_t));

            // the pixels immediately follow the structures
            Q.MemCpy(tx + 1, mt + 1, pixels);

            if (Q.StrNCmp(mt->Name, skyName, 3) == 0)
                RSky.InitSky(tx);
        }

        // sequence the animations
        const int ANIM_CYCLE = 2;
        for (int i = 0; i < m->NumMipTex; i++)
        {
            Texture_t* tx = loadmodel->Textures[i];
            if (tx == null || tx->Name[0] != (byte)'+')
                continue;
            if (tx->AnimNext != null)
                continue; // already sequenced

            // find the number of frames in the animation
            for (int k = 0; k < 10; k++)
            {
                anims[k] = null;
                altanims[k] = null;
            }

            int max = tx->Name[1];
            int altmax = 0;
            if (max is >= (byte)'a' and <= (byte)'z')
                max -= 'a' - 'A';
            if (max is >= (byte)'0' and <= (byte)'9')
            {
                max -= '0';
                altmax = 0;
                anims[max] = tx;
                max++;
            }
            else if (max is >= (byte)'A' and <= (byte)'J')
            {
                altmax = max - 'A';
                max = 0;
                altanims[altmax] = tx;
                altmax++;
            }
            else
            {
                Sys.Error($"Bad animating texture {tx->Name}");
            }

            for (int j = i + 1; j < m->NumMipTex; j++)
            {
                Texture_t* tx2 = loadmodel->Textures[j];
                if (tx2 == null || tx2->Name[0] != (byte)'+')
                    continue;
                if (Q.StrCmp(tx2->Name + 2, tx->Name + 2) != 0)
                    continue;

                int num = tx2->Name[1];
                if (num is >= (byte)'a' and <= (byte)'z')
                    num -= 'a' - 'A';
                if (num is >= (byte)'0' and <= (byte)'9')
                {
                    num -= '0';
                    anims[num] = tx2;
                    if (num + 1 > max)
                        max = num + 1;
                }
                else if (num is >= (byte)'A' and <= (byte)'J')
                {
                    num -= 'A';
                    altanims[num] = tx2;
                    if (num + 1 > altmax)
                        altmax = num + 1;
                }
                else
                {
                    Sys.Error($"Bad animating texture {tx->Name}");
                }
            }

            // link them all together
            for (int j = 0; j < max; j++)
            {
                Texture_t* tx2 = anims[j];
                if (tx2 == null)
                    Sys.Error($"Missing frame {j} of {tx->Name}");
                tx2->AnimTotal = max * ANIM_CYCLE;
                tx2->AnimMin = j * ANIM_CYCLE;
                tx2->AnimMax = (j + 1) * ANIM_CYCLE;
                tx2->AnimNext = anims[(j + 1) % max];
                if (altmax != 0)
                    tx2->AlternateAnims = altanims[0];
            }
            for (int j = 0; j < altmax; j++)
            {
                Texture_t* tx2 = altanims[j];
                if (tx2 == null)
                    Sys.Error($"Missing frame {j} of {tx->Name}");
                tx2->AnimTotal = altmax * ANIM_CYCLE;
                tx2->AnimMin = j * ANIM_CYCLE;
                tx2->AnimMax = (j + 1) * ANIM_CYCLE;
                tx2->AnimNext = altanims[(j + 1) % altmax];
                if (max != 0)
                    tx2->AlternateAnims = anims[0];
            }
        }
    }

    // Not in the original: WinQuake and Mark V trust lump offsets/lengths from
    // the .bsp, so this is a debug-only guard against the loaded file's size.
    [Conditional("DEBUG")]
    private static void AssertLumpInFile(Lump_t* l)
    {
        Debug.Assert(l->FileOfs >= 0 && l->FileLen >= 0 && (long)l->FileOfs + l->FileLen <= Com.com_filesize,
            "Mod_Load*: lump outside the loaded file");
    }

    // Mod_LoadLighting
    public static void LoadLighting(Lump_t* l)
    {
        if (l->FileLen == 0)
        {
            loadmodel->LightData = null;
            return;
        }
        AssertLumpInFile(l);
        loadmodel->LightData = (byte*)Hunk.AllocName(l->FileLen, loadname);
        Q.MemCpy(loadmodel->LightData, mod_base + l->FileOfs, l->FileLen);
    }

    // Mod_LoadVisibility
    public static void LoadVisibility(Lump_t* l)
    {
        if (l->FileLen == 0)
        {
            loadmodel->VisData = null;
            return;
        }
        AssertLumpInFile(l);
        loadmodel->VisData = (byte*)Hunk.AllocName(l->FileLen, loadname);
        Q.MemCpy(loadmodel->VisData, mod_base + l->FileOfs, l->FileLen);
    }

    // Mod_LoadEntities
    public static void LoadEntities(Lump_t* l)
    {
        if (l->FileLen == 0)
        {
            loadmodel->Entities = null;
            return;
        }
        AssertLumpInFile(l);
        loadmodel->Entities = (byte*)Hunk.AllocName(l->FileLen, loadname);
        Q.MemCpy(loadmodel->Entities, mod_base + l->FileOfs, l->FileLen);
    }

    // Mod_LoadVertexes
    public static void LoadVertexes(Lump_t* l)
    {
        if (l->FileLen % sizeof(DVertex_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DVertex_t);
        DVertex_t* @in = (DVertex_t*)(mod_base + l->FileOfs);
        MVertex_t* @out = (MVertex_t*)Hunk.AllocName(count * sizeof(MVertex_t), loadname);

        loadmodel->Vertexes = @out;
        loadmodel->NumVertexes = count;

        // dvertex_t and mvertex_t are both three packed floats, so on a
        // little-endian host Mod_LoadVertexes' per-component swap is a plain copy.
        if (!Com.bigendien)
        {
            Q.MemCpy(@out, @in, count * sizeof(DVertex_t));
            return;
        }

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            *@out = new Vec3_t(
                Com.LittleFloat(@in->X),
                Com.LittleFloat(@in->Y),
                Com.LittleFloat(@in->Z));
        }
    }

    // Mod_LoadSubmodels
    public static void LoadSubmodels(Lump_t* l)
    {
        if (l->FileLen % sizeof(DModel_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DModel_t);
        DModel_t* @in = (DModel_t*)(mod_base + l->FileOfs);
        DModel_t* @out = (DModel_t*)Hunk.AllocName(count * sizeof(DModel_t), loadname);

        loadmodel->Submodels = @out;
        loadmodel->NumSubmodels = count;

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            for (int j = 0; j < 3; j++)
            {
                // spread the mins / maxs by a pixel
                @out->Mins[j] = Com.LittleFloat(@in->Mins[j]) - 1;
                @out->Maxs[j] = Com.LittleFloat(@in->Maxs[j]) + 1;
                @out->Origin[j] = Com.LittleFloat(@in->Origin[j]);
            }
            for (int j = 0; j < BspFile.MAX_MAP_HULLS; j++)
                @out->HeadNode[j] = Com.LittleLong(@in->HeadNode[j]);
            @out->VisLeafs = Com.LittleLong(@in->VisLeafs);
            @out->FirstFace = Com.LittleLong(@in->FirstFace);
            @out->NumFaces = Com.LittleLong(@in->NumFaces);
        }
    }

    // Mod_LoadEdges
    public static void LoadEdges(Lump_t* l)
    {
        if (l->FileLen % sizeof(DEdge_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DEdge_t);
        DEdge_t* @in = (DEdge_t*)(mod_base + l->FileOfs);
        MEdge_t* @out = (MEdge_t*)Hunk.AllocName((count + 1) * sizeof(MEdge_t), loadname);

        loadmodel->Edges = @out;
        loadmodel->NumEdges = count;

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            @out->V[0] = (ushort)Com.LittleShort((short)@in->V[0]);
            @out->V[1] = (ushort)Com.LittleShort((short)@in->V[1]);
        }
    }

    // Mod_LoadTexinfo
    public static void LoadTexinfo(Lump_t* l)
    {
        if (l->FileLen % sizeof(TexInfo_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(TexInfo_t);
        TexInfo_t* @in = (TexInfo_t*)(mod_base + l->FileOfs);
        MTexInfo_t* @out = (MTexInfo_t*)Hunk.AllocName(count * sizeof(MTexInfo_t), loadname);

        loadmodel->Texinfo = @out;
        loadmodel->NumTexinfo = count;

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            for (int j = 0; j < 8; j++)
                @out->Vecs[j] = Com.LittleFloat(@in->Vecs[j]);

            Vec3_t vec0 = *(Vec3_t*)@out->Vecs;
            Vec3_t vec1 = *(Vec3_t*)(@out->Vecs + 4);
            float len1 = vec0.Length();
            float len2 = vec1.Length();
            len1 = (len1 + len2) / 2;
            if (len1 < 0.32)
                @out->MipAdjust = 4;
            else if (len1 < 0.49)
                @out->MipAdjust = 3;
            else if (len1 < 0.99)
                @out->MipAdjust = 2;
            else
                @out->MipAdjust = 1;

            int miptex = Com.LittleLong(@in->MipTex);
            @out->Flags = Com.LittleLong(@in->Flags);

            if (loadmodel->Textures == null)
            {
                @out->Texture = RMain.r_notexture_mip; // checkerboard texture
                @out->Flags = 0;
            }
            else
            {
                if (miptex >= loadmodel->NumTextures)
                    Sys.Error("miptex >= loadmodel->numtextures"u8);
                @out->Texture = loadmodel->Textures[miptex];
                if (@out->Texture == null)
                {
                    @out->Texture = RMain.r_notexture_mip; // texture not found
                    @out->Flags = 0;
                }
            }
        }
    }

    // CalcSurfaceExtents
    // Fills in s->texturemins[] and s->extents[]
    public static void CalcSurfaceExtents(MSurface_t* s)
    {
        Span<float> mins = [999999, 999999];
        Span<float> maxs = [-99999, -99999];

        MTexInfo_t* tex = s->TexInfo;

        for (int i = 0; i < s->NumEdges; i++)
        {
            int e = loadmodel->Surfedges[s->FirstEdge + i];
            MVertex_t* v = e >= 0
                ? &loadmodel->Vertexes[loadmodel->Edges[e].V[0]]
                : &loadmodel->Vertexes[loadmodel->Edges[-e].V[1]];

            for (int j = 0; j < 2; j++)
            {
                float val = Vector3.Dot(*v, *(Vector3*)(tex->Vecs + j * 4))
                     + tex->Vecs[j * 4 + 3];

                if (val < mins[j])
                    mins[j] = val;
                if (val > maxs[j])
                    maxs[j] = val;
            }
        }

        for (int i = 0; i < 2; i++)
        {
            int bmins = (int)MathF.Floor(mins[i] / 16);
            int bmaxs = (int)MathF.Ceiling(maxs[i] / 16);

            s->TextureMins[i] = (short)(bmins * 16);
            s->Extents[i] = (short)((bmaxs - bmins) * 16);
            if ((tex->Flags & BspFile.TEX_SPECIAL) == 0 && s->Extents[i] > 256)
                Sys.Error("Bad surface extents"u8);
        }
    }

    // Mod_LoadFaces
    public static void LoadFaces(Lump_t* l)
    {
        if (l->FileLen % sizeof(DFace_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DFace_t);
        DFace_t* @in = (DFace_t*)(mod_base + l->FileOfs);
        MSurface_t* @out = (MSurface_t*)Hunk.AllocName(count * sizeof(MSurface_t), loadname);

        loadmodel->Surfaces = @out;
        loadmodel->NumSurfaces = count;

        for (int surfnum = 0; surfnum < count; surfnum++, @in++, @out++)
        {
            @out->FirstEdge = Com.LittleLong(@in->FirstEdge);
            @out->NumEdges = Com.LittleShort(@in->NumEdges);
            @out->Flags = 0;

            int planenum = Com.LittleShort(@in->PlaneNum);
            int side = Com.LittleShort(@in->Side);
            if (side != 0)
                @out->Flags |= Surf.PLANEBACK;

            @out->Plane = loadmodel->Planes + planenum;

            @out->TexInfo = loadmodel->Texinfo + Com.LittleShort(@in->TexInfo);

            CalcSurfaceExtents(@out);

            // lighting info
            for (int i = 0; i < BspFile.MAXLIGHTMAPS; i++)
                @out->Styles[i] = @in->Styles[i];
            int lightofs = Com.LittleLong(@in->LightOfs);
            @out->Samples = lightofs == -1 ? null : loadmodel->LightData + lightofs;

            // set the drawing flags flag
            if (Q.StrNCmp(@out->TexInfo->Texture->Name, skyName, 3) == 0) // sky
            {
                @out->Flags |= Surf.DRAWSKY | Surf.DRAWTILED;
                continue;
            }

            if (Q.StrNCmp(@out->TexInfo->Texture->Name, turbName, 1) == 0) // turbulent
            {
                @out->Flags |= Surf.DRAWTURB | Surf.DRAWTILED;
                for (int i = 0; i < 2; i++)
                {
                    @out->Extents[i] = 16384;
                    @out->TextureMins[i] = -8192;
                }
                continue;
            }
        }
    }

    // Mod_SetParent
    public static void SetParent(MNode_t* node, MNode_t* parent)
    {
        node->Parent = parent;
        if (node->Contents < 0)
            return;
        SetParent(node->Children[0], node);
        SetParent(node->Children[1], node);
    }

    // Mod_LoadNodes
    public static void LoadNodes(Lump_t* l)
    {
        if (l->FileLen % sizeof(DNode_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DNode_t);
        DNode_t* @in = (DNode_t*)(mod_base + l->FileOfs);
        MNode_t* @out = (MNode_t*)Hunk.AllocName(count * sizeof(MNode_t), loadname);

        loadmodel->Nodes = @out;
        loadmodel->NumNodes = count;

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            for (int j = 0; j < 3; j++)
            {
                @out->MinMaxs[j] = Com.LittleShort(@in->Mins[j]);
                @out->MinMaxs[3 + j] = Com.LittleShort(@in->Maxs[j]);
            }

            int p = Com.LittleLong(@in->PlaneNum);
            @out->Plane = loadmodel->Planes + p;

            @out->FirstSurface = (ushort)Com.LittleShort((short)@in->FirstFace);
            @out->NumSurfaces = (ushort)Com.LittleShort((short)@in->NumFaces);

            for (int j = 0; j < 2; j++)
            {
                p = Com.LittleShort(@in->Children[j]);
                @out->Children[j] = p >= 0 ? loadmodel->Nodes + p : (MNode_t*)(loadmodel->Leafs + (-1 - p));
            }
        }

        SetParent(loadmodel->Nodes, null); // sets nodes and leafs
    }

    // Mod_LoadLeafs
    public static void LoadLeafs(Lump_t* l)
    {
        if (l->FileLen % sizeof(DLeaf_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DLeaf_t);
        DLeaf_t* @in = (DLeaf_t*)(mod_base + l->FileOfs);
        MLeaf_t* @out = (MLeaf_t*)Hunk.AllocName(count * sizeof(MLeaf_t), loadname);

        loadmodel->Leafs = @out;
        loadmodel->NumLeafs = count;

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            for (int j = 0; j < 3; j++)
            {
                @out->MinMaxs[j] = Com.LittleShort(@in->Mins[j]);
                @out->MinMaxs[3 + j] = Com.LittleShort(@in->Maxs[j]);
            }

            @out->Contents = Com.LittleLong(@in->Contents);

            @out->FirstMarkSurface = loadmodel->Marksurfaces + Com.LittleShort((short)@in->FirstMarkSurface);
            @out->NumMarkSurfaces = Com.LittleShort((short)@in->NumMarkSurfaces);

            int p = Com.LittleLong(@in->VisOfs);
            @out->CompressedVis = p == -1 ? null : loadmodel->VisData + p;
            @out->EFrags = null;

            for (int j = 0; j < 4; j++)
                @out->AmbientSoundLevel[j] = @in->AmbientLevel[j];
        }
    }

    // Mod_LoadClipnodes
    public static void LoadClipnodes(Lump_t* l)
    {
        if (l->FileLen % sizeof(DClipNode_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DClipNode_t);
        DClipNode_t* @in = (DClipNode_t*)(mod_base + l->FileOfs);
        DClipNode_t* @out = (DClipNode_t*)Hunk.AllocName(count * sizeof(DClipNode_t), loadname);

        loadmodel->Clipnodes = @out;
        loadmodel->NumClipnodes = count;

        Hull_t* hull = &loadmodel->Hulls[1];
        hull->ClipNodes = @out;
        hull->FirstClipNode = 0;
        hull->LastClipNode = count - 1;
        hull->Planes = loadmodel->Planes;
        hull->ClipMins[0] = -16;
        hull->ClipMins[1] = -16;
        hull->ClipMins[2] = -24;
        hull->ClipMaxs[0] = 16;
        hull->ClipMaxs[1] = 16;
        hull->ClipMaxs[2] = 32;

        hull = &loadmodel->Hulls[2];
        hull->ClipNodes = @out;
        hull->FirstClipNode = 0;
        hull->LastClipNode = count - 1;
        hull->Planes = loadmodel->Planes;
        hull->ClipMins[0] = -32;
        hull->ClipMins[1] = -32;
        hull->ClipMins[2] = -24;
        hull->ClipMaxs[0] = 32;
        hull->ClipMaxs[1] = 32;
        hull->ClipMaxs[2] = 64;

        for (int i = 0; i < count; i++, @out++, @in++)
        {
            @out->PlaneNum = Com.LittleLong(@in->PlaneNum);
            @out->Children[0] = Com.LittleShort(@in->Children[0]);
            @out->Children[1] = Com.LittleShort(@in->Children[1]);
        }
    }

    // Mod_MakeHull0
    // Duplicate the drawing hull structure as a clipping hull.
    public static void MakeHull0()
    {
        Hull_t* hull = &loadmodel->Hulls[0];

        MNode_t* @in = loadmodel->Nodes;
        int count = loadmodel->NumNodes;
        DClipNode_t* @out = (DClipNode_t*)Hunk.AllocName(count * sizeof(DClipNode_t), loadname);

        hull->ClipNodes = @out;
        hull->FirstClipNode = 0;
        hull->LastClipNode = count - 1;
        hull->Planes = loadmodel->Planes;

        for (int i = 0; i < count; i++, @out++, @in++)
        {
            @out->PlaneNum = (int)(@in->Plane - loadmodel->Planes);
            for (int j = 0; j < 2; j++)
            {
                MNode_t* child = @in->Children[j];
                @out->Children[j] = (short)(child->Contents < 0 ? child->Contents : child - loadmodel->Nodes);
            }
        }
    }

    // Mod_LoadMarksurfaces
    public static void LoadMarksurfaces(Lump_t* l)
    {
        if (l->FileLen % sizeof(short) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(short);
        short* @in = (short*)(mod_base + l->FileOfs);
        MSurface_t** @out = (MSurface_t**)Hunk.AllocName(count * sizeof(MSurface_t*), loadname);

        loadmodel->Marksurfaces = @out;
        loadmodel->NumMarksurfaces = count;

        for (int i = 0; i < count; i++)
        {
            int j = Com.LittleShort(@in[i]);
            if (j >= loadmodel->NumSurfaces)
                Sys.Error("Mod_ParseMarksurfaces: bad surface number"u8);
            @out[i] = loadmodel->Surfaces + j;
        }
    }

    // Mod_LoadSurfedges
    public static void LoadSurfedges(Lump_t* l)
    {
        if (l->FileLen % sizeof(int) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(int);
        int* @in = (int*)(mod_base + l->FileOfs);
        int* @out = (int*)Hunk.AllocName(count * sizeof(int), loadname);

        loadmodel->Surfedges = @out;
        loadmodel->NumSurfedges = count;

        for (int i = 0; i < count; i++)
            @out[i] = Com.LittleLong(@in[i]);
    }

    // Mod_LoadPlanes
    public static void LoadPlanes(Lump_t* l)
    {
        if (l->FileLen % sizeof(DPlane_t) != 0)
            Sys.Error($"MOD_LoadBmodel: funny lump size in {loadmodel->Name}");
        int count = l->FileLen / sizeof(DPlane_t);
        DPlane_t* @in = (DPlane_t*)(mod_base + l->FileOfs);
        MPlane_t* @out = (MPlane_t*)Hunk.AllocName(count * 2 * sizeof(MPlane_t), loadname);

        loadmodel->Planes = @out;
        loadmodel->NumPlanes = count;

        for (int i = 0; i < count; i++, @in++, @out++)
        {
            int bits = 0;
            for (int j = 0; j < 3; j++)
            {
                @out->Normal[j] = Com.LittleFloat(@in->Normal[j]);
                if (@out->Normal[j] < 0)
                    bits |= 1 << j;
            }

            @out->Dist = Com.LittleFloat(@in->Dist);
            @out->Type = (byte)Com.LittleLong(@in->Type);
            @out->SignBits = (byte)bits;
        }
    }

    // RadiusFromBounds
    [SkipLocalsInit]
    public static float RadiusFromBounds(in Vec3_t mins, in Vec3_t maxs)
    {
        Vec3_t corner = default;
        for (int i = 0; i < 3; i++)
            corner[i] = MathF.Abs(mins[i]) > MathF.Abs(maxs[i]) ? MathF.Abs(mins[i]) : MathF.Abs(maxs[i]);

        return corner.Length();
    }

    // Mod_LoadBrushModel
    public static void LoadBrushModel(Model_t* mod, void* buffer)
    {
        loadmodel->Type = ModType_t.Brush;

        DHeader_t* header = (DHeader_t*)buffer;

        int version = Com.LittleLong(header->Version);
        if (version != BspFile.BSPVERSION)
            Sys.Error($"Mod_LoadBrushModel: {mod->Name} has wrong version number ({version} should be {BspFile.BSPVERSION})");

        // swap all the lumps
        mod_base = (byte*)header;

        int* headerInts = (int*)header;
        for (int i = 0; i < sizeof(DHeader_t) / 4; i++)
            headerInts[i] = Com.LittleLong(headerInts[i]);

        // load into heap
        LoadVertexes(&header->Lumps[(int)BspLump.Vertexes]);
        LoadEdges(&header->Lumps[(int)BspLump.Edges]);
        LoadSurfedges(&header->Lumps[(int)BspLump.SurfEdges]);
        LoadTextures(&header->Lumps[(int)BspLump.Textures]);
        LoadLighting(&header->Lumps[(int)BspLump.Lighting]);
        LoadPlanes(&header->Lumps[(int)BspLump.Planes]);
        LoadTexinfo(&header->Lumps[(int)BspLump.TexInfo]);
        LoadFaces(&header->Lumps[(int)BspLump.Faces]);
        LoadMarksurfaces(&header->Lumps[(int)BspLump.MarkSurfaces]);
        LoadVisibility(&header->Lumps[(int)BspLump.Visibility]);
        LoadLeafs(&header->Lumps[(int)BspLump.Leafs]);
        LoadNodes(&header->Lumps[(int)BspLump.Nodes]);
        LoadClipnodes(&header->Lumps[(int)BspLump.ClipNodes]);
        LoadEntities(&header->Lumps[(int)BspLump.Entities]);
        LoadSubmodels(&header->Lumps[(int)BspLump.Models]);

        MakeHull0();

        mod->NumFrames = 2; // regular and alternate animation
        mod->Flags = 0;

        // set up the submodels (FIXME: this is confusing)
        byte* nameBuf = stackalloc byte[10];
        for (int i = 0; i < mod->NumSubmodels; i++)
        {
            DModel_t* bm = &mod->Submodels[i];

            mod->Hulls[0].FirstClipNode = bm->HeadNode[0];
            for (int j = 1; j < BspFile.MAX_MAP_HULLS; j++)
            {
                mod->Hulls[j].FirstClipNode = bm->HeadNode[j];
                mod->Hulls[j].LastClipNode = mod->NumClipnodes - 1;
            }

            mod->FirstModelSurface = bm->FirstFace;
            mod->NumModelSurfaces = bm->NumFaces;

            for (int j = 0; j < 3; j++)
            {
                mod->Maxs[j] = bm->Maxs[j];
                mod->Mins[j] = bm->Mins[j];
            }
            mod->Radius = RadiusFromBounds(mod->Mins, mod->Maxs);

            mod->NumLeafs = bm->VisLeafs;

            if (i < mod->NumSubmodels - 1)
            {
                // duplicate the basic information
                CStr.Write(nameBuf, $"*{i + 1}");
                loadmodel = FindName(nameBuf);
                *loadmodel = *mod;
                Q.StrCpy(loadmodel->Name, nameBuf);
                mod = loadmodel;
            }
        }
    }
}

// SURF_* flags (model.h)
public static class Surf
{
    public const int PLANEBACK = 2;
    public const int DRAWSKY = 4;
    public const int DRAWSPRITE = 8;
    public const int DRAWTURB = 0x10;
    public const int DRAWTILED = 0x20;
    public const int DRAWBACKGROUND = 0x40;
}
