#nullable enable

using System.Collections.Generic;

namespace Nerdbank.GitVersioning.ManagedGit
{
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
        /// The Git object Id of this <see cref="GitObjectId"/>.
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
}
