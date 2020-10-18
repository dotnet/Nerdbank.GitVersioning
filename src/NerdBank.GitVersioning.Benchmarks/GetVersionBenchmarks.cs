using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Nerdbank.GitVersioning;

namespace NerdBank.GitVersioning.Benchmarks
{
    class GetVersionBenchmarks
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

        [Benchmark]
        public void GetversionLibGit2()
        {
            var oracleA = VersionOracle.Create(this.RepositoryPath, Path.GetDirectoryName(this.VersionPath));
            this.Version = oracleA.Version;
        }
    }
}
