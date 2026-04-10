// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.
//
// Portions of this file are derived from AMD's D3D12 Memory Allocator sample code.
// Copyright (c) 2019-2026 Advanced Micro Devices, Inc.
// Licensed under the MIT License; see THIRD_PARTY_NOTICES.md in the repository root.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.Direct3D12MemoryAllocator;
using Vortice.Dxc;
using Vortice.DXGI;
using Vortice.DXGI.Debug;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;
using ResultCode = Vortice.DXGI.ResultCode;

namespace HelloDirect3D12MemoryAllocator;

public sealed unsafe class D3D12GraphicsDevice : IGraphicsDevice
{
    private const int RenderLatency = 3;
    private const int TextureSize = 256;

    private static readonly VertexPositionTexture[] CubeVertices =
    [
        new(new Vector3(-1.0f, -1.0f, 1.0f), new Vector2(0.0f, 1.0f)),
        new(new Vector3(-1.0f, 1.0f, 1.0f), new Vector2(0.0f, 0.0f)),
        new(new Vector3(1.0f, 1.0f, 1.0f), new Vector2(1.0f, 0.0f)),
        new(new Vector3(1.0f, -1.0f, 1.0f), new Vector2(1.0f, 1.0f)),

        new(new Vector3(1.0f, -1.0f, -1.0f), new Vector2(0.0f, 1.0f)),
        new(new Vector3(1.0f, 1.0f, -1.0f), new Vector2(0.0f, 0.0f)),
        new(new Vector3(-1.0f, 1.0f, -1.0f), new Vector2(1.0f, 0.0f)),
        new(new Vector3(-1.0f, -1.0f, -1.0f), new Vector2(1.0f, 1.0f)),

        new(new Vector3(-1.0f, -1.0f, -1.0f), new Vector2(0.0f, 1.0f)),
        new(new Vector3(-1.0f, 1.0f, -1.0f), new Vector2(0.0f, 0.0f)),
        new(new Vector3(-1.0f, 1.0f, 1.0f), new Vector2(1.0f, 0.0f)),
        new(new Vector3(-1.0f, -1.0f, 1.0f), new Vector2(1.0f, 1.0f)),

        new(new Vector3(1.0f, -1.0f, 1.0f), new Vector2(0.0f, 1.0f)),
        new(new Vector3(1.0f, 1.0f, 1.0f), new Vector2(0.0f, 0.0f)),
        new(new Vector3(1.0f, 1.0f, -1.0f), new Vector2(1.0f, 0.0f)),
        new(new Vector3(1.0f, -1.0f, -1.0f), new Vector2(1.0f, 1.0f)),

        new(new Vector3(-1.0f, 1.0f, 1.0f), new Vector2(0.0f, 1.0f)),
        new(new Vector3(-1.0f, 1.0f, -1.0f), new Vector2(0.0f, 0.0f)),
        new(new Vector3(1.0f, 1.0f, -1.0f), new Vector2(1.0f, 0.0f)),
        new(new Vector3(1.0f, 1.0f, 1.0f), new Vector2(1.0f, 1.0f)),

        new(new Vector3(-1.0f, -1.0f, -1.0f), new Vector2(0.0f, 1.0f)),
        new(new Vector3(-1.0f, -1.0f, 1.0f), new Vector2(0.0f, 0.0f)),
        new(new Vector3(1.0f, -1.0f, 1.0f), new Vector2(1.0f, 0.0f)),
        new(new Vector3(1.0f, -1.0f, -1.0f), new Vector2(1.0f, 1.0f))
    ];

    private static readonly ushort[] CubeIndices =
    [
        0, 1, 2, 0, 2, 3,
        4, 5, 6, 4, 6, 7,
        8, 9, 10, 8, 10, 11,
        12, 13, 14, 12, 14, 15,
        16, 17, 18, 16, 18, 19,
        20, 21, 22, 20, 22, 23
    ];

    private readonly uint _cbvSrvDescriptorSize;
    private readonly ID3D12CommandAllocator[] _commandAllocators;
    private readonly ID3D12GraphicsCommandList4 _commandList;
    private readonly Allocation _depthStencilAllocation;
    private readonly Format _depthStencilFormat;

