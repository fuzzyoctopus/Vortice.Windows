# D3D12 Upload Optimization Design

**Date:** 2026-04-14
**Project:** HelloDirect3D12MemoryAllocator sample
**Status:** Draft

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

**Thread-safety contract:** `UploadContext` is not thread-safe. All calls (`Begin`, `Submit`, `DeferRelease`, `SyncGraphicsQueue`, `ProcessDeferredDeletions`, `Flush`, `Dispose`) must be made from a single thread. Do not call from background threads or async upload paths without external synchronisation.

| Member | Type | Purpose |
|---|---|---|
| `CopyQueue` | `ID3D12CommandQueue` | `CommandListType.Copy`, named `"Copy Queue"` |
| `_allocator` | `ID3D12CommandAllocator` | Single allocator; reset before each `Begin()` after prior fence completes |
| `CommandList` | `ID3D12GraphicsCommandList` | Callers record `CopyBufferRegion` / `CopyTextureRegion` onto this |
| `_fence` + `_fenceEvent` | `ID3D12Fence` + `AutoResetEvent` | Copy-side fence for CPU-wait and GPU-GPU sync; initial fence value is 0 |
| `_fenceValue` | `ulong` | Monotonically incrementing submit counter; initialised to 0 |
| `_pendingReleases` | `List<(ulong, IDisposable)>` | Staging buffers and allocations waiting for fence signal |

### Changes to `D3D12GraphicsDevice.cs`

- `_initialUploadDisposables` field and `DisposeInitialUploadResources()` method **removed** — replaced by `UploadContext` deferred deletion
- `UploadContext _uploadContext` field **added**
- `CreateGeometryAndTextureResources` records copies on `_uploadContext.CommandList`
- **All destination resources in `CreateGeometryAndTextureResources` must be created in `ResourceStates.Common`** (not `CopyDest` as in the current code) — see Section 3 for rationale
- Post-copy resource barriers moved to `_commandList` (graphics), recorded after cross-queue sync
- `DrawFrame` calls `_uploadContext.ProcessDeferredDeletions()` and `_memoryAllocator.SetCurrentFrameIndex()` each frame
- `DrawFrame` calls `CheckFragmentation()` every 60 frames
- `Dispose()`: `_uploadContext.Flush()` then `_uploadContext.Dispose()` are inserted **immediately after `WaitIdle()`**, before any other resource disposal — see Section 4 for the full disposal order

---

## Section 2: `UploadContext` Public API & Internal Flow

```csharp
public sealed class UploadContext : IDisposable
{
    public ID3D12GraphicsCommandList CommandList { get; }

    // Reset allocator + command list.
    // CPU-waits if a prior batch is still in flight (_fenceValue > _fence.CompletedValue).
    // Initial state: _fenceValue = 0, CompletedValue = 0, so no wait on the first call.
    public void Begin();

    // Close, execute on CopyQueue, signal fence with ++_fenceValue. Returns that value.
    // First call signals value 1; each subsequent call increments by 1.
    public ulong Submit();

    // Queue disposables for release once fenceValue is completed.
    // Dispose each resource before its Allocation to avoid D3D12MA validation warnings.
    // Example: DeferRelease(fence, vertexUpload, vertexUploadAlloc, indexUpload, ...)
    public void DeferRelease(ulong fenceValue, params IDisposable[] resources);

    // GPU-side: graphicsQueue.Wait(_fence, fenceValue). No CPU block.
    // MUST be called BEFORE GraphicsQueue.ExecuteCommandList for the barrier pass —
    // the Wait inserts a hold token into the queue; work submitted after it is gated.
    public void SyncGraphicsQueue(ID3D12CommandQueue graphicsQueue, ulong fenceValue);

    // Check completed fence value; dispose any pending resources whose value has passed.
    // Called once per frame from DrawFrame.
    public void ProcessDeferredDeletions();

    // CPU-blocks until all in-flight copy submissions are done, then drains
    // _pendingReleases by calling ProcessDeferredDeletions() internally.
    // After Flush() returns, _pendingReleases is empty and all staging buffers are freed.
    // Call before Dispose() and before disposing any D3D12 device-owned resources.
    public void Flush();

    public void Dispose();
}
```

**Submit flow:**

```
Begin()
  └─ if (_fenceValue > _fence.CompletedValue) → CPU wait via _fenceEvent
  └─ _allocator.Reset()
  └─ _commandList.Reset(_allocator)

[ caller records copies on CommandList ]

Submit()
  └─ _commandList.Close()
  └─ CopyQueue.ExecuteCommandList(_commandList)
  └─ CopyQueue.Signal(_fence, ++_fenceValue)
  └─ return _fenceValue

DeferRelease(fenceValue, resource, allocation, ...)
  └─ append each to _pendingReleases in the order supplied
     (supply resource before its allocation so disposal order is correct)

SyncGraphicsQueue(graphicsQueue, fenceValue)          ← call BEFORE ExecuteCommandList
  └─ graphicsQueue.Wait(_fence, fenceValue)           ← GPU-side only, CPU continues

GraphicsQueue.ExecuteCommandList(barrierCommandList)  ← gated by the Wait above

ProcessDeferredDeletions()                            ← called each frame
  └─ completed = _fence.CompletedValue
  └─ foreach entry where entry.fence <= completed → entry.resource.Dispose()
  └─ remove released entries
```

`SyncGraphicsQueue` uses `ID3D12CommandQueue.Wait` (GPU-side), not a CPU event. The CPU keeps running; only the graphics queue stalls until the copy queue signals — enabling true asynchronous overlap.

