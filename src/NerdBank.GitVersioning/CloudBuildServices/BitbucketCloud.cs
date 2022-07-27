﻿namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for Bitbucket Cloud.
/// </summary>
/// <remarks>
/// The Bitbucket-specific properties referenced here are <see href="https://support.atlassian.com/bitbucket-cloud/docs/variables-and-secrets/">documented here</see>.
/// </remarks>
public class BitbucketCloud : ICloudBuild
{
    /// <inheritdoc />
    public bool IsApplicable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITBUCKET_REPO_SLUG"));

    /// <inheritdoc />
    public bool IsPullRequest => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BITBUCKET_PR_ID"));

    /// <inheritdoc />
    public string BuildingBranch => Environment.GetEnvironmentVariable("BITBUCKET_BRANCH");

    /// <inheritdoc />
    public string BuildingTag => Environment.GetEnvironmentVariable("BITBUCKET_TAG");

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
        return new Dictionary<string, string>();
    }
}
