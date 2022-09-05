// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Validation;

namespace Nerdbank.GitVersioning.Commands;

/// <summary>
/// Implementation of the "nbgv cloud" command that updates the build environments variables with version variables.
/// </summary>
public class CloudCommand
{
    private readonly TextWriter stdout;
    private readonly TextWriter stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudCommand"/> class.
    /// </summary>
    /// <param name="outputWriter">The <see cref="TextWriter"/> to write output to (e.g. <see cref="Console.Out" />).</param>
    /// <param name="errorWriter">The <see cref="TextWriter"/> to write error messages to (e.g. <see cref="Console.Error" />).</param>
    public CloudCommand(TextWriter outputWriter = null, TextWriter errorWriter = null)
    {
        this.stdout = outputWriter ?? TextWriter.Null;
        this.stderr = errorWriter ?? TextWriter.Null;
    }

    /// <summary>
    /// Defines the possible errors of the "cloud" command.
    /// </summary>
    public enum CloudCommandError
    {
        /// <summary>
        /// The specified CI system was not found.
        /// </summary>
        NoCloudBuildProviderMatch,

        /// <summary>
        /// A cloud variable was defined multiple times.
        /// </summary>
        DuplicateCloudVariable,

        /// <summary>
        /// No supported cloud build environment could be detected.
        /// </summary>
        NoCloudBuildEnvDetected,
    }

    private static string[] CloudProviderNames => CloudBuild.SupportedCloudBuilds.Select(cb => cb.GetType().Name).ToArray();

    /// <summary>
    /// Adds version variables to the the current cloud build environment.
    /// </summary>
    /// <exception cref="CloudCommandException">Thrown when the build environment could not be updated.</exception>
    /// <param name="projectDirectory">
    /// The path to the directory which may (or its ancestors may) define the version file.
    /// </param>
    /// <param name="metadata">
    /// Optionally adds an identifier to the build metadata part of a semantic version.
    /// </param>
    /// <param name="version">
    /// The string to use for the cloud build number. If not specified, the computed version will be used.
    /// </param>
    /// <param name="ciSystem">
    /// The CI system to activate. If not specified, auto-detection will be used.
    /// </param>
    /// <param name="allVars">
    /// Controls whether to define all version variables as cloud build variables.
    /// </param>
    /// <param name="commonVars">
    /// Controls whether to define common version variables as cloud build variables.
    /// </param>
    /// <param name="additionalVariables">
    /// Additional cloud build variables to define.
    /// </param>
    /// <param name="alwaysUseLibGit2">
    /// Force usage of LibGit2 for accessing the git repository.
    /// </param>
    public void SetBuildVariables(string projectDirectory, IEnumerable<string> metadata, string version, string ciSystem, bool allVars, bool commonVars, IEnumerable<KeyValuePair<string, string>> additionalVariables, bool alwaysUseLibGit2)
    {
        Requires.NotNull(projectDirectory, nameof(projectDirectory));
        Requires.NotNull(additionalVariables, nameof(additionalVariables));

        ICloudBuild activeCloudBuild = CloudBuild.Active;
        if (!string.IsNullOrEmpty(ciSystem))
        {
            int matchingIndex = Array.FindIndex(CloudProviderNames, m => string.Equals(m, ciSystem, StringComparison.OrdinalIgnoreCase));
            if (matchingIndex == -1)
            {
                throw new CloudCommandException(
                    $"No cloud provider found by the name: \"{ciSystem}\"",
                    CloudCommandError.NoCloudBuildProviderMatch);
            }

            activeCloudBuild = CloudBuild.SupportedCloudBuilds[matchingIndex];
        }

        using var context = GitContext.Create(projectDirectory, writable: alwaysUseLibGit2);
        var oracle = new VersionOracle(context, cloudBuild: activeCloudBuild);
        if (metadata is not null)
        {
            oracle.BuildMetadata.AddRange(metadata);
        }

        var variables = new Dictionary<string, string>();
        if (allVars)
        {
            foreach (KeyValuePair<string, string> pair in oracle.CloudBuildAllVars)
            {
                variables.Add(pair.Key, pair.Value);
            }
        }

        if (commonVars)
        {
            foreach (KeyValuePair<string, string> pair in oracle.CloudBuildVersionVars)
            {
                variables.Add(pair.Key, pair.Value);
            }
        }

        foreach (KeyValuePair<string, string> kvp in additionalVariables)
        {
            if (variables.ContainsKey(kvp.Key))
            {
                throw new CloudCommandException(
                    $"Cloud build variable \"{kvp.Key}\" specified more than once.",
                    CloudCommandError.DuplicateCloudVariable);
            }

            variables[kvp.Key] = kvp.Value;
        }

        if (activeCloudBuild is not null)
        {
            if (string.IsNullOrEmpty(version))
            {
                version = oracle.CloudBuildNumber;
            }

            activeCloudBuild.SetCloudBuildNumber(version, this.stdout, this.stderr);

            foreach (KeyValuePair<string, string> pair in variables)
            {
                activeCloudBuild.SetCloudBuildVariable(pair.Key, pair.Value, this.stdout, this.stderr);
            }
        }
        else
        {
            throw new CloudCommandException(
                "No cloud build detected.",
                CloudCommandError.NoCloudBuildEnvDetected);
        }
    }

    /// <summary>
    /// Exception indicating an error while setting build variables.
    /// </summary>
    public class CloudCommandException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CloudCommandException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="error">The error that occurred.</param>
        public CloudCommandException(string message, CloudCommandError error)
            : base(message) => this.Error = error;

        /// <summary>
        /// Gets the error that occurred.
        /// </summary>
        public CloudCommandError Error { get; }
    }
}
