namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Travis CI build support.
    /// </summary>
    /// <remarks>
    /// The Travis CI environment variables referenced here are documented here:
    /// https://docs.travis-ci.com/user/environment-variables/#default-environment-variables
    /// </remarks>
    internal class Travis: ICloudBuild
    {
        // TRAVIS_BRANCH can reference a branch or a tag, so make sure it starts with refs/heads
        public string BuildingBranch => CloudBuild.ShouldStartWith(Environment.GetEnvironmentVariable("TRAVIS_BRANCH"), "refs/heads/");

        public string BuildingTag => Environment.GetEnvironmentVariable("TRAVIS_TAG");

        public string GitCommitId => Environment.GetEnvironmentVariable("TRAVIS_COMMIT");

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS"));

        public bool IsPullRequest => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS_PULL_REQUEST_BRANCH"));

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
