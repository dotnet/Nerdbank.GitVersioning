using System;
using BenchmarkDotNet.Running;

namespace NerdBank.GitVersioning.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<GetVersionBenchmarks>();
        }
    }
}
