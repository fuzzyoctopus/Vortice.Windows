# D3D12 Upload Optimization Design

**Date:** 2026-04-14
**Project:** HelloDirect3D12MemoryAllocator sample
**Status:** Approved

---

## Overview

Apply five D3D12 upload best practices to the `HelloDirect3D12MemoryAllocator` sample:

1. Dedicated copy queue for asynchronous asset transfers
2. Batched copy commands (single command list per batch)
3. Fence-gated deferred deletion of staging buffers
4. Explicit `GetCopyableFootprints` alignment for texture uploads
5. Per-frame fragmentation monitoring via D3D12MA statistics API

---

## Files Changed

| File | Action |
|---|---|
| `samples/HelloDirect3D12MemoryAllocator/UploadContext.cs` | **New** — persistent copy queue helper |
| `samples/HelloDirect3D12MemoryAllocator/D3D12GraphicsDevice.cs` | **Modified** — integrates `UploadContext`, removes old upload path |

---

## Section 1: Architecture

### New file — `UploadContext.cs`

A `sealed` class that owns the entire copy pipeline. Held as a persistent field on `D3D12GraphicsDevice` for the device's lifetime.

| Member | Type | Purpose |
|---|---|---|
| `CopyQueue` | `ID3D12CommandQueue` | `CommandListType.Copy`, named `"Copy Queue"` |
| `_allocator` | `ID3D12CommandAllocator` | Single allocator; reset before each `Begin()` after prior fence completes |
| `CommandList` | `ID3D12GraphicsCommandList` | Callers record `CopyBufferRegion` / `CopyTextureRegion` onto this |
| `_fence` + `_fenceEvent` | `ID3D12Fence` + `AutoResetEvent` | Copy-side fence for CPU-wait and GPU-GPU sync |
| `_fenceValue` | `ulong` | Monotonically incrementing submit counter |
| `_pendingReleases` | `List<(ulong, IDisposable)>` | Staging buffers and allocations waiting for fence signal |

### Changes to `D3D12GraphicsDevice.cs`

- `_initialUploadDisposables` field and `DisposeInitialUploadResources()` method **removed** — replaced by `UploadContext` deferred deletion
- `UploadContext _uploadContext` field **added**
- `CreateGeometryAndTextureResources` records copies on `_uploadContext.CommandList`
- Post-copy resource barriers moved to `_commandList` (graphics), recorded after cross-queue sync
- `DrawFrame` calls `_uploadContext.ProcessDeferredDeletions()` and `_memoryAllocator.SetCurrentFrameIndex()` each frame
- `DrawFrame` calls `CheckFragmentation()` every 60 frames
- `Dispose()` calls `_uploadContext.Flush()` then `_uploadContext.Dispose()`

---

## Section 2: `UploadContext` Public API & Internal Flow

```csharp
public sealed class UploadContext : IDisposable
{
    public ID3D12GraphicsCommandList CommandList { get; }

    // Reset allocator + command list. CPU-waits if a prior batch is still in flight.
    public void Begin();

    // Close, execute on CopyQueue, signal fence. Returns the fence value for this batch.
    public ulong Submit();

    // Queue disposables for release once fenceValue is completed.
    public void DeferRelease(ulong fenceValue, params IDisposable[] resources);

    // GPU-side: graphicsQueue.Wait(_fence, fenceValue). No CPU block.
    public void SyncGraphicsQueue(ID3D12CommandQueue graphicsQueue, ulong fenceValue);

    // Check completed fence value; dispose any pending resources whose value has passed.
    // Called once per frame from DrawFrame.
    public void ProcessDeferredDeletions();

    // CPU-blocks until all in-flight submissions are done. Called from Dispose.
    public void Flush();

    public void Dispose();
}
```

**Submit flow:**

```
Begin()
  └─ if (_fenceValue > _fence.CompletedValue) → CPU wait (prior batch in flight)
  └─ _allocator.Reset()
  └─ _commandList.Reset(_allocator)

[ caller records copies on CommandList ]

Submit()
  └─ _commandList.Close()
  └─ CopyQueue.ExecuteCommandList(_commandList)
  └─ CopyQueue.Signal(_fence, ++_fenceValue)
  └─ return _fenceValue

DeferRelease(fenceValue, resources...)
  └─ append to _pendingReleases

SyncGraphicsQueue(graphicsQueue, fenceValue)
  └─ graphicsQueue.Wait(_fence, fenceValue)   ← GPU-side only, CPU continues

ProcessDeferredDeletions()                    ← called each frame
  └─ completed = _fence.CompletedValue
  └─ foreach entry where entry.fence <= completed → entry.resource.Dispose()
  └─ remove released entries
```

`SyncGraphicsQueue` uses `ID3D12CommandQueue.Wait` (GPU-side), not a CPU event. The CPU keeps running; only the graphics queue stalls until the copy queue signals — enabling true asynchronous overlap.

---

## Section 3: Texture Upload with `GetCopyableFootprints`

`UpdateSubresources` is replaced with an explicit footprint-aware copy that makes alignment visible.

