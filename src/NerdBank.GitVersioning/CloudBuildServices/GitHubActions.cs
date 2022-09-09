// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.CloudBuildServices;

internal class GitHubActions : ICloudBuild
{
    /// <inheritdoc/>
    public bool IsApplicable => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <inheritdoc/>
    public bool IsPullRequest => Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") == "PullRequestEvent";

    /// <inheritdoc/>
    public string BuildingBranch => (BuildingRef?.StartsWith("refs/heads/") ?? false) ? BuildingRef : null;

    /// <inheritdoc/>
    public string BuildingTag => (BuildingRef?.StartsWith("refs/tags/") ?? false) ? BuildingRef : null;

    /// <inheritdoc/>
    public string GitCommitId => IgnoreGitHubRef ? null : Environment.GetEnvironmentVariable("GITHUB_SHA");

    private static string BuildingRef => IgnoreGitHubRef ? null : Environment.GetEnvironmentVariable("GITHUB_REF");

    /// <summary>
    /// Gets a value indicating whether to ignore GitHub Actions environment variables that indicate where HEAD is.
    /// </summary>
    /// <remarks>
    /// This is useful in a GitHub workflow where HEAD was moved by some prior Action, such that the environment variables are stale.
    /// GitHub Actions does not allow these env vars to be changed mid-workflow, so in such cases NB.GV should just use HEAD.
    /// </remarks>
    private static bool IgnoreGitHubRef => string.Equals(Environment.GetEnvironmentVariable("IGNORE_GITHUB_REF"), "true", StringComparison.OrdinalIgnoreCase);

    private static string EnvironmentFile => Environment.GetEnvironmentVariable("GITHUB_ENV");

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        return new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        Utilities.FileOperationWithRetry(() => File.AppendAllLines(EnvironmentFile, new[] { $"{name}={value}" }));
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
