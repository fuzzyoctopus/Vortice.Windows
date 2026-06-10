// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Diagnostics;
using Vortice.Direct3D12;

namespace HelloDirect3D12MemoryAllocator;

/// <summary>
/// Persistent helper that owns a dedicated D3D12 copy queue, its fence, a single
/// command allocator, and a deferred-deletion list for staging resources.
/// </summary>
/// <remarks>
/// Not thread-safe. All methods — <see cref="Begin"/>, <see cref="Submit"/>,
/// <see cref="DeferRelease"/>, <see cref="SyncGraphicsQueue"/>,
/// <see cref="ProcessDeferredDeletions"/>, <see cref="Flush"/>, and
/// <see cref="Dispose"/> — must be called from a single thread.
/// Do not call from background threads or async upload paths without external synchronisation.
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
    private bool _isRecording;

    public ID3D12GraphicsCommandList CommandList { get; }
    public ID3D12CommandQueue CopyQueue => _copyQueue;

    public UploadContext(ID3D12Device device)
    {
        _copyQueue = device.CreateCommandQueue(CommandListType.Copy);
        _copyQueue.Name = "Copy Queue";

        _allocator = device.CreateCommandAllocator(CommandListType.Copy);
        CommandList = device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Copy, _allocator);
        // Close immediately so the first Begin() can Reset() cleanly.
        CommandList.Close();

        _fence = device.CreateFence();
        _fenceEvent = new AutoResetEvent(false);
    }

    /// <summary>
    /// Resets the allocator and command list ready for recording a new batch.
    /// CPU-waits if a prior Submit() batch is still in flight.
    /// Initial state: _fenceValue=0, CompletedValue=0 — no wait on the first call.
    /// </summary>
    public void Begin()
    {
        Debug.Assert(!_isRecording, "Begin() called while already recording — missing Submit().");
        _isRecording = true;

        if (_fenceValue > _fence.CompletedValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            _fenceEvent.WaitOne();
        }

        _allocator.Reset();
        CommandList.Reset(_allocator, null);
    }

    /// <summary>
    /// Closes the command list, executes it on the copy queue, and signals the fence.
    /// Returns the fence value for this submission (monotonically increasing from 1).
    /// </summary>
    public ulong Submit()
    {
        Debug.Assert(_isRecording, "Submit() called without a preceding Begin().");
        _isRecording = false;

        CommandList.Close();
        _copyQueue.ExecuteCommandList(CommandList);
        _copyQueue.Signal(_fence, ++_fenceValue);
        return _fenceValue;
    }

    /// <summary>
    /// Queues disposables for deferred release once <paramref name="fenceValue"/> is reached.
    /// Pass each ID3D12Resource before its paired Allocation to ensure correct disposal order.
    /// </summary>
    public void DeferRelease(ulong fenceValue, params IDisposable[] resources)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IDisposable resource in resources)
        {
            ArgumentNullException.ThrowIfNull(resource);
            _pendingReleases.Add((fenceValue, resource));
        }
    }

    /// <summary>
    /// Inserts a GPU-side wait into <paramref name="graphicsQueue"/> on the copy fence.
    /// No CPU block. Must be called BEFORE GraphicsQueue.ExecuteCommandList for the barrier pass.
    /// </summary>
    public void SyncGraphicsQueue(ID3D12CommandQueue graphicsQueue, ulong fenceValue)
    {
        graphicsQueue.Wait(_fence, fenceValue);
    }

    /// <summary>
    /// Checks the completed fence value and disposes any pending resources whose value has passed.
    /// Call once per frame from DrawFrame.
    /// </summary>
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

    /// <summary>
    /// CPU-blocks until all in-flight copy submissions are done, then drains _pendingReleases.
    /// After this returns, _pendingReleases is empty and all staging buffers are freed.
    /// Call before Dispose() and before disposing any D3D12 device-owned resources.
    /// </summary>
    public void Flush()
    {
        if (_fenceValue > _fence.CompletedValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            _fenceEvent.WaitOne();
        }
        ProcessDeferredDeletions();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Flush();   // drains _pendingReleases before releasing D3D12 objects

        CommandList.Dispose();
        _allocator.Dispose();
        _fence.Dispose();
        _fenceEvent.Dispose();
        _copyQueue.Dispose();
    }
}
