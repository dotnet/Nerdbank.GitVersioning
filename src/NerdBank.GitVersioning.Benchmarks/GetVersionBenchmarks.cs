using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Nerdbank.GitVersioning;
using NerdBank.GitVersioning.Managed;

namespace NerdBank.GitVersioning.Benchmarks
{
    public class GetVersionBenchmarks
    {
        [Params(
            "xunit;version.json",
            "Cuemon;version.json",
            "SuperSocket;version.json",
            "NerdBank.GitVersioning;version.json")]
        public string TestData;

        public string RepositoryName => this.TestData.Split(';')[0];

        public string VersionPath => this.TestData.Split(';')[1];

        public string RepositoryPath => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"Source\Repos",
                this.RepositoryName);

        public Version Version
        { get; set; }

        [Benchmark(Baseline = true)]
        public void GetVersionLibGit2()
        {
            var oracle = LibGit2VersionOracle.CreateLibGit2(this.RepositoryPath);
            this.Version = oracle.Version;
        }

        [Benchmark]
        public void GetVersionManaged()
        {
            var oracle = ManagedVersionOracle.CreateManaged(this.RepositoryPath);
            this.Version = oracle.Version;
        }
    }
}
