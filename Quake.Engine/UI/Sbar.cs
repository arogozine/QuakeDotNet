// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Quake.Engine.Memory;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// sbar.c -- status bar code. Rogue/Hipnotic mission-pack UI variants
/// (rsb_*/hsb_* arrays, gated on Com.rogue/Com.hipnotic exactly like the
/// original's `rogue`/`hipnotic` globals) are ported.
/// </summary>
public static unsafe class Sbar
{
    private const int SBAR_HEIGHT = 24; // sbar.h
    private const int STAT_MINUS = 10;  // num frame for '-' stats digit

    public static int sb_updates; // if >= vid.numpages, no update needed

    public static readonly QPic_t*[,] sb_nums = new QPic_t*[2, 11];
    public static QPic_t* sb_colon, sb_slash;
    public static QPic_t* sb_ibar;
    public static QPic_t* sb_sbar;
    public static QPic_t* sb_scorebar;

    public static readonly QPic_t*[,] sb_weapons = new QPic_t*[7, 8]; // 0 active, 1 owned, 2-5 flashes
    public static readonly QPic_t*[] sb_ammo = new QPic_t*[4];
    public static readonly QPic_t*[] sb_sigil = new QPic_t*[4];
    public static readonly QPic_t*[] sb_armor = new QPic_t*[3];
    public static readonly QPic_t*[] sb_items = new QPic_t*[32];

    public static readonly QPic_t*[,] sb_faces = new QPic_t*[7, 2]; // 0 gibbed, 1 dead, 2-6 alive; 0 static, 1 temp anim
    public static QPic_t* sb_face_invis;
    public static QPic_t* sb_face_quad;
    public static QPic_t* sb_face_invuln;
    public static QPic_t* sb_face_invis_invuln;

    // rsb_*/hsb_* (sbar.c) -- rogue/hipnotic mission-pack HUD graphics.
    public static readonly QPic_t*[] rsb_invbar = new QPic_t*[2];
    public static readonly QPic_t*[] rsb_weapons = new QPic_t*[5];
    public static readonly QPic_t*[] rsb_items = new QPic_t*[2];
    public static readonly QPic_t*[] rsb_ammo = new QPic_t*[3];
    public static QPic_t* rsb_teambord; // PGM 01/19/97 - team color border

    // MED 01/04/97 added two more weapons + 3 alternates for grenade launcher
    public static readonly QPic_t*[,] hsb_weapons = new QPic_t*[7, 5]; // 0 is active, 1 is owned, 2-5 are flashes
    // MED 01/04/97 added array to simplify weapon parsing
    public static readonly int[] hipweapons = { HIT_LASER_CANNON_BIT, HIT_MJOLNIR_BIT, 4, HIT_PROXIMITY_GUN_BIT };
    // MED 01/04/97 added hipnotic items array
    public static readonly QPic_t*[] hsb_items = new QPic_t*[2];

    public static bool sb_showscores;

    public static int sb_lines; // scan lines to draw
    // Not in the original -- sb_lines in physical buffer lines (sb_lines is in
    // status bar canvas units, see Canvas.cs); what the refresh sizes the 3D view by.
    public static int sb_physlines => sb_lines * Canvas.SbarScale;

    /*
    ===============
    Sbar_ShowScores / Sbar_DontShowScores / Sbar_Changed
    ===============
    */
    public static void ShowScores()
    {
        if (sb_showscores)
            return;
        sb_showscores = true;
        sb_updates = 0;
    }

    public static void DontShowScores()
    {
        sb_showscores = false;
        sb_updates = 0;
    }

    public static void Changed() => sb_updates = 0;

    private static void ShowScoresCmd() => ShowScores();
    private static void DontShowScoresCmd() => DontShowScores();

