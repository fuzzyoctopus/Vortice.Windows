# Implementation Plan: D3D12 Upload Optimization

**Date:** 2026-04-14
**Spec:** `docs/superpowers/specs/2026-04-14-d3d12-upload-optimization-design.md`
**Target files:**
- `samples/HelloDirect3D12MemoryAllocator/UploadContext.cs` (new)
- `samples/HelloDirect3D12MemoryAllocator/D3D12GraphicsDevice.cs` (modified)

---

## Overview

Eight discrete steps in dependency order. Each step is independently reviewable and leaves the file in a compilable state except during Step 5 (constructor rework), which must be completed in one pass.

---

## Step 1 — Create `UploadContext.cs` (new file)

**File:** `samples/HelloDirect3D12MemoryAllocator/UploadContext.cs`

Create the file with the full `UploadContext` class. No changes to `D3D12GraphicsDevice.cs` yet.

### Class skeleton and fields

```csharp
// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using Vortice.Direct3D12;

namespace HelloDirect3D12MemoryAllocator;

/// <summary>
/// Persistent helper that owns a dedicated D3D12 copy queue, its fence, a single
/// command allocator, and a deferred-deletion list for staging resources.
/// </summary>
/// <remarks>
/// Not thread-safe. All methods must be called from a single thread.
/// </remarks>
internal sealed class UploadContext : IDisposable
{
    private readonly ID3D12CommandQueue _copyQueue;
    private readonly ID3D12CommandAllocator _allocator;
    private readonly ID3D12Fence _fence;
    private readonly AutoResetEvent _fenceEvent;
    private readonly List<(ulong FenceValue, IDisposable Resource)> _pendingReleases = [];

    private ulong _fenceValue;   // starts at 0; Submit() pre-increments before signalling
    private bool _disposed;

    public ID3D12GraphicsCommandList CommandList { get; }
    public ID3D12CommandQueue CopyQueue => _copyQueue;
```

### Constructor

```csharp
    public UploadContext(ID3D12Device device)
    {
        _copyQueue = device.CreateCommandQueue(CommandListType.Copy);
        _copyQueue.Name = "Copy Queue";

        _allocator = device.CreateCommandAllocator(CommandListType.Copy);
        CommandList = device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Copy, _allocator);
        // Leave command list open; Begin() will Reset() it before each batch.
        // Close it now so the first Begin() can Reset() cleanly.
        CommandList.Close();

        _fence = device.CreateFence();
        _fenceEvent = new AutoResetEvent(false);
    }
```

### `Begin()`

Waits for any prior in-flight batch before resetting the allocator and command list.

```csharp
    public void Begin()
    {
        // If a previous Submit() is still in flight, wait for it.
        if (_fenceValue > _fence.CompletedValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            _fenceEvent.WaitOne();
        }

        _allocator.Reset();
        CommandList.Reset(_allocator, null);
    }
```

### `Submit()`

Closes and executes the command list on the copy queue, then signals the fence.

```csharp
    public ulong Submit()
    {
        CommandList.Close();
        _copyQueue.ExecuteCommandList(CommandList);
        _copyQueue.Signal(_fence, ++_fenceValue);
        return _fenceValue;
    }
```

### `DeferRelease()`

Queues disposables for lazy release. Callers must pass each `ID3D12Resource` before its
paired `Allocation` so disposal order is correct.

```csharp
    public void DeferRelease(ulong fenceValue, params IDisposable[] resources)
    {
        foreach (IDisposable resource in resources)
        {
            _pendingReleases.Add((fenceValue, resource));
        }
    }
```

### `SyncGraphicsQueue()`

Inserts a GPU-side wait into the graphics queue. No CPU block. Must be called **before**
`GraphicsQueue.ExecuteCommandList` for the barrier pass.

```csharp
    public void SyncGraphicsQueue(ID3D12CommandQueue graphicsQueue, ulong fenceValue)
    {
        graphicsQueue.Wait(_fence, fenceValue);
    }
```

### `ProcessDeferredDeletions()`

Frees any pending resources whose fence value has been reached. Called once per frame.

```csharp
    public void ProcessDeferredDeletions()
    {
        ulong completed = _fence.CompletedValue;
        _pendingReleases.RemoveAll(entry =>
        {
            if (entry.FenceValue > completed)
                return false;
            entry.Resource.Dispose();
            return true;
        });
    }
```

### `Flush()`

CPU-blocks until all in-flight submissions complete, then drains `_pendingReleases`.
After this returns `_pendingReleases` is empty.

```csharp
    public void Flush()
    {
        if (_fenceValue > _fence.CompletedValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            _fenceEvent.WaitOne();
        }
        ProcessDeferredDeletions();
    }
```

### `Dispose()`

