// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Nerdbank.GitVersioning.Benchmarks
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31, baseline: true)]
    [SimpleJob(RuntimeMoniker.Net60)]
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
            "Nerdbank.GitVersioning")]
        public string ProjectDirectory;

        public Version Version { get; set; }

        [Benchmark(Baseline = true)]
        public void GetVersionLibGit2()
        {
            using var context = GitContext.Create(GetPath(this.ProjectDirectory), writable: true);
            var oracle = new VersionOracle(context, cloudBuild: null);
            this.Version = oracle.Version;
        }

        [Benchmark]
        public void GetVersionManaged()
        {
            using var context = GitContext.Create(GetPath(this.ProjectDirectory), writable: false);
            var oracle = new VersionOracle(context, cloudBuild: null);
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
