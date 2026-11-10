// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

/// <summary>
/// snd_dma.c -- main control for any streaming sound output device.
/// The DirectSound-specific branches from the original (guarded by
/// #ifdef _WIN32) are omitted: SndHost.cs's single OS-agnostic backend
/// replaces both the DirectSound and waveOut paths, so there's nothing to
/// branch on here anymore.
/// </summary>
public static unsafe class SndDma
{
    public const int MAX_CHANNELS = 1024; // q_sound.h -- Mark V (johnfitz was 128, Fitz 512)
    public const int MAX_DYNAMIC_CHANNELS = 128; //johnfitz -- was 8
    private const int MAX_SFX = 1024; // q_sound.h -- Mark V

    // channels[MAX_CHANNELS]
    public static readonly Channel_t* channels = NativeMemory.AllocArrayZeroed<Channel_t>(MAX_CHANNELS);
    // total_channels
    public static int total_channels;

    // snd_blocked
    public static int snd_blocked;
    // snd_ambient
    private static bool snd_ambient = true;
    // snd_initialized
    public static bool snd_initialized;

    // shm
    public static Dma_t* shm;

    // listener_origin/forward/right/up
    public static Vec3_t listener_origin;
    public static Vec3_t listener_forward;
    public static Vec3_t listener_right;
    public static Vec3_t listener_up;
    // sound_nominal_clip_dist
    public static float sound_nominal_clip_dist = 1000.0f;

    // soundtime
    public static int soundtime;
    // paintedtime
    public static int paintedtime;

    // known_sfx -- hunk allocated [MAX_SFX]
    public static Sfx_t* known_sfx;
    // num_sfx
    public static int num_sfx;

    // ambient_sfx
    public static Sfx_t*[] ambient_sfx = new Sfx_t*[BspFile.NUM_AMBIENTS];

    // desired_speed, desired_bits -- unused by any ported code path (the
    // original never actually reads them either); kept for parity.
    public static int desired_speed = 11025;
    public static int desired_bits = 16;

    // sound_started
    public static bool sound_started;

    // fakedma, fakedma_updates
    public static bool fakedma;
    public static int fakedma_updates = 15;

    public static readonly Cvar_t* bgmvolume = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* volume = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* nosound = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* precache = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* loadas8bit = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* bgmbuffer = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* ambient_level = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* ambient_fade = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* snd_noextraupdate = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* snd_show = NativeMemory.AllocZeroed<Cvar_t>();
    public static readonly Cvar_t* _snd_mixahead = NativeMemory.AllocZeroed<Cvar_t>();

    private static void RegisterCvar(Cvar_t* cvar, Utf8String name, Utf8String value, bool archive = false)
    {
        cvar->Name = name;
        cvar->String = value;
        cvar->Archive = archive;
        Cvar.RegisterVariable(cvar);
    }

    // S_SoundInfo_f
    public static void SoundInfo_f()
    {
        if (!sound_started || shm == null)
        {
            Con.Printf("sound system not started\n"u8);
            return;
        }

        Con.Printf($"{shm->Channels - 1,5} stereo\n");
        Con.Printf($"{shm->Samples,5} samples\n");
        Con.Printf($"{shm->SamplePos,5} samplepos\n");
        Con.Printf($"{shm->SampleBits,5} samplebits\n");
        Con.Printf($"{shm->SubmissionChunk,5} submission_chunk\n");
        Con.Printf($"{shm->Speed,5} speed\n");
        Con.Printf($"0x{(nint)shm->Buffer:x} dma buffer\n");
        Con.Printf($"{total_channels,5} total_channels\n");
    }

    // S_Startup
    public static void Startup()
    {
        if (!snd_initialized)
        {
            return;
        }

        if (!fakedma)
        {
            int rc = SndHost.SNDDMA_Init();
            if (rc == 0)
            {
                sound_started = false;
                return;
            }
        }

        sound_started = true;
    }

