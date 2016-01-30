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
        /// Gets or sets a value indicating whether the project suggests the default
        /// value for the PublicRelease MSBuild property be true.
        /// </summary>
        [Output]
        public bool PublicReleaseDefault { get; private set; }

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
        /// Gets the build number (git height) for this version.
        /// </summary>
        [Output]
        public int BuildNumber { get; private set; }

        public override bool Execute()
        {
            try
            {
                Version typedVersion;
                VersionOptions versionOptions;
                using (var git = this.OpenGitRepo())
                {
                    var repoRoot = git?.Info?.WorkingDirectory;
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

                    if (!string.IsNullOrEmpty(this.BuildingRef) && versionOptions?.PublicReleaseRefSpec?.Length > 0)
                    {
                        this.PublicReleaseDefault = versionOptions.PublicReleaseRefSpec.Any(
                            expr => Regex.IsMatch(this.BuildingRef, expr));
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
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Log.LogErrorFromException(ex);
                return false;
            }

            return true;
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
            string repoRoot = Environment.CurrentDirectory;
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
