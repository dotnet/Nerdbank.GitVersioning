// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning;

[Flags]
public enum VersionFileRequirements
{
    /// <summary>
    /// No flags. Default behavior.
    /// </summary>
    Default = 0x0,

    /// <summary>
    /// We want a version options object initialized with <em>only</em> one version.json file (the one that matches other requirements),
    /// rather than the merge the result of all relevant version.json files.
    /// </summary>
    NonMergedResult = 0x1,

    /// <summary>
    /// We require a version.json file that specifies a version (i.e. has a "version" property).
    /// </summary>
    VersionSpecified = 0x2,

    /// <summary>
    /// Stop search at the first version.json found, even if it inherits from another.
    /// </summary>
    AcceptInheritingFile = 0x4,
}