    // S_Init
    public static void Init()
    {
        Con.Printf("\nSound Initialization\n"u8);

        if (Com.CheckParm("-nosound"u8) != 0)
        {
            return;
        }

        if (Com.CheckParm("-simsound"u8) != 0)
        {
            fakedma = true;
        }

        Cmd.AddCommand("play"u8, &Play);
        Cmd.AddCommand("playvol"u8, &PlayVol);
        Cmd.AddCommand("stopsound"u8, &StopAllSoundsC);
        Cmd.AddCommand("soundlist"u8, &SoundList);
        Cmd.AddCommand("soundinfo"u8, &SoundInfo_f);

        RegisterCvar(nosound, "nosound"u8, "0"u8);
        RegisterCvar(volume, "volume"u8, "0.7"u8, archive: true);
        RegisterCvar(precache, "precache"u8, "1"u8);
        RegisterCvar(loadas8bit, "loadas8bit"u8, "0"u8);
        RegisterCvar(bgmvolume, "bgmvolume"u8, "1"u8, archive: true);
        RegisterCvar(bgmbuffer, "bgmbuffer"u8, "4096"u8);
        RegisterCvar(ambient_level, "ambient_level"u8, "0.3"u8);
        RegisterCvar(ambient_fade, "ambient_fade"u8, "100"u8);
        RegisterCvar(snd_noextraupdate, "snd_noextraupdate"u8, "0"u8);
        RegisterCvar(snd_show, "snd_show"u8, "0"u8);
        RegisterCvar(_snd_mixahead, "_snd_mixahead"u8, "0.1"u8, archive: true);

        if (MemoryManager.MemSize < 0x800000)
        {
            Cvar.Set("loadas8bit"u8, "1"u8);
            Con.Printf("loading all sounds as 8bit\n"u8);
        }

        snd_initialized = true;

        Startup();

        SndMix.InitScaletable();

        known_sfx = (Sfx_t*)Hunk.AllocName(MAX_SFX * sizeof(Sfx_t), "sfx_t"u8);
        num_sfx = 0;

        // create a piece of DMA memory
        if (fakedma)
        {
            shm = (Dma_t*)Hunk.AllocName(sizeof(Dma_t), "shm"u8);
            shm->SplitBuffer = false;
            shm->SampleBits = 16;
            shm->Speed = 22050;
            shm->Channels = 2;
            shm->Samples = 32768;
            shm->SamplePos = 0;
            shm->SoundAlive = true;
            shm->GameAlive = true;
            shm->SubmissionChunk = 1;
            shm->Buffer = (byte*)Hunk.AllocName(1 << 16, "shmbuf"u8);
        }

        Con.Printf($"Sound sampling rate: {shm->Speed}\n");

        ambient_sfx[(int)Ambient.Water] = PrecacheSound("ambience/water1.wav"u8);
        ambient_sfx[(int)Ambient.Sky] = PrecacheSound("ambience/wind2.wav"u8);

        StopAllSounds(true);
    }

    // S_Shutdown
    public static void Shutdown()
    {
        if (!sound_started)
        {
            return;
        }

        if (shm != null)
        {
            shm->GameAlive = false;
        }

        shm = null;
        sound_started = false;

        if (!fakedma)
        {
            SndHost.SNDDMA_Shutdown();
        }
    }

    // S_FindName
    public static Sfx_t* FindName(byte* name)
    {
        if (name == null)
        {
            Sys.Error("S_FindName: NULL\n"u8);
        }

        if (Q.StrLen(name) >= QuakeDef.MAX_QPATH)
        {
            Sys.Error($"Sound name too long: {name}");
        }

        // see if already loaded
        for (int i = 0; i < num_sfx; i++)
        {
            if (Q.StrCmp(known_sfx[i].Name, name) == 0)
            {
                return &known_sfx[i];
            }
        }

        if (num_sfx == MAX_SFX)
        {
            Sys.Error("S_FindName: out of sfx_t"u8);
        }

        Sfx_t* sfx = &known_sfx[num_sfx];
        Q.StrCpy(sfx->Name, name);

        num_sfx++;

        return sfx;
    }

