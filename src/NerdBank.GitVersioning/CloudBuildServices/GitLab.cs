// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for GitLab.
/// </summary>
/// <remarks>
/// The GitLab-specific properties referenced here are <see href="https://docs.gitlab.com/ce/ci/variables/README.html">documented here</see>.
/// </remarks>
internal class GitLab : ICloudBuild
{
    /// <inheritdoc/>
    public string BuildingBranch =>
        Environment.GetEnvironmentVariable("CI_COMMIT_TAG") is null ?
            $"refs/heads/{Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME")}" : null;

    public string BuildingRef => this.BuildingBranch ?? this.BuildingTag;

    /// <inheritdoc/>
    public string BuildingTag =>
        Environment.GetEnvironmentVariable("CI_COMMIT_TAG") is not null ?
            $"refs/tags/{Environment.GetEnvironmentVariable("CI_COMMIT_TAG")}" : null;

    /// <inheritdoc/>
    public string GitCommitId => Environment.GetEnvironmentVariable("CI_COMMIT_SHA");

    /// <inheritdoc/>
    public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI"));

    /// <inheritdoc/>
    public bool IsPullRequest => false;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        return new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        return new Dictionary<string, string>();
    }
}
