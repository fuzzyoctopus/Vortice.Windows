// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Vortice.Direct3D12MemoryAllocator;

public readonly struct AllocatorDescription
{
    public AllocatorDescription(ID3D12Device device, IDXGIAdapter adapter)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public AllocatorFlags Flags { get; init; }
    public ID3D12Device? Device { get; init; }
    public ulong PreferredBlockSize { get; init; }
    public IDXGIAdapter? Adapter { get; init; }

    internal AllocatorDescriptionNative ToNative()
    {
        if (Device is null)
        {
            throw new ArgumentNullException(nameof(Device));
        }

        if (Adapter is null)
        {
            throw new ArgumentNullException(nameof(Adapter));
        }

        return new AllocatorDescriptionNative
        {
            Flags = Flags,
            Device = Device.NativePointer,
            PreferredBlockSize = PreferredBlockSize,
            AllocationCallbacks = IntPtr.Zero,
            Adapter = Adapter.NativePointer
        };
    }
}

public readonly struct AllocationDescription
{
    public static AllocationDescription Default(HeapType heapType)
    {
        return new AllocationDescription
        {
            HeapType = heapType
        };
    }

    public AllocationFlags Flags { get; init; }
    public HeapType HeapType { get; init; }
    public HeapFlags ExtraHeapFlags { get; init; }
    public IntPtr CustomPool { get; init; }
    public IntPtr PrivateData { get; init; }

    internal AllocationDescriptionNative ToNative()
    {
        return new AllocationDescriptionNative
        {
            Flags = Flags,
            HeapType = HeapType,
            ExtraHeapFlags = ExtraHeapFlags,
            CustomPool = CustomPool,
            PrivateData = PrivateData
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PoolDescription
{
    public PoolFlags Flags { get; init; }
    public HeapProperties HeapProperties { get; init; }
    public HeapFlags HeapFlags { get; init; }
    public ulong BlockSize { get; init; }
    public uint MinBlockCount { get; init; }
    public uint MaxBlockCount { get; init; }
    public ulong MinAllocationAlignment { get; init; }
    public IntPtr ProtectedSession { get; init; }
    public ResidencyPriority ResidencyPriority { get; init; }

    internal static PoolDescription FromNative(in PoolDescriptionNative native)
    {
        return new PoolDescription
        {
            Flags = native.Flags,
            HeapProperties = native.HeapProperties,
            HeapFlags = native.HeapFlags,
            BlockSize = native.BlockSize,
            MinBlockCount = native.MinBlockCount,
            MaxBlockCount = native.MaxBlockCount,
            MinAllocationAlignment = native.MinAllocationAlignment,
            ProtectedSession = native.ProtectedSession,
            ResidencyPriority = native.ResidencyPriority
        };
    }

    internal PoolDescriptionNative ToNative()
    {
        return new PoolDescriptionNative
        {
            Flags = Flags,
            HeapProperties = HeapProperties,
            HeapFlags = HeapFlags,
            BlockSize = BlockSize,
            MinBlockCount = MinBlockCount,
            MaxBlockCount = MaxBlockCount,
            MinAllocationAlignment = MinAllocationAlignment,
            ProtectedSession = ProtectedSession,
            ResidencyPriority = ResidencyPriority
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct AllocatorDescriptionNative
{
    public AllocatorFlags Flags;
    public IntPtr Device;
    public ulong PreferredBlockSize;
    public IntPtr AllocationCallbacks;
    public IntPtr Adapter;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AllocationDescriptionNative
{
    public AllocationFlags Flags;
    public HeapType HeapType;
    public HeapFlags ExtraHeapFlags;
    public IntPtr CustomPool;
    public IntPtr PrivateData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PoolDescriptionNative
{
    public PoolFlags Flags;
    public HeapProperties HeapProperties;
    public HeapFlags HeapFlags;
    public ulong BlockSize;
    public uint MinBlockCount;
    public uint MaxBlockCount;
    public ulong MinAllocationAlignment;
    public IntPtr ProtectedSession;
    public ResidencyPriority ResidencyPriority;
}
