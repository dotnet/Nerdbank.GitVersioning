// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for Bitbucket Cloud.
/// </summary>
/// <remarks>
/// The Bitbucket-specific properties referenced here are <see href="https://support.atlassian.com/bitbucket-cloud/docs/variables-and-secrets/">documented here</see>.
/// </remarks>
public class BitbucketCloud : ICloudBuild
{
    /// <inheritdoc />
    public bool IsApplicable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITBUCKET_PIPELINE_UUID")) &&
                                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITBUCKET_STEP_UUID")) &&
                                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITBUCKET_STEP_TRIGGERER_UUID"));

    /// <inheritdoc />
    public bool IsPullRequest => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITBUCKET_PR_ID"));

    /// <inheritdoc />
    public string BuildingBranch => CloudBuild.ShouldStartWith(Environment.GetEnvironmentVariable("BITBUCKET_BRANCH"), "refs/heads/");

    /// <inheritdoc />
    public string BuildingTag => CloudBuild.ShouldStartWith(Environment.GetEnvironmentVariable("BITBUCKET_TAG"), "refs/tags/");

    /// <inheritdoc />
    public string GitCommitId => Environment.GetEnvironmentVariable("BITBUCKET_COMMIT");

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        return new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        return SetCloudBuildVariable(name, value, stdout, stderr, isOutput: false);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr, bool isOutput)
    {
        // Bitbucket Cloud doesn't support output variables like Azure DevOps, so we ignore the isOutput parameter
        return new Dictionary<string, string>();
    }
}
