// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Identifies the hash algorithm / object format used by a Git repository.
/// </summary>
public enum GitObjectFormat
{
    /// <summary>
    /// SHA-1 object format (20-byte / 40-hex-character object IDs).
    /// </summary>
    Sha1 = 0,

    /// <summary>
    /// SHA-256 object format (32-byte / 64-hex-character object IDs).
    /// </summary>
    Sha256 = 1,
}
