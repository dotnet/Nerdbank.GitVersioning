#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nerdbank.GitVersioning.ManagedGit;
using Validation;

namespace Nerdbank.GitVersioning.Managed
{
    internal static class GitExtensions
    {
        /// <summary>
        /// The 0.0 version.
        /// </summary>
        private static readonly Version Version0 = new Version(0, 0);

        /// <summary>
        /// The 0.0 semver.
        /// </summary>
        private static readonly SemanticVersion SemVer0 = SemanticVersion.Parse("0.0");

        /// <summary>
        /// Maximum allowable value for the <see cref="Version.Build"/>
        /// and <see cref="Version.Revision"/> components.
        /// </summary>
        private const ushort MaximumBuildNumberOrRevisionComponent = 0xfffe;

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at <paramref name="commit"/>.
        /// </summary>
        /// <param name="repository">The git repository.</param>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
        /// <param name="baseVersion">Optional base version to calculate the height. If not specified, the base version will be calculated by scanning the repository.</param>
        /// <returns>The height of the commit. Always a positive integer.</returns>
        internal static int GetVersionHeight(GitRepository repository, GitCommit commit, string? repoRelativeProjectDirectory = null, Version? baseVersion = null)
        {
            Requires.Argument(repoRelativeProjectDirectory == null || !Path.IsPathRooted(repoRelativeProjectDirectory), nameof(repoRelativeProjectDirectory), "Path should be relative to repo root.");

            var tracker = new GitWalkTracker(repository, repoRelativeProjectDirectory);

            var versionOptions = tracker.GetVersion(commit);
            if (versionOptions == null)
            {
                return 0;
            }

            var baseSemVer =
                baseVersion != null ? SemanticVersion.Parse(baseVersion.ToString()) :
                versionOptions.Version ?? SemVer0;

            var versionHeightPosition = versionOptions.VersionHeightPosition;
            if (versionHeightPosition.HasValue)
            {
                int height = GetHeight(repository, commit, repoRelativeProjectDirectory, c => CommitMatchesVersion(c, baseSemVer, versionHeightPosition.Value, tracker));
                return height;
            }

            return 0;
        }

