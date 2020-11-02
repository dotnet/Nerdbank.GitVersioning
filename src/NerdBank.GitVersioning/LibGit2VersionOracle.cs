using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Validation;
using static LibGit2Sharp.RepositoryExtensions;

namespace Nerdbank.GitVersioning
{
    /// <summary>
    /// An implementation of the <see cref="VersionOptions"/> class which uses LibGit2 as its back-end.
    /// </summary>
    public class LibGit2VersionOracle : VersionOracle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public static VersionOracle CreateLibGit2(string projectDirectory, string gitRepoDirectory = null, string head = null, ICloudBuild cloudBuild = null, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            Requires.NotNull(projectDirectory, nameof(projectDirectory));
            if (string.IsNullOrEmpty(gitRepoDirectory))
            {
                gitRepoDirectory = projectDirectory;
            }

            using (var git = GitExtensions.OpenGitRepo(gitRepoDirectory))
            {
                return new LibGit2VersionOracle(projectDirectory, git, head == null ? null : git?.Lookup<LibGit2Sharp.Commit>(head), cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public LibGit2VersionOracle(string projectDirectory, LibGit2Sharp.Repository repo, ICloudBuild cloudBuild, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
            : this(projectDirectory, repo, null, cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public LibGit2VersionOracle(string projectDirectory, LibGit2Sharp.Repository repo, LibGit2Sharp.Commit head, ICloudBuild cloudBuild, int? overrideVersionHeightOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            var relativeRepoProjectDirectory = projectPathRelativeToGitRepoRoot ?? repo?.GetRepoRelativePath(projectDirectory);
            if (repo is object)
            {
                // If we're particularly git focused, normalize/reset projectDirectory to be the path we *actually* want to look at in case we're being redirected.
                projectDirectory = Path.Combine(repo.Info.WorkingDirectory, relativeRepoProjectDirectory);
            }

            var commit = head ?? repo?.Head.Tip;

            var committedVersion = VersionFile.GetVersion(commit, relativeRepoProjectDirectory);

            var workingVersion = head is object ? VersionFile.GetVersion(head, relativeRepoProjectDirectory) : VersionFile.GetVersion(projectDirectory);

            if (overrideVersionHeightOffset.HasValue)
            {
                if (committedVersion != null)
                {
                    committedVersion.VersionHeightOffset = overrideVersionHeightOffset.Value;
                }

                if (workingVersion != null)
                {
                    workingVersion.VersionHeightOffset = overrideVersionHeightOffset.Value;
                }
            }

            this.VersionOptions = committedVersion ?? workingVersion;

            this.GitCommitId = commit?.Id.Sha ?? cloudBuild?.GitCommitId ?? null;
            this.GitCommitDate = commit?.Author.When;
            this.VersionHeight = CalculateVersionHeight(relativeRepoProjectDirectory, commit, committedVersion, workingVersion);
            this.BuildingRef = cloudBuild?.BuildingTag ?? cloudBuild?.BuildingBranch ?? repo?.Head.CanonicalName;

            // Override the typedVersion with the special build number and revision components, when available.
            if (repo != null)
            {
                this.Version = GetIdAsVersion(commit, committedVersion, workingVersion, this.VersionHeight);
            }
            else
            {
                this.Version = this.VersionOptions?.Version.Version ?? Version0;
            }

            // get the commit id abbreviation only if the commit id is set
            if (!string.IsNullOrEmpty(this.GitCommitId))
            {
                var gitCommitIdShortFixedLength = this.VersionOptions?.GitCommitIdShortFixedLength ?? VersionOptions.DefaultGitCommitIdShortFixedLength;
                var gitCommitIdShortAutoMinimum = this.VersionOptions?.GitCommitIdShortAutoMinimum ?? 0;
                // get it from the git repository if there is a repository present and it is enabled
                if (repo != null && gitCommitIdShortAutoMinimum > 0)
                {
                    this.GitCommitIdShort = repo.ObjectDatabase.ShortenObjectId(commit, gitCommitIdShortAutoMinimum);
                }
                else
                {
                    this.GitCommitIdShort = this.GitCommitId.Substring(0, gitCommitIdShortFixedLength);
                }
            }

            this.VersionHeightOffset = this.VersionOptions?.VersionHeightOffsetOrDefault ?? 0;

            this.PrereleaseVersion = this.ReplaceMacros(this.VersionOptions?.Version?.Prerelease ?? string.Empty);

            this.CloudBuildNumberOptions = this.VersionOptions?.CloudBuild?.BuildNumberOrDefault ?? VersionOptions.CloudBuildNumberOptions.DefaultInstance;

            if (!string.IsNullOrEmpty(this.BuildingRef) && this.VersionOptions?.PublicReleaseRefSpec?.Count > 0)
            {
                this.PublicRelease = this.VersionOptions.PublicReleaseRefSpec.Any(
                    expr => Regex.IsMatch(this.BuildingRef, expr));
            }
        }

        private static int CalculateVersionHeight(string relativeRepoProjectDirectory, LibGit2Sharp.Commit headCommit, VersionOptions committedVersion, VersionOptions workingVersion)
        {
            var headCommitVersion = committedVersion?.Version ?? SemVer0;

            if (IsVersionFileChangedInWorkingTree(committedVersion, workingVersion))
            {
                var workingCopyVersion = workingVersion?.Version?.Version;

                if (workingCopyVersion == null || !workingCopyVersion.Equals(headCommitVersion))
                {
                    // The working copy has changed the major.minor version.
                    // So by definition the version height is 0, since no commit represents it yet.
                    return 0;
                }
            }

            return headCommit?.GetVersionHeight(relativeRepoProjectDirectory) ?? 0;
        }

        private static Version GetIdAsVersion(LibGit2Sharp.Commit headCommit, VersionOptions committedVersion, VersionOptions workingVersion, int versionHeight)
        {
            var version = IsVersionFileChangedInWorkingTree(committedVersion, workingVersion) ? workingVersion : committedVersion;

            return headCommit.GetIdAsVersionHelper(version, versionHeight);
        }
    }
}