    /*
    ===============
    Sbar_Init
    ===============
    */
    public static void Init()
    {
        for (int i = 0; i < 10; i++)
        {
            sb_nums[0, i] = Draw.PicFromWad($"num_{i}");
            sb_nums[1, i] = Draw.PicFromWad($"anum_{i}");
        }

        sb_nums[0, 10] = Draw.PicFromWad("num_minus"u8);
        sb_nums[1, 10] = Draw.PicFromWad("anum_minus"u8);

        sb_colon = Draw.PicFromWad("num_colon"u8);
        sb_slash = Draw.PicFromWad("num_slash"u8);

        sb_weapons[0, 0] = Draw.PicFromWad("inv_shotgun"u8);
        sb_weapons[0, 1] = Draw.PicFromWad("inv_sshotgun"u8);
        sb_weapons[0, 2] = Draw.PicFromWad("inv_nailgun"u8);
        sb_weapons[0, 3] = Draw.PicFromWad("inv_snailgun"u8);
        sb_weapons[0, 4] = Draw.PicFromWad("inv_rlaunch"u8);
        sb_weapons[0, 5] = Draw.PicFromWad("inv_srlaunch"u8);
        sb_weapons[0, 6] = Draw.PicFromWad("inv_lightng"u8);

        sb_weapons[1, 0] = Draw.PicFromWad("inv2_shotgun"u8);
        sb_weapons[1, 1] = Draw.PicFromWad("inv2_sshotgun"u8);
        sb_weapons[1, 2] = Draw.PicFromWad("inv2_nailgun"u8);
        sb_weapons[1, 3] = Draw.PicFromWad("inv2_snailgun"u8);
        sb_weapons[1, 4] = Draw.PicFromWad("inv2_rlaunch"u8);
        sb_weapons[1, 5] = Draw.PicFromWad("inv2_srlaunch"u8);
        sb_weapons[1, 6] = Draw.PicFromWad("inv2_lightng"u8);

        for (int i = 0; i < 5; i++)
        {
            sb_weapons[2 + i, 0] = Draw.PicFromWad($"inva{i + 1}_shotgun");
            sb_weapons[2 + i, 1] = Draw.PicFromWad($"inva{i + 1}_sshotgun");
            sb_weapons[2 + i, 2] = Draw.PicFromWad($"inva{i + 1}_nailgun");
            sb_weapons[2 + i, 3] = Draw.PicFromWad($"inva{i + 1}_snailgun");
            sb_weapons[2 + i, 4] = Draw.PicFromWad($"inva{i + 1}_rlaunch");
            sb_weapons[2 + i, 5] = Draw.PicFromWad($"inva{i + 1}_srlaunch");
            sb_weapons[2 + i, 6] = Draw.PicFromWad($"inva{i + 1}_lightng");
        }

        sb_ammo[0] = Draw.PicFromWad("sb_shells"u8);
        sb_ammo[1] = Draw.PicFromWad("sb_nails"u8);
        sb_ammo[2] = Draw.PicFromWad("sb_rocket"u8);
        sb_ammo[3] = Draw.PicFromWad("sb_cells"u8);

        sb_armor[0] = Draw.PicFromWad("sb_armor1"u8);
        sb_armor[1] = Draw.PicFromWad("sb_armor2"u8);
        sb_armor[2] = Draw.PicFromWad("sb_armor3"u8);

        sb_items[0] = Draw.PicFromWad("sb_key1"u8);
        sb_items[1] = Draw.PicFromWad("sb_key2"u8);
        sb_items[2] = Draw.PicFromWad("sb_invis"u8);
        sb_items[3] = Draw.PicFromWad("sb_invuln"u8);
        sb_items[4] = Draw.PicFromWad("sb_suit"u8);
        sb_items[5] = Draw.PicFromWad("sb_quad"u8);

        sb_sigil[0] = Draw.PicFromWad("sb_sigil1"u8);
        sb_sigil[1] = Draw.PicFromWad("sb_sigil2"u8);
        sb_sigil[2] = Draw.PicFromWad("sb_sigil3"u8);
        sb_sigil[3] = Draw.PicFromWad("sb_sigil4"u8);

        sb_faces[4, 0] = Draw.PicFromWad("face1"u8);
        sb_faces[4, 1] = Draw.PicFromWad("face_p1"u8);
        sb_faces[3, 0] = Draw.PicFromWad("face2"u8);
        sb_faces[3, 1] = Draw.PicFromWad("face_p2"u8);
        sb_faces[2, 0] = Draw.PicFromWad("face3"u8);
        sb_faces[2, 1] = Draw.PicFromWad("face_p3"u8);
        sb_faces[1, 0] = Draw.PicFromWad("face4"u8);
        sb_faces[1, 1] = Draw.PicFromWad("face_p4"u8);
        sb_faces[0, 0] = Draw.PicFromWad("face5"u8);
        sb_faces[0, 1] = Draw.PicFromWad("face_p5"u8);

        sb_face_invis = Draw.PicFromWad("face_invis"u8);
        sb_face_invuln = Draw.PicFromWad("face_invul2"u8);
        sb_face_invis_invuln = Draw.PicFromWad("face_inv2"u8);
        sb_face_quad = Draw.PicFromWad("face_quad"u8);

        Cmd.AddCommand("+showscores"u8, &ShowScoresCmd);
        Cmd.AddCommand("-showscores"u8, &DontShowScoresCmd);

        sb_sbar = Draw.PicFromWad("sbar"u8);
        sb_ibar = Draw.PicFromWad("ibar"u8);
        sb_scorebar = Draw.PicFromWad("scorebar"u8);

        // MED 01/04/97 added new hipnotic weapons
        if (Com.hipnotic)
        {
            hsb_weapons[0, 0] = Draw.PicFromWad("inv_laser"u8);
            hsb_weapons[0, 1] = Draw.PicFromWad("inv_mjolnir"u8);
            hsb_weapons[0, 2] = Draw.PicFromWad("inv_gren_prox"u8);
            hsb_weapons[0, 3] = Draw.PicFromWad("inv_prox_gren"u8);
            hsb_weapons[0, 4] = Draw.PicFromWad("inv_prox"u8);

            hsb_weapons[1, 0] = Draw.PicFromWad("inv2_laser"u8);
            hsb_weapons[1, 1] = Draw.PicFromWad("inv2_mjolnir"u8);
            hsb_weapons[1, 2] = Draw.PicFromWad("inv2_gren_prox"u8);
            hsb_weapons[1, 3] = Draw.PicFromWad("inv2_prox_gren"u8);
            hsb_weapons[1, 4] = Draw.PicFromWad("inv2_prox"u8);

            for (int i = 0; i < 5; i++)
            {
                hsb_weapons[2 + i, 0] = Draw.PicFromWad($"inva{i + 1}_laser");
                hsb_weapons[2 + i, 1] = Draw.PicFromWad($"inva{i + 1}_mjolnir");
                hsb_weapons[2 + i, 2] = Draw.PicFromWad($"inva{i + 1}_gren_prox");
                hsb_weapons[2 + i, 3] = Draw.PicFromWad($"inva{i + 1}_prox_gren");
                hsb_weapons[2 + i, 4] = Draw.PicFromWad($"inva{i + 1}_prox");
            }

            hsb_items[0] = Draw.PicFromWad("sb_wsuit"u8);
            hsb_items[1] = Draw.PicFromWad("sb_eshld"u8);
        }

        if (Com.rogue)
        {
            rsb_invbar[0] = Draw.PicFromWad("r_invbar1"u8);
            rsb_invbar[1] = Draw.PicFromWad("r_invbar2"u8);

            rsb_weapons[0] = Draw.PicFromWad("r_lava"u8);
            rsb_weapons[1] = Draw.PicFromWad("r_superlava"u8);
            rsb_weapons[2] = Draw.PicFromWad("r_gren"u8);
            rsb_weapons[3] = Draw.PicFromWad("r_multirock"u8);
            rsb_weapons[4] = Draw.PicFromWad("r_plasma"u8);

            rsb_items[0] = Draw.PicFromWad("r_shield1"u8);
            rsb_items[1] = Draw.PicFromWad("r_agrav1"u8);

            // PGM 01/19/97 - team color border
            rsb_teambord = Draw.PicFromWad("r_teambord"u8);
            // PGM 01/19/97 - team color border

            rsb_ammo[0] = Draw.PicFromWad("r_ammolava"u8);
            rsb_ammo[1] = Draw.PicFromWad("r_ammomulti"u8);
            rsb_ammo[2] = Draw.PicFromWad("r_ammoplasma"u8);
        }
    }