        /// <summary>
        /// Tests whether a commit is of a specified version, comparing major and minor components
        /// with the version.txt file defined by that commit.
        /// </summary>
        /// <param name="commit">The commit to test.</param>
        /// <param name="expectedVersion">The version to test for in the commit</param>
        /// <param name="comparisonPrecision">The last component of the version to include in the comparison.</param>
        /// <param name="tracker">The caching tracker for storing or fetching version information per commit.</param>
        /// <returns><c>true</c> if the <paramref name="commit"/> matches the major and minor components of <paramref name="expectedVersion"/>.</returns>
        private static bool CommitMatchesVersion(GitCommit commit, SemanticVersion expectedVersion, SemanticVersion.Position comparisonPrecision, GitWalkTracker tracker)
        {
            Requires.NotNull(expectedVersion, nameof(expectedVersion));

            var commitVersionData = tracker.GetVersion(commit);
            var semVerFromFile = commitVersionData?.Version;
            if (commitVersionData == null || semVerFromFile == null)
            {
                return false;
            }

            // If the version height position moved, that's an automatic reset in version height.
            if (commitVersionData.VersionHeightPosition != comparisonPrecision)
            {
                return false;
            }

            return !LibGit2.GitExtensions.WillVersionChangeResetVersionHeight(commitVersionData.Version, expectedVersion, comparisonPrecision);
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="repository">The Git repository.</param>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <param name="repoRelativeProjectDirectory">The path to the directory of the project whose version is being queried, relative to the repo root.</param>
        /// <param name="continueStepping">
        /// A function that returns <c>false</c> when we reach a commit that
        /// should not be included in the height calculation.
        /// May be null to count the height to the original commit.
        /// </param>
        /// <returns>The height of the commit. Always a positive integer.</returns>
        public static int GetHeight(GitRepository repository, GitCommit commit, string? repoRelativeProjectDirectory, Func<GitCommit, bool>? continueStepping = null)
        {
            var tracker = new GitWalkTracker(repository, repoRelativeProjectDirectory);
            return GetCommitHeight(repository, commit, tracker, continueStepping);
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified branch's head and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="repository">The Git repository.</param>
        /// <param name="startingCommit">The commit to measure the height of.</param>
        /// <param name="tracker">The caching tracker for storing or fetching version information per commit.</param>
        /// <param name="continueStepping">
        /// A function that returns <c>false</c> when we reach a commit that
        /// should not be included in the height calculation.
        /// May be null to count the height to the original commit.
        /// </param>
        /// <returns>The height of the branch.</returns>
        private static int GetCommitHeight(GitRepository repository, GitCommit startingCommit, GitWalkTracker tracker, Func<GitCommit, bool>? continueStepping)
        {
            if (continueStepping is object && !continueStepping(startingCommit))
            {
                return 0;
            }

            var commitsToEvaluate = new Stack<GitCommit>();
            bool TryCalculateHeight(GitCommit commit)
            {
                // Get max height among all parents, or schedule all missing parents for their own evaluation and return false.
                int maxHeightAmongParents = 0;
                bool parentMissing = false;
                foreach (GitObjectId parentId in commit.Parents)
                {
                    var parent = repository.GetCommit(parentId);
                    if (!tracker.TryGetVersionHeight(parent, out int parentHeight))
                    {
                        if (continueStepping is object && !continueStepping(parent))
                        {
                            // This parent isn't supposed to contribute to height.
                            continue;
                        }

                        commitsToEvaluate.Push(parent);
                        parentMissing = true;
                    }
                    else
                    {
                        maxHeightAmongParents = Math.Max(maxHeightAmongParents, parentHeight);
                    }
                }

                if (parentMissing)
                {
                    return false;
                }

                var versionOptions = tracker.GetVersion(commit);
                var pathFilters = versionOptions?.PathFilters;

                var includePaths =
                    pathFilters
                        ?.Where(filter => !filter.IsExclude)
                        .Select(filter => filter.RepoRelativePath)
                        .ToList();

                var excludePaths = pathFilters?.Where(filter => filter.IsExclude).ToList();

                var ignoreCase = repository.IgnoreCase;

                /*
                bool ContainsRelevantChanges(IEnumerable<TreeEntryChanges> changes) =>
                    excludePaths.Count == 0
                        ? changes.Any()
                        // If there is a single change that isn't excluded,
                        // then this commit is relevant.
                        : changes.Any(change => !excludePaths.Any(exclude => exclude.Excludes(change.Path, ignoreCase)));
                */

                int height = 1;

                if (pathFilters != null)
                {
                    var relevantCommit = true;

                    foreach (var parentId in commit.Parents)
                    {
                        var parent = repository.GetCommit(parentId);
                        relevantCommit = IsRelevantCommit(repository, commit, parent, pathFilters);

                        // If the diff between this commit and any of its parents
                        // does not touch a path that we care about, don't bump the
                        // height.
                        if (!relevantCommit)
                        {
                            break;
                        }
                    }

                    /*
                    // If there are no include paths, or any of the include
                    // paths refer to the root of the repository, then do not
                    // filter the diff at all.
                    var diffInclude =
                        includePaths.Count == 0 || pathFilters.Any(filter => filter.IsRoot)
                            ? null
                            : includePaths;

                    // If the diff between this commit and any of its parents
                    // does not touch a path that we care about, don't bump the
                    // height.
                    var relevantCommit =
                        commit.Parents.Any()
                            ? commit.Parents.Any(parent => ContainsRelevantChanges(commit.GetRepository().Diff
                                .Compare<TreeChanges>(parent.Tree, commit.Tree, diffInclude, DiffOptions)))
                            : ContainsRelevantChanges(commit.GetRepository().Diff
                                .Compare<TreeChanges>(null, commit.Tree, diffInclude, DiffOptions));
                    */

                    if (!relevantCommit)
                    {
                        height = 0;
                    }
                }

                tracker.RecordHeight(commit, height + maxHeightAmongParents);
                return true;
            }

            commitsToEvaluate.Push(startingCommit);
            while (commitsToEvaluate.Count > 0)
            {
                GitCommit commit = commitsToEvaluate.Peek();
                if (tracker.TryGetVersionHeight(commit, out _) || TryCalculateHeight(commit))
                {
                    commitsToEvaluate.Pop();
                }
            }

            Assumes.True(tracker.TryGetVersionHeight(startingCommit, out int result));
            return result;
        }

        private static bool IsRelevantCommit(GitRepository repository, GitCommit commit, GitCommit parent, IReadOnlyList<FilterPath> filters)
        {
            return IsRelevantCommit(
                repository,
                repository.GetTree(commit.Tree),
                repository.GetTree(parent.Tree),
                relativePath: string.Empty,
                filters);
        }

        private static bool IsRelevantCommit(GitRepository repository, GitTree tree, GitTree parent, string relativePath, IReadOnlyList<FilterPath> filters)
        {
            // Walk over all child nodes in the current tree. If a child node was found in the parent,
            // remove it, so that after the iteration the parent contains all nodes which have been
            // deleted.
            foreach (var child in tree.Children)
            {
                var entry = child.Value;
                GitTreeEntry? parentEntry = null;

                // If the entry is not present in the parent commit, it was added;
                // if the Sha does not match, it was modified.
                if (!parent.Children.TryGetValue(child.Key, out parentEntry)
                    || parentEntry.Sha != child.Value.Sha)
                {
                    // Determine whether the change was relevant.
                    var fullPath = $"{relativePath}{entry.Name}";

                    bool isRelevant =
                        // Either there are no include filters at all (i.e. everything is included), or there's an explicit include filter
                        (!filters.Any(f => f.IsInclude) || filters.Any(f => f.Includes(fullPath, repository.IgnoreCase)))
                        // The path is not excluded by any filters
                        && !filters.Any(f => f.Excludes(fullPath, repository.IgnoreCase));

                    // If the change was relevant, and the item is a directory, we need to recurse.
                    if (isRelevant && !entry.IsFile)
                    {
                        isRelevant = IsRelevantCommit(
                            repository,
                            repository.GetTree(entry.Sha),
                            parentEntry == null ? GitTree.Empty : repository.GetTree(parentEntry.Sha),
                            $"{fullPath}/",
                            filters);
                    }

                    // Quit as soon as any relevant change has been detected.
                    if (isRelevant)
                    {
                        return true;
                    }
                }

                if (parentEntry != null)
                {
                    parent.Children.Remove(child.Key);
                }
            }

            // Inspect removed entries (i.e. present in parent but not in the current tree)
            foreach (var child in parent.Children)
            {
                // Determine whether the change was relevant.
                var fullPath = Path.Combine(relativePath, child.Key);

                bool isRelevant =
                    filters.Any(f => f.Includes(fullPath, repository.IgnoreCase))
                    && !filters.Any(f => f.Excludes(fullPath, repository.IgnoreCase));

                if (isRelevant)
                {
                    return true;
                }
            }

            // No relevant changes have been detected
            return false;
        }

        internal static string? GetRepoRelativePath(this GitRepository repo, string absolutePath)
        {
            var repoRoot = repo.WorkingDirectory/* repo?.Info?.WorkingDirectory */?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && repoRoot != null && repoRoot.StartsWith("\\") && (repoRoot.Length == 1 || repoRoot[1] != '\\'))
            {
                // We're in a worktree, which libgit2sharp only gives us as a path relative to the root of the assumed drive.
                // Add the drive: to the front of the repoRoot.
                // repoRoot = repo.Info.Path.Substring(0, 2) + repoRoot;
            }

            if (repoRoot == null)
                return null;

            if (!absolutePath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Path '{absolutePath}' is not within repository '{repoRoot}'", nameof(absolutePath));
            }

            return absolutePath.Substring(repoRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Encodes a commit from history in a <see cref="Version"/>
        /// so that the original commit can be found later.
        /// </summary>
        /// <param name="commit">The commit whose ID and position in history is to be encoded.</param>
        /// <param name="versionOptions">The version options applicable at this point (either from commit or working copy).</param>
        /// <param name="versionHeight">The version height, previously calculated.</param>
        /// <returns>
        /// A version whose <see cref="Version.Build"/> and
        /// <see cref="Version.Revision"/> components are calculated based on the commit.
        /// </returns>
        /// <remarks>
        /// In the returned version, the <see cref="Version.Build"/> component is
        /// the height of the git commit while the <see cref="Version.Revision"/>
        /// component is the first four bytes of the git commit id (forced to be a positive integer).
        /// </remarks>
        internal static Version GetIdAsVersionHelper(this GitCommit? commit, VersionOptions? versionOptions, int versionHeight)
        {
            var baseVersion = versionOptions?.Version?.Version ?? Version0;
            int buildNumber = baseVersion.Build;
            int revision = baseVersion.Revision;

            // Don't use the ?? coalescing operator here because the position property getters themselves can return null, which should NOT be overridden with our default.
            // The default value is only appropriate if versionOptions itself is null.
            var versionHeightPosition = versionOptions != null ? versionOptions.VersionHeightPosition : SemanticVersion.Position.Build;
            var commitIdPosition = versionOptions != null ? versionOptions.GitCommitIdPosition : SemanticVersion.Position.Revision;

            // The compiler (due to WinPE header requirements) only allows 16-bit version components,
            // and forbids 0xffff as a value.
            if (versionHeightPosition.HasValue)
            {
                int adjustedVersionHeight = versionHeight == 0 ? 0 : versionHeight + (versionOptions?.VersionHeightOffset ?? 0);
                Verify.Operation(adjustedVersionHeight <= MaximumBuildNumberOrRevisionComponent, "Git height is {0}, which is greater than the maximum allowed {0}.", adjustedVersionHeight, MaximumBuildNumberOrRevisionComponent);
                switch (versionHeightPosition.Value)
                {
                    case SemanticVersion.Position.Build:
                        buildNumber = adjustedVersionHeight;
                        break;
                    case SemanticVersion.Position.Revision:
                        revision = adjustedVersionHeight;
                        break;
                }
            }

            if (commitIdPosition.HasValue)
            {
                switch (commitIdPosition.Value)
                {
                    case SemanticVersion.Position.Revision:
                        revision = commit != null
                            ? Math.Min(MaximumBuildNumberOrRevisionComponent, commit.Value.GetTruncatedCommitIdAsUInt16())
                            : 0;
                        break;
                }
            }

            return VersionExtensions.Create(baseVersion.Major, baseVersion.Minor, buildNumber, revision);
        }

        /// <summary>
        /// Takes the first 2 bytes of a commit ID (i.e. first 4 characters of its hex-encoded SHA)
        /// and returns them as an 16-bit unsigned integer.
        /// </summary>
        /// <param name="commit">The commit to identify with an integer.</param>
        /// <returns>The unsigned integer which identifies a commit.</returns>
        public static ushort GetTruncatedCommitIdAsUInt16(this GitCommit commit)
        {
            return commit.Sha.AsUInt16();
        }

        private class GitWalkTracker
        {
            private readonly Dictionary<GitObjectId, VersionOptions> commitVersionCache = new Dictionary<GitObjectId, VersionOptions>();
            private readonly Dictionary<GitObjectId, VersionOptions> blobVersionCache = new Dictionary<GitObjectId, VersionOptions>();
            private readonly Dictionary<GitObjectId, int> heights = new Dictionary<GitObjectId, int>();

            internal GitWalkTracker(GitRepository repository, string? repoRelativeDirectory)
            {
                this.Repository = repository;
                this.RepoRelativeDirectory = repoRelativeDirectory;
            }

            internal GitRepository Repository { get; }

            internal string? RepoRelativeDirectory { get; }

            internal bool TryGetVersionHeight(GitCommit commit, out int height) => this.heights.TryGetValue(commit.Sha, out height);

            internal void RecordHeight(GitCommit commit, int height) => this.heights.Add(commit.Sha, height);

            internal VersionOptions GetVersion(GitCommit commit)
            {
                if (!this.commitVersionCache.TryGetValue(commit.Sha, out VersionOptions? options))
                {
                    try
                    {
                        options = VersionFile.GetVersion(this.Repository, commit, this.RepoRelativeDirectory, this.blobVersionCache);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Unable to get version from commit: {commit.Sha}", ex);
                    }

                    this.commitVersionCache.Add(commit.Sha, options);
                }

                return options;
            }
        }
    }
}
