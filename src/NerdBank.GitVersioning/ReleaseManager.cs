// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using LibGit2Sharp;
using Nerdbank.GitVersioning.LibGit2;
using Newtonsoft.Json;
using Validation;
using Version = System.Version;

namespace Nerdbank.GitVersioning;

/// <summary>
/// Methods for creating releases.
/// </summary>
/// <remarks>
/// This class authors git commits, branches, etc. and thus must use libgit2 rather than our internal managed implementation which is read-only.
/// </remarks>
public class ReleaseManager
{
    private readonly TextWriter stdout;
    private readonly TextWriter stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleaseManager"/> class.
    /// </summary>
    /// <param name="outputWriter">The <see cref="TextWriter"/> to write output to (e.g. <see cref="Console.Out" />).</param>
    /// <param name="errorWriter">The <see cref="TextWriter"/> to write error messages to (e.g. <see cref="Console.Error" />).</param>
    public ReleaseManager(TextWriter outputWriter = null, TextWriter errorWriter = null)
    {
        this.stdout = outputWriter ?? TextWriter.Null;
        this.stderr = errorWriter ?? TextWriter.Null;
    }

    /// <summary>
    /// Defines the possible errors that can occur when preparing a release.
    /// </summary>
    public enum ReleasePreparationError
    {
        /// <summary>
        /// The project directory is not a git repository.
        /// </summary>
        NoGitRepo,

        /// <summary>
        /// There are pending changes in the project directory.
        /// </summary>
        UncommittedChanges,

        /// <summary>
        /// The "branchName" setting in "version.json" is invalid.
        /// </summary>
        InvalidBranchNameSetting,

        /// <summary>
        /// version.json/version.txt not found.
        /// </summary>
        NoVersionFile,

        /// <summary>
        /// Updating the version would result in a version lower than the previous version.
        /// </summary>
        VersionDecrement,

        /// <summary>
        /// Branch cannot be set to the specified version because the new version is not higher than the current version.
        /// </summary>
        NoVersionIncrement,

        /// <summary>
        /// Cannot create a branch because it already exists.
        /// </summary>
        BranchAlreadyExists,

        /// <summary>
        /// Cannot create a commit because user name and user email are not configured (either at the repo or global level).
        /// </summary>
        UserNotConfigured,

        /// <summary>
        /// HEAD is detached. A branch must be checked out first.
        /// </summary>
        DetachedHead,

        /// <summary>
        /// The versionIncrement setting cannot be applied to the current version.
        /// </summary>
        InvalidVersionIncrementSetting,
    }

    /// <summary>
    /// Enumerates the output formats supported by <see cref="ReleaseManager"/>.
    /// </summary>
    public enum ReleaseManagerOutputMode
    {
        /// <summary>
        /// Use unstructured text output.
        /// </summary>
        Text = 0,

        /// <summary>
        /// Output information about the release as JSON.
        /// </summary>
        Json = 1,
    }

