// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using Nerdbank.GitVersioning.ManagedGit;
using Validation;

namespace Nerdbank.GitVersioning.Managed;

internal static class GitExtensions
{
    /// <summary>
    /// The 0.0 semver.
    /// </summary>
    private static readonly SemanticVersion SemVer0 = SemanticVersion.Parse("0.0");

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive).
    /// </summary>
    /// <param name="context">The git context.</param>
    /// <param name="continueStepping">
    /// A function that returns <see langword="false"/> when we reach a commit that
    /// should not be included in the height calculation.
    /// May be null to count the height to the original commit.
    /// </param>
    /// <returns>The height of the commit. Always a positive integer.</returns>
    public static int GetHeight(ManagedGitContext context, Func<GitCommit, bool>? continueStepping = null)
    {
        Verify.Operation(context.Commit.HasValue, "No commit is selected.");
        var tracker = new GitWalkTracker(context);
        return GetCommitHeight(context.Repository, context.Commit.Value, tracker, continueStepping);
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

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive)
    /// that set the version to the value at <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The git context for which to calculate the height.</param>
    /// <param name="baseVersion">Optional base version to calculate the height. If not specified, the base version will be calculated by scanning the repository.</param>
    /// <returns>The height of the commit. Always a positive integer.</returns>
    internal static int GetVersionHeight(ManagedGitContext context, Version? baseVersion = null)
    {
        if (context.Commit is null)
        {
            return 0;
        }

        var tracker = new GitWalkTracker(context);

        VersionOptions? versionOptions = tracker.GetVersion(context.Commit.Value);
        if (versionOptions is null)
        {
            return 0;
        }

        SemanticVersion? baseSemVer =
            baseVersion is not null ? SemanticVersion.Parse(baseVersion.ToString()) :
            versionOptions.Version ?? SemVer0;

        SemanticVersion.Position? versionHeightPosition = versionOptions.VersionHeightPosition;
        if (versionHeightPosition.HasValue)
        {
            int height = GetHeight(context, c => CommitMatchesVersion(c, baseSemVer, versionHeightPosition.Value, tracker));
            return height;
        }

        return 0;
    }

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified branch's head and the most distant ancestor (inclusive).
    /// </summary>
    /// <param name="repository">The Git repository.</param>
    /// <param name="startingCommit">The commit to measure the height of.</param>
    /// <param name="tracker">The caching tracker for storing or fetching version information per commit.</param>
    /// <param name="continueStepping">
    /// A function that returns <see langword="false"/> when we reach a commit that
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
                GitCommit parent = repository.GetCommit(parentId);
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

            VersionOptions? versionOptions = tracker.GetVersion(commit);
            IReadOnlyList<FilterPath>? pathFilters = versionOptions?.PathFilters;

            var includePaths =
                pathFilters
                    ?.Where(filter => !filter.IsExclude)
                    .Select(filter => filter.RepoRelativePath)
                    .ToList();

            var excludePaths = pathFilters?.Where(filter => filter.IsExclude).ToList();

            bool ignoreCase = repository.IgnoreCase;

            int height = 1;

            if (pathFilters is not null)
            {
                // If the diff between this commit and any of its parents
                // touches a path that we care about, bump the height.
                bool relevantCommit = false, anyParents = false;
                foreach (GitObjectId parentId in commit.Parents)
                {
                    anyParents = true;
                    GitCommit parent = repository.GetCommit(parentId);
                    if (IsRelevantCommit(repository, commit, parent, pathFilters))
                    {
                        // No need to scan further, as a positive match will never turn negative.
                        relevantCommit = true;
                        break;
                    }
                }

                if (!anyParents)
                {
                    // A no-parent commit is relevant if it introduces anything in the filtered path.
                    relevantCommit = IsRelevantCommit(repository, commit, parent: default(GitCommit), pathFilters);
                }

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
            parent != default ? repository.GetTree(parent.Tree) : null,
            relativePath: string.Empty,
            filters);
    }

    private static bool IsRelevantCommit(GitRepository repository, GitTree tree, GitTree? parent, string relativePath, IReadOnlyList<FilterPath> filters)
    {
        // Walk over all child nodes in the current tree. If a child node was found in the parent,
        // remove it, so that after the iteration the parent contains all nodes which have been
        // deleted.
        foreach (KeyValuePair<string, GitTreeEntry> child in tree.Children)
        {
            GitTreeEntry? entry = child.Value;
            GitTreeEntry? parentEntry = null;

            // If the entry is not present in the parent commit, it was added;
            // if the Sha does not match, it was modified.
            if (parent is null ||
                !parent.Children.TryGetValue(child.Key, out parentEntry) ||
                parentEntry.Sha != child.Value.Sha)
            {
                // Determine whether the change was relevant.
                string? fullPath = $"{relativePath}{entry.Name}";

                bool isRelevant =
                    //// Either there are no include filters at all (i.e. everything is included), or there's an explicit include filter
                    (!filters.Any(f => f.IsInclude) || filters.Any(f => f.Includes(fullPath, repository.IgnoreCase))
                     || (!entry.IsFile && filters.Any(f => f.IncludesChildren(fullPath, repository.IgnoreCase))))
                    //// The path is not excluded by any filters
                    && !filters.Any(f => f.Excludes(fullPath, repository.IgnoreCase));

                // If the change was relevant, and the item is a directory, we need to recurse.
                if (isRelevant && !entry.IsFile)
                {
                    isRelevant = IsRelevantCommit(
                        repository,
                        repository.GetTree(entry.Sha),
                        parentEntry is null ? GitTree.Empty : repository.GetTree(parentEntry.Sha),
                        $"{fullPath}/",
                        filters);
                }

                // Quit as soon as any relevant change has been detected.
                if (isRelevant)
                {
                    return true;
                }
            }

            if (parentEntry is not null)
            {
                Assumes.NotNull(parent);
                parent.Children.Remove(child.Key);
            }
        }

        // Inspect removed entries (i.e. present in parent but not in the current tree)
        if (parent is not null)
        {
            foreach (KeyValuePair<string, GitTreeEntry> child in parent.Children)
            {
                // Determine whether the change was relevant.
                string? fullPath = Path.Combine(relativePath, child.Key);

                bool isRelevant =
                    filters.Any(f => f.Includes(fullPath, repository.IgnoreCase))
                    && !filters.Any(f => f.Excludes(fullPath, repository.IgnoreCase));

                if (isRelevant)
                {
                    return true;
                }
            }
        }

        // No relevant changes have been detected
        return false;
    }

    /// <summary>
    /// Tests whether a commit is of a specified version, comparing major and minor components
    /// with the version.txt file defined by that commit.
    /// </summary>
    /// <param name="commit">The commit to test.</param>
    /// <param name="expectedVersion">The version to test for in the commit.</param>
    /// <param name="comparisonPrecision">The last component of the version to include in the comparison.</param>
    /// <param name="tracker">The caching tracker for storing or fetching version information per commit.</param>
    /// <returns><see langword="true"/> if the <paramref name="commit"/> matches the major and minor components of <paramref name="expectedVersion"/>.</returns>
    private static bool CommitMatchesVersion(GitCommit commit, SemanticVersion expectedVersion, SemanticVersion.Position comparisonPrecision, GitWalkTracker tracker)
    {
        Requires.NotNull(expectedVersion, nameof(expectedVersion));

        VersionOptions? commitVersionData = tracker.GetVersion(commit);
        SemanticVersion? semVerFromFile = commitVersionData?.Version;
        if (commitVersionData is null || semVerFromFile is null)
        {
            return false;
        }

        // If the version height position moved, that's an automatic reset in version height.
        if (commitVersionData.VersionHeightPosition != comparisonPrecision)
        {
            return false;
        }

        return !SemanticVersion.WillVersionChangeResetVersionHeight(commitVersionData.Version, expectedVersion, comparisonPrecision);
    }

    private class GitWalkTracker
    {
        private readonly Dictionary<GitObjectId, VersionOptions?> commitVersionCache = new Dictionary<GitObjectId, VersionOptions?>();
        private readonly Dictionary<GitObjectId, VersionOptions?> blobVersionCache = new Dictionary<GitObjectId, VersionOptions?>();
        private readonly Dictionary<GitObjectId, int> heights = new Dictionary<GitObjectId, int>();
        private readonly ManagedGitContext context;

        internal GitWalkTracker(ManagedGitContext context)
        {
            this.context = context;
        }

        internal bool TryGetVersionHeight(GitCommit commit, out int height) => this.heights.TryGetValue(commit.Sha, out height);

        internal void RecordHeight(GitCommit commit, int height) => this.heights.Add(commit.Sha, height);

        internal VersionOptions? GetVersion(GitCommit commit)
        {
            if (!this.commitVersionCache.TryGetValue(commit.Sha, out VersionOptions? options))
            {
                try
                {
                    options = ((ManagedVersionFile)this.context.VersionFile).GetVersion(commit, this.context.RepoRelativeProjectDirectory, this.blobVersionCache, out string? actualDirectory);
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