    private readonly ID3D12Resource _depthStencilTexture;
    private readonly ID3D12DescriptorHeap _dsvDescriptorHeap;
    private readonly ID3D12Fence _frameFence;
    private readonly AutoResetEvent _frameFenceEvent;
    private readonly ID3D12Resource _indexBuffer;
    private readonly Allocation _indexBufferAllocation;
    private readonly IndexBufferView _indexBufferView;
    private readonly ID3D12InfoQueue1? _infoQueue1;
    private readonly List<IDisposable> _initialUploadDisposables = [];
    private readonly ID3D12DescriptorHeap[] _mainDescriptorHeaps = new ID3D12DescriptorHeap[RenderLatency];
    private readonly Allocator _memoryAllocator;
    private readonly Allocation[] _objectConstantAllocations = new Allocation[RenderLatency];
    private readonly IntPtr[] _objectConstantBufferData = new IntPtr[RenderLatency];

    private readonly ID3D12Resource[] _objectConstantBuffers = new ID3D12Resource[RenderLatency];
    private readonly int _objectConstantBufferSize;
    private readonly int _objectConstantBufferStride;
    private readonly Allocation[] _pixelConstantAllocations = new Allocation[RenderLatency];
    private readonly IntPtr[] _pixelConstantBufferData = new IntPtr[RenderLatency];

    private readonly ID3D12Resource[] _pixelConstantBuffers = new ID3D12Resource[RenderLatency];

    private readonly int _pixelConstantBufferSize;
    private readonly ID3D12Resource[] _renderTargets;
    private readonly ID3D12DescriptorHeap _rtvDescriptorHeap;

    private readonly uint _rtvDescriptorSize;
    private readonly ID3D12Resource _texture;
    private readonly Allocation _textureAllocation;

    private readonly Stopwatch _timer = Stopwatch.StartNew();

    private readonly ID3D12Resource _vertexBuffer;
    private readonly Allocation _vertexBufferAllocation;

    private readonly VertexBufferView _vertexBufferView;

    private readonly ID3D12Device2 Device;
    public readonly IDXGIFactory4 DXGIFactory;

    public readonly Window Window;
    private uint _backbufferIndex;
    private ulong _frameCount;
    private ulong _frameIndex;
    private ID3D12PipelineState _pipelineState = null!;
    private ID3D12RootSignature _rootSignature = null!;

    public D3D12GraphicsDevice(bool validation, Window window, uint? gpuIndex = null, string? gpuSubstring = null,
        Format depthStencilFormat = Format.D32_Float)
    {
        if (!IsSupported())
        {
            throw new InvalidOperationException("Direct3D12 is not supported on current OS");
        }

        Window = window;
        _depthStencilFormat = depthStencilFormat;
        _pixelConstantBufferSize = AlignConstantBufferSize(sizeof(PixelConstants));
        _objectConstantBufferStride = AlignConstantBufferSize(sizeof(ObjectConstants));
        _objectConstantBufferSize = _objectConstantBufferStride * 2;

        if (validation
            && D3D12GetDebugInterface(out ID3D12Debug? debug).Success)
        {
            debug!.EnableDebugLayer();
            debug.Dispose();
        }
        else
        {
            validation = false;
        }

        if (D3D12GetDebugInterface(out ID3D12DeviceRemovedExtendedDataSettings1? dredSettings).Success &&
            dredSettings is not null)
        {
            dredSettings.SetAutoBreadcrumbsEnablement(DredEnablement.ForcedOn);
            dredSettings.SetPageFaultEnablement(DredEnablement.ForcedOn);
            dredSettings.SetBreadcrumbContextEnablement(DredEnablement.ForcedOn);
            dredSettings.Dispose();
        }

        DXGIFactory = CreateDXGIFactory2<IDXGIFactory4>(validation);

        Device = CreateDevice(gpuIndex, gpuSubstring, out IDXGIAdapter1 selectedAdapter);
        using (selectedAdapter)
        {
            _memoryAllocator = D3D12MA.CreateAllocator(new AllocatorDescription(Device, selectedAdapter));
        }

        using (IDXGIFactory5? factory5 = DXGIFactory.QueryInterfaceOrNull<IDXGIFactory5>())
        {
            if (factory5 != null)
            {
                IsTearingSupported = factory5.PresentAllowTearing;
            }
        }

        _infoQueue1 = Device.QueryInterfaceOrNull<ID3D12InfoQueue1>();
        if (_infoQueue1 != null)
        {
            _infoQueue1.RegisterMessageCallback(DebugCallback);

#if DEBUG
            _infoQueue1.SetBreakOnSeverity(MessageSeverity.Corruption, true);
            _infoQueue1.SetBreakOnSeverity(MessageSeverity.Error, true);
#endif
        }

        GraphicsQueue = Device.CreateCommandQueue(CommandListType.Direct);
        GraphicsQueue.Name = "Graphics Queue";

        SwapChainDescription1 swapChainDesc = new()
        {
            BufferCount = RenderLatency,
            Width = (uint)window.ClientSize.Width,
            Height = (uint)window.ClientSize.Height,
            Format = Format.R8G8B8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0)
        };