    /// <summary>
    /// Prepares a release for the specified directory by creating a release branch and incrementing the version in the current branch.
    /// </summary>
    /// <exception cref="ReleasePreparationException">Thrown when the release could not be created.</exception>
    /// <param name="projectDirectory">
    /// The path to the directory which may (or its ancestors may) define the version file.
    /// </param>
    /// <param name="releaseUnstableTag">
    /// The prerelease tag to add to the version on the release branch. Pass <see langword="null"/> to omit/remove the prerelease tag.
    /// The leading hyphen may be specified or omitted.
    /// </param>
    /// <param name="nextVersion">
    /// The next version to save to the version file on the current branch. Pass <see langword="null"/> to automatically determine the next
    /// version based on the current version and the <paramref name="versionIncrement"/> setting in <c>version.json</c>.
    /// Parameter will be ignored if the current branch is a release branch.
    /// </param>
    /// <param name="versionIncrement">
    /// The increment to apply in order to determine the next version on the current branch.
    /// If specified, value will be used instead of the increment specified in <c>version.json</c>.
    /// Parameter will be ignored if the current branch is a release branch.
    /// </param>
    /// <param name="outputMode">
    /// The output format to use for writing to stdout.
    /// </param>
    /// <param name="unformattedCommitMessage">
    /// An optional, custom message to use for the commit that sets the new version number. May use <c>{0}</c> to substitute the new version number.
    /// </param>
    public void PrepareRelease(string projectDirectory, string releaseUnstableTag = null, Version nextVersion = null, VersionOptions.ReleaseVersionIncrement? versionIncrement = null, ReleaseManagerOutputMode outputMode = default, string unformattedCommitMessage = null)
    {
        Requires.NotNull(projectDirectory, nameof(projectDirectory));

        // open the git repository
        LibGit2Context context = this.GetRepository(projectDirectory);
        Repository repository = context.Repository;

        if (repository.Info.IsHeadDetached)
        {
            this.stderr.WriteLine("Detached head. Check out a branch first.");
            throw new ReleasePreparationException(ReleasePreparationError.DetachedHead);
        }

        // get the current version
        VersionOptions versionOptions = context.VersionFile.GetVersion();
        if (versionOptions is null)
        {
            this.stderr.WriteLine($"Failed to load version file for directory '{projectDirectory}'.");
            throw new ReleasePreparationException(ReleasePreparationError.NoVersionFile);
        }

        string releaseBranchName = this.GetReleaseBranchName(versionOptions);
        string originalBranchName = repository.Head.FriendlyName;
        SemanticVersion releaseVersion = string.IsNullOrEmpty(releaseUnstableTag)
            ? versionOptions.Version.WithoutPrepreleaseTags()
            : versionOptions.Version.SetFirstPrereleaseTag(releaseUnstableTag);

        // check if the current branch is the release branch
        if (string.Equals(originalBranchName, releaseBranchName, StringComparison.OrdinalIgnoreCase))
        {
            if (outputMode == ReleaseManagerOutputMode.Text)
            {
                this.stdout.WriteLine($"{releaseBranchName} branch advanced from {versionOptions.Version} to {releaseVersion}.");
            }
            else
            {
                var releaseInfo = new ReleaseInfo(new ReleaseBranchInfo(releaseBranchName, repository.Head.Tip.Id.ToString(), releaseVersion));
                this.WriteToOutput(releaseInfo);
            }

            this.UpdateVersion(context, versionOptions.Version, releaseVersion, unformattedCommitMessage);
            return;
        }

        SemanticVersion nextDevVersion = this.GetNextDevVersion(versionOptions, nextVersion, versionIncrement);

        // check if the current version on the current branch is different from the next version
        // otherwise, both the release branch and the dev branch would have the same version
        if (versionOptions.Version.Version == nextDevVersion.Version)
        {
            this.stderr.WriteLine($"Version on '{originalBranchName}' is already set to next version {nextDevVersion.Version}.");
            throw new ReleasePreparationException(ReleasePreparationError.NoVersionIncrement);
        }

        // check if the release branch already exists
        if (repository.Branches[releaseBranchName] is not null)
        {
            this.stderr.WriteLine($"Cannot create branch '{releaseBranchName}' because it already exists.");
            throw new ReleasePreparationException(ReleasePreparationError.BranchAlreadyExists);
        }

        // create release branch and update version
        Branch releaseBranch = repository.CreateBranch(releaseBranchName);
        global::LibGit2Sharp.Commands.Checkout(repository, releaseBranch);
        this.UpdateVersion(context, versionOptions.Version, releaseVersion, unformattedCommitMessage);

        if (outputMode == ReleaseManagerOutputMode.Text)
        {
            this.stdout.WriteLine($"{releaseBranchName} branch now tracks v{releaseVersion} stabilization and release.");
        }

        // update version on main branch
        global::LibGit2Sharp.Commands.Checkout(repository, originalBranchName);
        this.UpdateVersion(context, versionOptions.Version, nextDevVersion, unformattedCommitMessage);

        if (outputMode == ReleaseManagerOutputMode.Text)
        {
            this.stdout.WriteLine($"{originalBranchName} branch now tracks v{nextDevVersion} development.");
        }

        // Merge release branch back to main branch
        var mergeOptions = new MergeOptions()
        {
            CommitOnSuccess = true,
            MergeFileFavor = MergeFileFavor.Ours,
        };
        repository.Merge(releaseBranch, this.GetSignature(repository), mergeOptions);

        if (outputMode == ReleaseManagerOutputMode.Json)
        {
            var originalBranchInfo = new ReleaseBranchInfo(originalBranchName, repository.Head.Tip.Sha, nextDevVersion);
            var releaseBranchInfo = new ReleaseBranchInfo(releaseBranchName, repository.Branches[releaseBranchName].Tip.Id.ToString(), releaseVersion);
            var releaseInfo = new ReleaseInfo(originalBranchInfo, releaseBranchInfo);

            this.WriteToOutput(releaseInfo);
        }
    }