```csharp
// 1. Query footprint before allocating staging buffer
ResourceDescription textureDesc = ResourceDescription.Texture2D(
    Format.R8G8B8A8_UNorm, TextureSize, TextureSize);

Device.GetCopyableFootprints(
    ref textureDesc, firstSubresource: 0, numSubresources: 1, baseOffset: 0,
    out PlacedSubresourceFootprint footprint,
    out uint numRows,
    out ulong rowSizeInBytes,
    out ulong uploadSize);

// 2. Allocate staging buffer sized to uploadSize
ID3D12Resource textureUpload = _memoryAllocator.CreateResource(
    AllocationDescription.Default(HeapType.Upload),
    ResourceDescription.Buffer(uploadSize),
    ResourceStates.GenericRead,
    out Allocation textureUploadAllocation);

// 3. Map and copy row-by-row, respecting RowPitch alignment
void* mapped;
textureUpload.Map(0, null, &mapped).CheckError();
byte* dst = (byte*)mapped + footprint.Offset;
fixed (byte* src = textureData)
{
    for (uint row = 0; row < numRows; row++)
        Buffer.MemoryCopy(
            src + row * rowSizeInBytes,
            dst + row * footprint.Footprint.RowPitch,
            rowSizeInBytes, rowSizeInBytes);
}
textureUpload.Unmap(0, null);

// 4. Record copy on the upload context command list
TextureCopyLocation dstLoc = new(texture, subresourceIndex: 0);
TextureCopyLocation srcLoc = new(textureUpload, footprint);
_uploadContext.CommandList.CopyTextureRegion(dstLoc, 0, 0, 0, srcLoc, null);
```

`footprint.Footprint.RowPitch` is GPU-aligned (multiple of `D3D12_TEXTURE_DATA_PITCH_ALIGNMENT` = 256 bytes). `rowSizeInBytes` is the raw pixel row width. The row-by-row copy bridges that gap explicitly.

All resources (vertex buffer, index buffer, texture) are created in `ResourceStates.Common`. Copy queues implicitly promote `Common` → `CopyDest` on first use and decay back to `Common` on queue completion. Creating resources in `CopyDest` and using them on a copy queue is technically incorrect for cross-queue use.

---

## Section 4: Cross-Queue Sync & Resource State Transitions

### Constructor sequence (before)

```
CreateGeometryAndTextureResources()   // copies + barriers on _commandList (Direct)
_commandList.Close()
GraphicsQueue.ExecuteCommandList()
WaitIdle()                            // CPU blocks
DisposeInitialUploadResources()       // staging freed immediately
```

### Constructor sequence (after)

```
_uploadContext.Begin()
CreateGeometryAndTextureResources()         // copies recorded on _uploadContext.CommandList
                                            // NO barriers here — copy queue cannot issue them

ulong copyFence = _uploadContext.Submit()
_uploadContext.DeferRelease(copyFence, stagingBuffers...)
_uploadContext.SyncGraphicsQueue(GraphicsQueue, copyFence)
  └─ GraphicsQueue.Wait(_copyFence, copyFence)   // GPU-side only

// Graphics command list records ONLY post-copy barriers
_commandList.Reset(_commandAllocators[0])
_commandList.ResourceBarrierTransition(vertexBuffer, Common → VertexAndConstantBuffer)
_commandList.ResourceBarrierTransition(indexBuffer,  Common → IndexBuffer)
_commandList.ResourceBarrierTransition(texture,      Common → PixelShaderResource)
_commandList.Close()
GraphicsQueue.ExecuteCommandList(_commandList)   // executes only after copy queue signals

_frameFence = Device.CreateFence()
WaitIdle()    // still needed so barriers complete before first DrawFrame
```

`DisposeInitialUploadResources()` is deleted. `DeferRelease` replaces it — staging buffers are freed lazily once `ProcessDeferredDeletions()` sees the copy fence value has been reached.

---

## Section 5: Fragmentation Monitoring

**Metric:**
```
fragmentation = 1.0 - (AllocationBytes / BlockBytes)
```

- `BlockBytes` — total bytes committed in D3D12MA heaps
- `AllocationBytes` — bytes used by live allocations
- Threshold: **20%**

**Implementation:**

```csharp
private const int FragmentationCheckInterval = 60; // ~1s at 60fps

private void CheckFragmentation()
{
    _memoryAllocator.CalculateStatistics(out TotalStatistics stats);
    ref DetailedStatistics total = ref stats.Total;

    if (total.Stats.BlockBytes == 0)
        return;

    double fragmentation = 1.0 - (double)total.Stats.AllocationBytes
                                / total.Stats.BlockBytes;

    if (fragmentation > 0.20)
    {
        Debug.WriteLine(
            $"[D3D12MA] Fragmentation warning: {fragmentation:P1} " +
            $"(alloc={total.Stats.AllocationBytes / 1024}KB, " +
            $"block={total.Stats.BlockBytes / 1024}KB)");
    }
}
```

Called from `DrawFrame`:
```csharp
_memoryAllocator.SetCurrentFrameIndex((uint)_frameIndex);

if (_frameCount % FragmentationCheckInterval == 0)
    CheckFragmentation();
```

`CalculateStatistics` walks D3D12MA internals and is not free. Every 60 frames (~1 second at 60fps) balances responsiveness with overhead.

---

## Constraints & Non-Goals

- No new rendering features — this is a pure infrastructure change
- The sample still uploads only at startup; the `UploadContext` is designed to support runtime uploads but none are added
- No defragmentation pass is implemented — the design calls for monitoring only; a defrag pass is a future concern if the warning fires in practice
