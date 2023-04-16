// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// This code originally copied from https://github.com/dotnet/sourcelink/tree/c092238370e0437eb95722f28c79273244dc7f1a/src/Microsoft.Build.Tasks.Git
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See license information at https://github.com/dotnet/sourcelink/blob/c092238370e0437eb95722f28c79273244dc7f1a/License.txt.
#nullable enable

#if NETCOREAPP

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Nerdbank.GitVersioning.LibGit2;
using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

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
                string? directoryPath = LibGit2GitExtensions.FindLibGit2NativeBinaries(this.nativeDependencyBasePath);
                if (directoryPath is not null && NativeLibrary.TryLoad(Path.Combine(directoryPath, fileName), out p))
                {
                    // Cache this to make us a little faster next time.
                    this.lastLoadedLibrary = (unmanagedDllName, p);
                }
            }

            return p;
        }
    }
}
#endif