        using (IDXGISwapChain1 swapChain =
               DXGIFactory.CreateSwapChainForHwnd(GraphicsQueue, window.Handle, swapChainDesc))
        {
            DXGIFactory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);
            SwapChain = swapChain.QueryInterface<IDXGISwapChain3>();
            _backbufferIndex = SwapChain.CurrentBackBufferIndex;
        }

        _rtvDescriptorHeap =
            Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView,
                RenderLatency));
        _rtvDescriptorSize = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

        _dsvDescriptorHeap =
            Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));
        _cbvSrvDescriptorSize =
            Device.GetDescriptorHandleIncrementSize(DescriptorHeapType
                .ConstantBufferViewShaderResourceViewUnorderedAccessView);

        _renderTargets = new ID3D12Resource[RenderLatency];
        CpuDescriptorHandle rtvHandle = _rtvDescriptorHeap.GetCPUDescriptorHandleForHeapStart();
        for (uint i = 0; i < RenderLatency; i++)
        {
            _renderTargets[i] = SwapChain.GetBuffer<ID3D12Resource>(i);
            Device.CreateRenderTargetView(_renderTargets[i], null, rtvHandle);
            rtvHandle += (int)_rtvDescriptorSize;
        }

        _depthStencilTexture = _memoryAllocator.CreateResource(
            AllocationDescription.Default(HeapType.Default),
            ResourceDescription.Texture2D(_depthStencilFormat, swapChainDesc.Width, swapChainDesc.Height,
                flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite,
            out _depthStencilAllocation,
            new ClearValue(_depthStencilFormat, 1.0f));
        _depthStencilTexture.Name = "DepthStencil Texture";

        DepthStencilViewDescription depthStencilViewDesc = new()
        {
            Format = _depthStencilFormat, ViewDimension = DepthStencilViewDimension.Texture2D
        };
        Device.CreateDepthStencilView(_depthStencilTexture, depthStencilViewDesc,
            _dsvDescriptorHeap.GetCPUDescriptorHandleForHeapStart());

        _commandAllocators = new ID3D12CommandAllocator[RenderLatency];
        for (int i = 0; i < RenderLatency; i++)
        {
            _commandAllocators[i] = Device.CreateCommandAllocator(CommandListType.Direct);
        }

        CreateRootSignatureAndPipeline();
        CreatePerFrameResources();

        _commandList =
            Device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _commandAllocators[0]);

        CreateGeometryAndTextureResources(out _vertexBuffer, out _vertexBufferAllocation, out _vertexBufferView,
            out _indexBuffer, out _indexBufferAllocation, out _indexBufferView, out _texture, out _textureAllocation);

        _commandList.Close();
        GraphicsQueue.ExecuteCommandList(_commandList);

        _frameFence = Device.CreateFence();
        _frameFenceEvent = new AutoResetEvent(false);

        WaitIdle();
        DisposeInitialUploadResources();
    }

    public bool IsTearingSupported { get; }
    public ID3D12CommandQueue GraphicsQueue { get; }
    public IDXGISwapChain3 SwapChain { get; }

    public bool DrawFrame(Action<int, int> draw, [CallerMemberName] string? frameName = null)
    {
        int frame = (int)_frameIndex;
        UpdateConstantBuffers(frame);

        _commandAllocators[frame].Reset();
        _commandList.Reset(_commandAllocators[frame], _pipelineState);

        _commandList.BeginEvent("Frame");

        _commandList.ResourceBarrierTransition(_renderTargets[_backbufferIndex], ResourceStates.Present,
            ResourceStates.RenderTarget);

        CpuDescriptorHandle rtvDescriptor = new(_rtvDescriptorHeap.GetCPUDescriptorHandleForHeapStart(),
            (int)_backbufferIndex, _rtvDescriptorSize);
        CpuDescriptorHandle dsvDescriptor = _dsvDescriptorHeap.GetCPUDescriptorHandleForHeapStart();

        _commandList.OMSetRenderTargets(rtvDescriptor, dsvDescriptor);
        _commandList.ClearDepthStencilView(dsvDescriptor, ClearFlags.Depth, 1.0f, 0);
        _commandList.ClearRenderTargetView(rtvDescriptor, new Color4(0.0f, 0.2f, 0.4f));

        _commandList.RSSetViewport(new Viewport(Window.ClientSize.Width, Window.ClientSize.Height));
        _commandList.RSSetScissorRect(Window.ClientSize.Width, Window.ClientSize.Height);

        _commandList.SetPipelineState(_pipelineState);
        _commandList.SetGraphicsRootSignature(_rootSignature);

        ID3D12DescriptorHeap[] descriptorHeaps = [_mainDescriptorHeaps[frame]];
        _commandList.SetDescriptorHeaps(descriptorHeaps);
        _commandList.SetGraphicsRootDescriptorTable(0,
            _mainDescriptorHeaps[frame].GetGPUDescriptorHandleForHeapStart());

        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, _vertexBufferView);
        _commandList.IASetIndexBuffer(_indexBufferView);

        _commandList.SetGraphicsRootConstantBufferView(1, _objectConstantBuffers[frame].GPUVirtualAddress);
        _commandList.DrawIndexedInstanced((uint)CubeIndices.Length, 1, 0, 0, 0);

        _commandList.SetGraphicsRootConstantBufferView(1,
            _objectConstantBuffers[frame].GPUVirtualAddress + (ulong)_objectConstantBufferStride);
        _commandList.DrawIndexedInstanced((uint)CubeIndices.Length, 1, 0, 0, 0);

        draw(Window.ClientSize.Width, Window.ClientSize.Height);

        _commandList.ResourceBarrierTransition(_renderTargets[_backbufferIndex], ResourceStates.RenderTarget,
            ResourceStates.Present);
        _commandList.EndEvent();
        _commandList.Close();

        GraphicsQueue.ExecuteCommandList(_commandList);

        Result presentResult = SwapChain.Present(1, PresentFlags.None);
        if (presentResult.Failure
            && (presentResult.Code == ResultCode.DeviceRemoved.Code ||
                presentResult.Code == ResultCode.DeviceReset.Code))
        {
            return false;
        }

        GraphicsQueue.Signal(_frameFence, ++_frameCount);
        ulong gpuFrameCount = _frameFence.CompletedValue;
        if (_frameCount - gpuFrameCount >= RenderLatency)
        {
            _frameFence.SetEventOnCompletion(gpuFrameCount + 1, _frameFenceEvent);
            _frameFenceEvent.WaitOne();
        }

        _frameIndex = _frameCount % RenderLatency;
        _backbufferIndex = SwapChain.CurrentBackBufferIndex;
        return true;
    }

    public void Dispose()
    {
        WaitIdle();

        for (int i = 0; i < RenderLatency; i++)
        {
            _pixelConstantBuffers[i].Unmap(0);
            _objectConstantBuffers[i].Unmap(0);
        }

        _texture.Dispose();
        _textureAllocation.Dispose();
        _indexBuffer.Dispose();
        _indexBufferAllocation.Dispose();
        _vertexBuffer.Dispose();
        _vertexBufferAllocation.Dispose();

        for (int i = 0; i < RenderLatency; i++)
        {
            _pixelConstantBuffers[i].Dispose();
            _pixelConstantAllocations[i].Dispose();
            _objectConstantBuffers[i].Dispose();
            _objectConstantAllocations[i].Dispose();
            _mainDescriptorHeaps[i].Dispose();
            _commandAllocators[i].Dispose();
            _renderTargets[i].Dispose();
        }

        _depthStencilTexture.Dispose();
        _depthStencilAllocation.Dispose();
        _commandList.Dispose();
        _pipelineState.Dispose();
        _rootSignature.Dispose();
        _dsvDescriptorHeap.Dispose();
        _rtvDescriptorHeap.Dispose();
        SwapChain.Dispose();
        _frameFence.Dispose();
        GraphicsQueue.Dispose();
        _infoQueue1?.Dispose();
        _memoryAllocator.Dispose();

#if DEBUG
        uint refCount = Device.Release();
        if (refCount > 0)
        {
            ID3D12DebugDevice? d3d12DebugDevice = Device.QueryInterfaceOrNull<ID3D12DebugDevice>();
            if (d3d12DebugDevice != null)
            {
                d3d12DebugDevice.ReportLiveDeviceObjects(ReportLiveDeviceObjectFlags.Detail |
                                                         ReportLiveDeviceObjectFlags.IgnoreInternal);
                d3d12DebugDevice.Dispose();
            }
        }
#else
        Device.Dispose();
#endif

        DXGIFactory.Dispose();

#if DEBUG
        if (!DXGIGetDebugInterface1(out IDXGIDebug1? dxgiDebug).Success)
            return;
        

        dxgiDebug!.ReportLiveObjects(DebugAll,
            ReportLiveObjectFlags.Summary | ReportLiveObjectFlags.IgnoreInternal);
        dxgiDebug.Dispose();
#endif
    }

    public static bool IsSupported() => D3D12.IsSupported(FeatureLevel.Level_12_0);

    public static void PrintAdapterList(bool validation = false)
    {
        using IDXGIFactory4 factory = CreateDXGIFactory2<IDXGIFactory4>(validation);
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            if (adapter is null)
            {
                continue;
            }

            AdapterDescription1 desc = adapter.Description1;
            string suffix = (desc.Flags & AdapterFlags.Software) != 0 ? " (SOFTWARE)" : string.Empty;
            Console.WriteLine($"Adapter {i}: {desc.Description}{suffix}");
            adapter.Dispose();
        }
    }

    public void PrintAllocatorStats()
    {
        Console.WriteLine(_memoryAllocator.BuildStatsString(true));
    }

    public void WaitIdle()
    {
        ulong fenceValue = ++_frameCount;
        GraphicsQueue.Signal(_frameFence, fenceValue);
        _frameFence.SetEventOnCompletion(fenceValue, _frameFenceEvent);
        _frameFenceEvent.WaitOne();
    }

    private static int AlignConstantBufferSize(int sizeInBytes)
    {
        return (sizeInBytes + 255) & ~255;
    }

    private ID3D12Device2 CreateDevice(uint? gpuIndex, string? gpuSubstring, out IDXGIAdapter1 selectedAdapter)
    {
        ID3D12Device2? d3d12Device = default;
        selectedAdapter = null!;
        IDXGIAdapter1? selected = null;

        for (uint adapterIndex = 0;
             DXGIFactory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success;
             adapterIndex++)
        {
            if (adapter is null)
            {
                continue;
            }

            AdapterDescription1 desc = adapter.Description1;
            if ((desc.Flags & AdapterFlags.Software) != AdapterFlags.None)
            {
                adapter.Dispose();
                continue;
            }

            if (gpuIndex.HasValue && gpuIndex.Value != adapterIndex)
            {
                adapter.Dispose();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(gpuSubstring)
                && desc.Description.IndexOf(gpuSubstring, StringComparison.OrdinalIgnoreCase) < 0)
            {
                adapter.Dispose();
                continue;
            }

            if (D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out d3d12Device).Success)
            {
                selected = adapter;
                break;
            }

            adapter.Dispose();
        }

        if (d3d12Device is null || selected is null)
        {
            if (gpuIndex.HasValue || !string.IsNullOrWhiteSpace(gpuSubstring))
            {
                throw new InvalidOperationException("Cannot create ID3D12Device for the selected adapter.");
            }

            throw new PlatformNotSupportedException("Cannot create ID3D12Device.");
        }

        selectedAdapter = selected;
        return d3d12Device;
    }

    private void CreateRootSignatureAndPipeline()
    {
        RootSignatureFlags rootSignatureFlags = RootSignatureFlags.AllowInputAssemblerInputLayout;
        rootSignatureFlags |= RootSignatureFlags.DenyHullShaderRootAccess;
        rootSignatureFlags |= RootSignatureFlags.DenyDomainShaderRootAccess;
        rootSignatureFlags |= RootSignatureFlags.DenyGeometryShaderRootAccess;
        rootSignatureFlags |= RootSignatureFlags.DenyAmplificationShaderRootAccess;
        rootSignatureFlags |= RootSignatureFlags.DenyMeshShaderRootAccess;

        DescriptorRange1 pixelDescriptorCBVRange = new(DescriptorRangeType.ConstantBufferView, 1, 0, 0, 0);
        DescriptorRange1 textureSRVRange = new(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 1);

        RootSignatureDescription1 rootSignatureDesc = new(rootSignatureFlags)
        {
            Parameters =
            [
                new RootParameter1(new RootDescriptorTable1(pixelDescriptorCBVRange, textureSRVRange),
                    ShaderVisibility.Pixel),
                new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(1, 0),
                    ShaderVisibility.Vertex)
            ],
            StaticSamplers =
            [
                new StaticSamplerDescription(
                    0,
                    Filter.MinMagMipLinear,
                    TextureAddressMode.Border,
                    TextureAddressMode.Border,
                    TextureAddressMode.Border,
                    0.0f,
                    1,
                    ComparisonFunction.Never,
                    StaticBorderColor.TransparentBlack,
                    0.0f,
                    float.MaxValue,
                    ShaderVisibility.Pixel)
            ]
        };

        _rootSignature = Device.CreateRootSignature(rootSignatureDesc);

        InputElementDescription[] inputElementDescs =
        [
            new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
        ];

        ReadOnlyMemory<byte> vertexShaderByteCode = CompileBytecode(DxcShaderStage.Vertex, "VS.hlsl", "main");
        ReadOnlyMemory<byte> pixelShaderByteCode = CompileBytecode(DxcShaderStage.Pixel, "PS.hlsl", "main");

        GraphicsPipelineStateDescription psoDesc = new()
        {
            RootSignature = _rootSignature,
            VertexShader = vertexShaderByteCode,
            PixelShader = pixelShaderByteCode,
            InputLayout = new InputLayoutDescription(inputElementDescs),
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RasterizerState = RasterizerDescription.CullNone,
            BlendState = BlendDescription.Opaque,
            DepthStencilState = DepthStencilDescription.Default,
            RenderTargetFormats = [Format.R8G8B8A8_UNorm],
            DepthStencilFormat = _depthStencilFormat,
            SampleDescription = SampleDescription.Default
        };

        _pipelineState = Device.CreateGraphicsPipelineState(psoDesc);
    }

    private void CreatePerFrameResources()
    {
        for (int i = 0; i < RenderLatency; i++)
        {
            _mainDescriptorHeaps[i] = Device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                    2,
                    DescriptorHeapFlags.ShaderVisible));

            _pixelConstantBuffers[i] = _memoryAllocator.CreateResource(
                AllocationDescription.Default(HeapType.Upload),
                ResourceDescription.Buffer((ulong)_pixelConstantBufferSize),
                ResourceStates.GenericRead,
                out Allocation pixelConstantAllocation);
            _pixelConstantAllocations[i] = pixelConstantAllocation;

            _objectConstantBuffers[i] = _memoryAllocator.CreateResource(
                AllocationDescription.Default(HeapType.Upload),
                ResourceDescription.Buffer((ulong)_objectConstantBufferSize),
                ResourceStates.GenericRead,
                out Allocation objectConstantAllocation);
            _objectConstantAllocations[i] = objectConstantAllocation;

            void* pixelConstantBufferPointer;
            _pixelConstantBuffers[i].Map(0, null, &pixelConstantBufferPointer).CheckError();
            _pixelConstantBufferData[i] = (IntPtr)pixelConstantBufferPointer;

            void* objectConstantBufferPointer;
            _objectConstantBuffers[i].Map(0, null, &objectConstantBufferPointer).CheckError();
            _objectConstantBufferData[i] = (IntPtr)objectConstantBufferPointer;

            CpuDescriptorHandle cbvHandle = _mainDescriptorHeaps[i].GetCPUDescriptorHandleForHeapStart();
            ConstantBufferViewDescription cbvDesc = new(_pixelConstantBuffers[i], (uint)_pixelConstantBufferSize);
            Device.CreateConstantBufferView(cbvDesc, cbvHandle);
        }
    }

    private void CreateGeometryAndTextureResources(
        out ID3D12Resource vertexBuffer,
        out Allocation vertexBufferAllocation,
        out VertexBufferView vertexBufferView,
        out ID3D12Resource indexBuffer,
        out Allocation indexBufferAllocation,
        out IndexBufferView indexBufferView,
        out ID3D12Resource texture,
        out Allocation textureAllocation)
    {
        ulong vertexBufferSize = (ulong)(CubeVertices.Length * sizeof(VertexPositionTexture));
        ulong indexBufferSize = (ulong)(CubeIndices.Length * sizeof(ushort));

        vertexBuffer = _memoryAllocator.CreateResource(
            AllocationDescription.Default(HeapType.Default),
            ResourceDescription.Buffer(vertexBufferSize),
            ResourceStates.CopyDest,
            out vertexBufferAllocation);
        vertexBuffer.Name = "Vertex Buffer";

        ID3D12Resource vertexUpload = _memoryAllocator.CreateResource(
            AllocationDescription.Default(HeapType.Upload),
            ResourceDescription.Buffer(vertexBufferSize),
            ResourceStates.GenericRead,
            out Allocation vertexUploadAllocation);
        vertexUpload.SetData(CubeVertices);

        _commandList.CopyBufferRegion(vertexBuffer, 0, vertexUpload, 0, vertexBufferSize);
        _commandList.ResourceBarrierTransition(vertexBuffer, ResourceStates.CopyDest,
            ResourceStates.VertexAndConstantBuffer);

        indexBuffer = _memoryAllocator.CreateResource(
            AllocationDescription.Default(HeapType.Default),
            ResourceDescription.Buffer(indexBufferSize),
            ResourceStates.CopyDest,
            out indexBufferAllocation);
        indexBuffer.Name = "Index Buffer";

        ID3D12Resource indexUpload = _memoryAllocator.CreateResource(
            AllocationDescription.Default(HeapType.Upload),
            ResourceDescription.Buffer(indexBufferSize),
            ResourceStates.GenericRead,
            out Allocation indexUploadAllocation);
        indexUpload.SetData(CubeIndices);

        _commandList.CopyBufferRegion(indexBuffer, 0, indexUpload, 0, indexBufferSize);
        _commandList.ResourceBarrierTransition(indexBuffer, ResourceStates.CopyDest, ResourceStates.IndexBuffer);

        vertexBufferView = new VertexBufferView(vertexBuffer.GPUVirtualAddress, (uint)vertexBufferSize,
            (uint)sizeof(VertexPositionTexture));
        indexBufferView = new IndexBufferView(indexBuffer.GPUVirtualAddress, (uint)indexBufferSize, Format.R16_UInt);

        byte[] textureData = CreateCheckerboardTexture(TextureSize, TextureSize);
        texture = _memoryAllocator.CreateResource(
            AllocationDescription.Default(HeapType.Default),
            ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, TextureSize, TextureSize),
            ResourceStates.CopyDest,
            out textureAllocation);
        texture.Name = "Checkerboard Texture";

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

        _commandList.ResourceBarrierTransition(texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

        for (int i = 0; i < RenderLatency; i++)
        {
            CpuDescriptorHandle srvHandle = new(_mainDescriptorHeaps[i].GetCPUDescriptorHandleForHeapStart(), 1,
                _cbvSrvDescriptorSize);
            Device.CreateShaderResourceView(texture, null, srvHandle);
        }

        _initialUploadDisposables.Add(textureUploadAllocation);
        _initialUploadDisposables.Add(textureUpload);
        _initialUploadDisposables.Add(indexUploadAllocation);
        _initialUploadDisposables.Add(indexUpload);
        _initialUploadDisposables.Add(vertexUploadAllocation);
        _initialUploadDisposables.Add(vertexUpload);
    }

    private void UpdateConstantBuffers(int frame)
    {
        float time = (float)_timer.Elapsed.TotalSeconds;
        float oscillation = MathF.Sin(time * (MathF.PI * 2.0f)) * 0.5f + 0.5f;
        PixelConstants pixelConstants = new(new Vector4(oscillation, oscillation, oscillation, 1.0f));
        Unsafe.Write((void*)_pixelConstantBufferData[frame], pixelConstants);

        float aspectRatio = Math.Max(1, Window.ClientSize.Width) / (float)Math.Max(1, Window.ClientSize.Height);
        Matrix4x4 projection =
            Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (MathF.PI / 180.0f), aspectRatio, 0.1f, 1000.0f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(-0.4f, 1.7f, -3.5f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 viewProjection = view * projection;

        Matrix4x4 cube1World = Matrix4x4.CreateRotationZ(time);
        Matrix4x4 cube1WorldViewProjection = cube1World * viewProjection;
        cube1WorldViewProjection = Matrix4x4.Transpose(cube1WorldViewProjection);
        ObjectConstants cube1Constants = new(cube1WorldViewProjection);
        Unsafe.Write((void*)_objectConstantBufferData[frame], cube1Constants);

        Matrix4x4 cube2World = Matrix4x4.CreateScale(0.5f)
                               * Matrix4x4.CreateRotationX(time * 2.0f)
                               * Matrix4x4.CreateTranslation(new Vector3(-1.2f, 0.0f, 0.0f))
                               * cube1World;
        Matrix4x4 cube2WorldViewProjection = cube2World * viewProjection;
        cube2WorldViewProjection = Matrix4x4.Transpose(cube2WorldViewProjection);
        ObjectConstants cube2Constants = new(cube2WorldViewProjection);
        Unsafe.Write((byte*)_objectConstantBufferData[frame] + _objectConstantBufferStride, cube2Constants);
    }

    private static byte[] CreateCheckerboardTexture(int width, int height)
    {
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width + x) * 4;
                data[pixelIndex + 0] = x > 128 ? (byte)255 : (byte)0;
                data[pixelIndex + 1] = y > 128 ? (byte)255 : (byte)0;
                data[pixelIndex + 2] = 0;
                data[pixelIndex + 3] = 255;
            }
        }

        return data;
    }

    private void DisposeInitialUploadResources()
    {
        foreach (IDisposable disposable in _initialUploadDisposables)
        {
            disposable.Dispose();
        }

        _initialUploadDisposables.Clear();
    }

    private static void DebugCallback(MessageCategory category, MessageSeverity severity, MessageId id,
        string description)
    {
        if (severity is MessageSeverity.Message or MessageSeverity.Info)
        {
            return;
        }

        Console.Error.WriteLine($"D3D12 {severity} ({category}) [{id}]: {description}");
    }

    private static ReadOnlyMemory<byte> CompileBytecode(DxcShaderStage stage, string shaderName, string entryPoint)
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        string shaderSource = File.ReadAllText(Path.Combine(assetsPath, shaderName));

        using IDxcResult results = DxcCompiler.Compile(stage, shaderSource, entryPoint);
        if (results.GetStatus().Failure)
        {
            throw new Exception(results.GetErrors());
        }

        return results.GetObjectBytecodeMemory();
    }

    private readonly struct VertexPositionTexture(Vector3 position, Vector2 texCoord)
    {
        public readonly Vector3 Position = position;
        public readonly Vector2 TexCoord = texCoord;
    }

    private readonly struct PixelConstants(Vector4 color)
    {
        public readonly Vector4 Color = color;
    }

    private readonly struct ObjectConstants(Matrix4x4 worldViewProjection)
    {
        public readonly Matrix4x4 WorldViewProjection = worldViewProjection;
    }
}