    //=============================================================================
    // drawing routines are relative to the status bar location

    public static void DrawPic(int x, int y, QPic_t* pic)
    {
        if (Cl.gametype == (int)GameType.Deathmatch)
            Draw.Pic(x, y + (Canvas.Height - SBAR_HEIGHT), pic);
        else
            Draw.Pic(x + ((Canvas.Width - 320) >> 1), y + (Canvas.Height - SBAR_HEIGHT), pic);
    }

    public static void DrawTransPic(int x, int y, QPic_t* pic)
    {
        if (Cl.gametype == (int)GameType.Deathmatch)
            Draw.TransPic(x, y + (Canvas.Height - SBAR_HEIGHT), pic);
        else
            Draw.TransPic(x + ((Canvas.Width - 320) >> 1), y + (Canvas.Height - SBAR_HEIGHT), pic);
    }

    public static void DrawCharacter(int x, int y, int num)
    {
        if (Cl.gametype == (int)GameType.Deathmatch)
            Draw.Character(x + 4, y + Canvas.Height - SBAR_HEIGHT, num);
        else
            Draw.Character(x + ((Canvas.Width - 320) >> 1) + 4, y + Canvas.Height - SBAR_HEIGHT, num);
    }

    private static void DrawStringRaw(int x, int y, byte* str)
    {
        if (Cl.gametype == (int)GameType.Deathmatch)
            Draw.String(x, y + Canvas.Height - SBAR_HEIGHT, str);
        else
            Draw.String(x + ((Canvas.Width - 320) >> 1), y + Canvas.Height - SBAR_HEIGHT, str);
    }

    // Sbar_DrawString(x, y, va("...%3i...", n)) without allocating: $"Time :{minutes,3}".
    public static void DrawString(int x, int y, [InterpolatedStringHandlerArgument] scoped ref Utf8StringHandler text)
        => DrawStringRaw(x, y, text.GetWrittenBufferPtr());

    /*
    =============
    Sbar_itoa
    =============
    */
    public static int Itoa(int num, byte* buf)
    {
        byte* str = buf;

        if (num < 0)
        {
            *str++ = (byte)'-';
            num = -num;
        }

        int pow10;
        for (pow10 = 10; num >= pow10; pow10 *= 10) { }

        do
        {
            pow10 /= 10;
            int dig = num / pow10;
            *str++ = (byte)('0' + dig);
            num -= dig * pow10;
        } while (pow10 != 1);

        *str = 0;

        return (int)(str - buf);
    }

