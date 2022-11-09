// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// This code originally copied from https://github.com/dotnet/sourcelink/tree/c092238370e0437eb95722f28c79273244dc7f1a/src/Microsoft.Build.Tasks.Git
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See license information at https://github.com/dotnet/sourcelink/blob/c092238370e0437eb95722f28c79273244dc7f1a/License.txt.
#if NETCOREAPP

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

namespace Nerdbank.GitVersioning
{
    public class GitLoaderContext : AssemblyLoadContext
    {
        public const string RuntimePath = "./runtimes";

        public static readonly GitLoaderContext Instance = new GitLoaderContext();

        /// <inheritdoc/>
        protected override Assembly Load(AssemblyName assemblyName)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(GitLoaderContext).Assembly.Location), assemblyName.Name + ".dll");
            return File.Exists(path)
                ? this.LoadFromAssemblyPath(path)
                : Default.LoadFromAssemblyName(assemblyName);
        }
    }
}
#endif
