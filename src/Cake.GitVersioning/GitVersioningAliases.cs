// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Reflection;
using Cake.Core;
using Cake.Core.Annotations;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Commands;

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
        /// <param name="cakeContext">The context.</param>
        /// <param name="projectDirectory">Directory to start the search for version.json.</param>
        /// <returns>The version information from Git Versioning.</returns>
        /// <remarks>
        /// <para>Example:</para>
        /// <code><![CDATA[
        /// Task("GetVersion")
        ///     .Does(() =>
        /// {
        ///     Information(GetVersioningGetVersion().SemVer2)
        /// });
        /// ]]></code>
        /// </remarks>
        [CakeMethodAlias]
        public static VersionOracle GitVersioningGetVersion(this ICakeContext cakeContext, string projectDirectory = ".")
        {
            string fullProjectDirectory = new DirectoryInfo(projectDirectory).FullName;

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
        /// <param name="cakeContext">The context.</param>
        /// <param name="projectDirectory">Directory to start the search for version.json.</param>
        /// <param name="settings">The settings to use for updating variables.</param>
        /// <remarks>
        /// <para>Example:</para>
        /// <code><![CDATA[
        /// Task("SetBuildVersion")
        ///     .Does(() =>
        /// {
        ///     GitVersioningCloud()
        /// });
        /// ]]></code>
        /// </remarks>
        [CakeMethodAlias]
        public static void GitVersioningCloud(this ICakeContext cakeContext, string projectDirectory = ".", GitVersioningCloudSettings settings = null)
        {
            string fullProjectDirectory = new DirectoryInfo(projectDirectory).FullName;

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
                false);
        }
    }
}
