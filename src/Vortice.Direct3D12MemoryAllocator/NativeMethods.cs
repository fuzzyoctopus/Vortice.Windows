// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using Vortice.Direct3D12;

namespace Vortice.Direct3D12MemoryAllocator;

internal static unsafe partial class NativeMethods
{
    private const string LibraryName = "D3D12MA";

    [LibraryImport(LibraryName, EntryPoint = "D3D12MA_CreateAllocator")]
    internal static partial int D3D12MA_CreateAllocator(AllocatorDescriptionNative* description, out IntPtr allocator);

    [LibraryImport(LibraryName)]
    internal static partial int D3D12MA_Allocator_CreateResource(
        IntPtr allocator,
        AllocationDescriptionNative* allocationDescription,
        ResourceDescription* resourceDescription,
        ResourceStates initialResourceState,
        ClearValue* optimizedClearValue,
        out IntPtr allocation,
        Guid* resourceGuid,
        out IntPtr resource);

    [LibraryImport(LibraryName)]
    internal static partial int D3D12MA_Allocator_CreateAliasingResource(
        IntPtr allocator,
        IntPtr allocation,
        ulong allocationLocalOffset,
        ResourceDescription* resourceDescription,
        ResourceStates initialResourceState,
        ClearValue* optimizedClearValue,
        Guid* resourceGuid,
        out IntPtr resource);

    [LibraryImport(LibraryName)]
    internal static partial int D3D12MA_Allocator_CreatePool(
        IntPtr allocator,
        PoolDescriptionNative* poolDescription,
        out IntPtr pool);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Allocator_SetCurrentFrameIndex(IntPtr allocator, uint frameIndex);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Allocator_GetBudget(IntPtr allocator, out Budget localBudget, out Budget nonLocalBudget);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Allocator_CalculateStatistics(IntPtr allocator, out TotalStatisticsNative statistics);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Allocator_BuildStatsString(
        IntPtr allocator,
        out IntPtr statsString,
        [MarshalAs(UnmanagedType.Bool)] bool detailedMap);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Allocator_FreeStatsString(IntPtr allocator, IntPtr statsString);

    [LibraryImport(LibraryName)]
    internal static partial uint D3D12MA_Allocator_AddRef(IntPtr allocator);

    [LibraryImport(LibraryName)]
    internal static partial uint D3D12MA_Allocator_Release(IntPtr allocator);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool D3D12MA_Allocator_IsUMA(IntPtr allocator);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool D3D12MA_Allocator_IsCacheCoherentUMA(IntPtr allocator);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool D3D12MA_Allocator_IsGPUUploadHeapSupported(IntPtr allocator);

    [LibraryImport(LibraryName)]
    internal static partial ulong D3D12MA_Allocator_GetMemoryCapacity(IntPtr allocator, uint memorySegmentGroup);

    [LibraryImport(LibraryName)]
    internal static partial uint D3D12MA_Pool_AddRef(IntPtr pool);

    [LibraryImport(LibraryName)]
    internal static partial uint D3D12MA_Pool_Release(IntPtr pool);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Pool_GetDesc(IntPtr pool, PoolDescriptionNative* description);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Pool_GetStatistics(IntPtr pool, out Statistics statistics);

    [LibraryImport(LibraryName)]
    internal static partial void D3D12MA_Pool_CalculateStatistics(IntPtr pool, out DetailedStatistics statistics);

    [LibraryImport(LibraryName)]
    internal static partial uint D3D12MA_Allocation_AddRef(IntPtr allocation);

    [LibraryImport(LibraryName)]
    internal static partial uint D3D12MA_Allocation_Release(IntPtr allocation);

    [LibraryImport(LibraryName)]
    internal static partial ulong D3D12MA_Allocation_GetOffset(IntPtr allocation);

    [LibraryImport(LibraryName)]
    internal static partial ulong D3D12MA_Allocation_GetAlignment(IntPtr allocation);

    [LibraryImport(LibraryName)]
    internal static partial ulong D3D12MA_Allocation_GetSize(IntPtr allocation);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr D3D12MA_Allocation_GetResource(IntPtr allocation);
}
