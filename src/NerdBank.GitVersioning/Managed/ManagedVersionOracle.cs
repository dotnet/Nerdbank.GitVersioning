using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nerdbank.GitVersioning;
using Validation;

namespace NerdBank.GitVersioning.Managed
{
    public class ManagedVersionOracle : VersionOracle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public static VersionOracle CreateManaged(string projectDirectory, string gitRepoDirectory = null, ICloudBuild cloudBuild = null, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            Requires.NotNull(projectDirectory, nameof(projectDirectory));
            if (string.IsNullOrEmpty(gitRepoDirectory))
            {
                gitRepoDirectory = projectDirectory;
            }

            GitRepository repository = GitRepository.Create(gitRepoDirectory);
            return new ManagedVersionOracle(projectDirectory, repository, null, cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        internal ManagedVersionOracle(string projectDirectory, GitRepository repo, GitCommit? head, ICloudBuild cloudBuild, int? overrideVersionHeightOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            var relativeRepoProjectDirectory = projectPathRelativeToGitRepoRoot ?? repo?.GetRepoRelativePath(projectDirectory);
            if (repo is object)
            {
                // If we're particularly git focused, normalize/reset projectDirectory to be the path we *actually* want to look at in case we're being redirected.
                projectDirectory = Path.Combine(repo.WorkingDirectory /* repo.Info.WorkingDirectory */, relativeRepoProjectDirectory);
            }

            var commit = head ?? repo?.GetHeadCommit();

            (var commitedVersionPath, var committedVersion) = VersionFile.GetVersionOptions(repo, commit, relativeRepoProjectDirectory);

            (var workingVersionPath, var workingVersion) = head.HasValue
                ? VersionFile.GetVersionOptions(repo, head.Value, relativeRepoProjectDirectory)
                : VersionFile.GetVersionOptions(projectDirectory);

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
            this.GitCommitDate = commit?.Author.Date;
            this.VersionHeight = CalculateVersionHeight(repo, relativeRepoProjectDirectory, commit, commitedVersionPath, committedVersion, workingVersion);
            // this.BuildingRef = cloudBuild?.BuildingTag ?? cloudBuild?.BuildingBranch ?? repo?.Head.CanonicalName;

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
                    throw new NotImplementedException();
                    // this.GitCommitIdShort = repo.ObjectDatabase.ShortenObjectId(commit, gitCommitIdShortAutoMinimum);
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

        private static int CalculateVersionHeight(GitRepository repository, string relativeRepoProjectDirectory, GitCommit? headCommit, string commitedVersionPath, VersionOptions committedVersion, VersionOptions workingVersion)
        {
            if (repository == null || headCommit == null)
            {
                return 0;
            }

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

            WalkingVersionResolver resolver = new WalkingVersionResolver(repository, commitedVersionPath);
            return resolver.GetGitHeight();
        }

        private static Version GetIdAsVersion(GitCommit? headCommit, VersionOptions committedVersion, VersionOptions workingVersion, int versionHeight)
        {
            var version = IsVersionFileChangedInWorkingTree(committedVersion, workingVersion) ? workingVersion : committedVersion;

            return headCommit.GetIdAsVersionHelper(version, versionHeight);
        }
    }
}
