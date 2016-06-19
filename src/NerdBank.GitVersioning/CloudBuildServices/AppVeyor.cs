namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// The AppVeyor-specific properties referenced here are documented here:
    /// http://www.appveyor.com/docs/environment-variables 
    /// </remarks>
    internal class AppVeyor : ICloudBuild
    {
        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// AppVeyor's branch variable is the target branch of a PR, which is *NOT* to be misinterpreted 
        /// as building the target branch itself. So only set the branch built property if it's not a PR.
        /// </remarks>
        public string BuildingBranch => !this.IsPullRequest && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR_REPO_BRANCH"))
            ? $"refs/heads/{Environment.GetEnvironmentVariable("APPVEYOR_REPO_BRANCH")}"
            : null;

        public string BuildingRef => null;

        public string BuildingTag => string.Equals("true", Environment.GetEnvironmentVariable("APPVEYOR_REPO_TAG"), StringComparison.OrdinalIgnoreCase)
            ? $"refs/tags/{Environment.GetEnvironmentVariable("APPVEYOR_REPO_TAG_NAME")}"
            : null;

        public string GitCommitId => null;

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR"));

        public bool IsPullRequest => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR_PULL_REQUEST_NUMBER"));

        public void SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            // We ignore exit code so as to not fail the build when the cloud build number is not unique.
            RunAppveyor($"UpdateBuild -Version \"{buildNumber}\"", stdout, stderr);
        }

        public void SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            RunAppveyor($"SetVariable -Name {name} -Value \"{value}\"", stdout, stderr);
        }

        private static void RunAppveyor(string args, TextWriter stdout, TextWriter stderr)
        {
            try
            {
                Process.Start("appveyor", args)
                    .WaitForExit();
            }
            catch (Win32Exception ex) when ((uint)ex.HResult == 0x80004005)
            {
                (stderr ?? Console.Error).WriteLine("Could not find appveyor tool to set cloud build variable.");
            }
        }
    }
}
