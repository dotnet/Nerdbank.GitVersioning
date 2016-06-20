namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.IO;

    internal class TeamCity : ICloudBuild
    {
        public string BuildingBranch => null;

        public string BuildingTag => null;

        public string GitCommitId => Environment.GetEnvironmentVariable("BUILD_VCS_NUMBER");

        public bool IsApplicable => this.GitCommitId != null;

        public bool IsPullRequest => false;

        public void SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
        }

        public void SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
        }
    }
}
