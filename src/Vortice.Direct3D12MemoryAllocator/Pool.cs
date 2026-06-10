// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12MemoryAllocator;

public sealed unsafe class Pool : IDisposable
{
    internal Pool(IntPtr nativePointer)
    {
        NativePointer = nativePointer;
    }

    public IntPtr NativePointer { get; private set; }

    public bool IsNull => NativePointer == IntPtr.Zero;

    public PoolDescription Description
    {
        get
        {
            EnsureNotDisposed();
            PoolDescriptionNative description = default;
            NativeMethods.D3D12MA_Pool_GetDesc(NativePointer, &description);
            return PoolDescription.FromNative(in description);
        }
    }

    public void GetStatistics(out Statistics statistics)
    {
        EnsureNotDisposed();
        NativeMethods.D3D12MA_Pool_GetStatistics(NativePointer, out statistics);
    }

    public void CalculateStatistics(out DetailedStatistics statistics)
    {
        EnsureNotDisposed();
        NativeMethods.D3D12MA_Pool_CalculateStatistics(NativePointer, out statistics);
    }

    public uint AddRef()
    {
        EnsureNotDisposed();
        return NativeMethods.D3D12MA_Pool_AddRef(NativePointer);
    }

    public uint Release()
    {
        if (IsNull)
        {
            return 0;
        }

        uint result = NativeMethods.D3D12MA_Pool_Release(NativePointer);
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
            _ = NativeMethods.D3D12MA_Pool_Release(nativePointer);
        }
    }

    ~Pool()
    {
        IntPtr nativePointer = NativePointer;
        NativePointer = IntPtr.Zero;
        if (nativePointer != IntPtr.Zero)
        {
            _ = NativeMethods.D3D12MA_Pool_Release(nativePointer);
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(NativePointer == IntPtr.Zero, this);
    }
}
