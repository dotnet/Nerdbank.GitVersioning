namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// The Bamboo-specific properties referenced here are documented here:
    /// https://confluence.atlassian.com/bamboo/bamboo-variables-289277087.html
    /// </remarks>
    internal class AtlassianBamboo : ICloudBuild
    {
        public bool IsPullRequest => false; 

        public string BuildingTag => null;

        public string BuildingBranch => CloudBuild.ShouldStartWith(Environment.GetEnvironmentVariable("bamboo.planRepository.branch"), "refs/heads/");

        public string BuildingRef => this.BuildingBranch;

        public string GitCommitId => Environment.GetEnvironmentVariable("bamboo.planRepository.revision");

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("bamboo.buildKey"));

        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }
    }
}
