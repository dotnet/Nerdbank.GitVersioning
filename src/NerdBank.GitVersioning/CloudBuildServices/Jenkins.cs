namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <remarks>
    /// The Jenkins-specific properties referenced here are documented here:
    /// https://wiki.jenkins-ci.org/display/JENKINS/Git+Plugin#GitPlugin-Environmentvariables
    /// </remarks>
    internal class Jenkins : ICloudBuild
    {
        private static readonly Encoding UTF8NoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public string BuildingTag => null;

        public bool IsPullRequest => false;

        public string BuildingBranch => CloudBuild.ShouldStartWith(Branch, "refs/heads/");

        public string GitCommitId => Environment.GetEnvironmentVariable("GIT_COMMIT");

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL"));

        private static string Branch =>
            Environment.GetEnvironmentVariable("GIT_LOCAL_BRANCH")
                ?? Environment.GetEnvironmentVariable("GIT_BRANCH");

        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            WriteVersionFile(buildNumber);

            stdout.WriteLine($"## GIT_VERSION: {buildNumber}");

            return new Dictionary<string, string>
            {
                { "GIT_VERSION", buildNumber }
            };
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>
            {
                { name, value }
            };
        }

        private static void WriteVersionFile(string buildNumber)
        {
            var workspacePath = Environment.GetEnvironmentVariable("WORKSPACE");

            if (string.IsNullOrEmpty(workspacePath))
            {
                return;
            }

            var versionFilePath = Path.Combine(workspacePath, "jenkins_build_number.txt");

            Utilities.FileOperationWithRetry(() => File.WriteAllText(versionFilePath, buildNumber, UTF8NoBOM));
        }
    }
}
