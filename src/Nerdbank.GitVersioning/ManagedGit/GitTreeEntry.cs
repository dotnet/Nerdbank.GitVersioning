// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Represents an individual entry in the Git tree.
/// </summary>
public class GitTreeEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitTreeEntry"/> class.
    /// </summary>
    /// <param name="name">
    /// The name of the entry.
    /// </param>
    /// <param name="isFile">
    /// A vaolue indicating whether the current entry is a file.
    /// </param>
    /// <param name="sha">
    /// The Git object Id of the blob or tree of the current entry.
    /// </param>
    public GitTreeEntry(string name, bool isFile, GitObjectId sha)
    {
        this.Name = name;
        this.IsFile = isFile;
        this.Sha = sha;
    }

    /// <summary>
    /// Gets the name of the entry.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the current entry is a file.
    /// </summary>
    public bool IsFile { get; }

    /// <summary>
    /// Gets the Git object Id of the blob or tree of the current entry.
    /// </summary>
    public GitObjectId Sha { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.Name;
    }
}
