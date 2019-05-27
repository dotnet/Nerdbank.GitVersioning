namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using MSBuildExtensionTask;
    using Validation;

    public class GetBuildVersion : ContextAwareTask
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
        /// Gets or sets the relative path from the <see cref="GitRepoRoot"/> to the directory under it that contains the project being built.
        /// </summary>
        /// <value>
        /// If not supplied, the directories from <see cref="GitRepoRoot"/> to <see cref="Environment.CurrentDirectory"/>
        /// will be searched for version.json.
        /// If supplied, the value <em>must</em> fall beneath the <see cref="GitRepoRoot"/> (i.e. this value should not contain "..\").
        /// </value>
        /// <remarks>
        /// This property is useful when the project that MSBuild is building is not found under <see cref="GitRepoRoot"/> such that the
        /// relative path can be calculated automatically.
        /// </remarks>
        public string ProjectPathRelativeToGitRepoRoot { get; set; }

        /// <summary>
        /// Gets or sets the optional override build number offset.
        /// </summary>
        public int OverrideBuildNumberOffset { get; set; } = int.MaxValue;

        /// <summary>
        /// Gets or sets the path to the folder that contains the NB.GV .targets file.
        /// </summary>
        /// <remarks>
        /// This is particularly useful in .NET Core where discovering one's own assembly path
        /// is not allowed before .NETStandard 2.0.
        /// </remarks>
        [Required]
        public string TargetsPath { get; set; }

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
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyFileVersionAttribute"/>.
        /// </summary>
        [Output]
        public string AssemblyFileVersion { get; private set; }

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
        /// </summary>
        [Output]
        public string AssemblyInformationalVersion { get; private set; }

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
        /// Gets the first several characters of the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        [Output]
        public string GitCommitIdShort { get; private set; }

        /// <summary>
        /// Gets the Git revision control commit date for HEAD (the current source code version), expressed as the number of 100-nanosecond
        /// intervals that have elapsed since January 1, 0001 at 00:00:00.000 in the Gregorian calendar.
        /// </summary>
        [Output]
        public string GitCommitDateTicks { get; private set; }

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

        [Output]
        public string NuGetPackageVersion { get; private set; }

        [Output]
        public string ChocolateyPackageVersion { get; private set; }

        [Output]
        public string NpmPackageVersion { get; private set; }

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

        [Output]
        public ITaskItem[] CloudBuildVersionVars { get; private set; }

        protected override string UnmanagedDllDirectory => GitExtensions.FindLibGit2NativeBinaries(this.TargetsPath);

        protected override bool ExecuteInner()
        {
            try
            {
                if (!string.IsNullOrEmpty(this.ProjectPathRelativeToGitRepoRoot))
                {
                    Requires.Argument(!Path.IsPathRooted(this.ProjectPathRelativeToGitRepoRoot), nameof(this.ProjectPathRelativeToGitRepoRoot), "Path must be relative.");
                    Requires.Argument(!(
                        this.ProjectPathRelativeToGitRepoRoot.Contains(".." + Path.DirectorySeparatorChar) ||
                        this.ProjectPathRelativeToGitRepoRoot.Contains(".." + Path.AltDirectorySeparatorChar)),
                        nameof(this.ProjectPathRelativeToGitRepoRoot),
                        "Path must not use ..\\");
                }

                var cloudBuild = CloudBuild.Active;
                var overrideBuildNumberOffset = (this.OverrideBuildNumberOffset == int.MaxValue) ? (int?)null : this.OverrideBuildNumberOffset;
                var oracle = VersionOracle.Create(Directory.GetCurrentDirectory(), this.GitRepoRoot, cloudBuild, overrideBuildNumberOffset, this.ProjectPathRelativeToGitRepoRoot);
                if (!string.IsNullOrEmpty(this.DefaultPublicRelease))
                {
                    oracle.PublicRelease = string.Equals(this.DefaultPublicRelease, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (this.BuildMetadata != null)
                {
                    oracle.BuildMetadata.AddRange(this.BuildMetadata);
                }

                if (IsMisconfiguredPrereleaseAndSemVer1(oracle))
                {
                    this.Log.LogWarning("The 'nugetPackageVersion' is explicitly set to 'semVer': 1 but the prerelease version '{0}' is not SemVer1 compliant. Change the 'nugetPackageVersion'.'semVer' value to 2 or change the 'version' member to follow SemVer1 rules (e.g.: '{1}').", oracle.PrereleaseVersion, GetSemVer1WithoutPaddingOrBuildMetadata(oracle));
                }

                this.PublicRelease = oracle.PublicRelease;
                this.Version = oracle.Version.ToString();
                this.AssemblyVersion = oracle.AssemblyVersion.ToString();
                this.AssemblyFileVersion = oracle.AssemblyFileVersion.ToString();
                this.AssemblyInformationalVersion = oracle.AssemblyInformationalVersion;
                this.SimpleVersion = oracle.SimpleVersion.ToString();
                this.MajorMinorVersion = oracle.MajorMinorVersion.ToString();
                this.BuildNumber = oracle.BuildNumber;
                this.PrereleaseVersion = oracle.PrereleaseVersion;
                this.GitCommitId = oracle.GitCommitId;
                this.GitCommitIdShort = oracle.GitCommitIdShort;
                this.GitCommitDateTicks = oracle.GitCommitDate != null ? oracle.GitCommitDate.Value.Ticks.ToString(CultureInfo.InvariantCulture) : null;
                this.GitVersionHeight = oracle.VersionHeight;
                this.BuildMetadataFragment = oracle.BuildMetadataFragment;
                this.CloudBuildNumber = oracle.CloudBuildNumberEnabled ? oracle.CloudBuildNumber : null;
                this.NuGetPackageVersion = oracle.NuGetPackageVersion;
                this.ChocolateyPackageVersion = oracle.ChocolateyPackageVersion;
                this.NpmPackageVersion = oracle.NpmPackageVersion;

                IEnumerable<ITaskItem> cloudBuildVersionVars = null;
                if (oracle.CloudBuildVersionVarsEnabled)
                {
                    cloudBuildVersionVars = oracle.CloudBuildVersionVars
                        .Select(item => new TaskItem(item.Key, new Dictionary<string, string> { { "Value", item.Value } }));
                }

                if (oracle.CloudBuildAllVarsEnabled)
                {
                    var allVariables = oracle.CloudBuildAllVars
                        .Select(item => new TaskItem(item.Key, new Dictionary<string, string> { { "Value", item.Value } }));

                    if (cloudBuildVersionVars != null)
                    {
                        cloudBuildVersionVars = cloudBuildVersionVars
                            .Union(allVariables);
                    }
                    else
                    {
                        cloudBuildVersionVars = allVariables;
                    }
                }

                if (cloudBuildVersionVars != null)
                {
                    this.CloudBuildVersionVars = cloudBuildVersionVars.ToArray();
                }

                return !this.Log.HasLoggedErrors;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                this.Log.LogErrorFromException(ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the SemVer v1 format without padding or the build metadata.
        /// </summary>
        private static string GetSemVer1WithoutPaddingOrBuildMetadata(VersionOracle oracle)
        {
            Requires.NotNull(oracle, nameof(oracle));
            return $"{oracle.Version.ToStringSafe(3)}{SemanticVersionExtensions.MakePrereleaseSemVer1Compliant(oracle.PrereleaseVersion, 0)}";
        }

        /// <summary>
        /// Gets a value indicating whether the user wants SemVer v1 compliance yet specified a non-v1 compliant prerelease tag.
        /// </summary>
        private static bool IsMisconfiguredPrereleaseAndSemVer1(VersionOracle oracle)
        {
            Requires.NotNull(oracle, nameof(oracle));
            return oracle.VersionOptions?.NuGetPackageVersion?.SemVer == 1 && oracle.PrereleaseVersion != SemanticVersionExtensions.MakePrereleaseSemVer1Compliant(oracle.PrereleaseVersion, 0);
        }
    }
}
