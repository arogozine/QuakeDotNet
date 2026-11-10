// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

namespace Quake.Engine;

internal static unsafe class StringHelper
{
    // M_Setup_Key / M_SerialConfig_Key / M_ModemConfig_Key / M_LanConfig_Key
    // default case: if (l < maxLen) { buf[l+1] = 0; buf[l] = k; }
    internal static void FieldAppend(byte* buf, int maxLen, int key)
    {
        int l = Q.StrLen(buf);
        if (l < maxLen)
        {
            buf[l + 1] = 0;
            buf[l] = (byte)key;
        }
    }

    // K_BACKSPACE case: if (strlen(buf)) buf[strlen(buf) - 1] = 0;
    internal static void FieldBackspace(byte* buf)
    {
        int l = Q.StrLen(buf);
        if (l != 0)
        {
            buf[l - 1] = 0;
        }
    }
}
