using System.IO;
using System.Reflection;
using Cake.Core;
using Cake.Core.Annotations;
using Nerdbank.GitVersioning;

namespace Cake.GitVersioning
{
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
        /// <param name="context">The context.</param>
        /// <param name="projectDirectory">Directory to start the search for version.json.</param>
        /// <returns>The version information from Git Versioning.</returns>
        [CakeMethodAlias]
        public static VersionOracle GitVersioningGetVersion(this ICakeContext context, string projectDirectory = ".")
        {
            GitExtensions.HelpFindLibGit2NativeBinaries(Path.GetDirectoryName(Assembly.GetAssembly(typeof(GitVersioningAliases)).Location));

            return VersionOracle.Create(projectDirectory, null, CloudBuild.Active);
        }
    }
}
