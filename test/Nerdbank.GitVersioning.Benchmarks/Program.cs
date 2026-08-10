// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Nerdbank.GitVersioning.Benchmarks
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
                .AddJob(Job.Default.WithRuntime(CoreRuntime.Core90).AsBaseline());
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                config.AddJob(Job.Default.WithRuntime(ClrRuntime.Net472));
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
