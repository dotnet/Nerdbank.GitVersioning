namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Validation;

    /// <summary>
    /// Assembles version information in a variety of formats.
    /// </summary>
    public class VersionOracle
    {
        /// <summary>
        /// The 0.0 version.
        /// </summary>
        private static readonly Version Version0 = new Version(0, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public static VersionOracle Create(string projectDirectory, string gitRepoDirectory = null, ICloudBuild cloudBuild = null, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            Requires.NotNull(projectDirectory, nameof(projectDirectory));
            if (string.IsNullOrEmpty(gitRepoDirectory))
            {
                gitRepoDirectory = projectDirectory;
            }

            using (var git = GitExtensions.OpenGitRepo(gitRepoDirectory))
            {
                return new VersionOracle(projectDirectory, git, null, cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public VersionOracle(string projectDirectory, LibGit2Sharp.Repository repo, ICloudBuild cloudBuild, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
            : this(projectDirectory, repo, null, cloudBuild, overrideBuildNumberOffset, projectPathRelativeToGitRepoRoot)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public VersionOracle(string projectDirectory, LibGit2Sharp.Repository repo, LibGit2Sharp.Commit head, ICloudBuild cloudBuild, int? overrideBuildNumberOffset = null, string projectPathRelativeToGitRepoRoot = null)
        {
            var repoRoot = repo?.Info?.WorkingDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relativeRepoProjectDirectory = !string.IsNullOrWhiteSpace(repoRoot)
                ? (!string.IsNullOrEmpty(projectPathRelativeToGitRepoRoot)
                    ? projectPathRelativeToGitRepoRoot
                    : projectDirectory.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : null;

            var commit = head ?? repo?.Head.Commits.FirstOrDefault();

            var committedVersion = VersionFile.GetVersion(commit, relativeRepoProjectDirectory);

            var workingVersion = head != null ? VersionFile.GetVersion(head, relativeRepoProjectDirectory) : VersionFile.GetVersion(projectDirectory);

            if (overrideBuildNumberOffset.HasValue)
            {
                if (committedVersion != null)
                {
                    committedVersion.BuildNumberOffset = overrideBuildNumberOffset.Value;
                }

                if (workingVersion != null)
                {
                    workingVersion.BuildNumberOffset = overrideBuildNumberOffset.Value;
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

            this.VersionHeightOffset = this.VersionOptions?.BuildNumberOffsetOrDefault ?? 0;

            this.PrereleaseVersion = this.ReplaceMacros(this.VersionOptions?.Version?.Prerelease ?? string.Empty);

            this.CloudBuildNumberOptions = this.VersionOptions?.CloudBuild?.BuildNumberOrDefault ?? VersionOptions.CloudBuildNumberOptions.DefaultInstance;

            if (!string.IsNullOrEmpty(this.BuildingRef) && this.VersionOptions?.PublicReleaseRefSpec?.Length > 0)
            {
                this.PublicRelease = this.VersionOptions.PublicReleaseRefSpec.Any(
                    expr => Regex.IsMatch(this.BuildingRef, expr));
            }
        }

        /// <summary>
        /// Gets the BuildNumber to set the cloud build to (if applicable).
        /// </summary>
        public string CloudBuildNumber
        {
            get
            {
                var commitIdOptions = this.CloudBuildNumberOptions.IncludeCommitIdOrDefault;
                bool includeCommitInfo = commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.Always ||
                    (commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.NonPublicReleaseOnly && !this.PublicRelease);
                bool commitIdInRevision = includeCommitInfo && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.FourthVersionComponent;
                bool commitIdInBuildMetadata = includeCommitInfo && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.BuildMetadata;
                Version buildNumberVersion = commitIdInRevision ? this.Version : this.SimpleVersion;
                string buildNumberMetadata = FormatBuildMetadata(commitIdInBuildMetadata ? this.BuildMetadataWithCommitId : this.BuildMetadata);
                return buildNumberVersion + this.PrereleaseVersion + buildNumberMetadata;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the cloud build number should be set.
        /// </summary>
        [Ignore]
        public bool CloudBuildNumberEnabled => this.CloudBuildNumberOptions.EnabledOrDefault;

        /// <summary>
        /// Gets the build metadata identifiers, including the git commit ID as the first identifier if appropriate.
        /// </summary>
        [Ignore]
        public IEnumerable<string> BuildMetadataWithCommitId
        {
            get
            {
                if (!string.IsNullOrEmpty(this.GitCommitId))
                {
                    yield return this.GitCommitIdShort;
                }

                foreach (string identifier in this.BuildMetadata)
                {
                    yield return identifier;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether a version.json or version.txt file was found.
        /// </summary>
        public bool VersionFileFound => this.VersionOptions != null;

        /// <summary>
        /// Gets the version options used to initialize this instance.
        /// </summary>
        public VersionOptions VersionOptions { get; }

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyVersionAttribute"/>.
        /// </summary>
        public Version AssemblyVersion => GetAssemblyVersion(this.Version, this.VersionOptions).EnsureNonNegativeComponents();

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyFileVersionAttribute"/>.
        /// </summary>
        public Version AssemblyFileVersion => this.Version;

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
        /// </summary>
        public string AssemblyInformationalVersion =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}{FormatBuildMetadata(this.BuildMetadataWithCommitId)}";

        /// <summary>
        /// Gets or sets a value indicating whether the project is building
        /// in PublicRelease mode.
        /// </summary>
        public bool PublicRelease { get; set; }

        /// <summary>
        /// Gets the prerelease version information, including a leading hyphen.
        /// </summary>
        public string PrereleaseVersion { get; }

        /// <summary>
        /// Gets the prerelease version information, omitting the leading hyphen, if any.
        /// </summary>
        public string PrereleaseVersionNoLeadingHyphen => this.PrereleaseVersion?.TrimStart('-');

        /// <summary>
        /// Gets the version information without a Revision component.
        /// </summary>
        public Version SimpleVersion => this.Version.Build >= 0
                ? new Version(this.Version.Major, this.Version.Minor, this.Version.Build)
                : new Version(this.Version.Major, this.Version.Minor);

        /// <summary>
        /// Gets the build number (i.e. third integer, or PATCH) for this version.
        /// </summary>
        public int BuildNumber => Math.Max(0, this.Version.Build);

        /// <summary>
        /// Gets the <see cref="Version.Revision"/> component of the <see cref="Version"/>.
        /// </summary>
        public int VersionRevision => this.Version.Revision;

        /// <summary>
        /// Gets the major.minor version string.
        /// </summary>
        /// <value>
        /// The x.y string (no build number or revision number).
        /// </value>
        public Version MajorMinorVersion => new Version(this.Version.Major, this.Version.Minor);

        /// <summary>
        /// Gets the <see cref="Version.Major"/> component of the <see cref="Version"/>.
        /// </summary>
        public int VersionMajor => this.Version.Major;

        /// <summary>
        /// Gets the <see cref="Version.Minor"/> component of the <see cref="Version"/>.
        /// </summary>
        public int VersionMinor => this.Version.Minor;

        /// <summary>
        /// Gets the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        public string GitCommitId { get; }

        /// <summary>
        /// Gets the first several characters of the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        public string GitCommitIdShort { get; }

        /// <summary>
        /// Gets the Git revision control commit date for HEAD (the current source code version).
        /// </summary>
        public DateTimeOffset? GitCommitDate { get; }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at HEAD.
        /// </summary>
        public int VersionHeight { get; }

        /// <summary>
        /// The offset to add to the <see cref="VersionHeight"/>
        /// when calculating the integer to use as the <see cref="BuildNumber"/>
        /// or elsewhere that the {height} macro is used.
        /// </summary>
        public int VersionHeightOffset { get; }

        private string BuildingRef { get; }

        /// <summary>
        /// Gets the version for this project, with up to 4 components.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Gets a value indicating whether to set all cloud build variables prefaced with "NBGV_".
        /// </summary>
        [Ignore]
        public bool CloudBuildAllVarsEnabled => this.VersionOptions?.CloudBuildOrDefault.SetAllVariablesOrDefault
            ?? VersionOptions.CloudBuildOptions.DefaultInstance.SetAllVariablesOrDefault;

        /// <summary>
        /// Gets a dictionary of all cloud build variables that applies to this project,
        /// regardless of the current setting of <see cref="CloudBuildAllVarsEnabled"/>.
        /// </summary>
        [Ignore]
        public IDictionary<string, string> CloudBuildAllVars
        {
            get
            {
                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var properties = this.GetType().GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    if (property.GetCustomAttribute<IgnoreAttribute>() == null)
                    {
                        var value = property.GetValue(this);
                        if (value != null)
                        {
                            variables.Add($"NBGV_{property.Name}", value.ToString());
                        }
                    }
                }

                return variables;
            }
        }

        /// <summary>
        /// Gets a value indicating whether to set cloud build version variables.
        /// </summary>
        [Ignore]
        public bool CloudBuildVersionVarsEnabled => this.VersionOptions?.CloudBuildOrDefault.SetVersionVariablesOrDefault
            ?? VersionOptions.CloudBuildOptions.DefaultInstance.SetVersionVariablesOrDefault;

        /// <summary>
        /// Gets a dictionary of cloud build variables that applies to this project,
        /// regardless of the current setting of <see cref="CloudBuildVersionVarsEnabled"/>.
        /// </summary>
        [Ignore]
        public IDictionary<string, string> CloudBuildVersionVars
        {
            get
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "GitAssemblyInformationalVersion", this.AssemblyInformationalVersion },
                    { "GitBuildVersion", this.Version.ToString() },
                    { "GitBuildVersionSimple", this.SimpleVersion.ToString() },
                };
            }
        }

        /// <summary>
        /// Gets the list of build metadata identifiers to include in semver version strings.
        /// </summary>
        [Ignore]
        public List<string> BuildMetadata { get; } = new List<string>();

        /// <summary>
        /// Gets the +buildMetadata fragment for the semantic version.
        /// </summary>
        public string BuildMetadataFragment => FormatBuildMetadata(this.BuildMetadataWithCommitId);

        /// <summary>
        /// Gets the version to use for NuGet packages.
        /// </summary>
        public string NuGetPackageVersion => this.VersionOptions?.NuGetPackageVersionOrDefault.SemVerOrDefault == 1 ? this.NuGetSemVer1 : this.SemVer2;

        /// <summary>
        /// Gets the version to use for Chocolatey packages.
        /// </summary>
        /// <remarks>
        /// This always returns the NuGet subset of SemVer 1.0.
        /// </remarks>
        public string ChocolateyPackageVersion => this.NuGetSemVer1;

        /// <summary>
        /// Gets the version to use for NPM packages.
        /// </summary>
        public string NpmPackageVersion => this.SemVer2;

        /// <summary>
        /// Gets a SemVer 1.0 compliant string that represents this version, including the -COMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        public string SemVer1 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersionSemVer1}{this.SemVer1BuildMetadata}";

        /// <summary>
        /// Gets a SemVer 2.0 compliant string that represents this version, including a +COMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        public string SemVer2 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}{this.SemVer2BuildMetadata}";

        /// <summary>
        /// Gets the minimum number of digits to use for numeric identifiers in SemVer 1.
        /// </summary>
        public int SemVer1NumericIdentifierPadding => this.VersionOptions?.SemVer1NumericIdentifierPaddingOrDefault ?? 4;

        /// <summary>
        /// Gets the build metadata, compliant to the NuGet-compatible subset of SemVer 1.0.
        /// </summary>
        /// <remarks>
        /// When adding the git commit ID in a -prerelease tag, prefix a `g` because
        /// older NuGet clients (the ones that support only a subset of semver 1.0)
        /// cannot handle prerelease tags that begin with a number (which a git commit ID might).
        /// See <see href="https://github.com/AArnott/Nerdbank.GitVersioning/issues/260#issuecomment-445511898">this discussion</see>.
        /// </remarks>
        private string NuGetSemVer1BuildMetadata =>
            this.PublicRelease ? string.Empty : $"-g{this.GitCommitIdShort}";

        /// <summary>
        /// Gets the build metadata, compliant to SemVer 1.0.
        /// </summary>
        private string SemVer1BuildMetadata =>
            this.PublicRelease ? string.Empty : $"-{this.GitCommitIdShort}";

        /// <summary>
        /// Gets a SemVer 1.0 compliant string that represents this version, including the -gCOMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        private string NuGetSemVer1 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersionSemVer1}{this.NuGetSemVer1BuildMetadata}";

        /// <summary>
        /// Gets the build metadata that is appropriate for SemVer2 use.
        /// </summary>
        /// <remarks>
        /// We always put the commit ID in the -prerelease tag for non-public releases.
        /// But for public releases, we don't include it in the +buildMetadata section since it may be confusing for NuGet.
        /// See https://github.com/AArnott/Nerdbank.GitVersioning/pull/132#issuecomment-307208561
        /// </remarks>
        private string SemVer2BuildMetadata =>
            (this.PublicRelease ? string.Empty : this.GitCommitIdShortForNonPublicPrereleaseTag) + FormatBuildMetadata(this.BuildMetadata);

        private string PrereleaseVersionSemVer1 => SemanticVersionExtensions.MakePrereleaseSemVer1Compliant(this.PrereleaseVersion, this.SemVer1NumericIdentifierPadding);

        /// <summary>
        /// Gets the -gc0ffee or .gc0ffee suffix for the version.
        /// </summary>
        /// <remarks>
        /// The `g` prefix to the commit ID is to remain SemVer2 compliant particularly when the partial commit ID we use is made up entirely of numerals.
        /// SemVer2 forbids numerals to begin with leading zeros, but a git commit just might, so we begin with `g` always to avoid failures when the commit ID happens to be problematic.
        /// </remarks>
        private string GitCommitIdShortForNonPublicPrereleaseTag => (string.IsNullOrEmpty(this.PrereleaseVersion) ? "-" : ".") + "g" + this.GitCommitIdShort;

        private VersionOptions.CloudBuildNumberOptions CloudBuildNumberOptions { get; }

        private int VersionHeightWithOffset => this.VersionHeight + this.VersionHeightOffset;

        private static string FormatBuildMetadata(IEnumerable<string> identifiers) =>
            (identifiers?.Any() ?? false) ? "+" + string.Join(".", identifiers) : string.Empty;

        private static string FormatBuildMetadataSemVerV1(IEnumerable<string> identifiers) =>
            (identifiers?.Any() ?? false) ? "-" + string.Join("-", identifiers) : string.Empty;

        private static Version GetAssemblyVersion(Version version, VersionOptions versionOptions)
        {
            // If there is no repo, "version" could have uninitialized components (-1).
            version = version.EnsureNonNegativeComponents();

            var assemblyVersion = versionOptions?.AssemblyVersionOrDefault.Version ?? new System.Version(version.Major, version.Minor);
            var precision = versionOptions?.AssemblyVersionOrDefault.PrecisionOrDefault;

            assemblyVersion = new System.Version(
                assemblyVersion.Major,
                precision >= VersionOptions.VersionPrecision.Minor ? assemblyVersion.Minor : 0,
                precision >= VersionOptions.VersionPrecision.Build ? version.Build : 0,
                precision >= VersionOptions.VersionPrecision.Revision ? version.Revision : 0);
            return assemblyVersion.EnsureNonNegativeComponents(4);
        }

        /// <summary>
        /// Replaces any macros found in a prerelease or build metadata string.
        /// </summary>
        /// <param name="prereleaseOrBuildMetadata">The prerelease or build metadata.</param>
        /// <returns>The specified string, with macros substituted for actual values.</returns>
        private string ReplaceMacros(string prereleaseOrBuildMetadata) => prereleaseOrBuildMetadata?.Replace("{height}", this.VersionHeightWithOffset.ToString(CultureInfo.InvariantCulture));

        private static int CalculateVersionHeight(string relativeRepoProjectDirectory, LibGit2Sharp.Commit headCommit, VersionOptions committedVersion, VersionOptions workingVersion)
        {
            var headCommitVersion = committedVersion?.Version?.Version ?? Version0;

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

            return headCommit?.GetHeight(c => c.CommitMatchesMajorMinorVersion(headCommitVersion, relativeRepoProjectDirectory)) ?? 0;
        }

        private static Version GetIdAsVersion(LibGit2Sharp.Commit headCommit, VersionOptions committedVersion, VersionOptions workingVersion, int versionHeight)
        {
            var version = IsVersionFileChangedInWorkingTree(committedVersion, workingVersion) ? workingVersion : committedVersion;

            return headCommit.GetIdAsVersionHelper(version, versionHeight);
        }

        private static bool IsVersionFileChangedInWorkingTree(VersionOptions committedVersion, VersionOptions workingVersion)
        {
            if (workingVersion != null)
            {
                return !EqualityComparer<VersionOptions>.Default.Equals(workingVersion, committedVersion);
            }

            // A missing working version is a change only if it was previously commited.
            return committedVersion != null;
        }

        [AttributeUsage(AttributeTargets.Property)]
        private class IgnoreAttribute : Attribute
        {
        }
    }
}
