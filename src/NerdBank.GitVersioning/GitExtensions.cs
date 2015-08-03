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
        /// Takes the first 4 bytes of a commit ID (i.e. first 8 characters of its hex-encoded SHA)
        /// and returns them as an integer.
        /// </summary>
        /// <param name="commit">The commit to identify with an integer.</param>
        /// <returns>The integer which identifies a commit.</returns>
        public static int GetTruncatedCommitIdAsInteger(this Commit commit)
        {
            Requires.NotNull(commit, nameof(commit));
            return BitConverter.ToInt32(commit.Id.RawId, 0);
        }

        /// <summary>
        /// Looks up a commit by an integer that captures the first for bytes of its ID.
        /// </summary>
        /// <param name="repo">The repo to search for a matching commit.</param>
        /// <param name="truncatedId">The value returned from <see cref="GetTruncatedCommitIdAsInteger(Commit)"/>.</param>
        /// <returns>A matching commit.</returns>
        public static Commit GetCommitFromTruncatedIdInteger(this Repository repo, int truncatedId)
        {
            Requires.NotNull(repo, nameof(repo));

            byte[] rawId = BitConverter.GetBytes(truncatedId);
            return repo.Lookup<Commit>(EncodeAsHex(rawId));
        }

        /// <summary>
        /// Encodes a commit from history in a <see cref="System.Version"/>
        /// so that the original commit can be found later.
        /// </summary>
        /// <param name="commit">The commit whose ID and position in history is to be encoded.</param>
        /// <returns>
        /// A version whose <see cref="System.Version.Build"/> and
        /// <see cref="System.Version.Revision"/> components are calculated based on the commit.
        /// </returns>
        /// <remarks>
        /// In the returned version, the <see cref="System.Version.Build"/> component is
        /// the height of the git commit while the <see cref="System.Version.Revision"/>
        /// component is the first four bytes of the git commit id (forced to be a positive integer).
        /// </remarks>
        public static System.Version GetIdAsVersion(this Commit commit)
        {
            Requires.NotNull(commit, nameof(commit));

            var baseVersion = VersionTextFile.GetVersionFromFile(commit)?.Version;
            Verify.Operation(baseVersion != null, "No version.txt file found in the commit being built.");

            // The 3rd component of the version is the height of the git history at this point.
            // This helps ensure that within a major.minor release, each patch has an
            // incrementing integer.
            int build = commit.GetHeight();

            // The revision is set to the first four bytes of the git commit ID.
            // Except that since version components must be positive, we force it if
            // it naturally would be negative.
            // When doing a reverse-lookup, this means we have to try both positive
            // and negative values since we effectively only have 31-bits of useful
            // space in the int32.
            int revision = Math.Abs(commit.GetTruncatedCommitIdAsInteger());

            return new System.Version(baseVersion.Major, baseVersion.Minor, build, revision);
        }

        /// <summary>
        /// Looks up the commit that matches a specified version number.
        /// </summary>
        /// <param name="repo">The repository to search for a matching commit.</param>
        /// <param name="version">The version previously obtained from <see cref="GetIdAsVersion(Commit)"/>.</param>
        /// <returns>The matching commit id, or <c>null</c> if no match is found.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown in the very rare situation that more than one matching commit is found.
        /// </exception>
        public static Commit GetCommitFromVersion(this Repository repo, System.Version version)
        {
            Requires.NotNull(repo, nameof(repo));
            Requires.NotNull(version, nameof(version));

            int height = version.Build;

            // Only the least significant 31 bits of the revision component hold data.
            // The most significant bit (which stores positive or negative) must be 0
            // so that the component is positive. But since no such restriction exists in git,
            // we have to test both.
            string commitIdPrefix1 = EncodeAsHex(BitConverter.GetBytes(version.Revision));
            string commitIdPrefix2 = EncodeAsHex(BitConverter.GetBytes(-version.Revision));

            var possibleCommits = from commit in repo.ObjectDatabase.OfType<Commit>()
                                  where commit.Id.StartsWith(commitIdPrefix1) || commit.Id.StartsWith(commitIdPrefix2)
                                  // Extra disambiguation that may be necessary
                                  where commit.GetHeight() == height
                                  let majorMinor = VersionTextFile.GetVersionFromFile(commit).Version
                                  where majorMinor.Major == version.Major && majorMinor.Minor == version.Minor
                                  select commit;

            // Note we'll accept no match, or one match. But we throw if there is more than one match.
            return possibleCommits.SingleOrDefault();
        }

        /// <summary>
        /// Encodes a byte array as hex.
        /// </summary>
        /// <param name="buffer">The buffer to encode.</param>
        /// <returns>A hexidecimal string.</returns>
        private static string EncodeAsHex(byte[] buffer)
        {
            Requires.NotNull(buffer, nameof(buffer));

            var sb = new StringBuilder();
            for (int i = 0; i < buffer.Length; i++)
            {
                sb.AppendFormat("{0:x2}", buffer[i]);
            }

            return sb.ToString();
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
