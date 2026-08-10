// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics;
using LibGit2Sharp;

namespace Nerdbank.GitVersioning.LibGit2;

/// <summary>
/// A git context implemented in terms of LibGit2Sharp.
/// </summary>
[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
public class LibGit2Context : GitContext
{
    /// <summary>
    /// Caching field behind <see cref="HeadTags" /> property.
    /// </summary>
    private IReadOnlyCollection<string>? headTags;

    internal LibGit2Context(string workingTreeDirectory, string dotGitPath, string? committish = null)
        : base(workingTreeDirectory, dotGitPath)
    {
        // LibGit2Sharp config search paths are process-global, so do not reset them while other contexts may be active.
        Repository repository = new(workingTreeDirectory);
        try
        {
            if (repository.Info.WorkingDirectory is null)
            {
                throw new ArgumentException("Bare repositories not supported.", nameof(workingTreeDirectory));
            }

            this.Repository = repository;
            this.Commit = committish is null ? repository.Head.Tip : repository.Lookup<Commit>(committish);
            if (this.Commit is null && committish is object)
            {
                throw new ArgumentException("No matching commit found.", nameof(committish));
            }

            this.VersionFile = new LibGit2VersionFile(this);
        }
        catch
        {
            repository.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public override VersionFile VersionFile { get; }

    public Repository Repository { get; }

    public Commit? Commit { get; private set; }

    /// <inheritdoc />
    public override string? GitCommitId => this.Commit?.Sha;

    /// <inheritdoc />
    public override bool IsHead => this.Repository.Head?.Tip?.Equals(this.Commit) ?? false;

    /// <inheritdoc />
    public override DateTimeOffset? GitCommitDate => this.Commit?.Committer.When;

    /// <inheritdoc />
    public override DateTimeOffset? GitCommitAuthorDate => this.Commit?.Author.When;

    /// <inheritdoc />
    public override string HeadCanonicalName => this.Repository.Head.CanonicalName;

    /// <inheritdoc />
    public override IReadOnlyCollection<string>? HeadTags
    {
        get
        {
            return this.headTags ??= this.Commit is not null
                ? this.Repository.Tags
                    .Where(tag => tag.Target.Sha.Equals(this.Commit.Sha))
                    .Select(tag => tag.CanonicalName)
                    .ToList()
                : null;
        }
    }

    private string DebuggerDisplay => $"\"{this.WorkingTreePath}\" (libgit2)";

    /// <summary>Initializes a new instance of the <see cref="LibGit2Context"/> class.</summary>
    /// <param name="path">The path to the .git directory or somewhere in a git working tree.</param>
    /// <param name="committish">The SHA-1 or ref for a git commit.</param>
    /// <returns>The new instance.</returns>
    public static LibGit2Context Create(string path, string? committish = null)
    {
        FindGitPaths(path, out string? gitDirectory, out string? workingTreeDirectory, out string? workingTreeRelativePath);
        return new LibGit2Context(workingTreeDirectory, gitDirectory, committish)
        {
            RepoRelativeProjectDirectory = workingTreeRelativePath,
        };
    }

    /// <inheritdoc />
    public override bool IsIgnored(string path) => this.Repository.Ignore.IsPathIgnored(this.GetRepoRelativePath(path, replaceBackslashes: true));

    /// <inheritdoc />
    public override void ApplyTag(string name) => this.Repository.Tags.Add(name, this.Commit);

    /// <inheritdoc />
    public override bool TrySelectCommit(string committish)
    {
        try
        {
            this.Repository.RevParse(committish, out Reference? reference, out GitObject obj);
            if (obj is Commit commit)
            {
                this.Commit = commit;
                return true;
            }
        }
        catch (NotFoundException)
        {
        }

        return false;
    }

    /// <inheritdoc />
    public override void Stage(string path) => global::LibGit2Sharp.Commands.Stage(this.Repository, path);

    /// <inheritdoc/>
    public override string GetShortUniqueCommitId(int minLength) => this.Repository.ObjectDatabase.ShortenObjectId(this.Commit, minLength);

    /// <inheritdoc/>
    internal override int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion)
    {
        SemanticVersion? headCommitVersion = committedVersion?.Version ?? SemVer0;

        if (IsVersionFileChangedInWorkingTree(committedVersion, workingVersion))
        {
            System.Version? workingCopyVersion = workingVersion?.Version?.Version;

            if (workingCopyVersion is null || !workingCopyVersion.Equals(headCommitVersion))
            {
                // The working copy has changed the major.minor version.
                // So by definition the version height is 0, since no commit represents it yet.
                return 0;
            }
        }

        return LibGit2GitExtensions.GetVersionHeight(this);
    }

    /// <inheritdoc/>
    internal override System.Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight)
    {
        VersionOptions? version = IsVersionFileChangedInWorkingTree(committedVersion, workingVersion) ? workingVersion : committedVersion;

        return this.Commit.GetIdAsVersionHelper(version, versionHeight);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Repository.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    private protected override IReadOnlyCollection<string> GetRemoteNames()
        => this.Repository.Network.Remotes.Select(remote => remote.Name).ToArray();

    /// <inheritdoc />
    private protected override string? GetRemoteDefaultBranch(string remoteName)
    {
        string targetPrefix = $"refs/remotes/{remoteName}/";
        return this.Repository.Refs[$"refs/remotes/{remoteName}/HEAD"]?.TargetIdentifier is string targetIdentifier
            && targetIdentifier.StartsWith(targetPrefix, StringComparison.Ordinal)
            ? targetIdentifier.Substring(targetPrefix.Length)
            : null;
    }

    /// <inheritdoc />
    private protected override IReadOnlyCollection<string> GetLocalBranchNames()
        => this.Repository.Branches.Where(branch => !branch.IsRemote).Select(branch => branch.FriendlyName).ToArray();

    /// <inheritdoc />
    private protected override string? GetConfiguredDefaultBranch()
        => this.Repository.Config.Get<string>("init.defaultBranch")?.Value;
}
