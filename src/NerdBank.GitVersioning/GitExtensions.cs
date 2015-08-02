namespace NerdBank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using LibGit2Sharp;
    using Validation;

    /// <summary>
    /// Git extension methods.
    /// </summary>
    public static class GitExtensions
    {
        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <returns>The height of the commit. Always a positive integer.</returns>
        public static int GetHeight(this Commit commit)
        {
            Requires.NotNull(commit, nameof(commit));

            var heights = new Dictionary<ObjectId, int>();
            return GetCommitHeight(commit, heights);
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified branch's head and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="branch">The branch to measure the height of.</param>
        /// <returns>The height of the branch.</returns>
        public static int GetHeight(this Branch branch)
        {
            return GetHeight(branch.Commits.First());
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified branch's head and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <param name="heights">A cache of commits and their heights.</param>
        /// <returns>The height of the branch.</returns>
        private static int GetCommitHeight(Commit commit, Dictionary<ObjectId, int> heights)
        {
            Requires.NotNull(commit, nameof(commit));
            Requires.NotNull(heights, nameof(heights));

            int height;
            if (!heights.TryGetValue(commit.Id, out height))
            {
                height = 1;
                if (commit.Parents.Any())
                {
                    height += commit.Parents.Max(p => GetCommitHeight(p, heights));
                }

                heights[commit.Id] = height;
            }

            return height;
        }
    }
}