    // S_TouchSound
    public static void TouchSound(byte* name)
    {
        if (!sound_started)
        {
            return;
        }

        Sfx_t* sfx = FindName(name);
        Cache.Check(&sfx->Cache);
    }

    // S_PrecacheSound
    public static Sfx_t* PrecacheSound(Utf8String name)
    {
        if (!sound_started || nosound->Value != 0)
        {
            return null;
        }

        Sfx_t* sfx = FindName(name);

        if (precache->Value != 0)
        {
            Wav.LoadSound(sfx);
        }

        return sfx;
    }

    // SND_PickChannel
    public static Channel_t* PickChannel(int entnum, int entchannel)
    {
        int first_to_die = -1;
        int life_left = 0x7fffffff;

        for (int ch_idx = BspFile.NUM_AMBIENTS; ch_idx < BspFile.NUM_AMBIENTS + MAX_DYNAMIC_CHANNELS; ch_idx++)
        {
            if (entchannel != 0 // channel 0 never overrides
                && channels[ch_idx].EntNum == entnum
                && (channels[ch_idx].EntChannel == entchannel || entchannel == -1))
            {
                // allways override sound from same entity
                first_to_die = ch_idx;
                break;
            }

            // don't let monster sounds override player sounds
            if (channels[ch_idx].EntNum == Cl.viewentity && entnum != Cl.viewentity && channels[ch_idx].Sfx != null)
            {
                continue;
            }

            if (channels[ch_idx].End - paintedtime < life_left)
            {
                life_left = channels[ch_idx].End - paintedtime;
                first_to_die = ch_idx;
            }
        }

        if (first_to_die == -1)
        {
            return null;
        }

        if (channels[first_to_die].Sfx != null)
        {
            channels[first_to_die].Sfx = null;
        }

        return &channels[first_to_die];
    }

    // SND_Spatialize
    public static void Spatialize(Channel_t* ch)
    {
        // anything coming from the view entity will allways be full volume
        if (ch->EntNum == Cl.viewentity)
        {
            ch->LeftVol = ch->MasterVol;
            ch->RightVol = ch->MasterVol;
            return;
        }

        // calculate stereo seperation and distance attenuation
        Vec3_t source_vec = ch->Origin - listener_origin;

        float dist = MathLib.VectorNormalize(&source_vec) * ch->DistMult;

        float dot = Vector3.Dot(listener_right, source_vec);

        float rscale, lscale;
        if (shm->Channels == 1)
        {
            rscale = 1.0f;
            lscale = 1.0f;
        }
        else
        {
            rscale = 1.0f + dot;
            lscale = 1.0f - dot;
        }

        // add in distance effect
        float scale = (1.0f - dist) * rscale;
        ch->RightVol = (int)(ch->MasterVol * scale);
        if (ch->RightVol < 0)
        {
            ch->RightVol = 0;
        }

        scale = (1.0f - dist) * lscale;
        ch->LeftVol = (int)(ch->MasterVol * scale);
        if (ch->LeftVol < 0)
        {
            ch->LeftVol = 0;
        }
    }

