// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 1996-1997 Id Software, Inc.
// Copyright (C) 2026 Alexandre Rogozine
// Ported to C#/.NET; original source licensed under the GNU General Public License, version 2.

namespace Quake.Engine;

/// <summary>
/// common.c — generic doubly-linked list helpers (used for headnode-style lists).
/// </summary>
public static unsafe class Link
{
    // ClearLink is used for new headnodes
    public static void ClearLink(Link_t* l)
    {
        l->Prev = l->Next = l;
    }

    // RemoveLink
    public static void RemoveLink(Link_t* l)
    {
        l->Next->Prev = l->Prev;
        l->Prev->Next = l->Next;
    }

    // InsertLinkBefore
    public static void InsertLinkBefore(Link_t* l, Link_t* before)
    {
        l->Next = before;
        l->Prev = before->Prev;
        l->Prev->Next = l;
        l->Next->Prev = l;
    }

    // InsertLinkAfter
    public static void InsertLinkAfter(Link_t* l, Link_t* after)
    {
        l->Next = after->Next;
        l->Prev = after;
        l->Prev->Next = l;
        l->Next->Prev = l;
    }
}
