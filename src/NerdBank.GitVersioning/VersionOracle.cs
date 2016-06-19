namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Validation;

    /// <summary>
    /// Assembles version information in a variety of formats.
    /// </summary>
    public class VersionOracle
    {
        /// <summary>
        /// The cloud build suppport, if any.
        /// </summary>
        private readonly ICloudBuild cloudBuild;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        /// <param name="projectDirectory"></param>
        /// <param name="gitRepoDirectory"></param>
        /// <param name="cloudBuild"></param>
        /// <returns></returns>
        public static VersionOracle Create(string projectDirectory, string gitRepoDirectory = null, ICloudBuild cloudBuild = null)
        {
            Requires.NotNull(projectDirectory, nameof(projectDirectory));
            if (string.IsNullOrEmpty(gitRepoDirectory))
            {
                gitRepoDirectory = projectDirectory;
            }

            using (var git = OpenGitRepo(gitRepoDirectory))
            {
                return new VersionOracle(projectDirectory, git, cloudBuild);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        public VersionOracle(string projectDirectory, LibGit2Sharp.Repository repo, ICloudBuild cloudBuild)
        {
            this.cloudBuild = cloudBuild;
            this.VersionOptions =
                VersionFile.GetVersion(repo, projectDirectory) ??
                VersionFile.GetVersion(projectDirectory);

            var repoRoot = repo?.Info?.WorkingDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relativeRepoProjectDirectory = !string.IsNullOrWhiteSpace(repoRoot)
                ? projectDirectory.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : null;

            var commit = repo?.Head.Commits.FirstOrDefault();
            this.GitCommitId = commit?.Id.Sha ?? cloudBuild?.GitCommitId ?? null;
            this.VersionHeight = repo?.GetVersionHeight(relativeRepoProjectDirectory) ?? 0;
            this.BuildingRef = cloudBuild?.BuildingTag ?? cloudBuild?.BuildingBranch ?? repo?.Head.CanonicalName;

            // Override the typedVersion with the special build number and revision components, when available.
            this.Version = repo?.GetIdAsVersion(relativeRepoProjectDirectory, this.VersionHeight) ?? this.VersionOptions?.Version.Version;
            this.Version = this.Version ?? new Version();

            this.CloudBuildNumberOptions = this.VersionOptions?.CloudBuild?.BuildNumber ?? new VersionOptions.CloudBuildNumberOptions();

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
                var commitIdOptions = this.CloudBuildNumberOptions.IncludeCommitId ?? new VersionOptions.CloudBuildNumberCommitIdOptions();
                bool includeCommitInfo = commitIdOptions.When == VersionOptions.CloudBuildNumberCommitWhen.Always ||
                    (commitIdOptions.When == VersionOptions.CloudBuildNumberCommitWhen.NonPublicReleaseOnly && !this.PublicRelease);
                bool commitIdInRevision = includeCommitInfo && commitIdOptions.Where == VersionOptions.CloudBuildNumberCommitWhere.FourthVersionComponent;
                bool commitIdInBuildMetadata = includeCommitInfo && commitIdOptions.Where == VersionOptions.CloudBuildNumberCommitWhere.BuildMetadata;
                Version buildNumberVersion = commitIdInRevision ? this.Version : this.SimpleVersion;
                string buildNumberMetadata = FormatBuildMetadata(commitIdInBuildMetadata ? this.BuildMetadataWithCommitId : this.BuildMetadata);
                return buildNumberVersion + this.PrereleaseVersion + buildNumberMetadata;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the cloud build number should be set.
        /// </summary>
        public bool CloudBuildNumberEnabled => this.CloudBuildNumberOptions.Enabled;

        /// <summary>
        /// Gets the build metadata identifiers, including the git commit ID as the first identifier if appropriate.
        /// </summary>
        public IEnumerable<string> BuildMetadataWithCommitId
        {
            get
            {
                if (!string.IsNullOrEmpty(this.GitCommitId))
                {
                    yield return $"g{this.GitCommitId.Substring(0, 10)}";
                }

                foreach (string identifier in this.BuildMetadata)
                {
                    yield return identifier;
                }
            }
        }

        /// <summary>
        /// Gets the version options used to initialize this instance.
        /// </summary>
        private VersionOptions VersionOptions { get; }

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
        /// Gets the prerelease version information.
        /// </summary>
        public string PrereleaseVersion => this.VersionOptions?.Version.Prerelease ?? string.Empty;

        /// <summary>
        /// Gets the version information without a Revision component.
        /// </summary>
        public Version SimpleVersion => this.Version.Build >= 0
                ? new Version(this.Version.Major, this.Version.Minor, this.Version.Build)
                : new Version(this.Version.Major, this.Version.Minor);

        /// <summary>
        /// Gets the build number (git height + offset) for this version.
        /// </summary>
        public int BuildNumber => Math.Max(0, this.Version.Build);

        /// <summary>
        /// Gets or sets the major.minor version string.
        /// </summary>
        /// <value>
        /// The x.y string (no build number or revision number).
        /// </value>
        public Version MajorMinorVersion => new Version(this.Version.Major, this.Version.Minor);

        /// <summary>
        /// Gets the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        public string GitCommitId { get; }

        /// <summary>
        /// Gets the first several characters of the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        public string GitCommitIdShort => string.IsNullOrEmpty(this.GitCommitId) ? null : this.GitCommitId.Substring(0, 10);

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at HEAD.
        /// </summary>
        public int VersionHeight { get; }

        private string BuildingRef { get; }

        /// <summary>
        /// Gets the version for this project, with up to 4 components.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Gets a value indicating whether to set cloud build version variables.
        /// </summary>
        public bool CloudBuildVersionVarsEnabled => this.VersionOptions?.CloudBuild?.SetVersionVariables
            ?? (new VersionOptions.CloudBuildOptions()).SetVersionVariables;

        /// <summary>
        /// Gets a dictionary of cloud build variables that applies to this project,
        /// regardless of the current setting of <see cref="CloudBuildVersionVarsEnabled"/>.
        /// </summary>
        public IDictionary<string, string> CloudBuildVersionVars
        {
            get
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "GitAssemblyInformationalVersion", this.AssemblyInformationalVersion },
                    { "GitBuildVersion", this.Version.ToString() },
                };
            }
        }

        /// <summary>
        /// Gets the list of build metadata identifiers to include in semver version strings.
        /// </summary>
        public List<string> BuildMetadata { get; } = new List<string>();

        /// <summary>
        /// Gets the +buildMetadata fragment for the semantic version.
        /// </summary>
        public string BuildMetadataFragment => FormatBuildMetadata(this.BuildMetadataWithCommitId);

        /// <summary>
        /// Gets the version to use for NuGet packages.
        /// </summary>
        public string NuGetPackageVersion => this.SemVer1;

        /// <summary>
        /// Gets the version to use for NPM packages.
        /// </summary>
        public string NpmPackageVersion => this.SemVer1;

        /// <summary>
        /// Gets a SemVer 1.0 compliant string that represents this version, including the -gCOMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        public string SemVer1 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}{this.SemVer1BuildMetadata}";

        /// <summary>
        /// Gets a SemVer 2.0 compliant string that represents this version, including a +gCOMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        public string SemVer2 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}{this.SemVer2BuildMetadata}";

        private string SemVer1BuildMetadata =>
            this.PublicRelease ? string.Empty : $"-g{this.GitCommitIdShort}";

        private string SemVer2BuildMetadata =>
            FormatBuildMetadata(this.PublicRelease ? this.BuildMetadata : this.BuildMetadataWithCommitId);

        private VersionOptions.CloudBuildNumberOptions CloudBuildNumberOptions { get; }

        private static string FormatBuildMetadata(IEnumerable<string> identifiers) =>
            (identifiers?.Any() ?? false) ? "+" + string.Join(".", identifiers) : string.Empty;

        private static string FormatBuildMetadataSemVerV1(IEnumerable<string> identifiers) =>
            (identifiers?.Any() ?? false) ? "-" + string.Join("-", identifiers) : string.Empty;

        private static LibGit2Sharp.Repository OpenGitRepo(string repoRoot)
        {
            Requires.NotNullOrEmpty(repoRoot, nameof(repoRoot));
            while (!Directory.Exists(Path.Combine(repoRoot, ".git")))
            {
                repoRoot = Path.GetDirectoryName(repoRoot);
                if (repoRoot == null)
                {
                    return null;
                }
            }

            return new LibGit2Sharp.Repository(repoRoot);
        }

        private static Version GetAssemblyVersion(Version version, VersionOptions versionOptions)
        {
            var assemblyVersion = versionOptions?.AssemblyVersion?.Version ?? new System.Version(version.Major, version.Minor);
            assemblyVersion = new System.Version(
                assemblyVersion.Major,
                assemblyVersion.Minor,
                versionOptions?.AssemblyVersion?.Precision >= VersionOptions.VersionPrecision.Build ? version.Build : 0,
                versionOptions?.AssemblyVersion?.Precision >= VersionOptions.VersionPrecision.Revision ? version.Revision : 0);
            return assemblyVersion.EnsureNonNegativeComponents(4);
        }
    }
}