    private static bool IsVersionDecrement(SemanticVersion oldVersion, SemanticVersion newVersion)
    {
        if (newVersion.Version > oldVersion.Version)
        {
            return false;
        }
        else if (newVersion.Version == oldVersion.Version)
        {
            return string.IsNullOrEmpty(oldVersion.Prerelease) &&
                  !string.IsNullOrEmpty(newVersion.Prerelease);
        }
        else
        {
            // newVersion.Version < oldVersion.Version
            return true;
        }
    }

    private string GetReleaseBranchName(VersionOptions versionOptions)
    {
        Requires.NotNull(versionOptions, nameof(versionOptions));

        string branchNameFormat = versionOptions.ReleaseOrDefault.BranchNameOrDefault;

        // ensure there is a '{version}' placeholder in the branch name
        if (string.IsNullOrEmpty(branchNameFormat) || !branchNameFormat.Contains("{version}"))
        {
            this.stderr.WriteLine($"Invalid 'branchName' setting '{branchNameFormat}'. Missing version placeholder '{{version}}'.");
            throw new ReleasePreparationException(ReleasePreparationError.InvalidBranchNameSetting);
        }

        // replace the "{version}" placeholder with the actual version
        return branchNameFormat.Replace("{version}", versionOptions.Version.Version.ToString());
    }

    private void UpdateVersion(LibGit2Context context, SemanticVersion oldVersion, SemanticVersion newVersion, string unformattedCommitMessage)
    {
        Requires.NotNull(context, nameof(context));

        Signature signature = this.GetSignature(context.Repository);
        VersionOptions versionOptions = context.VersionFile.GetVersion();

        if (IsVersionDecrement(oldVersion, newVersion))
        {
            this.stderr.WriteLine($"Cannot change version from {oldVersion} to {newVersion} because {newVersion} is older than {oldVersion}.");
            throw new ReleasePreparationException(ReleasePreparationError.VersionDecrement);
        }

        if (!EqualityComparer<SemanticVersion>.Default.Equals(versionOptions.Version, newVersion))
        {
            if (versionOptions.VersionHeightPosition.HasValue && SemanticVersion.WillVersionChangeResetVersionHeight(versionOptions.Version, newVersion, versionOptions.VersionHeightPosition.Value))
            {
                // The version will be reset by this change, so remove the version height offset property.
                versionOptions.VersionHeightOffset = null;
            }

            versionOptions.Version = newVersion;
            string filePath = context.VersionFile.SetVersion(context.AbsoluteProjectDirectory, versionOptions, includeSchemaProperty: true);

            global::LibGit2Sharp.Commands.Stage(context.Repository, filePath);

            // Author a commit only if we effectively changed something.
            if (!context.Repository.Head.Tip.Tree.Equals(context.Repository.Index.WriteToTree()))
            {
                if (string.IsNullOrEmpty(unformattedCommitMessage))
                {
                    unformattedCommitMessage = "Set version to '{0}'";
                }

                string commitMessage = string.Format(CultureInfo.CurrentCulture, unformattedCommitMessage, versionOptions.Version);
                context.Repository.Commit(commitMessage, signature, signature, new CommitOptions() { AllowEmptyCommit = false });
            }
        }
    }

