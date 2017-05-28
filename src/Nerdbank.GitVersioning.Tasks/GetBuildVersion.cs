namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using MSBuildExtensionTask;

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

        [Output]
        public string GitCommitIdShort { get; private set; }

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
                var cloudBuild = CloudBuild.Active;
                var oracle = VersionOracle.Create(Directory.GetCurrentDirectory(), this.GitRepoRoot, cloudBuild);
                if (!string.IsNullOrEmpty(this.DefaultPublicRelease))
                {
                    oracle.PublicRelease = string.Equals(this.DefaultPublicRelease, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (this.BuildMetadata != null)
                {
                    oracle.BuildMetadata.AddRange(this.BuildMetadata);
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
                this.GitVersionHeight = oracle.VersionHeight;
                this.BuildMetadataFragment = oracle.BuildMetadataFragment;
                this.CloudBuildNumber = oracle.CloudBuildNumberEnabled ? oracle.CloudBuildNumber : null;
                this.NuGetPackageVersion = oracle.NuGetPackageVersion;
                this.NpmPackageVersion = oracle.NpmPackageVersion;

                if (oracle.CloudBuildVersionVarsEnabled)
                {
                    this.CloudBuildVersionVars = oracle.CloudBuildVersionVars
                        .Select(item => new TaskItem(item.Key, new Dictionary<string, string> { { "Value", item.Value } }))
                        .ToArray();
                }

                return !this.Log.HasLoggedErrors;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Log.LogErrorFromException(ex);
                return false;
            }
        }
    }
}
