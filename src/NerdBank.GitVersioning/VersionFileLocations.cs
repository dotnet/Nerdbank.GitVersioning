// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning;

/// <summary>
/// Describes locations of version files found during a search.
/// </summary>
public struct VersionFileLocations
{
    /// <summary>Gets or sets the absolute path to the directory where the first non-inheriting version file was found, if any.</summary>
    public string? NonInheritingVersionDirectory { get; set; }

    /// <summary>Gets or sets the absolute path to the directory where the first version.json file with an actual version property set was found, if any.</summary>
    public string? VersionSpecifyingVersionDirectory { get; set; }
}
