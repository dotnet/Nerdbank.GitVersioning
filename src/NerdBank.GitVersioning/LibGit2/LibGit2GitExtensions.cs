// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using LibGit2Sharp;
using Validation;
using Windows.Win32;
using Version = System.Version;

#nullable enable

namespace Nerdbank.GitVersioning.LibGit2;

/// <summary>
/// Git extension methods.
/// </summary>
public static class LibGit2GitExtensions
{
    /// <summary>
    /// Maximum allowable value for the <see cref="Version.Build"/>
    /// and <see cref="Version.Revision"/> components.
    /// </summary>
    private const ushort MaximumBuildNumberOrRevisionComponent = 0xfffe;

    /// <summary>
    /// The 0.0 version.
    /// </summary>
    private static readonly Version Version0 = new Version(0, 0);

    /// <summary>
    /// The 0.0 semver.
    /// </summary>
    private static readonly SemanticVersion SemVer0 = SemanticVersion.Parse("0.0");

    private static readonly LibGit2Sharp.CompareOptions DiffOptions = new LibGit2Sharp.CompareOptions()
    {
        // When calculating the height of a commit, we do not care if a file has been renamed only if it has been added or removed.
        // Calculating similarities can consume significant amounts of CPU, so disable it.
        Similarity = SimilarityOptions.None,
        ContextLines = 0,
    };

#if !NETCOREAPP
    private static FreeLibrarySafeHandle? nativeLibrary;
#endif

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive).
    /// </summary>
    /// <param name="context">The git context to read from.</param>
    /// <param name="continueStepping">
    /// A function that returns <see langword="false"/> when we reach a commit that
    /// should not be included in the height calculation.
    /// May be null to count the height to the original commit.
    /// </param>
    /// <returns>The height of the commit. Always a positive integer.</returns>
    public static int GetHeight(LibGit2Context context, Func<Commit, bool>? continueStepping = null)
    {
        Verify.Operation(context.Commit is object, "No commit is selected.");
        var tracker = new GitWalkTracker(context);
        return GetCommitHeight(context.Commit, tracker, continueStepping);
    }

    /// <summary>
    /// Takes the first 2 bytes of a commit ID (i.e. first 4 characters of its hex-encoded SHA)
    /// and returns them as an 16-bit unsigned integer.
    /// </summary>
    /// <param name="commit">The commit to identify with an integer.</param>
    /// <returns>The unsigned integer which identifies a commit.</returns>
    public static ushort GetTruncatedCommitIdAsUInt16(this Commit commit)
    {
        Requires.NotNull(commit, nameof(commit));
        return BinaryPrimitives.ReadUInt16BigEndian(commit.Id.RawId);
    }

    /// <summary>
    /// Looks up the commit that matches a specified version number.
    /// </summary>
    /// <param name="context">The git context to read from.</param>
    /// <param name="version">The version previously obtained from <see cref="VersionOracle.Version"/>.</param>
    /// <returns>The matching commit, or <see langword="null"/> if no match is found.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown in the very rare situation that more than one matching commit is found.
    /// </exception>
    public static Commit? GetCommitFromVersion(LibGit2Context context, Version version)
    {
        // Note we'll accept no match, or one match. But we throw if there is more than one match.
        return GetCommitsFromVersion(context, version).SingleOrDefault();
    }

    /// <summary>
    /// Looks up the commits that match a specified version number.
    /// </summary>
    /// <param name="context">The git context to read from.</param>
    /// <param name="version">The version previously obtained from <see cref="VersionOracle.Version"/>.</param>
    /// <returns>The matching commits, or an empty enumeration if no match is found.</returns>
    public static IEnumerable<Commit> GetCommitsFromVersion(LibGit2Context context, Version version)
    {
        Requires.NotNull(context, nameof(context));
        Requires.NotNull(version, nameof(version));

        var tracker = new GitWalkTracker(context);
        IEnumerable<Commit>? possibleCommits = from commit in GetCommitsReachableFromRefs(context.Repository)
                                               let commitVersionOptions = tracker.GetVersion(commit)
                                               where commitVersionOptions?.Version?.IsMatchingVersion(version) is true
                                               where !IsCommitIdMismatch(version, commitVersionOptions, commit)
                                               where !IsVersionHeightMismatch(version, commitVersionOptions, commit, tracker)
                                               select commit;

        return possibleCommits;
    }

    /// <summary>
    /// Finds the directory that contains the appropriate native libgit2 module.
    /// </summary>
    /// <param name="basePath">The path to the directory that contains the runtimes folder.</param>
    /// <returns>Receives the directory that native binaries are expected.</returns>
    public static string? FindLibGit2NativeBinaries(string basePath)
    {
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        // TODO: learn how to detect when to use "linux-musl".
        string? os =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
            null;

        if (os is null)
        {
            return null;
        }

        string candidatePath = Path.Combine(basePath, "runtimes", $"{os}-{arch}", "native");
        return Directory.Exists(candidatePath) ? candidatePath : null;
    }

    /// <summary>
    /// Loads the git2 native library from the expected path so that it's available later when needed.
    /// </summary>
    /// <param name="basePath"><inheritdoc cref="FindLibGit2NativeBinaries(string)" path="/param" /></param>
    /// <remarks>
    /// This method should only be called on .NET Framework, and only loads the module when on Windows.
    /// </remarks>
    public static void LoadNativeBinary(string basePath)
    {
#if NETCOREAPP
        throw new PlatformNotSupportedException();
#else
        if (nativeLibrary is null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (FindLibGit2NativeBinaries(basePath) is string directoryPath)
            {
                if (Directory.EnumerateFiles(directoryPath).FirstOrDefault() is string filePath)
                {
                    nativeLibrary = PInvoke.LoadLibrary(filePath);
                }
            }
        }
#endif
    }

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive)
    /// that set the version to the value at <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The git context to read from.</param>
    /// <param name="baseVersion">Optional base version to calculate the height. If not specified, the base version will be calculated by scanning the repository.</param>
    /// <returns>The height of the commit. Always a positive integer.</returns>
    internal static int GetVersionHeight(LibGit2Context context, Version? baseVersion = null)
    {
        if (context.Commit is null)
        {
            return 0;
        }

        var tracker = new GitWalkTracker(context);

        VersionOptions? versionOptions = tracker.GetVersion(context.Commit);
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
    /// Encodes a commit from history in a <see cref="Version"/>
    /// so that the original commit can be found later.
    /// </summary>
    /// <param name="commit">The commit whose ID and position in history is to be encoded.</param>
    /// <param name="versionOptions">The version options applicable at this point (either from commit or working copy).</param>
    /// <param name="versionHeight">The version height, previously calculated by a call to <see cref="GetVersionHeight(LibGit2Context, Version?)"/>.</param>
    /// <returns>
    /// A version whose <see cref="Version.Build"/> and
    /// <see cref="Version.Revision"/> components are calculated based on the commit.
    /// </returns>
    /// <remarks>
    /// In the returned version, the <see cref="Version.Build"/> component is
    /// the height of the git commit while the <see cref="Version.Revision"/>
    /// component is the first four bytes of the git commit id (forced to be a positive integer).
    /// </remarks>
    internal static Version GetIdAsVersionHelper(this Commit? commit, VersionOptions? versionOptions, int versionHeight)
    {
        Version? baseVersion = versionOptions?.Version?.Version ?? Version0;
        int buildNumber = baseVersion.Build;
        int revision = baseVersion.Revision;

        // Don't use the ?? coalescing operator here because the position property getters themselves can return null, which should NOT be overridden with our default.
        // The default value is only appropriate if versionOptions itself is null.
        SemanticVersion.Position? versionHeightPosition = versionOptions is not null ? versionOptions.VersionHeightPosition : SemanticVersion.Position.Build;
        SemanticVersion.Position? commitIdPosition = versionOptions is not null ? versionOptions.GitCommitIdPosition : SemanticVersion.Position.Revision;

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
                    revision = commit is object
                        ? Math.Min(MaximumBuildNumberOrRevisionComponent, commit.GetTruncatedCommitIdAsUInt16())
                        : 0;
                    break;
            }
        }

        return VersionExtensions.Create(baseVersion.Major, baseVersion.Minor, buildNumber, revision);
    }

    /// <summary>
    /// Returns the repository that <paramref name="repositoryMember"/> belongs to.
    /// </summary>
    /// <param name="repositoryMember">Member of the repository.</param>
    /// <returns>Repository that <paramref name="repositoryMember"/> belongs to.</returns>
    private static IRepository GetRepository(this IBelongToARepository repositoryMember)
    {
        return repositoryMember.Repository;
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
    private static bool CommitMatchesVersion(this Commit commit, SemanticVersion expectedVersion, SemanticVersion.Position comparisonPrecision, GitWalkTracker tracker)
    {
        Requires.NotNull(commit, nameof(commit));
        Requires.NotNull(expectedVersion, nameof(expectedVersion));

        VersionOptions? commitVersionData = tracker.GetVersion(commit);
        SemanticVersion? semVerFromFile = commitVersionData?.Version;
        if (semVerFromFile is null)
        {
            return false;
        }

        // If the version height position moved, that's an automatic reset in version height.
        if (commitVersionData!.VersionHeightPosition != comparisonPrecision)
        {
            return false;
        }

        return !SemanticVersion.WillVersionChangeResetVersionHeight(commitVersionData.Version, expectedVersion, comparisonPrecision);
    }

    /// <summary>
    /// Tests whether a commit's version-spec matches a given version-spec.
    /// </summary>
    /// <param name="commit">The commit to test.</param>
    /// <param name="expectedVersion">The version to test for in the commit.</param>
    /// <param name="comparisonPrecision">The last component of the version to include in the comparison.</param>
    /// <param name="tracker">The caching tracker for storing or fetching version information per commit.</param>
    /// <returns><see langword="true"/> if the <paramref name="commit"/> matches the major and minor components of <paramref name="expectedVersion"/>.</returns>
    private static bool CommitMatchesVersion(this Commit commit, Version expectedVersion, SemanticVersion.Position comparisonPrecision, GitWalkTracker tracker)
    {
        Requires.NotNull(commit, nameof(commit));
        Requires.NotNull(expectedVersion, nameof(expectedVersion));

        VersionOptions? commitVersionData = tracker.GetVersion(commit);
        SemanticVersion? semVerFromFile = commitVersionData?.Version;
        if (semVerFromFile is null)
        {
            return false;
        }

        for (SemanticVersion.Position position = SemanticVersion.Position.Major; position <= comparisonPrecision; position++)
        {
            int expectedValue = SemanticVersion.ReadVersionPosition(expectedVersion, position);
            int actualValue = SemanticVersion.ReadVersionPosition(semVerFromFile.Version, position);
            if (expectedValue != actualValue)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsVersionHeightMismatch(Version version, VersionOptions versionOptions, Commit commit, GitWalkTracker tracker)
    {
        Requires.NotNull(version, nameof(version));
        Requires.NotNull(versionOptions, nameof(versionOptions));
        Requires.NotNull(commit, nameof(commit));

        // The version.Build or version.Revision MAY represent the version height.
        SemanticVersion.Position? position = versionOptions.VersionHeightPosition;
        if (position.HasValue && position.Value <= SemanticVersion.Position.Revision)
        {
            int expectedVersionHeight = SemanticVersion.ReadVersionPosition(version, position.Value);

            int actualVersionOffset = versionOptions.VersionHeightOffsetOrDefault;
            int actualVersionHeight = GetCommitHeight(commit, tracker, c => CommitMatchesVersion(c, version, position.Value - 1, tracker));
            return expectedVersionHeight != actualVersionHeight + actualVersionOffset;
        }

        // It's not a mismatch, since for this commit, the version height wasn't recorded in the 4-integer version.
        return false;
    }

    private static bool IsCommitIdMismatch(Version version, VersionOptions versionOptions, Commit commit)
    {
        Requires.NotNull(version, nameof(version));
        Requires.NotNull(versionOptions, nameof(versionOptions));
        Requires.NotNull(commit, nameof(commit));

        // The version.Revision MAY represent the first 2 bytes of the git commit ID, but not if 3 integers were specified in version.json,
        // since in that case the 4th integer is the version height. But we won't know till we read the version.json file, so for now,
        SemanticVersion.Position? position = versionOptions.GitCommitIdPosition;
        if (position.HasValue && position.Value <= SemanticVersion.Position.Revision)
        {
            // prepare for it to be the commit ID.
            // The revision is a 16-bit unsigned integer, but is not allowed to be 0xffff.
            // So if the value is 0xfffe, consider that the actual last bit is insignificant
            // since the original git commit ID could have been either 0xffff or 0xfffe.
            int expectedCommitIdLeadingValue = SemanticVersion.ReadVersionPosition(version, position.Value);
            if (expectedCommitIdLeadingValue != -1)
            {
                ushort objectIdLeadingValue = (ushort)expectedCommitIdLeadingValue;
                ushort objectIdMask = (ushort)(objectIdLeadingValue == MaximumBuildNumberOrRevisionComponent ? 0xfffe : 0xffff);

                // Accept a big endian match or a little endian match.
                // Nerdbank.GitVersioning up to v3.4 would produce versions based on the endianness of the CPU it ran on (typically little endian).
                // Starting with v3.5, it deterministically used big endian. In order for `nbgv get-commits` to match on versions computed before and after the change,
                // we match on either endian setting.
                return !(commit.Id.StartsWith(objectIdLeadingValue, bigEndian: true, objectIdMask) || commit.Id.StartsWith(objectIdLeadingValue, bigEndian: false, objectIdMask));
            }
        }

        // It's not a mismatch, since for this commit, the commit ID wasn't recorded in the 4-integer version.
        return false;
    }

    /// <summary>
    /// Tests whether an object's ID starts with the specified 16-bits, or a subset of them.
    /// </summary>
    /// <param name="object">The object whose ID is to be tested.</param>
    /// <param name="leadingBytes">The leading 16-bits to be tested.</param>
    /// <param name="bigEndian"><see langword="true"/> to read the first two bytes as big endian (v3.5+ behavior); <see langword="false"/> to use little endian (v3.4 and earlier behavior).</param>
    /// <param name="bitMask">The mask that indicates which bits should be compared.</param>
    /// <returns><c>True</c> if the object's ID starts with <paramref name="leadingBytes"/> after applying the <paramref name="bitMask"/>.</returns>
    private static bool StartsWith(this ObjectId @object, ushort leadingBytes, bool bigEndian, ushort bitMask = 0xffff)
    {
        ushort truncatedObjectId = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(@object.RawId) : BinaryPrimitives.ReadUInt16LittleEndian(@object.RawId);
        return (truncatedObjectId & bitMask) == leadingBytes;
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
    /// <param name="startingCommit">The commit to measure the height of.</param>
    /// <param name="tracker">The caching tracker for storing or fetching version information per commit.</param>
    /// <param name="continueStepping">
    /// A function that returns <see langword="false"/> when we reach a commit that
    /// should not be included in the height calculation.
    /// May be null to count the height to the original commit.
    /// </param>
    /// <returns>The height of the branch.</returns>
    private static int GetCommitHeight(Commit startingCommit, GitWalkTracker tracker, Func<Commit, bool>? continueStepping)
    {
        Requires.NotNull(startingCommit, nameof(startingCommit));
        Requires.NotNull(tracker, nameof(tracker));

        if (continueStepping is object && !continueStepping(startingCommit))
        {
            return 0;
        }

        var commitsToEvaluate = new Stack<Commit>();
        bool TryCalculateHeight(Commit commit)
        {
            // Get max height among all parents, or schedule all missing parents for their own evaluation and return false.
            int maxHeightAmongParents = 0;
            bool parentMissing = false;
            foreach (Commit parent in commit.Parents)
            {
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

            bool ignoreCase = commit.GetRepository().Config.Get<bool>("core.ignorecase")?.Value ?? false;
            bool ContainsRelevantChanges(IEnumerable<TreeEntryChanges> changes) =>
                excludePaths?.Count == 0
                    ? changes.Any()
                    //// If there is a single change that isn't excluded,
                    //// then this commit is relevant.
                    : changes.Any(change => !excludePaths!.Any(exclude => exclude.Excludes(change.Path, ignoreCase)));

            int height = 1;

            if (includePaths is not null)
            {
                // If there are no include paths, or any of the include
                // paths refer to the root of the repository, then do not
                // filter the diff at all.
                List<string>? diffInclude =
                    includePaths.Count == 0 || pathFilters!.Any(filter => filter.IsRoot)
                        ? null
                        : includePaths;

                // If the diff between this commit and any of its parents
                // touches a path that we care about, bump the height.
                // A no-parent commit is relevant if it introduces anything in the filtered path.
                bool relevantCommit =
                    commit.Parents.Any()
                        ? commit.Parents.Any(parent => ContainsRelevantChanges(commit.GetRepository().Diff
                            .Compare<TreeChanges>(parent.Tree, commit.Tree, diffInclude, DiffOptions)))
                        : ContainsRelevantChanges(commit.GetRepository().Diff
                            .Compare<TreeChanges>(null, commit.Tree, diffInclude, DiffOptions));

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
            Commit commit = commitsToEvaluate.Peek();
            if (tracker.TryGetVersionHeight(commit, out _) || TryCalculateHeight(commit))
            {
                commitsToEvaluate.Pop();
            }
        }

        Assumes.True(tracker.TryGetVersionHeight(startingCommit, out int result));
        return result;
    }

    /// <summary>
    /// Enumerates over the set of commits in the repository that are reachable from any named reference.
    /// </summary>
    /// <param name="repo">The repo to search.</param>
    /// <returns>An enumerate of commits.</returns>
    private static IEnumerable<Commit> GetCommitsReachableFromRefs(Repository repo)
    {
        Requires.NotNull(repo, nameof(repo));

        var visitedCommitIds = new HashSet<ObjectId>();
        var breadthFirstQueue = new Queue<Commit>();

        // Start the discovery with HEAD, and all commits that have refs pointing to them.
        breadthFirstQueue.Enqueue(repo.Head.Tip);
        foreach (Reference? reference in repo.Refs)
        {
            var commit = reference.ResolveToDirectReference()?.Target as Commit;
            if (commit is object)
            {
                breadthFirstQueue.Enqueue(commit);
            }
        }

        while (breadthFirstQueue.Count > 0)
        {
            Commit head = breadthFirstQueue.Dequeue();
            if (visitedCommitIds.Add(head.Id))
            {
                yield return head;
                foreach (Commit parent in head.Parents)
                {
                    breadthFirstQueue.Enqueue(parent);
                }
            }
        }
    }

    private class GitWalkTracker
    {
        private readonly Dictionary<ObjectId, VersionOptions?> commitVersionCache = new Dictionary<ObjectId, VersionOptions?>();
        private readonly Dictionary<ObjectId, VersionOptions?> blobVersionCache = new Dictionary<ObjectId, VersionOptions?>();
        private readonly Dictionary<ObjectId, int> heights = new Dictionary<ObjectId, int>();
        private readonly LibGit2Context context;

        internal GitWalkTracker(LibGit2Context context)
        {
            this.context = context;
        }

        internal bool TryGetVersionHeight(Commit commit, out int height) => this.heights.TryGetValue(commit.Id, out height);

        internal void RecordHeight(Commit commit, int height) => this.heights.Add(commit.Id, height);

        internal VersionOptions? GetVersion(Commit commit)
        {
            if (!this.commitVersionCache.TryGetValue(commit.Id, out VersionOptions? options))
            {
                try
                {
                    options = ((LibGit2VersionFile)this.context.VersionFile).GetVersion(commit, this.context.RepoRelativeProjectDirectory, this.blobVersionCache, out string? actualDirectory);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Unable to get version from commit: {commit.Id.Sha}", ex);
                }

                this.commitVersionCache.Add(commit.Id, options);
            }

            return options;
        }
    }
}
