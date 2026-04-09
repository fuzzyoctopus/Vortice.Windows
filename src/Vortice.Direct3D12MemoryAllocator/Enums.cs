// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12MemoryAllocator;

[Flags]
public enum AllocatorFlags
{
    None = 0x00,
    SingleThreaded = 0x01,
    AlwaysCommitted = 0x02,
    DefaultPoolsNotZeroed = 0x04,
    MSAATexturesAlwaysCommitted = 0x08,
    DontPreferSmallBuffersCommitted = 0x10,
    DontUseTightAlignment = 0x20,
}

[Flags]
public enum PoolFlags
{
    None = 0x0,
    AlgorithmLinear = 0x1,
    MSAATexturesAlwaysCommitted = 0x2,
    AlwaysCommitted = 0x4,
    AlgorithmMask = AlgorithmLinear,
}

[Flags]
public enum AllocationFlags
{
    None = 0x00000000,
    Committed = 0x00000001,
    NeverAllocate = 0x00000002,
    WithinBudget = 0x00000004,
    UpperAddress = 0x00000008,
    CanAlias = 0x00000010,
    StrategyMinMemory = 0x00010000,
    StrategyMinTime = 0x00020000,
    StrategyMinOffset = 0x00040000,
    StrategyBestFit = StrategyMinMemory,
    StrategyFirstFit = StrategyMinTime,
    StrategyMask = StrategyMinMemory | StrategyMinTime | StrategyMinOffset,
}
