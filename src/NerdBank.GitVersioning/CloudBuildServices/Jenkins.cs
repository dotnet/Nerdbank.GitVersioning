// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for Jenkins.
/// </summary>
/// <remarks>
/// The Jenkins-specific properties referenced here are <see href="https://wiki.jenkins-ci.org/display/JENKINS/Git+Plugin#GitPlugin-Environmentvariables">documented here</see>.
/// </remarks>
internal class Jenkins : ICloudBuild
{
    private static readonly Encoding UTF8NoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public string BuildingTag => null;

    /// <inheritdoc/>
    public bool IsPullRequest => false;

    /// <inheritdoc/>
    public string BuildingBranch => CloudBuild.ShouldStartWith(Branch, "refs/heads/");

    /// <inheritdoc/>
    public string GitCommitId => Environment.GetEnvironmentVariable("GIT_COMMIT");

    /// <inheritdoc/>
    public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL"));

    private static string Branch =>
        Environment.GetEnvironmentVariable("GIT_LOCAL_BRANCH")
            ?? Environment.GetEnvironmentVariable("GIT_BRANCH");

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        WriteVersionFile(buildNumber);

        stdout.WriteLine($"## GIT_VERSION: {buildNumber}");

        return new Dictionary<string, string>
        {
            { "GIT_VERSION", buildNumber },
        };
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        return new Dictionary<string, string>
        {
            { name, value },
        };
    }

    private static void WriteVersionFile(string buildNumber)
    {
        string workspacePath = Environment.GetEnvironmentVariable("WORKSPACE");

        if (string.IsNullOrEmpty(workspacePath))
        {
            return;
        }

        string versionFilePath = Path.Combine(workspacePath, "jenkins_build_number.txt");

        Utilities.FileOperationWithRetry(() => File.WriteAllText(versionFilePath, buildNumber, UTF8NoBOM));
    }
}