```csharp
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        GC.SuppressFinalize(this);
        CommandList.Dispose();
        _allocator.Dispose();
        _fence.Dispose();
        _fenceEvent.Dispose();
        _copyQueue.Dispose();
    }
}
```

**Checkpoint:** File compiles standalone. No changes to `D3D12GraphicsDevice.cs` yet.

---

## Step 2 — Add `_uploadContext` field and remove `_initialUploadDisposables`

**File:** `D3D12GraphicsDevice.cs`

### Remove

```csharp
// DELETE this field (line ~88):
private readonly List<IDisposable> _initialUploadDisposables = [];
```

### Add

```csharp
// ADD after _memoryAllocator field:
private readonly UploadContext _uploadContext;
```

### Remove the helper method

Delete the entire `DisposeInitialUploadResources()` method (lines 758-766).

**Checkpoint:** Compiler will error on any remaining references to
`_initialUploadDisposables` or `DisposeInitialUploadResources()` — these are resolved in
subsequent steps.

---

## Step 3 — Instantiate `_uploadContext` in the constructor

**File:** `D3D12GraphicsDevice.cs`, constructor

Add immediately after `_memoryAllocator` is created (after line ~168):

```csharp
_uploadContext = new UploadContext(Device);
```

---

## Step 4 — Rework `CreateGeometryAndTextureResources`

**File:** `D3D12GraphicsDevice.cs`, `CreateGeometryAndTextureResources` method

This is the most significant method change. Make all edits in one pass.

### 4a — Change method signature

Remove the `out ID3D12Resource` parameters for staging buffers — they no longer need to be
returned to the caller (deferred deletion is handled internally):

```csharp
// The method signature does not change — staging locals are now truly local.
// outparams for vertexBuffer, vertexBufferAllocation, vertexBufferView,
// indexBuffer, indexBufferAllocation, indexBufferView, texture, textureAllocation
// are KEPT (the caller stores these). Only the staging resources become local.
```

### 4b — Change destination resource initial states to `Common`

```csharp
// BEFORE:
vertexBuffer = _memoryAllocator.CreateResource(
    AllocationDescription.Default(HeapType.Default),
    ResourceDescription.Buffer(vertexBufferSize),
    ResourceStates.CopyDest,          // ← wrong for copy queue
    out vertexBufferAllocation);

// AFTER:
vertexBuffer = _memoryAllocator.CreateResource(
    AllocationDescription.Default(HeapType.Default),
    ResourceDescription.Buffer(vertexBufferSize),
    ResourceStates.Common,            // ← copy queue promotes implicitly
    out vertexBufferAllocation);
```

Apply the same change to `indexBuffer` and `texture`.

### 4c — Record vertex and index buffer copies on `_uploadContext.CommandList`

```csharp
// BEFORE (using _commandList):
_commandList.CopyBufferRegion(vertexBuffer, 0, vertexUpload, 0, vertexBufferSize);
_commandList.ResourceBarrierTransition(vertexBuffer,
    ResourceStates.CopyDest, ResourceStates.VertexAndConstantBuffer);

_commandList.CopyBufferRegion(indexBuffer, 0, indexUpload, 0, indexBufferSize);
_commandList.ResourceBarrierTransition(indexBuffer,
    ResourceStates.CopyDest, ResourceStates.IndexBuffer);

// AFTER (using _uploadContext.CommandList, barriers removed — copy queue cannot issue them):
_uploadContext.CommandList.CopyBufferRegion(vertexBuffer, 0, vertexUpload, 0, vertexBufferSize);

_uploadContext.CommandList.CopyBufferRegion(indexBuffer, 0, indexUpload, 0, indexBufferSize);
```

### 4d — Replace `UpdateSubresources` with explicit `GetCopyableFootprints` copy

