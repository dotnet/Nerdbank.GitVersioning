// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Enumerates the various instruction types which can be found in a deltafied stream.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format#_deltified_representation"/>
public enum DeltaInstructionType
{
    /// <summary>
    /// Instructs the caller to insert a new byte range into the object.
    /// </summary>
    Insert = 0,

    /// <summary>
    /// Instructs the caller to copy a byte range from the source object.
    /// </summary>
    Copy = 1,
}
