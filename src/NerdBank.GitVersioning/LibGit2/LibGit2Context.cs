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
    internal LibGit2Context(string workingTreeDirectory, string dotGitPath, string? committish = null)
        : base(workingTreeDirectory, dotGitPath)
    {
        this.Repository = OpenGitRepo(workingTreeDirectory, useDefaultConfigSearchPaths: true);
        if (this.Repository.Info.WorkingDirectory is null)
        {
            throw new ArgumentException("Bare repositories not supported.", nameof(workingTreeDirectory));
        }

        this.Commit = committish is null ? this.Repository.Head.Tip : this.Repository.Lookup<Commit>(committish);
        if (this.Commit is null && committish is object)
        {
            throw new ArgumentException("No matching commit found.", nameof(committish));
        }

        this.VersionFile = new LibGit2VersionFile(this);
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
    public override DateTimeOffset? GitCommitDate => this.Commit?.Author.When;

    /// <inheritdoc />
    public override string HeadCanonicalName => this.Repository.Head.CanonicalName;

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

    /// <summary>
    /// Opens a <see cref="Repository"/> found at or above a specified path.
    /// </summary>
    /// <param name="path">The path to the .git directory or the working directory.</param>
    /// <param name="useDefaultConfigSearchPaths">
    /// Specifies whether to use default settings for looking up global and system settings.
    /// <para>
    /// By default (<paramref name="useDefaultConfigSearchPaths"/> == <see langword="false"/>), the repository will be configured to only
    /// use the repository-level configuration ignoring system or user-level configuration (set using <c>git config --global</c>.
    /// Thus only settings explicitly set for the repo will be available.
    /// </para>
    /// <para>
    /// For example using <c>Repository.Configuration.Get{string}("user.name")</c> to get the user's name will
    /// return the value set in the repository config or <see langword="null"/> if the user name has not been explicitly set for the repository.
    /// </para>
    /// <para>
    /// When the caller specifies to use the default configuration search paths (<paramref name="useDefaultConfigSearchPaths"/> == <see langword="true"/>)
    /// both repository level and global configuration will be available to the repo as well.
    /// </para>
    /// <para>
    /// In this mode, using <c>Repository.Configuration.Get{string}("user.name")</c> will return the
    /// value set in the user's global git configuration unless set on the repository level,
    /// matching the behavior of the <c>git</c> command.
    /// </para>
    /// </param>
    /// <returns>The <see cref="Repository"/> found for the specified path, or <see langword="null"/> if no git repo is found.</returns>
    internal static Repository OpenGitRepo(string path, bool useDefaultConfigSearchPaths = false)
    {
        if (useDefaultConfigSearchPaths)
        {
            // pass null to reset to defaults
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, null);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.System, null);
        }
        else
        {
            // Override Config Search paths to empty path to avoid new Repository instance to lookup for Global\System .gitconfig file
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, string.Empty);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.System, string.Empty);
        }

        return new Repository(path);
    }

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
}
