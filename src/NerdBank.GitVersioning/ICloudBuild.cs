// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning;

/// <summary>
/// Defines cloud build provider functionality.
/// </summary>
public interface ICloudBuild
{
    /// <summary>
    /// Gets a value indicating whether the active cloud build matches what this instance supports.
    /// </summary>
    bool IsApplicable { get; }

    /// <summary>
    /// Gets a value indicating whether a cloud build is validating a pull request.
    /// </summary>
    bool IsPullRequest { get; }

    /// <summary>
    /// Gets the branch being built by a cloud build, if applicable.
    /// </summary>
    string? BuildingBranch { get; }

    /// <summary>
    /// Gets the tag being built by a cloud build, if applicable.
    /// </summary>
    string? BuildingTag { get; }

    /// <summary>
    /// Gets the git commit ID being built by a cloud build, if applicable.
    /// </summary>
    string? GitCommitId { get; }

    /// <summary>
    /// Sets the build number for the cloud build, if supported.
    /// </summary>
    /// <param name="buildNumber">The build number to set.</param>
    /// <param name="stdout">An optional redirection for what should be written to the standard out stream.</param>
    /// <param name="stderr">An optional redirection for what should be written to the standard error stream.</param>
    /// <returns>A dictionary of environment/build variables that the caller should set to update the environment to match the new settings.</returns>
    IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter? stdout, TextWriter? stderr);

    /// <summary>
    /// Sets a cloud build variable, if supported.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="value">The value for the variable.</param>
    /// <param name="stdout">An optional redirection for what should be written to the standard out stream.</param>
    /// <param name="stderr">An optional redirection for what should be written to the standard error stream.</param>
    /// <returns>A dictionary of environment/build variables that the caller should set to update the environment to match the new settings.</returns>
    IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter? stdout, TextWriter? stderr);
}
