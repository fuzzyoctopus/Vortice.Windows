// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12MemoryAllocator;

public static unsafe partial class D3D12MA
{
    public static event DllImportResolver? ResolveLibrary;

    static D3D12MA()
    {
        ResolveLibrary += static (libraryName, assembly, searchPath) =>
        {
            if (libraryName is not "D3D12MA" and not "D3D12MA.dll")
            {
                return IntPtr.Zero;
            }

            string rid = RuntimeInformation.RuntimeIdentifier;
            string nugetNativeLibraryPath = Path.Combine(AppContext.BaseDirectory, $@"runtimes\{rid}\native\D3D12MA.dll");

            if (NativeLibrary.TryLoad(nugetNativeLibraryPath, out IntPtr handle))
            {
                return handle;
            }

            if (NativeLibrary.TryLoad("D3D12MA", assembly, searchPath, out handle))
            {
                return handle;
            }

            if (NativeLibrary.TryLoad("D3D12MA.dll", assembly, searchPath, out handle))
            {
                return handle;
            }

            return IntPtr.Zero;
        };

        NativeLibrary.SetDllImportResolver(typeof(D3D12MA).Assembly, OnDllImport);
    }

    public static Result CreateAllocator(AllocatorDescription description, out Allocator? allocator)
    {
        return CreateAllocator(in description, out allocator);
    }

    public static Result CreateAllocator(in AllocatorDescription description, out Allocator? allocator)
    {
        AllocatorDescriptionNative nativeDescription = description.ToNative();
        Result result = (Result)NativeMethods.D3D12MA_CreateAllocator(&nativeDescription, out IntPtr nativeAllocator);
        if (result.Failure)
        {
            allocator = default;
            return result;
        }

        allocator = new Allocator(nativeAllocator);
        return result;
    }

    public static Allocator CreateAllocator(in AllocatorDescription description)
    {
        CreateAllocator(description, out Allocator? allocator).CheckError();
        return allocator!;
    }

    private static IntPtr OnDllImport(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (TryResolveLibrary(libraryName, assembly, searchPath, out IntPtr nativeLibrary))
        {
            return nativeLibrary;
        }

        return NativeLibrary.Load(libraryName, assembly, searchPath);
    }

    private static bool TryResolveLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath, out IntPtr nativeLibrary)
    {
        var resolveLibrary = ResolveLibrary;
        if (resolveLibrary != null)
        {
            Delegate[] resolvers = resolveLibrary.GetInvocationList();
            foreach (DllImportResolver resolver in resolvers)
            {
                nativeLibrary = resolver(libraryName, assembly, searchPath);
                if (nativeLibrary != IntPtr.Zero)
                {
                    return true;
                }
            }
        }

        nativeLibrary = IntPtr.Zero;
        return false;
    }
}
