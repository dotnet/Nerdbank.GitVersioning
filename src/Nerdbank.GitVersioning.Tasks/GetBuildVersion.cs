namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using Nerdbank.GitVersioning;

    public class GetBuildVersion : Task
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GetBuildVersion"/> class.
        /// </summary>
        public GetBuildVersion()
        {
        }

        /// <summary>
        /// Gets or sets the ref (branch or tag) being built.
        /// </summary>
        public string BuildingRef { get; set; }

        /// <summary>
        /// Gets or sets identifiers to append as build metadata.
        /// </summary>
        public string[] BuildMetadata { get; set; }

        /// <summary>
        /// Gets or sets the value of the PublicRelease property in MSBuild at the
        /// start of this Task.
        /// </summary>
        /// <value>Expected to be "true", "false", or empty.</value>
        public string DefaultPublicRelease { get; set; }

        /// <summary>
        /// Gets or sets the path to the repo root. If null or empty, behavior defaults to using Environment.CurrentDirectory and searching upwards.
        /// </summary>
        public string GitRepoRoot { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the project is building
        /// in PublicRelease mode.
        /// </summary>
        [Output]
        public bool PublicRelease { get; private set; }

        /// <summary>
        /// Gets the version string to use in the compiled assemblies.
        /// </summary>
        [Output]
        public string Version { get; private set; }

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyVersionAttribute"/>.
        /// </summary>
        [Output]
        public string AssemblyVersion { get; private set; }

        /// <summary>
        /// Gets the version string to use in the official release name (lacks revision number).
        /// </summary>
        [Output]
        public string SimpleVersion { get; private set; }

        /// <summary>
        /// Gets or sets the major.minor version string.
        /// </summary>
        /// <value>
        /// The x.y string (no build number or revision number).
        /// </value>
        [Output]
        public string MajorMinorVersion { get; set; }

        /// <summary>
        /// Gets or sets the prerelease version, or empty if this is a final release.
        /// </summary>
        /// <value>
        /// The prerelease version.
        /// </value>
        [Output]
        public string PrereleaseVersion { get; set; }

        /// <summary>
        /// Gets the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        [Output]
        public string GitCommitId { get; private set; }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at HEAD.
        /// </summary>
        [Output]
        public int GitVersionHeight { get; private set; }

        /// <summary>
        /// Gets the +buildMetadata fragment for the semantic version.
        /// </summary>
        [Output]
        public string BuildMetadataFragment { get; private set; }

        /// <summary>
        /// Gets the build number (git height) for this version.
        /// </summary>
        [Output]
        public int BuildNumber { get; private set; }

        /// <summary>
        /// Gets the BuildNumber to set the cloud build to (if applicable).
        /// </summary>
        [Output]
        public string CloudBuildNumber { get; private set; }

        /// <summary>
        /// Gets a value indicating whether to set cloud build version variables.
        /// </summary>
        [Output]
        public bool SetCloudBuildVersionVars { get; private set; }

        public override bool Execute()
        {
            try
            {
                Version typedVersion;
                VersionOptions versionOptions;
                using (var git = this.OpenGitRepo())
                {
                    var repoRoot = git?.Info?.WorkingDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var relativeRepoProjectDirectory = !string.IsNullOrWhiteSpace(repoRoot)
                        ? Environment.CurrentDirectory.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        : null;

                    var commit = git?.Head.Commits.FirstOrDefault();
                    this.GitCommitId = commit?.Id.Sha ?? string.Empty;
                    this.GitVersionHeight = git?.GetVersionHeight(relativeRepoProjectDirectory) ?? 0;
                    if (string.IsNullOrEmpty(this.BuildingRef))
                    {
                        this.BuildingRef = git?.Head.CanonicalName;
                    }

                    versionOptions =
                        VersionFile.GetVersion(git, Environment.CurrentDirectory) ??
                        VersionFile.GetVersion(Environment.CurrentDirectory);

                    this.PublicRelease = string.Equals(this.DefaultPublicRelease, "true", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(this.DefaultPublicRelease))
                    {
                        if (!string.IsNullOrEmpty(this.BuildingRef) && versionOptions?.PublicReleaseRefSpec?.Length > 0)
                        {
                            this.PublicRelease = versionOptions.PublicReleaseRefSpec.Any(
                                expr => Regex.IsMatch(this.BuildingRef, expr));
                        }
                    }

                    this.PrereleaseVersion = versionOptions?.Version.Prerelease ?? string.Empty;

                    // Override the typedVersion with the special build number and revision components, when available.
                    typedVersion = git?.GetIdAsVersion(relativeRepoProjectDirectory, this.GitVersionHeight) ?? versionOptions?.Version.Version;
                }

                typedVersion = typedVersion ?? new Version();
                var typedVersionWithoutRevision = typedVersion.Build >= 0
                    ? new Version(typedVersion.Major, typedVersion.Minor, typedVersion.Build)
                    : new Version(typedVersion.Major, typedVersion.Minor);
                this.SimpleVersion = typedVersionWithoutRevision.ToString();
                var majorMinorVersion = new Version(typedVersion.Major, typedVersion.Minor);
                this.MajorMinorVersion = majorMinorVersion.ToString();
                Version assemblyVersion = GetAssemblyVersion(typedVersion, versionOptions);
                this.AssemblyVersion = assemblyVersion.ToStringSafe(4);
                this.BuildNumber = Math.Max(0, typedVersion.Build);
                this.Version = typedVersion.ToString();

                this.SetCloudBuildVersionVars = versionOptions?.CloudBuild?.SetVersionVariables
                    ?? (new VersionOptions.CloudBuildOptions()).SetVersionVariables;

                var buildMetadata = this.BuildMetadata?.ToList() ?? new List<string>();
                if (!string.IsNullOrEmpty(this.GitCommitId))
                {
                    buildMetadata.Insert(0, $"g{this.GitCommitId.Substring(0, 10)}");
                }

                this.BuildMetadataFragment = FormatBuildMetadata(buildMetadata);

                var buildNumber = versionOptions?.CloudBuild?.BuildNumber ?? new VersionOptions.CloudBuildNumberOptions();
                if (buildNumber.Enabled)
                {
                    var commitIdOptions = buildNumber.IncludeCommitId ?? new VersionOptions.CloudBuildNumberCommitIdOptions();
                    bool includeCommitInfo = commitIdOptions.When == VersionOptions.CloudBuildNumberCommitWhen.Always ||
                        (commitIdOptions.When == VersionOptions.CloudBuildNumberCommitWhen.NonPublicReleaseOnly && !this.PublicRelease);
                    bool commitIdInRevision = includeCommitInfo && commitIdOptions.Where == VersionOptions.CloudBuildNumberCommitWhere.FourthVersionComponent;
                    bool commitIdInBuildMetadata = includeCommitInfo && commitIdOptions.Where == VersionOptions.CloudBuildNumberCommitWhere.BuildMetadata;
                    Version buildNumberVersion = commitIdInRevision ? typedVersion : typedVersionWithoutRevision;
                    string buildNumberMetadata = FormatBuildMetadata(commitIdInBuildMetadata ? (IEnumerable<string>)buildMetadata : this.BuildMetadata);
                    this.CloudBuildNumber = buildNumberVersion + this.PrereleaseVersion + buildNumberMetadata;
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Log.LogErrorFromException(ex);
                return false;
            }

            return true;
        }

        private static string FormatBuildMetadata(IEnumerable<string> identifiers)
        {
            return identifiers?.Any() ?? false
                ? "+" + string.Join(".", identifiers)
                : string.Empty;
        }

        private static Version GetAssemblyVersion(Version version, VersionOptions versionOptions)
        {
            var assemblyVersion = versionOptions?.AssemblyVersion?.Version ?? new System.Version(version.Major, version.Minor);
            assemblyVersion = new System.Version(
                assemblyVersion.Major,
                assemblyVersion.Minor,
                versionOptions?.AssemblyVersion?.Precision >= VersionOptions.VersionPrecision.Build ? version.Build : 0,
                versionOptions?.AssemblyVersion?.Precision >= VersionOptions.VersionPrecision.Revision ? version.Revision : 0);
            return assemblyVersion;
        }

        private LibGit2Sharp.Repository OpenGitRepo()
        {
            string repoRoot = string.IsNullOrEmpty(this.GitRepoRoot) ? Environment.CurrentDirectory : this.GitRepoRoot;
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
    }
}
