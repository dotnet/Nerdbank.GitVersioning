using System.IO;
using System.Reflection;
using Cake.Core;
using Cake.Core.Annotations;
using Nerdbank.GitVersioning;

namespace Cake.GitVersioning
{
    using System;
    using System.Linq;

    using Validation;

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
            var fullProjectDirectory = (new DirectoryInfo(projectDirectory)).FullName;

            string directoryName = Path.GetDirectoryName(Assembly.GetAssembly(typeof(GitVersioningAliases)).Location);

            if (string.IsNullOrWhiteSpace(directoryName))
            {
                throw new InvalidOperationException("Could not locate the Cake.GitVersioning library");
            }

            // Even after adding the folder containing the native libgit2 DLL to the PATH, DllNotFoundException is still thrown
            // Workaround this by copying the contents of the found folder to the current directory
            GitExtensions.HelpFindLibGit2NativeBinaries(directoryName, out var attemptedDirectory);

            // The HelpFindLibGit2NativeBinaries method throws if the directory does not exist
            var directoryInfo = new DirectoryInfo(attemptedDirectory);

            // There should only be a single file in the directory, but we do not know its extension
            // So, we will just get a list of all files rather than trying to determine the correct name and extension
            // If there are other files there for some reason, it should not matter as long as we don't overwrite anything in the current directory
            var fileInfos = directoryInfo.GetFiles();

            foreach (var fileInfo in fileInfos)
            {
                // Copy the file to the Cake.GitVersioning DLL directory, without overwriting anything
                string destFileName = Path.Combine(directoryName, fileInfo.Name);

                if (!File.Exists(destFileName))
                {
                    File.Copy(fileInfo.FullName, destFileName);
                }
            }

            return VersionOracle.Create(fullProjectDirectory, null, CloudBuild.Active);
        }
    }
}
