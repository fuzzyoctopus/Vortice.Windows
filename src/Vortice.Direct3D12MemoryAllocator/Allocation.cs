// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using Vortice.Direct3D12;

namespace Vortice.Direct3D12MemoryAllocator;

public sealed class Allocation : IDisposable
{
    internal Allocation(IntPtr nativePointer)
    {
        NativePointer = nativePointer;
    }

    public IntPtr NativePointer { get; private set; }

    public bool IsNull => NativePointer == IntPtr.Zero;

    public ulong Offset
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.D3D12MA_Allocation_GetOffset(NativePointer);
        }
    }

    public ulong Alignment
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.D3D12MA_Allocation_GetAlignment(NativePointer);
        }
    }

    public ulong Size
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.D3D12MA_Allocation_GetSize(NativePointer);
        }
    }

    public ID3D12Resource? Resource
    {
        get
        {
            EnsureNotDisposed();
            IntPtr nativeResource = NativeMethods.D3D12MA_Allocation_GetResource(NativePointer);
            if (nativeResource == IntPtr.Zero)
            {
                return null;
            }

            return new ID3D12Resource(nativeResource);
        }
    }

    public uint AddRef()
    {
        EnsureNotDisposed();
        return NativeMethods.D3D12MA_Allocation_AddRef(NativePointer);
    }

    public uint Release()
    {
        if (IsNull)
        {
            return 0;
        }

        uint result = NativeMethods.D3D12MA_Allocation_Release(NativePointer);
        if (result == 0)
        {
            NativePointer = IntPtr.Zero;
        }

        return result;
    }

    public void Dispose()
    {
        IntPtr nativePointer = NativePointer;
        NativePointer = IntPtr.Zero;

        GC.SuppressFinalize(this);
        if (nativePointer != IntPtr.Zero)
        {
            _ = NativeMethods.D3D12MA_Allocation_Release(nativePointer);
        }
    }

    ~Allocation()
    {
        IntPtr nativePointer = NativePointer;
        NativePointer = IntPtr.Zero;
        if (nativePointer != IntPtr.Zero)
        {
            _ = NativeMethods.D3D12MA_Allocation_Release(nativePointer);
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(NativePointer == IntPtr.Zero, this);
    }
}