    private Signature GetSignature(Repository repository)
    {
        Signature signature = repository.Config.BuildSignature(DateTimeOffset.Now);
        if (signature is null)
        {
            this.stderr.WriteLine("Cannot create commits in this repo because git user name and email are not configured.");
            throw new ReleasePreparationException(ReleasePreparationError.UserNotConfigured);
        }

        return signature;
    }

    private LibGit2Context GetRepository(string projectDirectory)
    {
        // open git repo and use default configuration (in order to commit we need a configured user name and email
        // which is most likely configured on a user/system level rather than the repo level.
        var context = GitContext.Create(projectDirectory, engine: GitContext.Engine.ReadWrite);
        if (!context.IsRepository)
        {
            this.stderr.WriteLine($"No git repository found above directory '{projectDirectory}'.");
            throw new ReleasePreparationException(ReleasePreparationError.NoGitRepo);
        }

        var libgit2context = (LibGit2Context)context;

        // abort if there are any pending changes
        RepositoryStatus status = libgit2context.Repository.RetrieveStatus();
        if (status.IsDirty)
        {
            // This filter copies the internal logic used by LibGit2 behind RepositoryStatus.IsDirty to tell if
            // a repo is dirty or not
            // Could be simplified if https://github.com/libgit2/libgit2sharp/pull/2004 is ever merged
            var changedFiles = status.Where(file => file.State != FileStatus.Ignored && file.State != FileStatus.Unaltered).ToList();
            string changesFilesFormatted = string.Join(Environment.NewLine, changedFiles.Select(t => $"- {t.FilePath} changed with {nameof(FileStatus)} {t.State}"));
            this.stderr.WriteLine($"No uncommitted changes are allowed, but {changedFiles.Count} are present in directory '{projectDirectory}':");
            this.stderr.WriteLine(changesFilesFormatted);
            throw new ReleasePreparationException(ReleasePreparationError.UncommittedChanges);
        }

        // check if repo is configured so we can create commits
        _ = this.GetSignature(libgit2context.Repository);

        return libgit2context;
    }

    private SemanticVersion GetNextDevVersion(VersionOptions versionOptions, Version nextVersionOverride, VersionOptions.ReleaseVersionIncrement? versionIncrementOverride)
    {
        SemanticVersion currentVersion = versionOptions.Version;

        SemanticVersion nextDevVersion;
        if (nextVersionOverride is not null)
        {
            nextDevVersion = new SemanticVersion(nextVersionOverride, currentVersion.Prerelease, currentVersion.BuildMetadata);
        }
        else
        {
            // Determine the increment to use:
            // Use parameter versionIncrementOverride if it has a value, otherwise use setting from version.json.
            VersionOptions.ReleaseVersionIncrement versionIncrement = versionIncrementOverride ?? versionOptions.ReleaseOrDefault.VersionIncrementOrDefault;

            // The increment is only valid if the current version has the required precision:
            //  - increment settings "Major" and "Minor" are always valid.
            //  - increment setting "Build" is only valid if the version has at lease three segments.
            bool isValidIncrement = versionIncrement != VersionOptions.ReleaseVersionIncrement.Build ||
                                   versionOptions.Version.Version.Build >= 0;

            if (!isValidIncrement)
            {
                this.stderr.WriteLine($"Cannot apply version increment 'build' to version '{versionOptions.Version}' because it only has major and minor segments.");
                throw new ReleasePreparationException(ReleasePreparationError.InvalidVersionIncrementSetting);
            }

            nextDevVersion = currentVersion.Increment(versionIncrement);
        }

        // return next version with prerelease tag specified in version.json
        return nextDevVersion.SetFirstPrereleaseTag(versionOptions.ReleaseOrDefault.FirstUnstableTagOrDefault);
    }

