// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Collections;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Represents a Git commit, as stored in the Git object database.
/// </summary>
public struct GitCommit : IEquatable<GitCommit>
{
    /// <summary>
    /// Gets or sets the <see cref="GitObjectId"/> of the file tree which represents directory
    /// structure of the repository at the time of the commit.
    /// </summary>
    public GitObjectId Tree { get; set; }

    /// <summary>
    /// Gets or sets a <see cref="GitObjectId"/> which uniquely identifies the <see cref="GitCommit"/>.
    /// </summary>
    public GitObjectId Sha { get; set; }

    /// <summary>
    /// Gets or sets the first parent of this commit.
    /// </summary>
    public GitObjectId? FirstParent { get; set; }

    /// <summary>
    /// Gets or sets the second parent of this commit.
    /// </summary>
    public GitObjectId? SecondParent { get; set; }

    /// <summary>
    /// Gets or sets additional parents (3rd parent and on) of this commit, if any.
    /// </summary>
    public List<GitObjectId>? AdditionalParents { get; set; }

    /// <summary>
    /// Gets an enumerator for parents of this commit.
    /// </summary>
    public ParentEnumerable Parents => new ParentEnumerable(this);

    /// <summary>
    /// Gets or sets the author of this commit.
    /// </summary>
    public GitSignature? Author { get; set; }

    public static bool operator ==(GitCommit left, GitCommit right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(GitCommit left, GitCommit right)
    {
        return !Equals(left, right);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is GitCommit)
        {
            return this.Equals((GitCommit)obj);
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Equals(GitCommit other)
    {
        return this.Sha.Equals(other.Sha);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return this.Sha.GetHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Git Commit: {this.Sha}";
    }

    /// <summary>
    /// An enumerable for parents of a commit.
    /// </summary>
    public struct ParentEnumerable : IEnumerable<GitObjectId>
    {
        private readonly GitCommit owner;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParentEnumerable"/> struct.
        /// </summary>
        /// <param name="owner">The commit whose parents are to be enumerated.</param>
        public ParentEnumerable(GitCommit owner)
        {
            this.owner = owner;
        }

        /// <inheritdoc />
        public IEnumerator<GitObjectId> GetEnumerator() => new ParentEnumerator(this.owner);

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    /// <summary>
    /// An enumerator for a commit's parents.
    /// </summary>
    public struct ParentEnumerator : IEnumerator<GitObjectId>
    {
        private readonly GitCommit owner;

        private int position;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParentEnumerator"/> struct.
        /// </summary>
        /// <param name="owner">The commit whose parents are to be enumerated.</param>
        public ParentEnumerator(GitCommit owner)
        {
            this.owner = owner;
            this.position = -1;
        }

        /// <inheritdoc />
        public GitObjectId Current
        {
            get
            {
                if (this.position < 0)
                {
                    throw new InvalidOperationException("Call MoveNext first.");
                }

                return this.position switch
                {
                    0 => this.owner.FirstParent ?? throw new InvalidOperationException("No more elements."),
                    1 => this.owner.SecondParent ?? throw new InvalidOperationException("No more elements."),
                    _ => this.owner.AdditionalParents?[this.position - 2] ?? throw new InvalidOperationException("No more elements."),
                };
            }
        }

        /// <inheritdoc />
        object IEnumerator.Current => this.Current;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public bool MoveNext()
        {
            return ++this.position switch
            {
                0 => this.owner.FirstParent.HasValue,
                1 => this.owner.SecondParent.HasValue,
                _ => this.owner.AdditionalParents?.Count > this.position - 2,
            };
        }

        /// <inheritdoc />
        public void Reset() => this.position = -1;
    }
}
