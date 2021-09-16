namespace Cake.GitVersioning
{
    using System;
    using System.IO;
    using System.Reflection;
    using Cake.Core;
    using Cake.Core.Annotations;
    using Nerdbank.GitVersioning;
    using Nerdbank.GitVersioning.Commands;

    /// <summary>
    /// Contains functionality for using Nerdbank.GitVersioning.
    /// </summary>
    [CakeAliasCategory("Git Versioning")]
    public static class GitVersioningAliases
    {
        /// <summary>
        /// Gets the Git Versioning version from the current repo.
        /// </summary>
        /// <example>
        /// Task("GetVersion")
        ///     .Does(() =>
        /// {
        ///     Information(GetVersioningGetVersion().SemVer2)
        /// });
        /// </example>
        /// <param name="cakeContext">The context.</param>
        /// <param name="projectDirectory">Directory to start the search for version.json.</param>
        /// <returns>The version information from Git Versioning.</returns>
        [CakeMethodAlias]
        public static VersionOracle GitVersioningGetVersion(this ICakeContext cakeContext, string projectDirectory = ".")
        {
            var fullProjectDirectory = (new DirectoryInfo(projectDirectory)).FullName;

            string directoryName = Path.GetDirectoryName(Assembly.GetAssembly(typeof(GitVersioningAliases)).Location);

            if (string.IsNullOrWhiteSpace(directoryName))
            {
                throw new InvalidOperationException("Could not locate the Cake.GitVersioning library");
            }

            var gitContext = GitContext.Create(fullProjectDirectory);
            return new VersionOracle(gitContext, cloudBuild: CloudBuild.Active);
        }

        /// <summary>
        /// Adds versioning information to the current build environment's variables.
        /// </summary>
        /// <example>
        /// Task("SetBuildVersion")
        ///     .Does(() =>
        /// {
        ///     GitVersioningCloud()
        /// });
        /// </example>
        /// <param name="cakeContext">The context.</param>
        /// <param name="projectDirectory">Directory to start the search for version.json.</param>
        /// <param name="settings">The settings to use for updating variables.</param>
        [CakeMethodAlias]
        public static void GitVersioningCloud(this ICakeContext cakeContext, string projectDirectory = ".", GitVersioningCloudSettings settings = null)
        {
            var fullProjectDirectory = (new DirectoryInfo(projectDirectory)).FullName;

            string directoryName = Path.GetDirectoryName(Assembly.GetAssembly(typeof(GitVersioningAliases)).Location);

            if (string.IsNullOrWhiteSpace(directoryName))
            {
                throw new InvalidOperationException("Could not locate the Cake.GitVersioning library");
            }

            settings ??= new GitVersioningCloudSettings();

            var cloudCommand = new CloudCommand(Console.Out, Console.Error);
            cloudCommand.SetBuildVariables(
                fullProjectDirectory,
                settings.Metadata,
                settings.Version,
                settings.CISystem?.ToString(),
                settings.AllVariables,
                settings.CommonVariables,
                settings.AdditionalVariables,
                false
            );
        }

    }
}
