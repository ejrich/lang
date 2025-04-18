// Copyright (c) .NET Foundation and Contributors. All Rights Reserved. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace LLVMSharp.Interop;

public static unsafe partial class LLVM
{
    public static event DllImportResolver ResolveLibrary;

    static LLVM()
    {
        if (!Configuration.DisableResolveLibraryHook)
        {
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), OnDllImport);
        }
    }

    private static IntPtr OnDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName.Equals("libLLVM", StringComparison.Ordinal) && TryResolveLLVM(assembly, searchPath, out var nativeLibrary))
        {
            return nativeLibrary;
        }

        if (TryResolveLibrary(libraryName, assembly, searchPath, out nativeLibrary))
        {
            return nativeLibrary;
        }

        return IntPtr.Zero;
    }

    private static bool TryResolveLLVM(Assembly assembly, DllImportSearchPath? searchPath, out IntPtr nativeLibrary)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var llvmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libLLVM.so.19.1");
            return NativeLibrary.TryLoad(llvmPath, out nativeLibrary);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // TODO Add LLVM-C.dll and lld-link.exe to the project
            var llvmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LLVM-C.dll");
            return NativeLibrary.TryLoad(llvmPath, out nativeLibrary);
        }

        nativeLibrary = IntPtr.Zero;
        return false;
    }

    private static bool TryResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath, out IntPtr nativeLibrary)
    {
        var resolveLibrary = ResolveLibrary;

        if (resolveLibrary is not null)
        {
            var resolvers = resolveLibrary.GetInvocationList().Cast<DllImportResolver>();

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
