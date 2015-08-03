namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using NerdBank.GitVersioning;

    public class GetBuildVersion : Task
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GetBuildVersion"/> class.
        /// </summary>
        public GetBuildVersion()
        {
        }

        /// <summary>
        /// Gets the version string to use in the compiled assemblies.
        /// </summary>
        [Output]
        public string Version { get; private set; }

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
        /// head and the original commit (inclusive).
        /// </summary>
        [Output]
        public int GitHeadHeight { get; private set; }

        /// <summary>
        /// Gets the build number (git height) for this version.
        /// </summary>
        [Output]
        public int BuildNumber { get; private set; }

        /// <summary>
        /// The file that contains the version base (Major.Minor.Build) to use.
        /// </summary>
        [Required]
        public string VersionFile { get; set; }

        /// <summary>
        /// Gets or sets the path to the git repo root or some directory beneath it.
        /// </summary>
        public string GitRepoPath { get; set; }

        public override bool Execute()
        {
            try
            {
                Version typedVersion;
                using (var git = this.OpenGitRepo())
                {
                    var commit = git?.Head.Commits.FirstOrDefault();
                    this.GitCommitId = commit?.Id.Sha ?? string.Empty;
                    this.GitHeadHeight = commit?.GetHeight() ?? 0;

                    string prerelease = null;
                    commit?.GetVersionFromTxtFile(out typedVersion, out prerelease);
                    this.PrereleaseVersion = prerelease;

                    // Override the typedVersion with the special build number and revision components.
                    typedVersion = commit?.GetIdAsVersion();
                }

                typedVersion = typedVersion ?? new Version();
                this.SimpleVersion = new Version(typedVersion.Major, typedVersion.Minor, typedVersion.Build).ToString();
                this.MajorMinorVersion = new Version(typedVersion.Major, typedVersion.Minor).ToString();
                this.BuildNumber = typedVersion.Build;
                this.Version = typedVersion.ToString();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Log.LogErrorFromException(ex);
                return false;
            }

            return true;
        }

        private LibGit2Sharp.Repository OpenGitRepo()
        {
            if (string.IsNullOrEmpty(this.GitRepoPath))
            {
                return null;
            }

            string repoRoot = this.GitRepoPath;
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
