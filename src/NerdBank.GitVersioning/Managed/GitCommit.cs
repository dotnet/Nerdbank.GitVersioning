#nullable enable

using System;
using System.Collections.Generic;

namespace NerdBank.GitVersioning.Managed
{
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
        /// Gets or sets a list of all parents of this commit.
        /// </summary>
        public List<GitObjectId> Parents { get; set; }

        /// <summary>
        /// Gets or sets the author of this commit.
        /// </summary>
        public GitSignature? Author { get; set; }

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
        public static bool operator ==(GitCommit left, GitCommit right)
        {
            return Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(GitCommit left, GitCommit right)
        {
            return !Equals(left, right);
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
    }
}
