// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12MemoryAllocator;

[StructLayout(LayoutKind.Sequential)]
public struct Statistics
{
    public uint BlockCount;
    public uint AllocationCount;
    public ulong BlockBytes;
    public ulong AllocationBytes;
}

[StructLayout(LayoutKind.Sequential)]
public struct DetailedStatistics
{
    public Statistics Stats;
    public uint UnusedRangeCount;
    public ulong AllocationSizeMin;
    public ulong AllocationSizeMax;
    public ulong UnusedRangeSizeMin;
    public ulong UnusedRangeSizeMax;
}

[StructLayout(LayoutKind.Sequential)]
public struct Budget
{
    public Statistics Stats;
    public ulong UsageBytes;
    public ulong BudgetBytes;
}

public struct TotalStatistics
{
    public DetailedStatistics HeapTypeDefault;
    public DetailedStatistics HeapTypeUpload;
    public DetailedStatistics HeapTypeReadback;
    public DetailedStatistics HeapTypeCustom;
    public DetailedStatistics HeapTypeGPUUpload;

    public DetailedStatistics MemorySegmentGroupLocal;
    public DetailedStatistics MemorySegmentGroupNonLocal;

    public DetailedStatistics Total;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TotalStatisticsNative
{
    public DetailedStatistics HeapType0;
    public DetailedStatistics HeapType1;
    public DetailedStatistics HeapType2;
    public DetailedStatistics HeapType3;
    public DetailedStatistics HeapType4;

    public DetailedStatistics MemorySegmentGroup0;
    public DetailedStatistics MemorySegmentGroup1;

    public DetailedStatistics Total;

    public TotalStatistics ToManaged()
    {
        return new TotalStatistics
        {
            HeapTypeDefault = HeapType0,
            HeapTypeUpload = HeapType1,
            HeapTypeReadback = HeapType2,
            HeapTypeCustom = HeapType3,
            HeapTypeGPUUpload = HeapType4,
            MemorySegmentGroupLocal = MemorySegmentGroup0,
            MemorySegmentGroupNonLocal = MemorySegmentGroup1,
            Total = Total
        };
    }
}