    /*
    =============
    Sbar_DrawNum
    =============
    */
    public static void DrawNum(int x, int y, int num, int digits, int color)
    {
        byte* str = stackalloc byte[12];
        int l = Itoa(num, str);
        byte* ptr = str;
        if (l > digits)
            ptr += l - digits;
        if (l < digits)
            x += (digits - l) * 24;

        while (*ptr != 0)
        {
            int frame = *ptr == '-' ? STAT_MINUS : *ptr - '0';

            DrawTransPic(x, y, sb_nums[color, frame]);
            x += 24;
            ptr++;
        }
    }

    //=============================================================================


    public static readonly int* fragsort = NativeMemory.AllocArrayZeroed<int>(MAX_SCOREBOARD);

    internal const int SCOREBOARDTEXT_COLS = 20;
    // scoreboardtext[MAX_SCOREBOARD][20] -- flat buffer, indexed by row * SCOREBOARDTEXT_COLS + col.
    public static readonly byte* scoreboardtext = NativeMemory.AllocArrayZeroed<byte>(MAX_SCOREBOARD * SCOREBOARDTEXT_COLS);
    public static readonly int* scoreboardtop = NativeMemory.AllocArrayZeroed<int>(MAX_SCOREBOARD);
    public static readonly int* scoreboardbottom = NativeMemory.AllocArrayZeroed<int>(MAX_SCOREBOARD);
    public static int scoreboardlines;

    /*
    ===============
    Sbar_SortFrags
    ===============
    */
    public static void SortFrags()
    {
        // sort by frags
        scoreboardlines = 0;
        for (int i = 0; i < Cl.maxclients; i++)
        {
            if (Cl.scores[i].Name[0] != 0)
            {
                fragsort[scoreboardlines] = i;
                scoreboardlines++;
            }
        }

        for (int i = 0; i < scoreboardlines; i++)
            for (int j = 0; j < scoreboardlines - 1 - i; j++)
                if (Cl.scores[fragsort[j]].Frags < Cl.scores[fragsort[j + 1]].Frags)
                    (fragsort[j], fragsort[j + 1]) = (fragsort[j + 1], fragsort[j]);
    }

    public static int ColorForMap(int m) => m < 128 ? m + 8 : m + 8;

    /*
    ===============
    Sbar_SoloScoreboard
    ===============
    */
    public static void SoloScoreboard()
    {
        DrawString(8, 4, $"Monsters:{Cl.stats[(int)Stat.Monsters],3} /{Cl.stats[(int)Stat.TotalMonsters],3}");
        DrawString(8, 12, $"Secrets :{Cl.stats[(int)Stat.Secrets],3} /{Cl.stats[(int)Stat.TotalSecrets],3}");

        // time
        int minutes = (int)(Cl.time / 60);
        int seconds = (int)(Cl.time - 60 * minutes);
        int tens = seconds / 10;
        int units = seconds - 10 * tens;
        DrawString(184, 4, $"Time :{minutes,3}:{tens}{units}");

        // draw level name
        int l = (int)Q.StrLen(Cl.levelname);
        DrawStringRaw(232 - l * 4, 12, Cl.levelname);
    }

    /*
    ===============
    Sbar_DrawScoreboard
    ===============
    */
    public static void DrawScoreboard()
    {
        SoloScoreboard();
        if (Cl.gametype == (int)GameType.Deathmatch)
            DeathmatchOverlay();
    }

    //=============================================================================

