namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// TeamCity CI build support.
    /// </summary>
    /// <remarks>
    /// The TeamCIty-specific properties referenced here are documented here:
    /// https://confluence.jetbrains.com/display/TCD8/Predefined+Build+Parameters
    /// </remarks>
    internal class TeamCity : ICloudBuild
    {
        public string BuildingBranch => CloudBuild.IfStartsWith(BuildingRef, "refs/heads/");

        public string BuildingTag => CloudBuild.IfStartsWith(BuildingRef, "refs/tags/");

        public string GitCommitId => Environment.GetEnvironmentVariable("BUILD_VCS_NUMBER");

        public bool IsApplicable => this.GitCommitId != null;

        public bool IsPullRequest => false;

        private static string BuildingRef => Environment.GetEnvironmentVariable("BUILD_GIT_BRANCH");

        public IReadOnlyDictionary<string, string>  SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            (stdout ?? Console.Out).WriteLine($"##teamcity[buildNumber '{buildNumber}']");
            return new Dictionary<string, string>();
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            (stdout ?? Console.Out).WriteLine($"##teamcity[setParameter name='{name}' value='{value}']");
            (stdout ?? Console.Out).WriteLine($"##teamcity[setParameter name='system.{name}' value='{value}']");

            return new Dictionary<string, string>();
        }
    }
}
