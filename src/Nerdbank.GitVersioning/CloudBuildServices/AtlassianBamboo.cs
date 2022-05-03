// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for Atlassian Bamboo.
/// </summary>
/// <remarks>
/// The Bamboo-specific properties referenced here are <see href="https://confluence.atlassian.com/bamboo/bamboo-variables-289277087.html">documented here</see>.
/// </remarks>
internal class AtlassianBamboo : ICloudBuild
{
    /// <inheritdoc/>
    public bool IsPullRequest => false;

    /// <inheritdoc/>
    public string BuildingTag => null;

    /// <inheritdoc/>
    public string BuildingBranch => CloudBuild.ShouldStartWith(Environment.GetEnvironmentVariable("bamboo.planRepository.branch"), "refs/heads/");

    public string BuildingRef => this.BuildingBranch;

    /// <inheritdoc/>
    public string GitCommitId => Environment.GetEnvironmentVariable("bamboo.planRepository.revision");

    /// <inheritdoc/>
    public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("bamboo.buildKey"));

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
