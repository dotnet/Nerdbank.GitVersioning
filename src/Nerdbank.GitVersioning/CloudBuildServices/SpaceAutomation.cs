// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// SpaceAutomation CI build support.
/// </summary>
/// <remarks>
/// The SpaceAutomation-specific properties referenced here are <see href="https://www.jetbrains.com/help/space/automation-environment-variables.html">documented here</see>.
/// </remarks>
internal class SpaceAutomation : ICloudBuild
{
    /// <inheritdoc/>
    public string BuildingBranch => CloudBuild.IfStartsWith(BuildingRef, "refs/heads/");

    /// <inheritdoc/>
    public string BuildingTag => CloudBuild.IfStartsWith(BuildingRef, "refs/tags/");

    /// <inheritdoc/>
    public string GitCommitId => Environment.GetEnvironmentVariable("JB_SPACE_GIT_REVISION");

    /// <inheritdoc/>
    public bool IsApplicable => this.GitCommitId is not null;

    /// <inheritdoc/>
    public bool IsPullRequest => false;

    private static string BuildingRef => Environment.GetEnvironmentVariable("JB_SPACE_GIT_BRANCH");

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
