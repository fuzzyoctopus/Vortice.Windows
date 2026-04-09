// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using SharpGen.Runtime;
using Vortice;

namespace HelloDirect3D12;

public static class Program
{
    private class TestApplication : Application
    {
        private readonly bool _useD3D12MemoryAllocator;

        public TestApplication(bool useD3D12MemoryAllocator)
        {
            _useD3D12MemoryAllocator = useD3D12MemoryAllocator;
        }

        protected override void InitializeBeforeRun()
        {
            var validation = false;
#if DEBUG
            validation = true;
#endif

            _graphicsDevice = new D3D12GraphicsDevice(validation, MainWindow!, _useD3D12MemoryAllocator);
        }

        protected override void OnKeyboardEvent(KeyboardKey key, bool pressed)
        {
            if (key == KeyboardKey.Space && pressed)
            {
                D3D12GraphicsDevice graphicsDevice = (D3D12GraphicsDevice)_graphicsDevice!;
                graphicsDevice.UseRenderPass = !graphicsDevice.UseRenderPass;
            }
        }
    }

    public static void Main(string[] args)
    {
#if DEBUG
        Configuration.EnableObjectTracking = true;
#endif

        bool useD3D12MemoryAllocator = false;
        foreach (string arg in args)
        {
            if (arg.Equals("--use-d3d12ma", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--d3d12ma", StringComparison.OrdinalIgnoreCase))
            {
                useD3D12MemoryAllocator = true;
                break;
            }
        }

        using TestApplication app = new(useD3D12MemoryAllocator);
        app.Run();
    }
}
