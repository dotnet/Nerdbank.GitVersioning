using System;
using System.IO;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Nerdbank.GitVersioning.Managed;

namespace Nerdbank.GitVersioning.Benchmarks
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31, baseline: true)]
    [SimpleJob(RuntimeMoniker.NetCoreApp21)]
    [SimpleJob(RuntimeMoniker.Net461)]
    public class GetVersionBenchmarks
    {
        // You must manually clone these repositories:
        // - On Windows, to %USERPROFILE%\Source\Repose
        // - On Unix, to ~/git/
        [Params(
            "xunit",
            "Cuemon",
            "SuperSocket",
            "NerdBank.GitVersioning")]
        public string ProjectDirectory;

        public Version Version { get; set; }

        [Benchmark(Baseline = true)]
        public void GetVersionLibGit2()
        {
            var oracle = LibGit2VersionOracle.CreateLibGit2(GetPath(this.ProjectDirectory));
            this.Version = oracle.Version;
        }

        [Benchmark]
        public void GetVersionManaged()
        {
            var oracle = ManagedVersionOracle.CreateManaged(GetPath(this.ProjectDirectory));
            this.Version = oracle.Version;
        }

        private static string GetPath(string repositoryName)
        {
            string path = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    @"Source\Repos",
                    repositoryName);
            }
            else
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    @"git",
                    repositoryName);
            }

            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"The directory '{path}' could not be found");
            }

            return path;
        }
    }
}
