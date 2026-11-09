// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2002-2009 John Fitzgibbons and others
// Copyright (C) 2009-2014 Baker and others
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

using Quake.Engine.Memory;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static Quake.Engine.QuakeDef;

namespace Quake.Engine;

/// <summary>
/// common.c -- misc functions used in client and server.
/// </summary>
public static unsafe class Com
{
    public static readonly Utf8String GAMENAME = "id1"u8;

    private const int NUM_SAFE_ARGVS = 7;
    internal const int CMDLINE_LENGTH = 256;
    private const int MAX_FILES_IN_PACK = 2048;

    // if a packfile directory differs from this, it is assumed to be hacked
    private const int PAK0_COUNT = 339;
    private const int PAK0_CRC = 32981;

    private const int VA_BUFFER_SIZE = 1024;
    internal static readonly byte* va_string = NativeMemory.AllocArrayZeroed<byte>(VA_BUFFER_SIZE);

    /*
    ============
    va

    does a varargs printf into a temp buffer, so I don't need to have
    varargs versions of all text functions.
    FIXME: make this buffer size safe someday
    ============
    */

    public static byte* Va(string s)
    {
        if (s.Length >= VA_BUFFER_SIZE)
            s = s[..(VA_BUFFER_SIZE - 1)];
        CStr.Write(va_string, s);
        return va_string;
    }

    /*
    ============================================================================
                        LINK LIST / LIBRARY REPLACEMENT FUNCTIONS
    ============================================================================
    */
    // Q_memset/Q_memcpy/... live in Q.cs; ClearLink/RemoveLink/... live in Link.cs.

    /*
    ============================================================================
                        BYTE ORDER FUNCTIONS
    ============================================================================
    */

    // bigendien
    public static readonly bool bigendien = !BitConverter.IsLittleEndian;

    // ShortSwap
    public static short ShortSwap(short l) => BinaryPrimitives.ReverseEndianness(l);

    // ShortNoSwap
    public static short ShortNoSwap(short l) => l;

    // LongSwap
    public static int LongSwap(int l) => BinaryPrimitives.ReverseEndianness(l);

    // LongNoSwap
    public static int LongNoSwap(int l) => l;

