// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BenchmarkDotNet.Running;

namespace Nerdbank.GitVersioning.Benchmarks
{
    internal class Program
    {
        private static void Main(string[] args) =>
                   BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
