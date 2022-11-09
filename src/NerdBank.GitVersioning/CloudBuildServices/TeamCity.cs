// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// TeamCity CI build support.
/// </summary>
/// <remarks>
/// The TeamCity-specific properties referenced here are <see href="https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html">documented here</see>.
/// </remarks>
internal class TeamCity : ICloudBuild
{
    /// <inheritdoc/>
    public string BuildingBranch => CloudBuild.IfStartsWith(BuildingRef, "refs/heads/");

    /// <inheritdoc/>
    public string BuildingTag => CloudBuild.IfStartsWith(BuildingRef, "refs/tags/");

    /// <inheritdoc/>
    public string GitCommitId => Environment.GetEnvironmentVariable("BUILD_VCS_NUMBER");

    /// <inheritdoc/>
    public bool IsApplicable => this.GitCommitId is not null;

    /// <inheritdoc/>
    public bool IsPullRequest => false;

    private static string BuildingRef => Environment.GetEnvironmentVariable("BUILD_GIT_BRANCH");

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        (stdout ?? Console.Out).WriteLine($"##teamcity[buildNumber '{buildNumber}']");
        return new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        (stdout ?? Console.Out).WriteLine($"##teamcity[setParameter name='{name}' value='{value}']");
        (stdout ?? Console.Out).WriteLine($"##teamcity[setParameter name='system.{name}' value='{value}']");

        return new Dictionary<string, string>();
    }
}