    public static float FloatSwap(float f) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToInt32Bits(f)));

    // FloatNoSwap
    public static float FloatNoSwap(float f) => f;

    // BigShort
    public static short BigShort(short l) => bigendien ? ShortNoSwap(l) : ShortSwap(l);

    // LittleShort
    public static short LittleShort(short l) => bigendien ? ShortSwap(l) : ShortNoSwap(l);

    // BigLong
    public static int BigLong(int l) => bigendien ? LongNoSwap(l) : LongSwap(l);

    // LittleLong
    public static int LittleLong(int l) => bigendien ? LongSwap(l) : LongNoSwap(l);

    // BigFloat
    public static float BigFloat(float f) => bigendien ? FloatNoSwap(f) : FloatSwap(f);

    // LittleFloat
    public static float LittleFloat(float f) => bigendien ? FloatSwap(f) : FloatNoSwap(f);

    /*
    ============================================================================
                        COMMAND LINE
    ============================================================================
    */

    private static readonly byte*[] largv = new byte*[MAX_NUM_ARGVS + NUM_SAFE_ARGVS + 1];
    private static readonly Utf8String argvdummy = " "u8;

    private static readonly Utf8String[] safeargvs =
    [
        "-stdvid"u8, "-nolan"u8, "-nosound"u8, "-nocdaudio"u8,
        "-nojoy"u8, "-nomouse"u8, "-dibonly"u8,
    ];

    // com_token
    public static readonly byte* com_token = NativeMemory.AllocArray<byte>(1024);

    // com_argc
    public static int com_argc;

    // com_argv
    public static byte*[] com_argv = [];

    // com_cmdline
    public static readonly byte* com_cmdline = NativeMemory.AllocArrayZeroed<byte>(CMDLINE_LENGTH);

    // standard_quake, rogue, hipnotic
    public static bool standard_quake = true, rogue, hipnotic;

    // proghack
    public static bool proghack;

    // this graphic needs to be in the pak file to use registered features
    // pop
    public static readonly ushort[] pop =
    [
        0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
        0x0000, 0x0000, 0x6600, 0x0000, 0x0000, 0x0000, 0x6600, 0x0000,
        0x0000, 0x0066, 0x0000, 0x0000, 0x0000, 0x0000, 0x0067, 0x0000,
        0x0000, 0x6665, 0x0000, 0x0000, 0x0000, 0x0000, 0x0065, 0x6600,
        0x0063, 0x6561, 0x0000, 0x0000, 0x0000, 0x0000, 0x0061, 0x6563,
        0x0064, 0x6561, 0x0000, 0x0000, 0x0000, 0x0000, 0x0061, 0x6564,
        0x0064, 0x6564, 0x0000, 0x6469, 0x6969, 0x6400, 0x0064, 0x6564,
        0x0063, 0x6568, 0x6200, 0x0064, 0x6864, 0x0000, 0x6268, 0x6563,
        0x0000, 0x6567, 0x6963, 0x0064, 0x6764, 0x0063, 0x6967, 0x6500,
        0x0000, 0x6266, 0x6769, 0x6a68, 0x6768, 0x6a69, 0x6766, 0x6200,
        0x0000, 0x0062, 0x6566, 0x6666, 0x6666, 0x6666, 0x6562, 0x0000,
        0x0000, 0x0000, 0x0062, 0x6364, 0x6664, 0x6362, 0x0000, 0x0000,
        0x0000, 0x0000, 0x0000, 0x0062, 0x6662, 0x0000, 0x0000, 0x0000,
        0x0000, 0x0000, 0x0000, 0x0061, 0x6661, 0x0000, 0x0000, 0x0000,
        0x0000, 0x0000, 0x0000, 0x0000, 0x6500, 0x0000, 0x0000, 0x0000,
        0x0000, 0x0000, 0x0000, 0x0000, 0x6400, 0x0000, 0x0000, 0x0000,
    ];

    // registered
    public static readonly Cvar_t* registered_cvar = Cvar.Alloc("registered"u8, "0"u8);

    // cmdline
    private static readonly Cvar_t* cmdline_cvar = Cvar.Alloc("cmdline"u8, "0"u8, server: true);

    public static bool com_modified; // set true if using non-id files
    public static int static_registered = 1; // only for startup check, then set

    // COM_CheckParm
    // Returns the position (1 to argc-1) in the program's argument list
    // where the given parameter apears, or 0 if not present
    public static int CheckParm(Utf8String parm)
    {
        for (int i = 1; i < com_argc; i++)
        {
            if (com_argv[i] == null)
                continue; // NEXTSTEP sometimes clears appkit vars.
            if (Q.StrCmp(parm, com_argv[i]) == 0)
                return i;
        }

        return 0;
    }

    // COM_InitArgv
    public static void InitArgv(int argc, byte*[] argv)
    {
        // reconstitute the command line for the cmdline externally visible cvar
        int n = 0;

        for (int j = 0; j < MAX_NUM_ARGVS && j < argc; j++)
        {
            int i = 0;

            while (n < CMDLINE_LENGTH - 1 && argv[j][i] != 0)
            {
                com_cmdline[n++] = argv[j][i++];
            }

            if (n < CMDLINE_LENGTH - 1)
                com_cmdline[n++] = (byte)' ';
            else
                break;
        }

        com_cmdline[n] = 0;

        bool safe = false;

        for (com_argc = 0; com_argc < MAX_NUM_ARGVS && com_argc < argc; com_argc++)
        {
            largv[com_argc] = argv[com_argc];
            if (Q.StrCmp(new Utf8String("-safe"u8), argv[com_argc]) == 0)
                safe = true;
        }

        if (safe)
        {
            // force all the safe-mode switches. Note that we reserved extra space in
            // case we need to add these, so we don't need an overflow check
            for (int i = 0; i < NUM_SAFE_ARGVS; i++)
            {
                largv[com_argc] = safeargvs[i];
                com_argc++;
            }
        }

        largv[com_argc] = argvdummy;
        com_argv = largv;

        if (CheckParm("-rogue"u8) != 0)
        {
            rogue = true;
            standard_quake = false;
        }

        if (CheckParm("-hipnotic"u8) != 0)
        {
            hipnotic = true;
            standard_quake = false;
        }
    }

    // COM_Init
    public static void Init()
    {
        // registered_cvar/cmdline_cvar are process-lifetime statics, but
        // Cvar.RegisterVariable repoints their .String at a fresh
        // Zone.Malloc'd copy every time it runs (variable->String =
        // Zone.Malloc(...); Q.StrCpy(...)) -- fine for the original, where
        // Com_Init runs exactly once per process, but this port's test
        // suite calls Host.Init/Com.Init once per test class, each with its
        // own Zone arena that gets NativeMemory.Free'd in that class's
        // Teardown. Without this reset, the next call's RegisterVariable
        // would read variable->String (still pointing into the previous
        // test class's already-freed arena) as its "copy the old value"
        // source -- a use-after-free that only surfaces as an
        // AccessViolationException once that memory is actually unmapped,
        // deep into a full test run. Resetting to the literal default here
        // keeps every call self-contained, matching Cvar_t's original
        // static-string-literal initializer.
        registered_cvar->String = "0"u8;
        cmdline_cvar->String = "0"u8;

        Cvar.RegisterVariable(registered_cvar);
        Cvar.RegisterVariable(cmdline_cvar);
        Cmd.AddCommand("path"u8, &Path_f);

        InitFilesystem();
        CheckRegistered();
    }

    // va() is not ported: string.Format already supersedes it, and that is the
    // established convention elsewhere in the port (Sys.Error, Con.Printf).

    // memsearch -- just for debugging
    public static int MemSearch(byte* start, int count, int search)
    {
        for (int i = 0; i < count; i++)
            if (start[i] == search)
                return i;
        return -1;
    }

    /*
    ============================================================================
                        FILE / PATH STRING PARSING
    ============================================================================
    */

    // COM_SkipPath
    public static byte* SkipPath(byte* pathname)
    {
        byte* last = pathname;
        while (*pathname != 0)
        {
            if (*pathname == (byte)'/')
                last = pathname + 1;
            pathname++;
        }
        return last;
    }

    // COM_StripExtension
    public static void StripExtension(byte* @in, byte* @out)
    {
        while (*@in != 0 && *@in != (byte)'.')
            *@out++ = *@in++;
        *@out = 0;
    }

    internal static readonly byte* extenBuf = NativeMemory.AllocArray<byte>(8);

    // COM_FileExtension
    public static byte* FileExtension(byte* @in)
    {
        while (*@in != 0 && *@in != (byte)'.')
            @in++;

        if (*@in == 0)
        {
            extenBuf[0] = 0;
            return extenBuf;
        }

        @in++;
        int i;
        for (i = 0; i < 7 && *@in != 0; i++, @in++)
            extenBuf[i] = *@in;
        extenBuf[i] = 0;
        return extenBuf;
    }

    private static readonly Utf8String questionModel = "?model?"u8;

    // COM_FileBase
    public static void FileBase(byte* @in, byte* @out)
    {
        byte* s = @in + Q.StrLen(@in) - 1;

        while (s != @in && *s != (byte)'.')
            s--;

        byte* s2;
        for (s2 = s; *s2 != 0 && *s2 != (byte)'/'; s2--)
        {
        }

        if (s - s2 < 2)
        {
            Q.StrCpy(@out, questionModel);
        }
        else
        {
            s--;
            int count = (int)(s - s2);
            for (int i = 0; i < count; i++)
                @out[i] = s2[1 + i];
            @out[count] = 0;
        }
    }

    // COM_DefaultExtension
    public static void DefaultExtension(byte* path, Utf8String extension)
    {
        // if path doesn't have a .EXT, append extension
        // (extension should include the .)
        byte* src = path + Q.StrLen(path) - 1;

        while (*src != (byte)'/' && src != path)
        {
            if (*src == (byte)'.')
                return; // it has an extension
            src--;
        }

        Q.StrCat(path, extension);
    }

    // COM_Parse
    // Parse a token out of a string
    public static byte* Parse(byte* data)
    {
        int len = 0;
        com_token[0] = 0;

        if (data == null)
            return null;

        int c;
    skipwhite:
        while ((c = *data) <= ' ')
        {
            if (c == 0)
                return null; // end of file;
            data++;
        }

        // skip // comments
        if (c == '/' && data[1] == '/')
        {
            while (*data != 0 && *data != '\n')
                data++;
            goto skipwhite;
        }

        // handle quoted strings specially
        if (c == '\"')
        {
            data++;
            while (true)
            {
                c = *data++;
                if (c == '\"' || c == 0)
                {
                    com_token[len] = 0;
                    return data;
                }
                com_token[len] = (byte)c;
                len++;
            }
        }

        // parse single characters
        if (c is '{' or '}' or ')' or '(' or '\'' or ':')
        {
            com_token[len] = (byte)c;
            len++;
            com_token[len] = 0;
            return data + 1;
        }

        // parse a regular word
        do
        {
            com_token[len] = (byte)c;
            data++;
            len++;
            c = *data;
            if (c is '{' or '}' or ')' or '(' or '\'' or ':')
                break;
        } while (c > 32);

        com_token[len] = 0;
        return data;
    }

    /*
    =============================================================================
                        QUAKE FILESYSTEM
    =============================================================================
    */

    // com_filesize
    public static int com_filesize;

    // com_cachedir
    public static readonly byte* com_cachedir = NativeMemory.AllocArrayZeroed<byte>(MAX_OSPATH);

    // com_gamedir
    public static readonly byte* com_gamedir = NativeMemory.AllocArrayZeroed<byte>(MAX_OSPATH);

    // com_searchpaths
    public static SearchPath_t* com_searchpaths;

    // loadcache
    public static CacheUser* loadcache;

    // loadbuf
    public static byte* loadbuf;

    // loadsize
    public static int loadsize;

    private static readonly Utf8String slash = "/"u8;

    // COM_WriteFile
    // The filename will be prefixed by the current game directory
    public static void WriteFile(byte* filename, void* data, int len)
    {
        byte* name = stackalloc byte[MAX_OSPATH];
        Q.StrCpy(name, com_gamedir);
        Q.StrCat(name, slash);
        Q.StrCat(name, filename);

        int handle = Sys.FileOpenWrite(name);
        if (handle == -1)
        {
            Sys.Printf($"COM_WriteFile: failed on {name}\n");
            return;
        }

        Sys.Printf($"COM_WriteFile: {name}\n");
        Sys.FileWrite(handle, data, len);
        Sys.FileClose(handle);
    }

    // COM_CreatePath
    // Only used for CopyFile
    public static void CreatePath(byte* path)
    {
        for (byte* ofs = path + 1; *ofs != 0; ofs++)
        {
            if (*ofs == (byte)'/')
            {
                // create the directory
                *ofs = 0;
                Sys.Mkdir(path);
                *ofs = (byte)'/';
            }
        }
    }

    // COM_CopyFile
    // Copies a file over from the net to the local cache, creating any directories
    // needed. This is for the convenience of developers using ISDN from home.
    public static void CopyFile(byte* netpath, byte* cachepath)
    {
        int inHandle;
        int remaining = Sys.FileOpenRead(netpath, &inHandle);
        CreatePath(cachepath); // create directories up to the cache file
        int outHandle = Sys.FileOpenWrite(cachepath);

        byte* buf = stackalloc byte[4096];
        while (remaining > 0)
        {
            int count = remaining < 4096 ? remaining : 4096;
            Sys.FileRead(inHandle, buf, count);
            Sys.FileWrite(outHandle, buf, count);
            remaining -= count;
        }

        Sys.FileClose(inHandle);
        Sys.FileClose(outHandle);
    }

    // COM_FindFile
    // Finds the file in the search path.
    // Sets com_filesize and one of handle or file.
    // freshHandle mirrors the original's handle-vs-FILE* split: false shares a
    // pack's already-open handle (like the original does for the handle case),
    // true always opens an independent Sys handle (like the original's fopen
    // for the FILE* case), since this port has no FILE* type to distinguish them.
    public static int FindFile(byte* filename, int* outHandle, bool freshHandle)
    {
        SearchPath_t* search = com_searchpaths;
        if (proghack)
        {
            // gross hack to use quake 1 progs with quake 2 maps
            if (Q.StrCmp(filename, "progs.dat"u8) == 0)
                search = search->Next;
        }

        for (; search != null; search = search->Next)
        {
            if (search->Pack != null)
            {
                // look through all the pak file elements
                Pack_t* pak = search->Pack;
                for (int i = 0; i < pak->NumFiles; i++)
                {
                    if (Q.StrCmp(pak->Files[i].Name, filename) == 0)
                    {
                        // found it!
                        Sys.Printf($"PackFile: {pak->FileName} : {filename}\n");

                        int handle;
                        if (!freshHandle)
                        {
                            handle = pak->Handle;
                            Sys.FileSeek(pak->Handle, pak->Files[i].FilePos);
                        }
                        else
                        {
                            // open a new file on the pakfile
                            Sys.FileOpenRead(pak->FileName, &handle);
                            if (handle != -1)
                                Sys.FileSeek(handle, pak->Files[i].FilePos);
                        }

                        *outHandle = handle;
                        com_filesize = pak->Files[i].FileLen;
                        return com_filesize;
                    }
                }
            }
            else
            {
                // check a file in the directory tree
                if (static_registered == 0)
                {
                    // if not a registered version, don't ever go beyond base
                    if (Q.StrRChr(filename, (byte)'/') != null || Q.StrRChr(filename, (byte)'\\') != null)
                        continue;
                }

                byte* netpath = stackalloc byte[MAX_OSPATH];
                Q.StrCpy(netpath, search->FileName);
                Q.StrCat(netpath, slash);
                Q.StrCat(netpath, filename);

                int findtime = Sys.FileTime(netpath);
                if (findtime == -1)
                    continue;

                // see if the file needs to be updated in the cache
                byte* cachepath = stackalloc byte[MAX_OSPATH];
                if (com_cachedir[0] == 0)
                {
                    Q.StrCpy(cachepath, netpath);
                }
                else
                {
                    // WinQuake always built with _WIN32 defined: strip a
                    // "C:"-style drive prefix from netpath before appending
                    // it to com_cachedir, instead of concatenating it whole.
                    Q.StrCpy(cachepath, com_cachedir);
                    if (Q.StrLen(netpath) < 2 || netpath[1] != (byte)':')
                        Q.StrCat(cachepath, netpath);
                    else
                        Q.StrCat(cachepath, netpath + 2);

                    int cachetime = Sys.FileTime(cachepath);
                    if (cachetime < findtime)
                        CopyFile(netpath, cachepath);
                    Q.StrCpy(netpath, cachepath);
                }

                Sys.Printf($"FindFile: {netpath}\n");
                int i;
                com_filesize = Sys.FileOpenRead(netpath, &i);
                if (!freshHandle)
                {
                    *outHandle = i;
                }
                else
                {
                    Sys.FileClose(i);
                    Sys.FileOpenRead(netpath, &i);
                    *outHandle = i;
                }

                return com_filesize;
            }
        }

        Sys.Printf($"FindFile: can't find {filename}\n");

        *outHandle = -1;
        com_filesize = -1;
        return -1;
    }

    // COM_FindFile
    public static int FindFile(byte* filename, int* handle) => FindFile(filename, handle, false);

    // COM_OpenFile
    // filename never has a leading slash, but may contain directory walks
    // returns a handle and a length
    // it may actually be inside a pak file
    public static int OpenFile(Utf8String filename, int* handle) => FindFile(filename, handle, false);

    // COM_FOpenFile
    // If the requested file is inside a packfile, a new handle will be opened
    // into the file (the original opens a new FILE*; there is no FILE* type in
    // this port, so a fresh Sys file handle is used instead).
    public static int FOpenFile(Utf8String filename, nint* file)
    {
        int handle;
        int len = FindFile(filename, &handle, true);
        *file = handle;
        return len;
    }

    // COM_CloseFile
    // If it is a pak file handle, don't really close it
    public static void CloseFile(int h)
    {
        for (SearchPath_t* s = com_searchpaths; s != null; s = s->Next)
            if (s->Pack != null && s->Pack->Handle == h)
                return;

        Sys.FileClose(h);
    }

    // COM_LoadFile
    // Filename are reletive to the quake directory.
    // Allways appends a 0 byte.
    public static byte* LoadFile(byte* path, int usehunk)
    {
        int h;
        int len = OpenFile(path, &h);
        if (h == -1)
            return null;

        // extract the filename base name for hunk tag
        byte* baseName = stackalloc byte[32];
        FileBase(path, baseName);

        byte* buf;
        if (usehunk == 1)
            buf = (byte*)Hunk.AllocName(len + 1, baseName);
        else if (usehunk == 2)
            buf = (byte*)Hunk.TempAlloc(len + 1);
        else if (usehunk == 0)
            buf = (byte*)Zone.Malloc(len + 1);
        else if (usehunk == 3)
            buf = (byte*)Cache.Alloc(loadcache, len + 1, baseName);
        else if (usehunk == 4)
            buf = len + 1 > loadsize ? (byte*)Hunk.TempAlloc(len + 1) : loadbuf;
        else
        {
            Sys.Error("COM_LoadFile: bad usehunk"u8);
            return null;
        }

        if (buf == null)
            Sys.Error($"COM_LoadFile: not enough space for {path}");

        buf[len] = 0;

        Draw.BeginDisc();
        Sys.FileRead(h, buf, len);
        CloseFile(h);
        Draw.EndDisc();

        return buf;
    }

    // COM_LoadHunkFile
    public static byte* LoadHunkFile(Utf8String path) => LoadFile(path, 1);

    // COM_LoadCacheFile
    public static void LoadCacheFile(byte* path, CacheUser* cu)
    {
        loadcache = cu;
        LoadFile(path, 3);
    }

    // COM_LoadStackFile
    // uses temp hunk if larger than bufsize
    public static byte* LoadStackFile(byte* path, void* buffer, int bufsize)
    {
        loadbuf = (byte*)buffer;
        loadsize = bufsize;
        return LoadFile(path, 4);
    }

    // COM_LoadPackFile
    // Takes an explicit (not game tree related) path to a pak file.
    // Loads the header and directory, adding the files at the beginning
    // of the list so they override previous pack files.
    public static Pack_t* LoadPackFile(byte* packfile)
    {
        int packhandle;
        if (Sys.FileOpenRead(packfile, &packhandle) == -1)
            return null;

        DPackHeader_t header;
        Sys.FileRead(packhandle, &header, sizeof(DPackHeader_t));
        if (header.Id[0] != (byte)'P' || header.Id[1] != (byte)'A' ||
            header.Id[2] != (byte)'C' || header.Id[3] != (byte)'K')
            Sys.Error($"{packfile} is not a packfile");

        header.DirOfs = LittleLong(header.DirOfs);
        header.DirLen = LittleLong(header.DirLen);

        // Neither WinQuake nor Mark V validate these; debug-only guards.
        Debug.Assert(header.DirLen >= 0 && header.DirOfs >= 0, "COM_LoadPackFile: negative directory offset/length");

        int numpackfiles = header.DirLen / sizeof(DPackFile_t);

        if (numpackfiles > MAX_FILES_IN_PACK)
            Sys.Error($"{packfile} has {numpackfiles} files");

        if (numpackfiles != PAK0_COUNT)
            com_modified = true; // not the original file

        PackFile_t* newfiles = (PackFile_t*)Hunk.AllocName(numpackfiles * sizeof(PackFile_t), "packfile"u8);

        // Sized to the directory actually in the file (the original used a
        // MAX_FILES_IN_PACK-sized stack array, ~128 KB); DirLen may not be a
        // multiple of sizeof(DPackFile_t), so it is the read length that counts.
        int dirLen = Math.Max(header.DirLen, 0);
        DPackFile_t* info = (DPackFile_t*)NativeMemory.AllocArray<byte>(Math.Max(dirLen, 1));
        try
        {
            Sys.FileSeek(packhandle, header.DirOfs);
            int dirRead = Sys.FileRead(packhandle, info, dirLen);
            Debug.Assert(dirRead == dirLen, "COM_LoadPackFile: truncated pack directory");

            // crc the directory to check for modifications
            Crc.Init(out ushort crc);
            byte* infoBytes = (byte*)info;
            for (int i = 0; i < dirLen; i++)
                Crc.ProcessByte(ref crc, infoBytes[i]);
            if (Crc.Value(crc) != PAK0_CRC)
                com_modified = true;

            // parse the directory
            for (int i = 0; i < numpackfiles; i++)
            {
                Q.StrCpy(newfiles[i].Name, info[i].Name);
                newfiles[i].FilePos = LittleLong(info[i].FilePos);
                newfiles[i].FileLen = LittleLong(info[i].FileLen);
            }
        }
        finally
        {
            NativeMemory.Free(info);
        }

        Pack_t* pack = (Pack_t*)Hunk.Alloc(sizeof(Pack_t));
        Q.StrCpy(pack->FileName, packfile);
        pack->Handle = packhandle;
        pack->NumFiles = numpackfiles;
        pack->Files = newfiles;

        Con.Printf($"Added packfile {packfile} ({numpackfiles} files)\n");
        return pack;
    }

    // itoa: writes the decimal representation of value (no sign handling needed --
    // only ever called with the small non-negative pak index) followed by a 0 byte.
    private static void WriteAsciiInt(byte* dest, int value)
    {
        CStr.Write(dest, value.ToString());
    }

    // COM_AddGameDirectory
    // Sets com_gamedir, adds the directory to the head of the path,
    // then loads and adds pak1.pak pak2.pak ...
    public static void AddGameDirectory(byte* dir)
    {
        Q.StrCpy(com_gamedir, dir);

        // add the directory to the search path
        SearchPath_t* search = (SearchPath_t*)Hunk.Alloc(sizeof(SearchPath_t));
        Q.StrCpy(search->FileName, dir);
        search->Next = com_searchpaths;
        com_searchpaths = search;

        // add any pak files in the format pak0.pak pak1.pak, ...
        byte* numBuf = stackalloc byte[16];
        byte* pakfile = stackalloc byte[MAX_OSPATH];
        for (int i = 0; ; i++)
        {
            Q.StrCpy(pakfile, dir);
            Q.StrCat(pakfile, "/pak"u8);
            WriteAsciiInt(numBuf, i);
            Q.StrCat(pakfile, numBuf);
            Q.StrCat(pakfile, ".pak"u8);

            Pack_t* pak = LoadPackFile(pakfile);
            if (pak == null)
                break;

            search = (SearchPath_t*)Hunk.Alloc(sizeof(SearchPath_t));
            search->Pack = pak;
            search->Next = com_searchpaths;
            com_searchpaths = search;
        }
    }

    // COM_InitFilesystem
    public static void InitFilesystem()
    {
        byte* basedir = stackalloc byte[MAX_OSPATH];

        // -basedir <path>
        // Overrides the system supplied base directory (under GAMENAME)
        int i = CheckParm("-basedir"u8);
        if (i != 0 && i < com_argc - 1)
            Q.StrCpy(basedir, com_argv[i + 1]);
        else
            Q.StrCpy(basedir, Host.host_parms.BaseDir);

        int j = Q.StrLen(basedir);
        if (j > 0 && (basedir[j - 1] == (byte)'\\' || basedir[j - 1] == (byte)'/'))
            basedir[j - 1] = 0;

        // -cachedir <path>
        // Overrides the system supplied cache directory (NULL or /qcache)
        // -cachedir - will disable caching.
        i = CheckParm("-cachedir"u8);
        if (i != 0 && i < com_argc - 1)
        {
            if (com_argv[i + 1][0] == (byte)'-')
                com_cachedir[0] = 0;
            else
                Q.StrCpy(com_cachedir, com_argv[i + 1]);
        }
        else if (Host.host_parms.CacheDir != null && *Host.host_parms.CacheDir != 0)
        {
            Q.StrCpy(com_cachedir, Host.host_parms.CacheDir);
        }
        else
        {
            com_cachedir[0] = 0;
        }

        // start up with GAMENAME by default (id1)
        byte* gamedir = stackalloc byte[MAX_OSPATH];
        Q.StrCpy(gamedir, basedir);
        // Q.StrCat(gamedir, Va("/" + GAMENAME));
        Q.StrCat(gamedir, "/"u8);
        Q.StrCat(gamedir, GAMENAME);
        AddGameDirectory(gamedir);

        if (CheckParm("-rogue"u8) != 0)
        {
            Q.StrCpy(gamedir, basedir);
            Q.StrCat(gamedir, "/rogue"u8);
            AddGameDirectory(gamedir);
        }

        if (CheckParm("-hipnotic"u8) != 0)
        {
            Q.StrCpy(gamedir, basedir);
            Q.StrCat(gamedir, "/hipnotic"u8);
            AddGameDirectory(gamedir);
        }

        // -game <gamedir>
        // Adds basedir/gamedir as an override game
        i = CheckParm("-game"u8);
        if (i != 0 && i < com_argc - 1)
        {
            com_modified = true;
            Q.StrCpy(gamedir, basedir);
            Q.StrCat(gamedir, "/"u8);
            Q.StrCat(gamedir, com_argv[i + 1]);
            AddGameDirectory(gamedir);
        }

        // -path <dir or packfile> [<dir or packfile>] ...
        // Fully specifies the exact serach path, overriding the generated one
        i = CheckParm("-path"u8);
        if (i != 0)
        {
            com_modified = true;
            com_searchpaths = null;
            while (++i < com_argc)
            {
                if (com_argv[i] == null || com_argv[i][0] == (byte)'+' || com_argv[i][0] == (byte)'-')
                    break;

                SearchPath_t* search = (SearchPath_t*)Hunk.Alloc(sizeof(SearchPath_t));
                if (Q.StrCmp(FileExtension(com_argv[i]), "pak"u8) == 0)
                {
                    search->Pack = LoadPackFile(com_argv[i]);
                    if (search->Pack == null)
                        Sys.Error($"Couldn't load packfile: {com_argv[i]}");
                }
                else
                {
                    Q.StrCpy(search->FileName, com_argv[i]);
                }

                search->Next = com_searchpaths;
                com_searchpaths = search;
            }
        }

        if (CheckParm("-proghack"u8) != 0)
            proghack = true;
    }

    // COM_Path_f
    public static void Path_f()
    {
        Con.Printf("Current search path:\n"u8);
        for (SearchPath_t* s = com_searchpaths; s != null; s = s->Next)
        {
            if (s->Pack != null)
                Con.Printf($"{s->Pack->FileName} ({s->Pack->NumFiles} files)\n");
            else
                Con.Printf($"{s->FileName}\n");
        }
    }

    // COM_CheckRegistered
    // Looks for the pop.txt file and verifies it.
    // Sets the "registered" cvar.
    // Immediately exits out if an alternate game was attempted to be started without
    // being registered.
    public static void CheckRegistered()
    {
        ushort* check = stackalloc ushort[128];

        int h;
        OpenFile("gfx/pop.lmp"u8, &h);
        static_registered = 0;

        if (h == -1)
        {
            Con.Printf("Playing shareware version.\n"u8);
            if (com_modified)
                Sys.Error("You must have the registered version to use modified games"u8);
            return;
        }

        Sys.FileRead(h, check, 128 * sizeof(ushort));
        CloseFile(h);

        for (int i = 0; i < 128; i++)
            if (pop[i] != (ushort)BigShort((short)check[i]))
                Sys.Error("Corrupted data file."u8);

        Cvar.Set(cmdline_cvar->Name, com_cmdline);
        Cvar.Set(registered_cvar->Name, "1"u8);
        static_registered = 1;
        Con.Printf("Playing registered version.\n"u8);
    }
}
