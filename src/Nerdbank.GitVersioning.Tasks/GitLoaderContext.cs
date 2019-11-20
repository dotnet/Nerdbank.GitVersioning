// This code originally copied from https://github.com/dotnet/sourcelink/tree/c092238370e0437eb95722f28c79273244dc7f1a/src/Microsoft.Build.Tasks.Git
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.
#if NETCOREAPP

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

namespace Nerdbank.GitVersioning
{
    internal class GitLoaderContext : AssemblyLoadContext
    {
        public static readonly GitLoaderContext Instance = new GitLoaderContext();

        // When invoked as a MSBuild task, the native libraries will be at
        // ../runtimes. When invoked from the nbgv CLI, the libraries
        // will be at ./runtimes.
        // This property allows code which consumes GitLoaderContext to 
        // differentiate between these different locations.
        // In the case of the nbgv CLI, the value is set in Program.Main()
        public static string RuntimePath = "../runtimes";

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(GitLoaderContext).Assembly.Location), assemblyName.Name + ".dll");
            return File.Exists(path)
                ? LoadFromAssemblyPath(path)
                : Default.LoadFromAssemblyName(assemblyName);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var modulePtr = IntPtr.Zero;

            if (unmanagedDllName.StartsWith("git2-", StringComparison.Ordinal) ||
                unmanagedDllName.StartsWith("libgit2-", StringComparison.Ordinal))
            {
                var directory = GetNativeLibraryDirectory();
                var extension = GetNativeLibraryExtension();

                if (!unmanagedDllName.EndsWith(extension, StringComparison.Ordinal))
                {
                    unmanagedDllName += extension;
                }

                var nativeLibraryPath = Path.Combine(directory, unmanagedDllName);
                if (!File.Exists(nativeLibraryPath))
                {
                    nativeLibraryPath = Path.Combine(directory, "lib" + unmanagedDllName);
                }

                modulePtr = LoadUnmanagedDllFromPath(nativeLibraryPath);
            }

            return (modulePtr != IntPtr.Zero) ? modulePtr : base.LoadUnmanagedDll(unmanagedDllName);
        }

        internal static string GetNativeLibraryDirectory()
        {
            var dir = Path.GetDirectoryName(typeof(GitLoaderContext).Assembly.Location);
            return Path.Combine(dir, RuntimePath, RuntimeIdMap.GetNativeLibraryDirectoryName(RuntimeEnvironment.GetRuntimeIdentifier()), "native");
        }

        private static string GetNativeLibraryExtension()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ".dll";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return ".dylib";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return ".so";
            }

            throw new PlatformNotSupportedException();
        }
    }
}
#endif
