namespace NerdBank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Nerdbank.GitVersioning;

    internal class GitHubActions : ICloudBuild
    {
        public bool IsApplicable => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        public bool IsPullRequest => Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") == "PullRequestEvent";

        public string BuildingBranch => (BuildingRef?.StartsWith("refs/heads/") ?? false) ? BuildingRef : null;

        public string BuildingTag => (BuildingRef?.StartsWith("refs/tags/") ?? false) ? BuildingRef : null;

        public string GitCommitId => Environment.GetEnvironmentVariable("GITHUB_SHA");

        private static string BuildingRef => Environment.GetEnvironmentVariable("GITHUB_REF");

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
