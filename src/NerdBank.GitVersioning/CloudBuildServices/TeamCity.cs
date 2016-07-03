namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    internal class TeamCity : ICloudBuild
    {
        public string BuildingBranch => null;

        public string BuildingTag => null;

        public string GitCommitId => Environment.GetEnvironmentVariable("BUILD_VCS_NUMBER");

        public bool IsApplicable => this.GitCommitId != null;

        public bool IsPullRequest => false;

        public IReadOnlyDictionary<string, string>  SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }
    }
}
