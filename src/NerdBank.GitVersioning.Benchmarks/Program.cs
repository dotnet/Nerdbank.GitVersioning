using System;
using BenchmarkDotNet.Running;

namespace Nerdbank.GitVersioning.Benchmarks
{
    class Program
    {
        static void Main(string[] args) =>
                   BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