---

## Section 3: Texture Upload with `GetCopyableFootprints`

### Resource initial state change (action required)

The existing code creates destination resources in `ResourceStates.CopyDest`. **This must be changed to `ResourceStates.Common`** for all three destination resources (vertex buffer, index buffer, texture):

```csharp
// BEFORE (wrong for cross-queue use):
vertexBuffer = _memoryAllocator.CreateResource(..., ResourceStates.CopyDest, ...);
indexBuffer  = _memoryAllocator.CreateResource(..., ResourceStates.CopyDest, ...);
texture      = _memoryAllocator.CreateResource(..., ResourceStates.CopyDest, ...);

// AFTER (correct):
vertexBuffer = _memoryAllocator.CreateResource(..., ResourceStates.Common, ...);
indexBuffer  = _memoryAllocator.CreateResource(..., ResourceStates.Common, ...);
texture      = _memoryAllocator.CreateResource(..., ResourceStates.Common, ...);
```

Copy queues implicitly promote resources from `Common` → `CopyDest` on first use and decay them back to `Common` on queue completion. Creating destination resources in `CopyDest` implies the graphics queue set that state — which it did not — making the subsequent `Common → VertexAndConstantBuffer` barrier in Section 4 incorrect. With `Common` as the initial state the barrier source is unambiguous.

### Texture upload — replacing `UpdateSubresources`

`UpdateSubresources` is replaced with an explicit footprint-aware copy:

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

`footprint.Footprint.RowPitch` is GPU-aligned (multiple of `D3D12_TEXTURE_DATA_PITCH_ALIGNMENT` = 256 bytes). `rowSizeInBytes` is the raw pixel row width. The row-by-row copy bridges that gap explicitly — this is the alignment the original `UpdateSubresources` was hiding.

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
CreateGeometryAndTextureResources()          // copies on _uploadContext.CommandList
                                             // NO barriers here — copy queue cannot issue them
                                             // All dest resources created in Common (see Section 3)

ulong copyFence = _uploadContext.Submit()

// Defer all six staging objects: resource before its Allocation in each pair
_uploadContext.DeferRelease(copyFence,
    vertexUpload, vertexUploadAllocation,
    indexUpload,  indexUploadAllocation,
    textureUpload, textureUploadAllocation)

// GPU-side wait inserted into graphics queue BEFORE the barrier submission
_uploadContext.SyncGraphicsQueue(GraphicsQueue, copyFence)
  └─ GraphicsQueue.Wait(_copyFence, copyFence)   // GPU-side only, CPU continues

// Graphics command list records ONLY post-copy barriers (Common → target)
_commandList.Reset(_commandAllocators[0])
_commandList.ResourceBarrierTransition(vertexBuffer, Common → VertexAndConstantBuffer)
_commandList.ResourceBarrierTransition(indexBuffer,  Common → IndexBuffer)
_commandList.ResourceBarrierTransition(texture,      Common → PixelShaderResource)
_commandList.Close()
GraphicsQueue.ExecuteCommandList(_commandList)   // held by Wait until copy queue signals

_frameFence = Device.CreateFence()
WaitIdle()    // CPU-blocks until barrier pass completes; safe to start DrawFrame after this
```

`DisposeInitialUploadResources()` is deleted entirely. Staging objects are freed lazily by `ProcessDeferredDeletions()` once `_fence.CompletedValue` reaches `copyFence`.

### Disposal order in `Dispose()`

The new `Dispose()` sequence — insert `Flush` and upload context disposal **immediately after `WaitIdle()`**, before any resource or queue disposal:

```csharp
WaitIdle();

_uploadContext.Flush();    // CPU-wait: ensures all copy work and deferred releases complete
_uploadContext.Dispose();  // releases CopyQueue, allocator, command list, fence

// ... existing disposal continues unchanged below this point ...
for (int i = 0; i < RenderLatency; i++) { ... }
_depthStencilTexture.Dispose();
...
_memoryAllocator.Dispose();
...
Device.Dispose();
```

`_uploadContext.Flush()` must complete before any D3D12 resource it may still reference (staging buffers in `_pendingReleases`) is freed, and before `_memoryAllocator.Dispose()`. Placing both calls immediately after `WaitIdle()` — before any other disposal — guarantees this.

---

## Section 5: Fragmentation Monitoring

**Metric:**
```
fragmentation = 1.0 - (AllocationBytes / BlockBytes)
```

- `BlockBytes` — total bytes committed in D3D12MA heaps
- `AllocationBytes` — bytes used by live allocations
- Threshold: **20%**
- Early-exit when `BlockBytes == 0` (before any allocations exist)

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
            $"(alloc={total.Stats.AllocationBytes / 1024.0:F1}KB, " +
            $"block={total.Stats.BlockBytes / 1024.0:F1}KB)");
    }
}
```

Called from `DrawFrame` — use `_frameCount` (monotonically increasing) for `SetCurrentFrameIndex`, not `_frameIndex` (which is a modulo slot):

```csharp
_memoryAllocator.SetCurrentFrameIndex((uint)_frameCount);

if (_frameCount % FragmentationCheckInterval == 0)
    CheckFragmentation();
```

`CalculateStatistics` walks D3D12MA internals and is not free. Every 60 frames (~1 second at 60fps) balances responsiveness with overhead.

---

## Constraints & Non-Goals

- No new rendering features — this is a pure infrastructure change
- The sample still uploads only at startup; `UploadContext` is designed to support runtime uploads but none are added here
- No defragmentation pass is implemented — monitoring only; a defrag pass is a future concern if the warning fires in practice
