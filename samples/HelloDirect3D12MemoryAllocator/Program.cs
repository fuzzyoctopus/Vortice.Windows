// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using SharpGen.Runtime;
using Vortice;

namespace HelloDirect3D12MemoryAllocator;

public static class Program
{
    private sealed class TestApplication : Application
    {
        private readonly CommandLineOptions _options;

        public TestApplication(CommandLineOptions options)
        {
            _options = options;
        }

        protected override void InitializeBeforeRun()
        {
            bool validation = false;
#if DEBUG
            validation = true;
#endif

            _graphicsDevice = new D3D12GraphicsDevice(validation, MainWindow!, _options.GpuIndex, _options.GpuSubstring);
        }

        protected override void OnKeyboardEvent(KeyboardKey key, bool pressed)
        {
            if (!pressed || _graphicsDevice is not D3D12GraphicsDevice graphicsDevice)
            {
                return;
            }

            if (key == KeyboardKey.j)
            {
                graphicsDevice.PrintAllocatorStats();
            }
        }
    }

    private readonly record struct CommandLineOptions(bool Help, bool ListAdapters, uint? GpuIndex, string? GpuSubstring)
    {
        public static bool TryParse(string[] args, out CommandLineOptions options)
        {
            bool help = false;
            bool listAdapters = false;
            uint? gpuIndex = null;
            string? gpuSubstring = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    help = true;
                }
                else if (arg.Equals("--list", StringComparison.OrdinalIgnoreCase) || arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
                {
                    listAdapters = true;
                }
                else if ((arg.Equals("--gpu", StringComparison.OrdinalIgnoreCase) || arg.Equals("-g", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    gpuSubstring = args[++i];
                }
                else if ((arg.Equals("--gpu-index", StringComparison.OrdinalIgnoreCase) || arg.Equals("-i", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    if (!uint.TryParse(args[++i], out uint parsedIndex))
                    {
                        options = default;
                        return false;
                    }

                    gpuIndex = parsedIndex;
                }
                else
                {
                    options = default;
                    return false;
                }
            }

            options = new CommandLineOptions(help, listAdapters, gpuIndex, gpuSubstring);
            return true;
        }
    }

    public static void Main(string[] args)
    {
#if DEBUG
        Configuration.EnableObjectTracking = true;
#endif

        if (!CommandLineOptions.TryParse(args, out CommandLineOptions options))
        {
            Console.Error.WriteLine("Invalid command line.");
            PrintHelp();
            return;
        }

        if (options.Help)
        {
            PrintHelp();
            return;
        }

        if (options.ListAdapters)
        {
            D3D12GraphicsDevice.PrintAdapterList();
            return;
        }

        using TestApplication app = new(options);
        app.Run();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("D3D12 Memory Allocator Hello Sample");
        Console.WriteLine("  -h, --help                 Show help");
        Console.WriteLine("  -l, --list                 List available adapters");
        Console.WriteLine("  -g, --gpu <name>           Select adapter by substring");
        Console.WriteLine("  -i, --gpu-index <index>    Select adapter by index");
        Console.WriteLine("Press 'J' while running to print D3D12MA stats.");
    }
}