    // S_StartSound
    public static void StartSound(int entnum, int entchannel, Sfx_t* sfx, in Vec3_t origin, float fvol, float attenuation)
    {
        if (!sound_started)
        {
            return;
        }

        if (sfx == null)
        {
            return;
        }

        if (nosound->Value != 0)
        {
            return;
        }

        int vol = (int)(fvol * 255);

        // pick a channel to play on
        Channel_t* target_chan = PickChannel(entnum, entchannel);
        if (target_chan == null)
        {
            return;
        }

        // spatialize
        *target_chan = default;
        target_chan->Origin = origin;
        target_chan->DistMult = attenuation / sound_nominal_clip_dist;
        target_chan->MasterVol = vol;
        target_chan->EntNum = entnum;
        target_chan->EntChannel = entchannel;
        Spatialize(target_chan);

        if (target_chan->LeftVol == 0 && target_chan->RightVol == 0)
        {
            return; // not audible at all
        }

        // new channel
        SfxCache_t* sc = Wav.LoadSound(sfx);
        if (sc == null)
        {
            target_chan->Sfx = null;
            return; // couldn't load the sound's data
        }

        target_chan->Sfx = sfx;
        target_chan->Pos = 0;
        target_chan->End = paintedtime + sc->Length;

        // if an identical sound has also been started this frame, offset the pos
        // a bit to keep it from just making the first one louder
        Channel_t* check = &channels[BspFile.NUM_AMBIENTS];
        for (int ch_idx = BspFile.NUM_AMBIENTS; ch_idx < BspFile.NUM_AMBIENTS + MAX_DYNAMIC_CHANNELS; ch_idx++, check++)
        {
            if (check == target_chan)
            {
                continue;
            }

            if (check->Sfx == sfx && check->Pos == 0)
            {
                int skip = Q.Rand() % (int)(0.1 * shm->Speed);
                if (skip >= target_chan->End)
                {
                    skip = target_chan->End - 1;
                }

                target_chan->Pos += skip;
                target_chan->End -= skip;
                break;
            }
        }
    }

    // S_StopSound
    public static void StopSound(int entnum, int entchannel)
    {
        for (int i = 0; i < MAX_DYNAMIC_CHANNELS; i++)
        {
            if (channels[i].EntNum == entnum && channels[i].EntChannel == entchannel)
            {
                channels[i].End = 0;
                channels[i].Sfx = null;
                return;
            }
        }
    }

    // S_StopAllSounds
    public static void StopAllSounds(bool clear)
    {
        if (!sound_started)
        {
            return;
        }

        total_channels = MAX_DYNAMIC_CHANNELS + BspFile.NUM_AMBIENTS; // no statics

        NativeMemory.Clear(channels, MAX_CHANNELS);

        if (clear)
        {
            ClearBuffer();
        }
    }

    // S_StopAllSoundsC
    public static void StopAllSoundsC() => StopAllSounds(true);

    // S_BlockSound (snd_win.c) -- the port's OpenAL backend follows the
    // waveOut path (see SndHost), so snd_iswave is always true here; the
    // DirectSound "takes care of blocking itself" case doesn't exist.
    public static void BlockSound()
    {
        snd_blocked++;

        if (snd_blocked == 1)
        {
            SndHost.Reset(); // waveOutReset (hWaveOut)
        }
    }

    // S_UnblockSound (snd_win.c)
    public static void UnblockSound()
    {
        snd_blocked--;
    }

    // S_ClearBuffer
    public static void ClearBuffer()
    {
        if (!sound_started || shm == null || shm->Buffer == null)
        {
            return;
        }

        int clear = shm->SampleBits == 8 ? 0x80 : 0;

        NativeMemory.Fill(shm->Buffer, (nuint)(shm->Samples * shm->SampleBits / 8), (byte)clear);
    }

    // S_StaticSound
    public static void StaticSound(Sfx_t* sfx, in Vec3_t origin, float vol, float attenuation)
    {
        if (sfx == null)
        {
            return;
        }

        if (total_channels == MAX_CHANNELS)
        {
            Con.Printf("total_channels == MAX_CHANNELS\n"u8);
            return;
        }

        Channel_t* ss = &channels[total_channels];
        total_channels++;

        SfxCache_t* sc = Wav.LoadSound(sfx);
        if (sc == null)
        {
            return;
        }

        if (sc->LoopStart == -1)
        {
            Con.Printf($"Sound {sfx->Name} not looped\n");
            return;
        }

        ss->Sfx = sfx;
        ss->Origin = origin;
        ss->MasterVol = (int)vol;
        ss->DistMult = (attenuation / 64) / sound_nominal_clip_dist;
        ss->End = paintedtime + sc->Length;

        Spatialize(ss);
    }