    private void WriteToOutput(ReleaseInfo releaseInfo)
    {
        string json = JsonConvert.SerializeObject(releaseInfo, Formatting.Indented, new SemanticVersionJsonConverter());
        this.stdout.WriteLine(json);
    }

    /// <summary>
    /// Exception indicating an error during preparation of a release.
    /// </summary>
    public class ReleasePreparationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReleasePreparationException"/> class.
        /// </summary>
        /// <param name="error">The error that occurred.</param>
        public ReleasePreparationException(ReleasePreparationError error) => this.Error = error;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReleasePreparationException"/> class.
        /// </summary>
        /// <param name="error">The error that occurred.</param>
        /// <param name="innerException">The inner exception.</param>
        public ReleasePreparationException(ReleasePreparationError error, Exception innerException)
            : base(null, innerException) => this.Error = error;

        /// <summary>
        /// Gets the error that occurred.
        /// </summary>
        public ReleasePreparationError Error { get; }
    }

    /// <summary>
    /// Encapsulates information on a release created through <see cref="ReleaseManager"/>.
    /// </summary>
    public class ReleaseInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseInfo"/> class.
        /// </summary>
        /// <param name="currentBranch">Information on the branch the release was created from.</param>
        public ReleaseInfo(ReleaseBranchInfo currentBranch)
            : this(currentBranch, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseInfo"/> class.
        /// </summary>
        /// <param name="currentBranch">Information on the branch the release was created from.</param>
        /// <param name="newBranch">Information on the newly created branch.</param>
        [JsonConstructor]
        public ReleaseInfo(ReleaseBranchInfo currentBranch, ReleaseBranchInfo newBranch)
        {
            Requires.NotNull(currentBranch, nameof(currentBranch));
            //// skip null check for newBranch, it is allowed to be null.

            this.CurrentBranch = currentBranch;
            this.NewBranch = newBranch;
        }

        /// <summary>
        /// Gets information on the 'current' branch, i.e. the branch the release was created from.
        /// </summary>
        public ReleaseBranchInfo CurrentBranch { get; }

        /// <summary>
        /// Gets information on the new branch created by <see cref="ReleaseManager"/>.
        /// </summary>
        /// <value>
        /// Information on the newly created branch as instance of <see cref="ReleaseBranchInfo"/> or <see langword="null"/>, if no new branch was created.
        /// </value>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public ReleaseBranchInfo NewBranch { get; }
    }

    /// <summary>
    /// Encapsulates information on a branch created or updated by <see cref="ReleaseManager"/>.
    /// </summary>
    public class ReleaseBranchInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseBranchInfo"/> class.
        /// </summary>
        /// <param name="name">The name of the branch.</param>
        /// <param name="commit">The id of the branch's tip.</param>
        /// <param name="version">The version configured in the branch's <c>version.json</c>.</param>
        public ReleaseBranchInfo(string name, string commit, SemanticVersion version)
        {
            Requires.NotNullOrWhiteSpace(name, nameof(name));
            Requires.NotNullOrWhiteSpace(commit, nameof(commit));
            Requires.NotNull(version, nameof(version));

            this.Name = name;
            this.Commit = commit;
            this.Version = version;
        }

        /// <summary>
        /// Gets the name of the branch, e.g. <c>main</c>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the id of the branch's tip commit after the update.
        /// </summary>
        public string Commit { get; }

        /// <summary>
        /// Gets the version configured in the branch's <c>version.json</c>.
        /// </summary>
        public SemanticVersion Version { get; }
    }
}
