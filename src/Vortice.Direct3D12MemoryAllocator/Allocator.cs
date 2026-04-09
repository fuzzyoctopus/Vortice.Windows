// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using Vortice.Direct3D12;

namespace Vortice.Direct3D12MemoryAllocator;

public sealed unsafe class Allocator : IDisposable
{
    internal Allocator(IntPtr nativePointer)
    {
        NativePointer = nativePointer;
    }

    public IntPtr NativePointer { get; private set; }

    public bool IsNull => NativePointer == IntPtr.Zero;

    public bool IsUMA
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.D3D12MA_Allocator_IsUMA(NativePointer);
        }
    }

    public bool IsCacheCoherentUMA
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.D3D12MA_Allocator_IsCacheCoherentUMA(NativePointer);
        }
    }

    public bool IsGPUUploadHeapSupported
    {
        get
        {
            EnsureNotDisposed();
            return NativeMethods.D3D12MA_Allocator_IsGPUUploadHeapSupported(NativePointer);
        }
    }

    public ulong GetMemoryCapacity(uint memorySegmentGroup)
    {
        EnsureNotDisposed();
        return NativeMethods.D3D12MA_Allocator_GetMemoryCapacity(NativePointer, memorySegmentGroup);
    }

    public Result CreatePool(PoolDescription description, out Pool? pool)
    {
        EnsureNotDisposed();

        PoolDescriptionNative nativeDescription = description.ToNative();
        Result result = (Result)NativeMethods.D3D12MA_Allocator_CreatePool(NativePointer, &nativeDescription, out IntPtr nativePool);
        if (result.Failure)
        {
            pool = default;
            return result;
        }

        pool = new Pool(nativePool);
        return result;
    }

    public Pool CreatePool(PoolDescription description)
    {
        CreatePool(description, out Pool? pool).CheckError();
        return pool!;
    }

    public void SetCurrentFrameIndex(uint frameIndex)
    {
        EnsureNotDisposed();
        NativeMethods.D3D12MA_Allocator_SetCurrentFrameIndex(NativePointer, frameIndex);
    }

    public void GetBudget(out Budget localBudget, out Budget nonLocalBudget)
    {
        EnsureNotDisposed();
        NativeMethods.D3D12MA_Allocator_GetBudget(NativePointer, out localBudget, out nonLocalBudget);
    }

    public void CalculateStatistics(out TotalStatistics statistics)
    {
        EnsureNotDisposed();
        NativeMethods.D3D12MA_Allocator_CalculateStatistics(NativePointer, out TotalStatisticsNative nativeStatistics);
        statistics = nativeStatistics.ToManaged();
    }

    public string BuildStatsString(bool detailedMap)
    {
        EnsureNotDisposed();

        NativeMethods.D3D12MA_Allocator_BuildStatsString(NativePointer, out IntPtr nativeStatsString, detailedMap);
        if (nativeStatsString == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return MarshalNativeString(nativeStatsString);
        }
        finally
        {
            NativeMethods.D3D12MA_Allocator_FreeStatsString(NativePointer, nativeStatsString);
        }
    }

    public uint AddRef()
    {
        EnsureNotDisposed();
        return NativeMethods.D3D12MA_Allocator_AddRef(NativePointer);
    }

    public uint Release()
    {
        if (IsNull)
        {
            return 0;
        }

        uint result = NativeMethods.D3D12MA_Allocator_Release(NativePointer);
        if (result == 0)
        {
            NativePointer = IntPtr.Zero;
        }

        return result;
    }

    public Result CreateResource(
        AllocationDescription allocationDescription,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        ClearValue? optimizedClearValue,
        out Allocation? allocation,
        out ID3D12Resource? resource)
    {
        EnsureNotDisposed();

        AllocationDescriptionNative nativeAllocationDescription = allocationDescription.ToNative();
        AllocationDescriptionNative* allocationDescriptionPtr = &nativeAllocationDescription;
        ResourceDescription* resourceDescriptionPtr = &resourceDescription;
        Guid resourceGuid = typeof(ID3D12Resource).GUID;

        Result result;
        if (optimizedClearValue.HasValue)
        {
            ClearValue clearValue = optimizedClearValue.Value;
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateResource(
                NativePointer,
                allocationDescriptionPtr,
                resourceDescriptionPtr,
                initialResourceState,
                &clearValue,
                out IntPtr nativeAllocation,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                allocation = new Allocation(nativeAllocation);
                resource = new ID3D12Resource(nativeResource);
                return result;
            }
        }
        else
        {
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateResource(
                NativePointer,
                allocationDescriptionPtr,
                resourceDescriptionPtr,
                initialResourceState,
                null,
                out IntPtr nativeAllocation,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                allocation = new Allocation(nativeAllocation);
                resource = new ID3D12Resource(nativeResource);
                return result;
            }
        }

        allocation = default;
        resource = default;
        return result;
    }

    public Result CreateResource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        AllocationDescription allocationDescription,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        ClearValue? optimizedClearValue,
        out Allocation? allocation,
        out T? resource) where T : ID3D12Resource
    {
        EnsureNotDisposed();

        AllocationDescriptionNative nativeAllocationDescription = allocationDescription.ToNative();
        AllocationDescriptionNative* allocationDescriptionPtr = &nativeAllocationDescription;
        ResourceDescription* resourceDescriptionPtr = &resourceDescription;
        Guid resourceGuid = typeof(T).GUID;

        Result result;
        if (optimizedClearValue.HasValue)
        {
            ClearValue clearValue = optimizedClearValue.Value;
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateResource(
                NativePointer,
                allocationDescriptionPtr,
                resourceDescriptionPtr,
                initialResourceState,
                &clearValue,
                out IntPtr nativeAllocation,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                allocation = new Allocation(nativeAllocation);
                resource = MarshallingHelpers.FromPointer<T>(nativeResource);
                return result;
            }
        }
        else
        {
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateResource(
                NativePointer,
                allocationDescriptionPtr,
                resourceDescriptionPtr,
                initialResourceState,
                null,
                out IntPtr nativeAllocation,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                allocation = new Allocation(nativeAllocation);
                resource = MarshallingHelpers.FromPointer<T>(nativeResource);
                return result;
            }
        }

        allocation = default;
        resource = default;
        return result;
    }

    public ID3D12Resource CreateResource(
        AllocationDescription allocationDescription,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        out Allocation allocation,
        ClearValue? optimizedClearValue = null)
    {
        CreateResource(
            allocationDescription,
            resourceDescription,
            initialResourceState,
            optimizedClearValue,
            out Allocation? tempAllocation,
            out ID3D12Resource? resource).CheckError();

        allocation = tempAllocation!;
        return resource!;
    }

    public T CreateResource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        AllocationDescription allocationDescription,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        out Allocation allocation,
        ClearValue? optimizedClearValue = null) where T : ID3D12Resource
    {
        CreateResource(
            allocationDescription,
            resourceDescription,
            initialResourceState,
            optimizedClearValue,
            out Allocation? tempAllocation,
            out T? resource).CheckError();

        allocation = tempAllocation!;
        return resource!;
    }

    public Result CreateAliasingResource(
        Allocation allocation,
        ulong allocationLocalOffset,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        ClearValue? optimizedClearValue,
        out ID3D12Resource? resource)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(allocation);
        ObjectDisposedException.ThrowIf(allocation.NativePointer == IntPtr.Zero, allocation);

        ResourceDescription* resourceDescriptionPtr = &resourceDescription;
        Guid resourceGuid = typeof(ID3D12Resource).GUID;

        Result result;
        if (optimizedClearValue.HasValue)
        {
            ClearValue clearValue = optimizedClearValue.Value;
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateAliasingResource(
                NativePointer,
                allocation.NativePointer,
                allocationLocalOffset,
                resourceDescriptionPtr,
                initialResourceState,
                &clearValue,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                resource = new ID3D12Resource(nativeResource);
                return result;
            }
        }
        else
        {
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateAliasingResource(
                NativePointer,
                allocation.NativePointer,
                allocationLocalOffset,
                resourceDescriptionPtr,
                initialResourceState,
                null,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                resource = new ID3D12Resource(nativeResource);
                return result;
            }
        }

        resource = default;
        return result;
    }

    public Result CreateAliasingResource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        Allocation allocation,
        ulong allocationLocalOffset,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        ClearValue? optimizedClearValue,
        out T? resource) where T : ID3D12Resource
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(allocation);
        ObjectDisposedException.ThrowIf(allocation.NativePointer == IntPtr.Zero, allocation);

        ResourceDescription* resourceDescriptionPtr = &resourceDescription;
        Guid resourceGuid = typeof(T).GUID;

        Result result;
        if (optimizedClearValue.HasValue)
        {
            ClearValue clearValue = optimizedClearValue.Value;
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateAliasingResource(
                NativePointer,
                allocation.NativePointer,
                allocationLocalOffset,
                resourceDescriptionPtr,
                initialResourceState,
                &clearValue,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                resource = MarshallingHelpers.FromPointer<T>(nativeResource);
                return result;
            }
        }
        else
        {
            result = (Result)NativeMethods.D3D12MA_Allocator_CreateAliasingResource(
                NativePointer,
                allocation.NativePointer,
                allocationLocalOffset,
                resourceDescriptionPtr,
                initialResourceState,
                null,
                &resourceGuid,
                out IntPtr nativeResource);

            if (result.Success)
            {
                resource = MarshallingHelpers.FromPointer<T>(nativeResource);
                return result;
            }
        }

        resource = default;
        return result;
    }

    public ID3D12Resource CreateAliasingResource(
        Allocation allocation,
        ulong allocationLocalOffset,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        ClearValue? optimizedClearValue = null)
    {
        CreateAliasingResource(
            allocation,
            allocationLocalOffset,
            resourceDescription,
            initialResourceState,
            optimizedClearValue,
            out ID3D12Resource? resource).CheckError();

        return resource!;
    }

    public T CreateAliasingResource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        Allocation allocation,
        ulong allocationLocalOffset,
        ResourceDescription resourceDescription,
        ResourceStates initialResourceState,
        ClearValue? optimizedClearValue = null) where T : ID3D12Resource
    {
        CreateAliasingResource(
            allocation,
            allocationLocalOffset,
            resourceDescription,
            initialResourceState,
            optimizedClearValue,
            out T? resource).CheckError();

        return resource!;
    }

    public void Dispose()
    {
        IntPtr nativePointer = NativePointer;
        NativePointer = IntPtr.Zero;

        GC.SuppressFinalize(this);
        if (nativePointer != IntPtr.Zero)
        {
            _ = NativeMethods.D3D12MA_Allocator_Release(nativePointer);
        }
    }

    ~Allocator()
    {
        IntPtr nativePointer = NativePointer;
        NativePointer = IntPtr.Zero;
        if (nativePointer != IntPtr.Zero)
        {
            _ = NativeMethods.D3D12MA_Allocator_Release(nativePointer);
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(NativePointer == IntPtr.Zero, this);
    }

    private static string MarshalNativeString(IntPtr nativeString)
    {
        if (nativeString == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (Marshal.ReadByte(nativeString, 1) == 0)
        {
            string? unicodeString = Marshal.PtrToStringUni(nativeString);
            if (!string.IsNullOrEmpty(unicodeString))
            {
                return unicodeString;
            }
        }

        return Marshal.PtrToStringUTF8(nativeString) ?? Marshal.PtrToStringAnsi(nativeString) ?? string.Empty;
    }
}