    // S_UpdateAmbientSounds
    public static void UpdateAmbientSounds()
    {
        if (!snd_ambient)
        {
            return;
        }

        if (Cl.worldmodel == null)
        {
            return;
        }

        MLeaf_t* l = Model.PointInLeaf(listener_origin, Cl.worldmodel);

        if (l == null || ambient_level->Value == 0)
        {
            for (int ac = 0; ac < BspFile.NUM_AMBIENTS; ac++)
            {
                channels[ac].Sfx = null;
            }

            return;
        }

        for (int ambient_channel = 0; ambient_channel < BspFile.NUM_AMBIENTS; ambient_channel++)
        {
            Channel_t* chan = &channels[ambient_channel];
            chan->Sfx = ambient_sfx[ambient_channel];

            float vol = ambient_level->Value * l->AmbientSoundLevel[ambient_channel];
            if (vol < 8)
            {
                vol = 0;
            }

            // don't adjust volume too fast
            if (chan->MasterVol < vol)
            {
                chan->MasterVol += (int)(Host.host_frametime * ambient_fade->Value);
                if (chan->MasterVol > vol)
                {
                    chan->MasterVol = (int)vol;
                }
            }
            else if (chan->MasterVol > vol)
            {
                chan->MasterVol -= (int)(Host.host_frametime * ambient_fade->Value);
                if (chan->MasterVol < vol)
                {
                    chan->MasterVol = (int)vol;
                }
            }

            chan->LeftVol = chan->RightVol = chan->MasterVol;
        }
    }

    // S_Update
    public static void Update(in Vec3_t origin, in Vec3_t forward, in Vec3_t right, in Vec3_t up)
    {
        if (!sound_started || snd_blocked > 0)
        {
            return;
        }

        listener_origin = origin;
        listener_forward = forward;
        listener_right = right;
        listener_up = up;

        // update general area ambient sound sources
        UpdateAmbientSounds();

        Channel_t* combine = null;

        // update spatialization for static and dynamic sounds
        for (int i = BspFile.NUM_AMBIENTS; i < total_channels; i++)
        {
            Channel_t* ch = &channels[i];
            if (ch->Sfx == null)
            {
                continue;
            }

            Spatialize(ch); // respatialize channel
            if (ch->LeftVol == 0 && ch->RightVol == 0)
            {
                continue;
            }

            // try to combine static sounds with a previous channel of the same
            // sound effect so we don't mix five torches every frame
            if (i >= MAX_DYNAMIC_CHANNELS + BspFile.NUM_AMBIENTS)
            {
                // see if it can just use the last one
                if (combine != null && combine->Sfx == ch->Sfx)
                {
                    combine->LeftVol += ch->LeftVol;
                    combine->RightVol += ch->RightVol;
                    ch->LeftVol = ch->RightVol = 0;
                    continue;
                }

                // search for one
                int j;
                combine = &channels[MAX_DYNAMIC_CHANNELS + BspFile.NUM_AMBIENTS];
                for (j = MAX_DYNAMIC_CHANNELS + BspFile.NUM_AMBIENTS; j < i; j++, combine++)
                {
                    if (combine->Sfx == ch->Sfx)
                    {
                        break;
                    }
                }

                if (j == total_channels)
                {
                    combine = null;
                }
                else
                {
                    if (combine != ch)
                    {
                        combine->LeftVol += ch->LeftVol;
                        combine->RightVol += ch->RightVol;
                        ch->LeftVol = ch->RightVol = 0;
                    }
                    continue;
                }
            }
        }

        // debugging output
        if (snd_show->Value != 0)
        {
            int total = 0;
            for (int i = 0; i < total_channels; i++)
            {
                if (channels[i].Sfx != null && (channels[i].LeftVol != 0 || channels[i].RightVol != 0))
                {
                    total++;
                }
            }

            Con.Printf($"----({total})----\n");
        }

        // mix some sound
        Update_();
    }

    // GetSoundtime
    private static int buffers;
    private static int oldsamplepos;

