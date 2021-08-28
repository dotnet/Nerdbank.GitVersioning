namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
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
        public string BuildingBranch =>
            Environment.GetEnvironmentVariable("CI_COMMIT_TAG") is null ? 
                $"refs/heads/{Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME")}" : null;

        public string BuildingRef => this.BuildingBranch ?? this.BuildingTag;

        public string BuildingTag =>
            Environment.GetEnvironmentVariable("CI_COMMIT_TAG") is not null ?
                $"refs/tags/{Environment.GetEnvironmentVariable("CI_COMMIT_TAG")}" : null;

        public string GitCommitId => Environment.GetEnvironmentVariable("CI_COMMIT_SHA");

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI"));

        public bool IsPullRequest => false;

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
