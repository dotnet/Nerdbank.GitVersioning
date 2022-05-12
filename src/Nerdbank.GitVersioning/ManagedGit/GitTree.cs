// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Represents a git tree.
/// </summary>
public class GitTree
{
    /// <summary>
    /// Gets an empty <see cref="GitTree"/>.
    /// </summary>
    public static GitTree Empty { get; } = new GitTree();

    /// <summary>
    /// Gets or sets the Git object Id of this <see cref="GitObjectId"/>.
    /// </summary>
    public GitObjectId Sha { get; set; }

    /// <summary>
    /// Gets a dictionary which contains all entries in the current tree, accessible by name.
    /// </summary>
    public Dictionary<string, GitTreeEntry> Children { get; } = new Dictionary<string, GitTreeEntry>();

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Git tree: {this.Sha}";
    }
}
