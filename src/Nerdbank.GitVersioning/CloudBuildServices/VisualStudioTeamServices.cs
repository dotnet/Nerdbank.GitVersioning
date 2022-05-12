// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for Azure DevOps.
/// </summary>
/// <remarks>
/// The VSTS-specific properties referenced here are <see href="https://msdn.microsoft.com/en-us/Library/vs/alm/Build/scripts/variables">documented here</see>.
/// </remarks>
internal class VisualStudioTeamServices : ICloudBuild
{
    /// <inheritdoc/>
    public bool IsPullRequest => BuildingRef?.StartsWith("refs/pull/") ?? false;

    /// <inheritdoc/>
    public string BuildingTag => CloudBuild.IfStartsWith(BuildingRef, "refs/tags/");

    /// <inheritdoc/>
    public string BuildingBranch => CloudBuild.IfStartsWith(BuildingRef, "refs/heads/");

    /// <inheritdoc/>
    public string GitCommitId => null;

    /// <inheritdoc/>
    public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECTID"));

    private static string BuildingRef => Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCH");

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        (stdout ?? Console.Out).WriteLine($"##vso[build.updatebuildnumber]{buildNumber}");
        return GetDictionaryFor("Build.BuildNumber", buildNumber);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        Utilities.FileOperationWithRetry(() =>
            (stdout ?? Console.Out).WriteLine($"##vso[task.setvariable variable={name};]{value}"));
        return GetDictionaryFor(name, value);
    }

    private static IReadOnlyDictionary<string, string> GetDictionaryFor(string variableName, string value)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { GetEnvironmentVariableNameForVariable(variableName), value },
        };
    }

    private static string GetEnvironmentVariableNameForVariable(string name) => name.ToUpperInvariant().Replace('.', '_');
}
