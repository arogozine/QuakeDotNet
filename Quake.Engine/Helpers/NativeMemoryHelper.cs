// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Quake.Engine;

internal static unsafe class NativeMemoryHelper
{
    extension(NativeMemory)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte* AllocSimdAligned(int size)
        {
            int offset = (size & 31);
            size += 32 - offset;

            return (byte*)NativeMemory.AlignedAlloc((nuint)size, 32);
        }
    }

    extension <T>(NativeMemory)
        where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T* Alloc()
        {
            return (T*)NativeMemory.Alloc((nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T* AllocZeroed()
        {
            return (T*)NativeMemory.AllocZeroed((nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T* AllocSimdAlignedArray(int size)
        {
            size *= sizeof(T);
            int offset = (size & 31);
            size += 32 - offset;

            return (T*)NativeMemory.AlignedAlloc((nuint)size, 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T* AllocArray(int size)
        {
            return (T*)NativeMemory.Alloc((nuint)size, elementSize: (nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T* AllocArrayZeroed([ConstantExpected] int size)
        {
            return (T*)NativeMemory.AllocZeroed((nuint)size, elementSize: (nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T** AllocPointerArray([ConstantExpected] int size)
        {
            return (T**)NativeMemory.AllocZeroed((nuint)size, elementSize: (nuint)IntPtr.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Clear(T* ptr)
        {
            NativeMemory.Clear(ptr, (nuint)sizeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Clear(T* ptr, int count)
        {
            NativeMemory.Clear(ptr, (nuint)(count * sizeof(T)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ClearPointerArray(T** ptr, int count)
        {
            NativeMemory.Clear(ptr, (nuint)(count * IntPtr.Size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Copy(T* source, T* destination, int count)
        {
            NativeMemory.Copy((void*)source, (void*)destination, (nuint)(count * sizeof(T)));
        }
    }
}
