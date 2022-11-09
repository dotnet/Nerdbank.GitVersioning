// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text.RegularExpressions;
using Validation;

namespace Nerdbank.GitVersioning;

/// <summary>
/// Extension methods for <see cref="SemanticVersion"/>.
/// </summary>
internal static class SemanticVersionExtensions
{
    /// <summary>
    /// A regex that matches on numeric identifiers for prerelease or build metadata.
    /// </summary>
    private static readonly Regex NumericIdentifierRegex = new Regex(@"(?<![\w-])(\d+)(?![\w-])");

    /// <summary>
    /// Gets a new semantic with the specified version component (major/minor) incremented.
    /// </summary>
    /// <param name="currentVersion">The version to increment.</param>
    /// <param name="increment">Specifies whether to increment the major or minor version.</param>
    /// <returns>Returns a new <see cref="SemanticVersion"/> object with either the major or minor version incremented by 1.</returns>
    internal static SemanticVersion Increment(this SemanticVersion currentVersion, VersionOptions.ReleaseVersionIncrement increment)
    {
        Requires.NotNull(currentVersion, nameof(currentVersion));
        Requires.Argument(
            increment != VersionOptions.ReleaseVersionIncrement.Build || currentVersion.Version.Build >= 0,
            nameof(increment),
            "Cannot apply version increment '{0}' to version '{1}'",
            increment,
            currentVersion);

        int major = currentVersion.Version.Major;
        int minor = currentVersion.Version.Minor;
        int build = currentVersion.Version.Build;

        switch (increment)
        {
            case VersionOptions.ReleaseVersionIncrement.Major:
                major += 1;
                minor = 0;
                build = 0;
                break;
            case VersionOptions.ReleaseVersionIncrement.Minor:
                minor += 1;
                build = 0;
                break;

            case VersionOptions.ReleaseVersionIncrement.Build:
                build += 1;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(increment));
        }

        // use the appropriate constructor for the new version object
        // depending on whether the current versions has 2, 3 or 4 segments
        Version newVersion;
        if (currentVersion.Version.Build >= 0 && currentVersion.Version.Revision > 0)
        {
            // 4 segment version
            newVersion = new Version(major, minor, build, 0);
        }
        else if (currentVersion.Version.Build >= 0)
        {
            // 3 segment version
            newVersion = new Version(major, minor, build);
        }
        else
        {
            // 2 segment version
            newVersion = new Version(major, minor);
        }

        return new SemanticVersion(newVersion, currentVersion.Prerelease, currentVersion.BuildMetadata);
    }

    /// <summary>
    /// Sets the first prerelease tag of the specified semantic version to the specified value.
    /// </summary>
    /// <param name="version">The version which's prerelease tag to modify.</param>
    /// <param name="newFirstTag">The new prerelease tag. The leading hyphen may be specified or omitted.</param>
    /// <returns>Returns a new instance of <see cref="SemanticVersion"/> with the updated prerelease tag.</returns>
    internal static SemanticVersion SetFirstPrereleaseTag(this SemanticVersion version, string newFirstTag)
    {
        Requires.NotNull(version, nameof(version));

        newFirstTag = newFirstTag ?? string.Empty;

        string preRelease;
        if (string.IsNullOrEmpty(version.Prerelease))
        {
            preRelease = newFirstTag;
        }
        else if (version.Prerelease.Contains("."))
        {
            preRelease = newFirstTag + version.Prerelease.Substring(version.Prerelease.IndexOf("."));
        }
        else
        {
            preRelease = newFirstTag;
        }

        if (!string.IsNullOrEmpty(preRelease) && !preRelease.StartsWith("-"))
        {
            preRelease = "-" + preRelease;
        }

        return new SemanticVersion(version.Version, preRelease, version.BuildMetadata);
    }

    /// <summary>
    /// Removes all prerelease tags from the semantic version.
    /// </summary>
    /// <param name="version">The version to remove the prerelease tags from.</param>
    /// <returns>Returns a new instance <see cref="SemanticVersion"/> which does not contain any prerelease tags.</returns>
    internal static SemanticVersion WithoutPrepreleaseTags(this SemanticVersion version)
    {
        return new SemanticVersion(version.Version, null, version.BuildMetadata);
    }

    /// <summary>
    /// Converts a semver 2 compliant "-beta.5" prerelease tag to a semver 1 compatible one.
    /// </summary>
    /// <param name="prerelease">The semver 2 prerelease tag, including its leading hyphen.</param>
    /// <param name="paddingSize">The minimum number of digits to use for any numeric identifier.</param>
    /// <returns>A semver 1 compliant prerelease tag. For example "-beta-0005".</returns>
    internal static string MakePrereleaseSemVer1Compliant(string prerelease, int paddingSize)
    {
        if (string.IsNullOrEmpty(prerelease))
        {
            return prerelease;
        }

        string paddingFormatter = "{0:" + new string('0', paddingSize) + "}";

        string semver1 = prerelease;

        // Identify numeric identifiers and pad them.
        Assumes.True(prerelease.StartsWith("-"));
        semver1 = "-" + NumericIdentifierRegex.Replace(semver1.Substring(1), m => string.Format(CultureInfo.InvariantCulture, paddingFormatter, int.Parse(m.Groups[1].Value)));

        semver1 = semver1.Replace('.', '-');

        return semver1;
    }
}
