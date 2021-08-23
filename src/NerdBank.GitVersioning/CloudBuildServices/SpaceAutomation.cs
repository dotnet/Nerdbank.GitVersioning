using System;
using System.Collections.Generic;
using System.IO;

namespace Nerdbank.GitVersioning.CloudBuildServices
{
    /// <summary>
    /// SpaceAutomation CI build support.
    /// </summary>
    /// <remarks>
    /// The SpaceAutomation-specific properties referenced here are documented here:
    /// https://www.jetbrains.com/help/space/automation-environment-variables.html
    /// </remarks>
    internal class SpaceAutomation : ICloudBuild
    {
        public string BuildingBranch => CloudBuild.IfStartsWith(BuildingRef, "refs/heads/");

        public string BuildingTag => CloudBuild.IfStartsWith(BuildingRef, "refs/tags/");

        public string GitCommitId => Environment.GetEnvironmentVariable("JB_SPACE_GIT_REVISION");

        public bool IsApplicable => this.GitCommitId is not null;

        public bool IsPullRequest => false;

        private static string BuildingRef => Environment.GetEnvironmentVariable("JB_SPACE_GIT_BRANCH");

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