    /*
    ===============
    Sbar_DrawInventory
    ===============
    */
    public static void DrawInventory()
    {
        if (Com.rogue)
        {
            if (Cl.stats[(int)Stat.ActiveWeapon] >= RIT_LAVA_NAILGUN)
                DrawPic(0, -24, rsb_invbar[0]);
            else
                DrawPic(0, -24, rsb_invbar[1]);
        }
        else
        {
            DrawPic(0, -24, sb_ibar);
        }

        // weapons
        for (int i = 0; i < 7; i++)
        {
            if ((Cl.items & (RShared_IT_SHOTGUN << i)) != 0)
            {
                float time = Cl.item_gettime[i];
                int flashon = (int)((Cl.time - time) * 10);
                if (flashon >= 10)
                {
                    flashon = Cl.stats[(int)Stat.ActiveWeapon] == (RShared_IT_SHOTGUN << i) ? 1 : 0;
                }
                else
                {
                    flashon = (flashon % 5) + 2;
                }

                DrawPic(i * 24, -16, sb_weapons[flashon, i]);

                if (flashon > 1)
                    sb_updates = 0; // force update to remove flash
            }
        }

        // MED 01/04/97
        // hipnotic weapons
        if (Com.hipnotic)
        {
            bool grenadeflashing = false;
            for (int i = 0; i < 4; i++)
            {
                if ((Cl.items & (1 << hipweapons[i])) != 0)
                {
                    float time = Cl.item_gettime[hipweapons[i]];
                    int flashon = (int)((Cl.time - time) * 10);
                    if (flashon >= 10)
                    {
                        flashon = Cl.stats[(int)Stat.ActiveWeapon] == (1 << hipweapons[i]) ? 1 : 0;
                    }
                    else
                    {
                        flashon = (flashon % 5) + 2;
                    }

                    // check grenade launcher
                    if (i == 2)
                    {
                        if ((Cl.items & HIT_PROXIMITY_GUN) != 0)
                        {
                            if (flashon != 0)
                            {
                                grenadeflashing = true;
                                DrawPic(96, -16, hsb_weapons[flashon, 2]);
                            }
                        }
                    }
                    else if (i == 3)
                    {
                        if ((Cl.items & (RShared_IT_SHOTGUN << 4)) != 0)
                        {
                            if (flashon != 0 && !grenadeflashing)
                            {
                                DrawPic(96, -16, hsb_weapons[flashon, 3]);
                            }
                            else if (!grenadeflashing)
                            {
                                DrawPic(96, -16, hsb_weapons[0, 3]);
                            }
                        }
                        else
                            DrawPic(96, -16, hsb_weapons[flashon, 4]);
                    }
                    else
                        DrawPic(176 + (i * 24), -16, hsb_weapons[flashon, i]);

                    if (flashon > 1)
                        sb_updates = 0; // force update to remove flash
                }
            }
        }

        if (Com.rogue)
        {
            // check for powered up weapon.
            if (Cl.stats[(int)Stat.ActiveWeapon] >= RIT_LAVA_NAILGUN)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (Cl.stats[(int)Stat.ActiveWeapon] == (RIT_LAVA_NAILGUN << i))
                    {
                        DrawPic((i + 2) * 24, -16, rsb_weapons[i]);
                    }
                }
            }
        }

        // ammo counts
        for (int i = 0; i < 4; i++)
        {
            Utf3String num = default;
            num.Format(Cl.stats[(int)Stat.Shells + i]);
            if (num[0] != ' ')
                DrawCharacter((6 * i + 1) * 8 - 2, -24, 18 + num[0] - '0');
            if (num[1] != ' ')
                DrawCharacter((6 * i + 2) * 8 - 2, -24, 18 + num[1] - '0');
            if (num[2] != ' ')
                DrawCharacter((6 * i + 3) * 8 - 2, -24, 18 + num[2] - '0');
        }

        bool flashon2 = false;
        // items
        for (int i = 0; i < 6; i++)
        {
            if ((Cl.items & (1 << (17 + i))) != 0)
            {
                float time = Cl.item_gettime[17 + i];
                if (time != 0 && time > Cl.time - 2 && flashon2)
                {
                    // flash frame
                    sb_updates = 0;
                }
                else
                {
                    // MED 01/04/97 changed keys
                    if (!Com.hipnotic || i > 1)
                    {
                        DrawPic(192 + i * 16, -16, sb_items[i]);
                    }
                }
                if (time != 0 && time > Cl.time - 2)
                    sb_updates = 0;
            }
        }

        // MED 01/04/97 added hipnotic items
        // hipnotic items
        if (Com.hipnotic)
        {
            for (int i = 0; i < 2; i++)
                if ((Cl.items & (1 << (24 + i))) != 0)
                {
                    float time = Cl.item_gettime[24 + i];
                    if (time != 0 && time > Cl.time - 2 && flashon2)
                    {
                        // flash frame
                        sb_updates = 0;
                    }
                    else
                    {
                        DrawPic(288 + i * 16, -16, hsb_items[i]);
                    }
                    if (time != 0 && time > Cl.time - 2)
                        sb_updates = 0;
                }
        }

        if (Com.rogue)
        {
            // new rogue items
            for (int i = 0; i < 2; i++)
            {
                if ((Cl.items & (1 << (29 + i))) != 0)
                {
                    float time = Cl.item_gettime[29 + i];

                    if (time != 0 && time > Cl.time - 2 && flashon2)
                    {
                        // flash frame
                        sb_updates = 0;
                    }
                    else
                    {
                        DrawPic(288 + i * 16, -16, rsb_items[i]);
                    }

                    if (time != 0 && time > Cl.time - 2)
                        sb_updates = 0;
                }
            }
        }
        else
        {
            // sigils
            for (int i = 0; i < 4; i++)
            {
                if ((Cl.items & (1 << (28 + i))) != 0)
                {
                    float time = Cl.item_gettime[28 + i];
                    if (time != 0 && time > Cl.time - 2 && flashon2)
                    {
                        sb_updates = 0;
                    }
                    else
                    {
                        DrawPic(320 - 32 + i * 8, -16, sb_sigil[i]);
                    }
                    if (time != 0 && time > Cl.time - 2)
                        sb_updates = 0;
                }
            }
        }
    }

    // IT_SHOTGUN (quakedef.h) -- named distinctly to avoid clashing with any
    // future shared items-flag home.
    private const int RShared_IT_SHOTGUN = 1;

    //=============================================================================

    /*
    ===============
    Sbar_DrawFrags
    ===============
    */
    public static void DrawFrags()
    {
        SortFrags();

        int l = scoreboardlines <= 4 ? scoreboardlines : 4;

        int x = 23;
        int xofs = Cl.gametype == (int)GameType.Deathmatch ? 0 : (Canvas.Width - 320) >> 1;
        int y = Canvas.Height - SBAR_HEIGHT - 23;

        Utf3String num = default;

        for (int i = 0; i < l; i++)
        {
            int k = fragsort[i];
            ScoreBoard_t* s = &Cl.scores[k];
            if (s->Name[0] == 0)
                continue;

            int top = s->Colors & 0xf0;
            int bottom = (s->Colors & 15) << 4;
            top = ColorForMap(top);
            bottom = ColorForMap(bottom);

            Draw.Fill(xofs + x * 8 + 10, y, 28, 4, top);
            Draw.Fill(xofs + x * 8 + 10, y + 4, 28, 3, bottom);


            num.Format(s->Frags);
            DrawCharacter((x + 1) * 8, -24, num[0]);
            DrawCharacter((x + 2) * 8, -24, num[1]);
            DrawCharacter((x + 3) * 8, -24, num[2]);

            if (k == Cl.viewentity - 1)
            {
                DrawCharacter(x * 8 + 2, -24, 16);
                DrawCharacter((x + 4) * 8 - 4, -24, 17);
            }
            x += 4;
        }
    }

    //=============================================================================

    /*
    ===============
    Sbar_DrawFace
    ===============
    */
    public static void DrawFace()
    {
        // PGM 01/19/97 - team color drawing
        // PGM 03/02/97 - fixed so color swatch only appears in CTF modes
        if (Com.rogue && Cl.maxclients != 1 && Host.teamplay->Value > 3 && Host.teamplay->Value < 7)
        {
            ScoreBoard_t* s = &Cl.scores[Cl.viewentity - 1];
            // draw background
            int top = s->Colors & 0xf0;
            int bottom = (s->Colors & 15) << 4;
            top = ColorForMap(top);
            bottom = ColorForMap(bottom);

            int xofs = Cl.gametype == (int)GameType.Deathmatch ? 113 : ((Canvas.Width - 320) >> 1) + 113;

            DrawPic(112, 0, rsb_teambord);
            Draw.Fill(xofs, Canvas.Height - SBAR_HEIGHT + 3, 22, 9, top);
            Draw.Fill(xofs, Canvas.Height - SBAR_HEIGHT + 12, 22, 9, bottom);

            // draw number
            Utf3String num = default;
            num.Format(s->Frags);

            if (top == 8)
            {
                if (num[0] != ' ')
                    DrawCharacter(109, 3, 18 + num[0] - '0');
                if (num[1] != ' ')
                    DrawCharacter(116, 3, 18 + num[1] - '0');
                if (num[2] != ' ')
                    DrawCharacter(123, 3, 18 + num[2] - '0');
            }
            else
            {
                DrawCharacter(109, 3, num[0]);
                DrawCharacter(116, 3, num[1]);
                DrawCharacter(123, 3, num[2]);
            }

            return;
        }
        // PGM 01/19/97 - team color drawing

        if ((Cl.items & (IT_INVISIBILITY | IT_INVULNERABILITY)) == (IT_INVISIBILITY | IT_INVULNERABILITY))
        {
            DrawPic(112, 0, sb_face_invis_invuln);
            return;
        }
        if ((Cl.items & IT_QUAD) != 0)
        {
            DrawPic(112, 0, sb_face_quad);
            return;
        }
        if ((Cl.items & IT_INVISIBILITY) != 0)
        {
            DrawPic(112, 0, sb_face_invis);
            return;
        }
        if ((Cl.items & IT_INVULNERABILITY) != 0)
        {
            DrawPic(112, 0, sb_face_invuln);
            return;
        }

        int f = Cl.stats[(int)Stat.Health] >= 100 ? 4 : Cl.stats[(int)Stat.Health] / 20;
        if (f < 0)
            f = 0;

        int anim;
        if (Cl.time <= Cl.faceanimtime)
        {
            anim = 1;
            sb_updates = 0; // make sure the anim gets drawn over
        }
        else
        {
            anim = 0;
        }
        DrawPic(112, 0, sb_faces[f, anim]);
    }

    /*
    ===============
    Sbar_Draw
    ===============
    */
    public static void Draw_()
    {
        if (Screen.scr_con_current == Vid.vid.ConHeight)
            return; // console is full screen

        if (sb_updates >= Vid.vid.NumPages)
            return;

        Screen.scr_copyeverything = 1;

        sb_updates++;

        Canvas.Set(CanvasType.Sbar);

        // the side bars are cleared in physical pixels (sb_lines is in status bar canvas units)
        if (sb_lines != 0 && Canvas.Width > 320)
            Draw.TileClear(0, (int)Vid.vid.Height - sb_lines * Canvas.Scale, (int)Vid.vid.Width, sb_lines * Canvas.Scale);

        if (sb_lines > 24)
        {
            DrawInventory();
            if (Cl.maxclients != 1)
                DrawFrags();
        }

        if (sb_showscores || Cl.stats[(int)Stat.Health] <= 0)
        {
            DrawPic(0, 0, sb_scorebar);
            DrawScoreboard();
            sb_updates = 0;
        }
        else if (sb_lines != 0)
        {
            DrawPic(0, 0, sb_sbar);

            // keys (hipnotic only)
            // MED 01/04/97 moved keys here so they would not be overwritten
            if (Com.hipnotic)
            {
                if ((Cl.items & IT_KEY1) != 0)
                    DrawPic(209, 3, sb_items[0]);
                if ((Cl.items & IT_KEY2) != 0)
                    DrawPic(209, 12, sb_items[1]);
            }

            // armor
            if ((Cl.items & IT_INVULNERABILITY) != 0)
            {
                DrawNum(24, 0, 666, 3, 1);
                DrawPic(0, 0, Draw.draw_disc);
            }
            else
            {
                if (Com.rogue)
                {
                    DrawNum(24, 0, Cl.stats[(int)Stat.Armor], 3, Cl.stats[(int)Stat.Armor] <= 25 ? 1 : 0);
                    if ((Cl.items & RIT_ARMOR3) != 0)
                        DrawPic(0, 0, sb_armor[2]);
                    else if ((Cl.items & RIT_ARMOR2) != 0)
                        DrawPic(0, 0, sb_armor[1]);
                    else if ((Cl.items & RIT_ARMOR1) != 0)
                        DrawPic(0, 0, sb_armor[0]);
                }
                else
                {
                    DrawNum(24, 0, Cl.stats[(int)Stat.Armor], 3, Cl.stats[(int)Stat.Armor] <= 25 ? 1 : 0);
                    if ((Cl.items & IT_ARMOR3) != 0)
                        DrawPic(0, 0, sb_armor[2]);
                    else if ((Cl.items & IT_ARMOR2) != 0)
                        DrawPic(0, 0, sb_armor[1]);
                    else if ((Cl.items & IT_ARMOR1) != 0)
                        DrawPic(0, 0, sb_armor[0]);
                }
            }

            // face
            DrawFace();

            // health
            DrawNum(136, 0, Cl.stats[(int)Stat.Health], 3, Cl.stats[(int)Stat.Health] <= 25 ? 1 : 0);

            // ammo icon
            if (Com.rogue)
            {
                if ((Cl.items & RIT_SHELLS) != 0)
                    DrawPic(224, 0, sb_ammo[0]);
                else if ((Cl.items & RIT_NAILS) != 0)
                    DrawPic(224, 0, sb_ammo[1]);
                else if ((Cl.items & RIT_ROCKETS) != 0)
                    DrawPic(224, 0, sb_ammo[2]);
                else if ((Cl.items & RIT_CELLS) != 0)
                    DrawPic(224, 0, sb_ammo[3]);
                else if ((Cl.items & RIT_LAVA_NAILS) != 0)
                    DrawPic(224, 0, rsb_ammo[0]);
                else if ((Cl.items & RIT_PLASMA_AMMO) != 0)
                    DrawPic(224, 0, rsb_ammo[1]);
                else if ((Cl.items & RIT_MULTI_ROCKETS) != 0)
                    DrawPic(224, 0, rsb_ammo[2]);
            }
            else
            {
                if ((Cl.items & IT_SHELLS) != 0)
                    DrawPic(224, 0, sb_ammo[0]);
                else if ((Cl.items & IT_NAILS) != 0)
                    DrawPic(224, 0, sb_ammo[1]);
                else if ((Cl.items & IT_ROCKETS) != 0)
                    DrawPic(224, 0, sb_ammo[2]);
                else if ((Cl.items & IT_CELLS) != 0)
                    DrawPic(224, 0, sb_ammo[3]);
            }

            DrawNum(248, 0, Cl.stats[(int)Stat.Ammo], 3, Cl.stats[(int)Stat.Ammo] <= 10 ? 1 : 0);
        }

        Canvas.Set(CanvasType.Sbar); // the scoreboard overlays use the menu canvas

        if (Canvas.Width > 320)
        {
            if (Cl.gametype == (int)GameType.Deathmatch)
                MiniDeathmatchOverlay();
        }
    }

    //=============================================================================

    /*
    ==================
    Sbar_IntermissionNumber
    ==================
    */
    public static void IntermissionNumber(int x, int y, int num, int digits, int color)
    {
        byte* str = stackalloc byte[12];
        int l = Itoa(num, str);
        byte* ptr = str;
        if (l > digits)
            ptr += l - digits;
        if (l < digits)
            x += (digits - l) * 24;

        while (*ptr != 0)
        {
            int frame = *ptr == '-' ? STAT_MINUS : *ptr - '0';
            Draw.TransPic(x, y, sb_nums[color, frame]);
            x += 24;
            ptr++;
        }
    }

    /*
    ==================
    Sbar_DeathmatchOverlay
    ==================
    */
    public static void DeathmatchOverlay()
    {
        Screen.scr_copyeverything = 1;
        Screen.scr_fullupdate = 0;

        Canvas.Set(CanvasType.Menu);
        QPic_t* pic = Draw.CachePic("gfx/ranking.lmp"u8);
        Menu.DrawPic((320 - pic->Width) / 2, 8, pic);

        SortFrags();

        int l = scoreboardlines;

        int x = 80;
        int y = 40;
        for (int i = 0; i < l; i++)
        {
            int k = fragsort[i];
            ScoreBoard_t* s = &Cl.scores[k];
            if (s->Name[0] == 0)
                continue;

            int top = s->Colors & 0xf0;
            int bottom = (s->Colors & 15) << 4;
            top = ColorForMap(top);
            bottom = ColorForMap(bottom);

            Draw.Fill(x, y, 40, 4, top);
            Draw.Fill(x, y + 4, 40, 4, bottom);

            Utf3String num = default;
            num.Format(s->Frags);

            Draw.Character(x + 8, y, num[0]);
            Draw.Character(x + 16, y, num[1]);
            Draw.Character(x + 24, y, num[2]);

            if (k == Cl.viewentity - 1)
                Draw.Character(x - 8, y, 12);
            Draw.String(x + 64, y, s->Name);

            y += 10;
        }
    }

    /*
    ==================
    Sbar_MiniDeathmatchOverlay
    ==================
    */
    public static void MiniDeathmatchOverlay()
    {
        if (Canvas.Width < 512 || sb_lines == 0)
            return;

        Screen.scr_copyeverything = 1;
        Screen.scr_fullupdate = 0;

        SortFrags();

        int l = scoreboardlines;
        int y = Canvas.Height - sb_lines;
        int numlines = sb_lines / 8;
        if (numlines < 3)
            return;

        // find us
        int i;
        for (i = 0; i < scoreboardlines; i++)
            if (fragsort[i] == Cl.viewentity - 1)
                break;

        if (i == scoreboardlines) // we're not there
            i = 0;
        else // figure out start
            i -= numlines / 2;

        if (i > scoreboardlines - numlines)
            i = scoreboardlines - numlines;
        if (i < 0)
            i = 0;

        int x = 324;
        for (; i < scoreboardlines && y < Canvas.Height - 8; i++)
        {
            int k = fragsort[i];
            ScoreBoard_t* s = &Cl.scores[k];
            if (s->Name[0] == 0)
                continue;

            int top = s->Colors & 0xf0;
            int bottom = (s->Colors & 15) << 4;
            top = ColorForMap(top);
            bottom = ColorForMap(bottom);

            Draw.Fill(x, y + 1, 40, 3, top);
            Draw.Fill(x, y + 4, 40, 4, bottom);

            Utf3String num = default;
            num.Format(s->Frags);

            Draw.Character(x + 8, y, num[0]);
            Draw.Character(x + 16, y, num[1]);
            Draw.Character(x + 24, y, num[2]);

            if (k == Cl.viewentity - 1)
            {
                Draw.Character(x, y, 16);
                Draw.Character(x + 32, y, 17);
            }
            Draw.String(x + 48, y, s->Name);

            y += 8;
        }
    }

    /*
    ==================
    Sbar_IntermissionOverlay
    ==================
    */
    public static void IntermissionOverlay()
    {
        Screen.scr_copyeverything = 1;
        Screen.scr_fullupdate = 0;

        if (Cl.gametype == (int)GameType.Deathmatch)
        {
            DeathmatchOverlay();
            return;
        }

        Canvas.Set(CanvasType.Menu);
        QPic_t* pic = Draw.CachePic("gfx/complete.lmp"u8);
        Draw.Pic(64, 24, pic);

        pic = Draw.CachePic("gfx/inter.lmp"u8);
        Draw.TransPic(0, 56, pic);

        // time
        int dig = Cl.completed_time / 60;
        IntermissionNumber(160, 64, dig, 3, 0);
        int num = Cl.completed_time - dig * 60;
        Draw.TransPic(234, 64, sb_colon);
        Draw.TransPic(246, 64, sb_nums[0, num / 10]);
        Draw.TransPic(266, 64, sb_nums[0, num % 10]);

        IntermissionNumber(160, 104, Cl.stats[(int)Stat.Secrets], 3, 0);
        Draw.TransPic(232, 104, sb_slash);
        IntermissionNumber(240, 104, Cl.stats[(int)Stat.TotalSecrets], 3, 0);

        IntermissionNumber(160, 144, Cl.stats[(int)Stat.Monsters], 3, 0);
        Draw.TransPic(232, 144, sb_slash);
        IntermissionNumber(240, 144, Cl.stats[(int)Stat.TotalMonsters], 3, 0);
    }

    /*
    ==================
    Sbar_FinaleOverlay
    ==================
    */
    public static void FinaleOverlay()
    {
        Screen.scr_copyeverything = 1;

        Canvas.Set(CanvasType.Menu);
        QPic_t* pic = Draw.CachePic("gfx/finale.lmp"u8);
        Draw.TransPic((320 - pic->Width) / 2, 16, pic);
    }
}
