// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Diagnostics;

namespace Nerdbank.GitVersioning.CloudBuildServices;

/// <summary>
/// Cloud build handling for AppVeyor.
/// </summary>
/// <remarks>
/// The AppVeyor-specific properties referenced here are <see href="http://www.appveyor.com/docs/environment-variables">documented here</see>.
/// </remarks>
internal class AppVeyor : ICloudBuild
{
    /// <inheritdoc/>
    /// <remarks>
    /// AppVeyor's branch variable is the target branch of a PR, which is *NOT* to be misinterpreted
    /// as building the target branch itself. So only set the branch built property if it's not a PR.
    /// </remarks>
    public string BuildingBranch => !this.IsPullRequest && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR_REPO_BRANCH"))
        ? $"refs/heads/{Environment.GetEnvironmentVariable("APPVEYOR_REPO_BRANCH")}"
        : null;

    public string BuildingRef => null;

    /// <inheritdoc/>
    public string BuildingTag => string.Equals("true", Environment.GetEnvironmentVariable("APPVEYOR_REPO_TAG"), StringComparison.OrdinalIgnoreCase)
        ? $"refs/tags/{Environment.GetEnvironmentVariable("APPVEYOR_REPO_TAG_NAME")}"
        : null;

    /// <inheritdoc/>
    public string GitCommitId => null;

    /// <inheritdoc/>
    public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR"));

    /// <inheritdoc/>
    public bool IsPullRequest => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR_PULL_REQUEST_NUMBER"));

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        // We ignore exit code so as to not fail the build when the cloud build number is not unique.
        RunAppveyor($"UpdateBuild -Version \"{buildNumber}\"", stdout, stderr);
        return new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        RunAppveyor($"SetVariable -Name {name} -Value \"{value}\"", stdout, stderr);
        return new Dictionary<string, string>();
    }

    private static void RunAppveyor(string args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            // Skip this if this build is running in our own unit tests, since that can
            // mess with AppVeyor's actual build information.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_NBGV_UnitTest")))
            {
                Process.Start("appveyor", args)
                    .WaitForExit();
            }
        }
        catch (Win32Exception ex) when ((uint)ex.HResult == 0x80004005)
        {
            (stderr ?? Console.Error).WriteLine("Could not find appveyor tool to set cloud build variable.");
        }
    }
}
