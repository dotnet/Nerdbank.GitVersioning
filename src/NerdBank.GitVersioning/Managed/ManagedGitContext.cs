// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics;
using Nerdbank.GitVersioning.ManagedGit;
using Validation;

namespace Nerdbank.GitVersioning.Managed;

/// <summary>
/// A git context implemented without any native code dependency.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public class ManagedGitContext : GitContext
{
    /// <summary>
    /// Caching field behind <see cref="HeadTags" /> property.
    /// </summary>
    private IReadOnlyCollection<string>? headTags;

    internal ManagedGitContext(string workingDirectory, string dotGitPath, string? committish = null)
        : base(workingDirectory, dotGitPath)
    {
        var repo = GitRepository.Create(workingDirectory);
        if (repo is null)
        {
            throw new ArgumentException("No git repo found here.", nameof(workingDirectory));
        }

        this.Commit = committish is null ? repo.GetHeadCommit() : (repo.Lookup(committish) is { } objectId ? (GitCommit?)repo.GetCommit(objectId) : null);
        if (this.Commit is null && committish is object)
        {
            throw new ArgumentException("No matching commit found.", nameof(committish));
        }

        this.Repository = repo;
        this.VersionFile = new ManagedVersionFile(this);
    }

    public GitRepository Repository { get; }

    public GitCommit? Commit { get; private set; }

    /// <inheritdoc />
    public override VersionFile VersionFile { get; }

    /// <inheritdoc />
    public override string? GitCommitId => this.Commit?.Sha.ToString();

    /// <inheritdoc />
    public override bool IsHead => this.Repository.GetHeadCommit().Equals(this.Commit);

    /// <inheritdoc />
    public override DateTimeOffset? GitCommitDate => this.Commit is { } commit ? (commit.Author?.Date ?? this.Repository.GetCommit(commit.Sha, readAuthor: true).Author?.Date) : null;

    /// <inheritdoc />
    public override string HeadCanonicalName => this.Repository.GetHeadAsReferenceOrSha().ToString() ?? throw new InvalidOperationException("Unable to determine the HEAD position.");

    /// <inheritdoc />
    public override IReadOnlyCollection<string>? HeadTags
    {
        get => this.headTags ??= this.Repository.Lookup("HEAD") is GitObjectId head ? this.Repository.LookupTags(head) : null;
    }

    private string DebuggerDisplay => $"\"{this.WorkingTreePath}\" (managed)";

    /// <summary>Initializes a new instance of the <see cref="ManagedGitContext"/> class.</summary>
    /// <param name="path">The path to the .git directory or somewhere in a git working tree.</param>
    /// <param name="committish">The SHA-1 or ref for a git commit.</param>
    /// <returns>The new instance.</returns>
    public static ManagedGitContext Create(string path, string? committish = null)
    {
        FindGitPaths(path, out string? gitDirectory, out string? workingTreeDirectory, out string? workingTreeRelativePath);
        return new ManagedGitContext(workingTreeDirectory, gitDirectory, committish)
        {
            RepoRelativeProjectDirectory = workingTreeRelativePath,
        };
    }

    /// <inheritdoc />
    public override void ApplyTag(string name) => throw new NotSupportedException();

    /// <inheritdoc />
    public override bool TrySelectCommit(string committish)
    {
        if (this.Repository.Lookup(committish) is { } objectId)
        {
            this.Commit = this.Repository.GetCommit(objectId);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override void Stage(string path) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override string GetShortUniqueCommitId(int minLength)
    {
        Verify.Operation(this.Commit is object, "No commit is selected.");
        return this.Repository.ShortenObjectId(this.Commit.Value.Sha, minLength);
    }

    /// <inheritdoc/>
    internal override int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion)
    {
        SemanticVersion? headCommitVersion = committedVersion?.Version ?? SemVer0;

        if (IsVersionFileChangedInWorkingTree(committedVersion, workingVersion))
        {
            Version? workingCopyVersion = workingVersion?.Version?.Version;

            if (workingCopyVersion is null || !workingCopyVersion.Equals(headCommitVersion))
            {
                // The working copy has changed the major.minor version.
                // So by definition the version height is 0, since no commit represents it yet.
                return 0;
            }
        }

        return GitExtensions.GetVersionHeight(this);
    }

    /// <inheritdoc/>
    internal override Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight)
    {
        VersionOptions? version = IsVersionFileChangedInWorkingTree(committedVersion, workingVersion) ? workingVersion : committedVersion;

        return this.GetIdAsVersionHelper(version, versionHeight);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Repository.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Encodes a commit from history in a <see cref="Version"/>
    /// so that the original commit can be found later.
    /// </summary>
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
    private Version GetIdAsVersionHelper(VersionOptions? versionOptions, int versionHeight)
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
                    revision = this.Commit.HasValue
                        ? Math.Min(MaximumBuildNumberOrRevisionComponent, this.Commit.Value.GetTruncatedCommitIdAsUInt16())
                        : 0;
                    break;
            }
        }

        return VersionExtensions.Create(baseVersion.Major, baseVersion.Minor, buildNumber, revision);
    }
}