    public static void GetSoundtime()
    {
        int fullsamples = shm->Samples / shm->Channels;

        int samplepos = SndHost.SNDDMA_GetDMAPos();

        if (samplepos < oldsamplepos)
        {
            buffers++; // buffer wrapped

            if (paintedtime > 0x40000000)
            {
                // time to chop things off to avoid 32 bit limits
                buffers = 0;
                paintedtime = fullsamples;
                StopAllSounds(true);
            }
        }
        oldsamplepos = samplepos;

        soundtime = (buffers * fullsamples) + (samplepos / shm->Channels);
    }

    // S_Update_
    public static void Update_()
    {
        if (!sound_started || snd_blocked > 0)
        {
            return;
        }

        // Updates DMA time
        GetSoundtime();

        // check to make sure that we haven't overshot
        if (paintedtime < soundtime)
        {
            paintedtime = soundtime;
        }

        // mix ahead of current position
        int endtime = soundtime + (int)(_snd_mixahead->Value * shm->Speed);
        int samps = shm->Samples >> (shm->Channels - 1);
        if (endtime - soundtime > samps)
        {
            endtime = soundtime + samps;
        }

        SndMix.PaintChannels(endtime);

        SndHost.SNDDMA_Submit();
    }

    // console functions

    private static int play_hash = 345;

    // S_Play
    [SkipLocalsInit]
    public static void Play()
    {
        byte* name = stackalloc byte[256];
        int i = 1;
        while (i < Cmd.Argc())
        {
            byte* arg = Cmd.Argv(i);
            if (Q.StrRChr(arg, (byte)'.') == null)
            {
                Q.StrCpy(name, arg);
                Q.StrCat(name, ".wav"u8);
            }
            else
            {
                Q.StrCpy(name, arg);
            }

            Sfx_t* sfx = PrecacheSound(name);
            StartSound(play_hash++, 0, sfx, listener_origin, 1.0f, 1.0f);
            i++;
        }
    }

    private static int playvol_hash = 543;

    // S_PlayVol
    [SkipLocalsInit]
    public static void PlayVol()
    {
        byte* name = stackalloc byte[256];
        int i = 1;
        while (i < Cmd.Argc())
        {
            byte* arg = Cmd.Argv(i);
            if (Q.StrRChr(arg, (byte)'.') == null)
            {
                Q.StrCpy(name, arg);
                Q.StrCat(name, ".wav"u8);
            }
            else
            {
                Q.StrCpy(name, arg);
            }

            Sfx_t* sfx = PrecacheSound(name);
            float vol = Q.Atof(Cmd.Argv(i + 1));
            StartSound(playvol_hash++, 0, sfx, listener_origin, vol, 1.0f);
            i += 2;
        }
    }

    // S_SoundList
    public static void SoundList()
    {
        int total = 0;
        for (int i = 0; i < num_sfx; i++)
        {
            Sfx_t* sfx = &known_sfx[i];
            SfxCache_t* sc = (SfxCache_t*)Cache.Check(&sfx->Cache);
            if (sc == null)
            {
                continue;
            }

            int size = sc->Length * sc->Width * (sc->Stereo + 1);
            total += size;
            Con.Printf(sc->LoopStart >= 0 ? "L"u8 : " "u8);
            Con.Printf($"({sc->Width * 8,2}b) {size,6} : {sfx->Name}\n");
        }
        Con.Printf($"Total resident: {total}\n");
    }

    // S_LocalSound
    public static void LocalSound(Utf8String sound)
    {
        if (nosound->Value != 0)
        {
            return;
        }

        if (!sound_started)
        {
            return;
        }

        Sfx_t* sfx = PrecacheSound(sound);
        if (sfx == null)
        {
            Con.Printf($"S_LocalSound: can't cache {sound}\n");
            return;
        }
        StartSound(Cl.viewentity, -1, sfx, MathLib.vec3_origin, 1, 1);
    }

    // S_BeginPrecaching
    public static void BeginPrecaching() { }

    // S_EndPrecaching
    public static void EndPrecaching() { }
}
