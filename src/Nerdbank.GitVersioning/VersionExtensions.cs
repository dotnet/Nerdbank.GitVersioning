// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Validation;

namespace Nerdbank.GitVersioning;

/// <summary>
/// Extension methods for the <see cref="Version"/> class.
/// </summary>
public static class VersionExtensions
{
    /// <summary>
    /// Returns a <see cref="Version"/> instance where the specified number of components
    /// are guaranteed to be non-negative. Any applicable negative components are converted to zeros.
    /// </summary>
    /// <param name="version">The version to use as a template for the returned value.</param>
    /// <param name="fieldCount">The number of version components to ensure are non-negative.</param>
    /// <returns>
    /// The same as <paramref name="version"/> except with any applicable negative values
    /// translated to zeros.
    /// </returns>
    public static Version EnsureNonNegativeComponents(this Version version, int fieldCount = 4)
    {
        Requires.NotNull(version, nameof(version));
        Requires.Range(fieldCount >= 0 && fieldCount <= 4, nameof(fieldCount));

        int maj = fieldCount >= 1 ? Math.Max(0, version.Major) : version.Major;
        int min = fieldCount >= 2 ? Math.Max(0, version.Minor) : version.Minor;
        int bld = fieldCount >= 3 ? Math.Max(0, version.Build) : version.Build;
        int rev = fieldCount >= 4 ? Math.Max(0, version.Revision) : version.Revision;

        if (version.Major == maj &&
            version.Minor == min &&
            version.Build == bld &&
            version.Revision == rev)
        {
            return version;
        }

        if (rev >= 0)
        {
            return new Version(maj, min, bld, rev);
        }
        else if (bld >= 0)
        {
            return new Version(maj, min, bld);
        }
        else
        {
            throw Assumes.NotReachable();
        }
    }

    /// <summary>
    /// Converts the value of the current System.Version object to its equivalent System.String
    /// representation. A specified count indicates the number of components to return.
    /// </summary>
    /// <param name="version">The instance to serialize as a string.</param>
    /// <param name="fieldCount">The number of components to return. The fieldCount ranges from 0 to 4.</param>
    /// <returns>
    /// The System.String representation of the values of the major, minor, build, and
    /// revision components of the current System.Version object, each separated by a
    /// period character ('.'). The fieldCount parameter determines how many components
    /// are returned.fieldCount Return Value 0 An empty string (""). 1 major 2 major.minor
    /// 3 major.minor.build 4 major.minor.build.revision For example, if you create System.Version
    /// object using the constructor Version(1,3,5), ToString(2) returns "1.3" and ToString(4)
    /// returns "1.3.5.0".
    /// </returns>
    public static string ToStringSafe(this Version version, int fieldCount)
    {
        return version.EnsureNonNegativeComponents(fieldCount).ToString(fieldCount);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Version"/> class,
    /// allowing for the last two integers to possibly be -1.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="build">The build version.</param>
    /// <param name="revision">The revision.</param>
    /// <returns>The newly created <see cref="Version"/>.</returns>
    internal static Version Create(int major, int minor, int build, int revision)
    {
        return
            build == -1 ? new Version(major, minor) :
            revision == -1 ? new Version(major, minor, build) :
            new Version(major, minor, build, revision);
    }
}
