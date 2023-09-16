// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Collections;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Represents a Git annotated tag, as stored in the Git object database.
/// </summary>
public struct GitAnnotatedTag : IEquatable<GitAnnotatedTag>
{
    /// <summary>
    /// Gets or sets the <see cref="GitObjectId"/> of object this tag is pointing at.
    /// </summary>
    public GitObjectId Object { get; set; }

    /// <summary>
    /// Gets or sets a <see cref="GitObjectId"/> which uniquely identifies the <see cref="GitAnnotatedTag"/>.
    /// </summary>
    public GitObjectId Sha { get; set; }

    /// <summary>
    /// Gets or sets the tag name of this annotated tag.
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    /// Gets or sets the type of the object this tag is pointing to, e.g. "commit" or, for nested tags, "tag".
    /// </summary>
    public string Type { get; set; }

    public static bool operator ==(GitAnnotatedTag left, GitAnnotatedTag right) => left.Equals(right);

    public static bool operator !=(GitAnnotatedTag left, GitAnnotatedTag right) => !(left == right);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GitAnnotatedTag tag ? this.Equals(tag) : false;

    /// <inheritdoc/>
    public bool Equals(GitAnnotatedTag other) => this.Sha.Equals(other.Sha);

    /// <inheritdoc/>
    public override int GetHashCode() => this.Sha.GetHashCode();

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Git Tag: {this.Tag} with id {this.Sha}";
    }
}
