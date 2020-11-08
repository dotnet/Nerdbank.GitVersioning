using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.ManagedGit;
using Validation;

namespace Nerdbank.GitVersioning.Managed
{
    /// <summary>
    /// An implementation of the <see cref="VersionOptions"/> class which uses LibGit2 as its back-end.
    /// </summary>
    public class ManagedVersionOracle : VersionOracle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public static new VersionOracle Create(string projectDirectory, string gitRepoDirectory = null, string head = null, ICloudBuild cloudBuild = null, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            Requires.NotNull(projectDirectory, nameof(projectDirectory));
            if (string.IsNullOrEmpty(gitRepoDirectory))
            {
                gitRepoDirectory = projectDirectory;
            }

            using var git = GitRepository.Create(gitRepoDirectory);
            return new ManagedVersionOracle(projectDirectory, git, head == null ? (GitCommit?)null : git.GetCommit(GitObjectId.Parse(head), readAuthor: true), cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public ManagedVersionOracle(string projectDirectory, GitRepository repo, ICloudBuild cloudBuild, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
            : this(projectDirectory, repo, null, cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public ManagedVersionOracle(string projectDirectory, GitRepository repo, GitCommit? head, ICloudBuild cloudBuild, int? overrideVersionHeightOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            var relativeRepoProjectDirectory = projectPathRelativeToGitRepoRoot ?? repo?.GetRepoRelativePath(projectDirectory);
            if (repo is object)
            {
                // If we're particularly git focused, normalize/reset projectDirectory to be the path we *actually* want to look at in case we're being redirected.
                projectDirectory = Path.Combine(repo.WorkingDirectory, relativeRepoProjectDirectory);
            }

            var commit = head ?? repo?.GetHeadCommit(readAuthor: true);

            var committedVersion = VersionFile.GetVersion(repo, commit, relativeRepoProjectDirectory);

            var workingVersion = head is object ? VersionFile.GetVersion(repo, head.Value, relativeRepoProjectDirectory) : VersionFile.GetVersion(projectDirectory);

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

            this.GitCommitId = commit?.Sha.ToString() ?? cloudBuild?.GitCommitId ?? null;
            this.GitCommitDate = commit?.Author?.Date;
            this.VersionHeight = CalculateVersionHeight(repo, relativeRepoProjectDirectory, commit, committedVersion, workingVersion);
            this.BuildingRef = cloudBuild?.BuildingTag ?? cloudBuild?.BuildingBranch ?? repo?.GetHeadAsReferenceOrSha() as string;

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
                    this.GitCommitIdShort = repo.ShortenObjectId(commit.Value.Sha, gitCommitIdShortAutoMinimum);
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

        private static int CalculateVersionHeight(GitRepository repository, string relativeRepoProjectDirectory, GitCommit? headCommit, VersionOptions committedVersion, VersionOptions workingVersion)
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

            if (headCommit == null)
            {
                return 0;
            }

            return GitExtensions.GetVersionHeight(repository, headCommit.Value, relativeRepoProjectDirectory);
        }

        private static Version GetIdAsVersion(GitCommit? headCommit, VersionOptions committedVersion, VersionOptions workingVersion, int versionHeight)
        {
            var version = IsVersionFileChangedInWorkingTree(committedVersion, workingVersion) ? workingVersion : committedVersion;

            return headCommit.GetIdAsVersionHelper(version, versionHeight);
        }
    }
}
