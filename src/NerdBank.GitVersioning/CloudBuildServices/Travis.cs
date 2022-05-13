// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Travis CI build support.
/// </summary>
/// <remarks>
/// The Travis CI environment variables referenced here are <see href="https://docs.travis-ci.com/user/environment-variables/#default-environment-variables">documented here</see>.
/// </remarks>
internal class Travis : ICloudBuild
{
    // TRAVIS_BRANCH can reference a branch or a tag, so make sure it starts with refs/heads

    /// <inheritdoc/>
    public string BuildingBranch => CloudBuild.ShouldStartWith(Environment.GetEnvironmentVariable("TRAVIS_BRANCH"), "refs/heads/");

    /// <inheritdoc/>
    public string BuildingTag => Environment.GetEnvironmentVariable("TRAVIS_TAG");

    /// <inheritdoc/>
    public string GitCommitId => Environment.GetEnvironmentVariable("TRAVIS_COMMIT");

    /// <inheritdoc/>
    public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS"));

    /// <inheritdoc/>
    public bool IsPullRequest => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS_PULL_REQUEST_BRANCH"));

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
