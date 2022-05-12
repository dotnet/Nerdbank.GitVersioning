// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1403 // File may only contain a single namespace
#pragma warning disable SA1649 // File name should match first type name

#if NETFRAMEWORK

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace Nerdbank.GitVersioning.Tasks
{
    internal static class AssemblyLoader
    {
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
        [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
        internal static void LoaderInitializer()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var required = new AssemblyName(args.Name);
                string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), required.Name + ".dll");
                if (File.Exists(path))
                {
                    AssemblyName actual = AssemblyName.GetAssemblyName(path);
                    if (actual.Version >= required.Version)
                    {
                        return Assembly.LoadFile(path);
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}

#endif