```csharp
// BEFORE:
ulong textureUploadSize = texture.GetRequiredIntermediateSize(0, 1);
ID3D12Resource textureUpload = _memoryAllocator.CreateResource(
    AllocationDescription.Default(HeapType.Upload),
    ResourceDescription.Buffer(textureUploadSize),
    ResourceStates.GenericRead,
    out Allocation textureUploadAllocation);

fixed (byte* textureDataPtr = textureData)
{
    SubresourceData subresourceData = new(textureDataPtr, TextureSize * 4, TextureSize * TextureSize * 4);
    _commandList.UpdateSubresources(texture, textureUpload, 0, 0, 1, &subresourceData);
}
_commandList.ResourceBarrierTransition(texture,
    ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

// AFTER:
ResourceDescription textureDesc = ResourceDescription.Texture2D(
    Format.R8G8B8A8_UNorm, TextureSize, TextureSize);

Device.GetCopyableFootprints(ref textureDesc, 0, 1, 0,
    out PlacedSubresourceFootprint footprint,
    out uint numRows,
    out ulong rowSizeInBytes,
    out ulong uploadSize);

ID3D12Resource textureUpload = _memoryAllocator.CreateResource(
    AllocationDescription.Default(HeapType.Upload),
    ResourceDescription.Buffer(uploadSize),
    ResourceStates.GenericRead,
    out Allocation textureUploadAllocation);

void* mappedTexture;
textureUpload.Map(0, null, &mappedTexture).CheckError();
byte* texDst = (byte*)mappedTexture + footprint.Offset;
fixed (byte* texSrc = textureData)
{
    for (uint row = 0; row < numRows; row++)
    {
        Buffer.MemoryCopy(
            texSrc + row * rowSizeInBytes,
            texDst + row * footprint.Footprint.RowPitch,
            rowSizeInBytes, rowSizeInBytes);
    }
}
textureUpload.Unmap(0, null);

TextureCopyLocation texDstLoc = new(texture, 0);
TextureCopyLocation texSrcLoc = new(textureUpload, footprint);
_uploadContext.CommandList.CopyTextureRegion(texDstLoc, 0, 0, 0, texSrcLoc, null);
```

### 4e — Remove the old `_initialUploadDisposables` lines at the end of the method

```csharp
// DELETE these six lines:
_initialUploadDisposables.Add(textureUploadAllocation);
_initialUploadDisposables.Add(textureUpload);
_initialUploadDisposables.Add(indexUploadAllocation);
_initialUploadDisposables.Add(indexUpload);
_initialUploadDisposables.Add(vertexUploadAllocation);
_initialUploadDisposables.Add(vertexUpload);
```

The staging resources are now local variables. They will be passed to `DeferRelease` from
the constructor after `Submit()` (Step 5).

**Checkpoint:** `CreateGeometryAndTextureResources` now returns all staging objects as local
variables. The method no longer compiles cleanly because the staging locals go out of scope
without being passed anywhere — this is intentional and resolved in Step 5.

> **Note:** `CreateGeometryAndTextureResources` must also return the six staging locals so
> the constructor (Step 5) can pass them to `DeferRelease`. Update the method to use `out`
> parameters for the six staging objects, or restructure so the method is called with
> `_uploadContext.Begin()` already called and the staging variables are passed back via
> `out` parameters. The cleanest approach is to add six `out` parameters at the end of the
> method signature:
>
> ```csharp
> private void CreateGeometryAndTextureResources(
>     out ID3D12Resource vertexBuffer, out Allocation vertexBufferAllocation,
>     out VertexBufferView vertexBufferView,
>     out ID3D12Resource indexBuffer, out Allocation indexBufferAllocation,
>     out IndexBufferView indexBufferView,
>     out ID3D12Resource texture, out Allocation textureAllocation,
>     // new staging out-params:
>     out ID3D12Resource vertexUpload, out Allocation vertexUploadAllocation,
>     out ID3D12Resource indexUpload,  out Allocation indexUploadAllocation,
>     out ID3D12Resource textureUpload, out Allocation textureUploadAllocation)
> ```

---

## Step 5 — Rework the constructor upload sequence

**File:** `D3D12GraphicsDevice.cs`, constructor body

Replace the block that currently reads:

```csharp
_commandList =
    Device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _commandAllocators[0]);

CreateGeometryAndTextureResources(out _vertexBuffer, ...);

_commandList.Close();
GraphicsQueue.ExecuteCommandList(_commandList);

_frameFence = Device.CreateFence();
_frameFenceEvent = new AutoResetEvent(false);

WaitIdle();
DisposeInitialUploadResources();
```

With:

```csharp
_commandList =
    Device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _commandAllocators[0]);
// Close immediately — the copy queue owns the first open command list via UploadContext.
_commandList.Close();

// Begin the copy batch before recording any copies.
_uploadContext.Begin();

CreateGeometryAndTextureResources(
    out _vertexBuffer, out _vertexBufferAllocation, out _vertexBufferView,
    out _indexBuffer, out _indexBufferAllocation, out _indexBufferView,
    out _texture, out _textureAllocation,
    out ID3D12Resource vertexUpload, out Allocation vertexUploadAllocation,
    out ID3D12Resource indexUpload,  out Allocation indexUploadAllocation,
    out ID3D12Resource textureUpload, out Allocation textureUploadAllocation);

// Submit all copies on the copy queue and get the fence value.
ulong copyFence = _uploadContext.Submit();

// Defer staging resource release until the copy fence is reached.
// Pass resource before its Allocation in each pair.
_uploadContext.DeferRelease(copyFence,
    vertexUpload, vertexUploadAllocation,
    indexUpload,  indexUploadAllocation,
    textureUpload, textureUploadAllocation);

// Insert a GPU-side wait into the graphics queue BEFORE submitting the barrier pass.
_uploadContext.SyncGraphicsQueue(GraphicsQueue, copyFence);

// Record post-copy barriers on the graphics command list.
_commandList.Reset(_commandAllocators[0], null);
_commandList.ResourceBarrierTransition(
    _vertexBuffer, ResourceStates.Common, ResourceStates.VertexAndConstantBuffer);
_commandList.ResourceBarrierTransition(
    _indexBuffer, ResourceStates.Common, ResourceStates.IndexBuffer);
_commandList.ResourceBarrierTransition(
    _texture, ResourceStates.Common, ResourceStates.PixelShaderResource);
_commandList.Close();
GraphicsQueue.ExecuteCommandList(_commandList);

_frameFence = Device.CreateFence();
_frameFenceEvent = new AutoResetEvent(false);

// CPU-wait until the barrier pass completes; safe to start DrawFrame after this.
WaitIdle();
// Note: _uploadContext staging resources are freed lazily by ProcessDeferredDeletions()
// in DrawFrame once the copy fence value is reached.
```

