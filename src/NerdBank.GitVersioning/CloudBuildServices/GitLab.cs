namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// The GitLab-specific properties referenced here are documented here:
    /// https://docs.gitlab.com/ce/ci/variables/README.html
    /// </remarks>
    internal class GitLab : ICloudBuild
    {
        public string BuildingBranch => Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME");

        public string BuildingRef => Environment.GetEnvironmentVariable("CI_COMMIT_TAG");

        public string BuildingTag => Environment.GetEnvironmentVariable("CI_COMMIT_TAG");

        public string GitCommitId => Environment.GetEnvironmentVariable("CI_COMMIT_SHA");

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI"));

        public bool IsPullRequest => false;

        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            // Gitlab uses env vars for conveying build parameters
            Environment.SetEnvironmentVariable(name, value);

            // Log this var to the console to assist CI debug
            (stdout ?? Console.Out).WriteLine($"##Cloud Build Variable name={name}, value={value}");

            // Return the pair as our result
            return new Dictionary<string, string>
            {
                { name, value }
            };
        }
    }
}
