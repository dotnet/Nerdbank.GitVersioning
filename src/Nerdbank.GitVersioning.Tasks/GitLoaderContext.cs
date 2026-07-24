// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// This code originally copied from https://github.com/dotnet/sourcelink/tree/c092238370e0437eb95722f28c79273244dc7f1a/src/Microsoft.Build.Tasks.Git
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See license information at https://github.com/dotnet/sourcelink/blob/c092238370e0437eb95722f28c79273244dc7f1a/License.txt.
#nullable enable

#if NETCOREAPP

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Nerdbank.GitVersioning.LibGit2;

namespace Nerdbank.GitVersioning
{
    public class GitLoaderContext : AssemblyLoadContext
    {
        public const string RuntimePath = "./runtimes";
        private readonly string nativeDependencyBasePath;

        private (string?, IntPtr) lastLoadedLibrary;

        /// <summary>
        /// Initializes a new instance of the <see cref="GitLoaderContext"/> class.
        /// </summary>
        /// <param name="nativeDependencyBasePath">The path to the directory that contains the "runtimes" folder.</param>
        public GitLoaderContext(string nativeDependencyBasePath)
        {
            this.nativeDependencyBasePath = nativeDependencyBasePath;
        }

        /// <inheritdoc/>
        protected override Assembly Load(AssemblyName assemblyName)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(GitLoaderContext).Assembly.Location)!, assemblyName.Name + ".dll");
            return File.Exists(path)
                ? this.LoadFromAssemblyPath(path)
                : Default.LoadFromAssemblyName(assemblyName);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            // Only intercept loads for libgit2 native libraries. Returning a handle from the
            // libgit2 runtimes directory for other libraries (e.g. hostfxr, KERNEL32) causes
            // EntryPointNotFoundException when P/Invokes hit the wrong module.
            if (!IsLibGit2LibraryName(unmanagedDllName))
            {
                return IntPtr.Zero;
            }

            IntPtr p = base.LoadUnmanagedDll(unmanagedDllName);

            if (p == IntPtr.Zero)
            {
                if (unmanagedDllName == this.lastLoadedLibrary.Item1)
                {
                    return this.lastLoadedLibrary.Item2;
                }

                string prefix =
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? string.Empty :
                    "lib";

                string? extension =
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ".so" :
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" :
                    null;

                string fileName = $"{prefix}{unmanagedDllName}{extension}";
                if (LibGit2GitExtensions.FindLibGit2NativeBinaries(this.nativeDependencyBasePath) is string directoryPath)
                {
                    if (!NativeLibrary.TryLoad(Path.Combine(directoryPath, fileName), out p))
                    {
                        // Fall back to any libgit2 binary in the directory. This covers hash
                        // mismatches between LibGit2Sharp and LibGit2Sharp.NativeBinaries.
                        string? nativeLibraryPath = Directory.EnumerateFiles(directoryPath)
                            .FirstOrDefault(static path => IsLibGit2LibraryName(Path.GetFileName(path)));
                        if (nativeLibraryPath is not null)
                        {
                            NativeLibrary.TryLoad(nativeLibraryPath, out p);
                        }
                    }

                    if (p != IntPtr.Zero)
                    {
                        // Cache this to make us a little faster next time.
                        this.lastLoadedLibrary = (unmanagedDllName, p);
                    }
                }
            }

            return p;
        }

        /// <summary>
        /// Returns whether <paramref name="name"/> refers to a libgit2 native library.
        /// </summary>
        /// <param name="name">An unmanaged library name or file name.</param>
        /// <returns><see langword="true"/> if the name is a libgit2 native library; otherwise <see langword="false"/>.</returns>
        private static bool IsLibGit2LibraryName(string name)
        {
            // Strip directory if present.
            name = Path.GetFileName(name);

            // Strip common native library extensions.
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }

            // Unix DllImport names may include the "lib" prefix (e.g. libgit2-5853918).
            if (name.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(3);
            }

            // LibGit2Sharp uses hashed names like "git2-5853918" (and historically "git2").
            return name.StartsWith("git2", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