**Checkpoint:** Constructor compiles and runs. The copy queue handles all uploads; the
graphics queue handles only barriers; staging buffers are deferred for release.

---

## Step 6 — Update `DrawFrame`

**File:** `D3D12GraphicsDevice.cs`, `DrawFrame` method

Add three calls at the top of the method, before `UpdateConstantBuffers`:

```csharp
public bool DrawFrame(Action<int, int> draw, [CallerMemberName] string? frameName = null)
{
    int frame = (int)_frameIndex;

    // 1. Advance D3D12MA frame index for budget tracking (monotonic, not modulo).
    _memoryAllocator.SetCurrentFrameIndex((uint)_frameCount);

    // 2. Free any staging buffers whose copy fence value has been reached.
    _uploadContext.ProcessDeferredDeletions();

    // 3. Check for heap fragmentation every 60 frames.
    if (_frameCount % FragmentationCheckInterval == 0)
        CheckFragmentation();

    UpdateConstantBuffers(frame);
    // ... rest of DrawFrame unchanged ...
```

---

## Step 7 — Update `Dispose()`

**File:** `D3D12GraphicsDevice.cs`, `Dispose()` method

Insert `Flush` and upload context disposal **immediately after `WaitIdle()`**, before any
other resource disposal:

```csharp
public void Dispose()
{
    WaitIdle();

    // Drain copy queue and free all remaining staging resources.
    _uploadContext.Flush();
    _uploadContext.Dispose();

    // ... existing disposal sequence continues unchanged below ...
    for (int i = 0; i < RenderLatency; i++)
    {
        _pixelConstantBuffers[i].Unmap(0);
        // ...
    }
    // ...
    _memoryAllocator.Dispose();
    // ...
}
```

---

## Step 8 — Add `CheckFragmentation()`

**File:** `D3D12GraphicsDevice.cs`

Add the constant and method alongside the other private helpers (e.g., after
`UpdateConstantBuffers`):

```csharp
private const int FragmentationCheckInterval = 60;

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

**Required using:** Ensure `using System.Diagnostics;` is present (it is — `Stopwatch` already uses it).

---

## Step Order Summary

| Step | What | File | Safe to stop after? |
|---|---|---|---|
| 1 | Create `UploadContext.cs` | New file | Yes — doesn't affect device |
| 2 | Add `_uploadContext` field, remove `_initialUploadDisposables` | Device | No — breaks compile |
| 3 | Instantiate `_uploadContext` in constructor | Device | No — field unset |
| 4 | Rework `CreateGeometryAndTextureResources` | Device | No — staging refs broken |
| 5 | Rework constructor upload sequence | Device | Yes — compiles, runs |
| 6 | Update `DrawFrame` | Device | Yes |
| 7 | Update `Dispose()` | Device | Yes |
| 8 | Add `CheckFragmentation()` | Device | Yes |

Steps 2–5 should be completed in one session as they leave the file in an intermediate
broken state. Steps 6–8 are independently safe to apply.

---

## Verification

After all steps:

1. Build succeeds with no warnings
2. Run the sample — spinning cubes render correctly
3. Press `J` — allocator stats print to console as before
4. Enable D3D12 debug layer (DEBUG build) — no validation errors, especially:
   - No "invalid resource state" errors (confirms `Common` initial state is correct)
   - No "resource used on wrong queue type" errors (confirms copy commands only on copy queue)
5. Attach a debugger; set breakpoint in `CheckFragmentation` — confirm it is called
6. Confirm `ProcessDeferredDeletions()` frees staging resources within one frame after
   `copyFence` completes (inspect `_pendingReleases` in debugger on frame 1)
