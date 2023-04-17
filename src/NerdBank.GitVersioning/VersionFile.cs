// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using Newtonsoft.Json;
using Validation;

namespace Nerdbank.GitVersioning;

/// <summary>
/// Exposes queries and mutations on a version.json or version.txt file.
/// </summary>
public abstract class VersionFile
{
    /// <summary>
    /// The filename of the version.txt file.
    /// </summary>
    public const string TxtFileName = "version.txt";

    /// <summary>
    /// The filename of the version.json file.
    /// </summary>
    public const string JsonFileName = "version.json";

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionFile"/> class.
    /// </summary>
    /// <param name="context">The git context to use when reading version files.</param>
    protected VersionFile(GitContext context)
    {
        this.Context = context;
    }

    /// <summary>
    /// Gets the git context to use when reading version files.
    /// </summary>
    protected GitContext Context { get; }

    /// <summary>
    /// Checks whether a version file is defined.
    /// </summary>
    /// <returns><see langword="true"/> if the version file is found; otherwise <see langword="false"/>.</returns>
    public bool IsVersionDefined() => this.GetVersion() is object;

    /// <inheritdoc cref="GetWorkingCopyVersion(out string?)"/>
    public VersionOptions? GetWorkingCopyVersion() => this.GetWorkingCopyVersion(out _);

    /// <summary>
    /// Reads the version file from the working tree and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="actualDirectory">Set to the actual directory that the version file was found in, which may be <see cref="GitContext.WorkingTreePath"/> or one of its ancestors.</param>
    /// <returns>The version information read from the file, or <see langword="null"/> if the file wasn't found.</returns>
    public VersionOptions? GetWorkingCopyVersion(out string? actualDirectory) => this.GetWorkingCopyVersion(this.Context.AbsoluteProjectDirectory, out actualDirectory);

    /// <inheritdoc cref="SetVersion(string, VersionOptions, bool)"/>
    /// <param name="unstableTag">The optional unstable tag to include in the file.</param>
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
    public string SetVersion(string projectDirectory, System.Version version, string? unstableTag = null, bool includeSchemaProperty = false)
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
    {
        return this.SetVersion(projectDirectory, VersionOptions.FromVersion(version, unstableTag), includeSchemaProperty);
    }

    /// <summary>
    /// Writes the version.json file to a directory within a repo with the specified version information.
    /// </summary>
    /// <param name="projectDirectory">
    /// The path to the directory in which to write the version.json file.
    /// The file's impact will be all descendent projects and directories from this specified directory,
    /// except where any of those directories have their own version.json file.
    /// </param>
    /// <param name="version">The version information to write to the file.</param>
    /// <param name="includeSchemaProperty">A value indicating whether to serialize the $schema property for easier editing in most JSON editors.</param>
    /// <returns>The path to the file written.</returns>
    public string SetVersion(string projectDirectory, VersionOptions version, bool includeSchemaProperty = true)
    {
        Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));
        Requires.NotNull(version, nameof(version));
        Requires.Argument(version.Version is object || version.Inherit, nameof(version), $"{nameof(VersionOptions.Version)} must be set for a root-level version.json file.");

        Directory.CreateDirectory(projectDirectory);

        string versionTxtPath = Path.Combine(projectDirectory, TxtFileName);
        if (File.Exists(versionTxtPath))
        {
            if (version.IsDefaultVersionTheOnlyPropertySet)
            {
                File.WriteAllLines(
                    versionTxtPath,
                    new[] { version.Version?.Version.ToString() ?? string.Empty, version.Version?.Prerelease ?? string.Empty });
                return versionTxtPath;
            }
            else
            {
                // The file must be upgraded to use the more descriptive JSON format.
                File.Delete(versionTxtPath);
            }
        }

        string repoRelativeProjectDirectory = this.Context.GetRepoRelativePath(projectDirectory);
        string versionJsonPath = Path.Combine(projectDirectory, JsonFileName);
        string jsonContent = JsonConvert.SerializeObject(
            version,
            VersionOptions.GetJsonSettings(version.Inherit, includeSchemaProperty, repoRelativeProjectDirectory));
        File.WriteAllText(versionJsonPath, jsonContent);
        return versionJsonPath;
    }

    /// <inheritdoc cref="GetVersion(out string?)"/>
    public VersionOptions? GetVersion() => this.GetVersion(out string? actualDirectory);

    /// <summary>
    /// Reads the version file from the selected git commit (or working copy if no commit is selected) and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="actualDirectory">Receives the absolute path to the directory where the version file was found, if any.</param>
    /// <returns>The version information read from the file, or <see langword="null"/> if the file wasn't found.</returns>
    public VersionOptions? GetVersion(out string? actualDirectory)
    {
        return this.Context.GitCommitId is null
           ? this.GetWorkingCopyVersion(out actualDirectory)
           : this.GetVersionCore(out actualDirectory);
    }

    /// <summary>
    /// Tries to read a version.json file from the specified string, but favors returning null instead of throwing a <see cref="JsonSerializationException"/>.
    /// </summary>
    /// <param name="jsonContent">The content of the version.json file.</param>
    /// <param name="repoRelativeBaseDirectory">Directory that this version.json file is relative to the root of the repository.</param>
    /// <returns>The deserialized <see cref="VersionOptions"/> object, if deserialization was successful.</returns>
    protected static VersionOptions? TryReadVersionJsonContent(string jsonContent, string? repoRelativeBaseDirectory)
    {
        try
        {
            return JsonConvert.DeserializeObject<VersionOptions>(jsonContent, VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: repoRelativeBaseDirectory));
        }
        catch (JsonSerializationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
    /// </summary>
    /// <param name="versionTextContent">The content of the version.txt file to read.</param>
    /// <returns>The version information read from the file; or <see langword="null"/> if a deserialization error occurs.</returns>
    protected static VersionOptions TryReadVersionFile(TextReader versionTextContent)
    {
        string? versionLine = versionTextContent.ReadLine();
        string? prereleaseVersion = versionTextContent.ReadLine();
        if (!string.IsNullOrEmpty(prereleaseVersion))
        {
            if (!prereleaseVersion.StartsWith("-"))
            {
                // SemVer requires that prerelease suffixes begin with a hyphen, so add one if it's missing.
                prereleaseVersion = "-" + prereleaseVersion;
            }
        }

        SemanticVersion semVer;
        Verify.Operation(SemanticVersion.TryParse(versionLine + prereleaseVersion, out semVer), "Unrecognized version format.");
        return new VersionOptions
        {
            Version = semVer,
        };
    }

    /// <summary>
    /// Reads the version file from <see cref="GitContext.GitCommitId"/> in the <see cref="Context"/> and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="actualDirectory">Receives the absolute path to the directory where the version file was found, if any.</param>
    /// <returns>The version information read from the file, or <see langword="null"/> if the file wasn't found.</returns>
    /// <remarks>This method is only called if <see cref="GitContext.GitCommitId"/> is not null.</remarks>
    protected abstract VersionOptions? GetVersionCore(out string? actualDirectory);

    /// <summary>
    /// Reads a version file from the working tree, without any regard to a git repo.
    /// </summary>
    /// <param name="startingDirectory">The path to start the search from.</param>
    /// <param name="actualDirectory">Receives the directory where the version file was found.</param>
    /// <returns>The version options, if found.</returns>
    protected VersionOptions? GetWorkingCopyVersion(string startingDirectory, out string? actualDirectory)
    {
        string? searchDirectory = startingDirectory;
        while (searchDirectory is object)
        {
            // Do not search above the working tree root.
            string? parentDirectory = string.Equals(searchDirectory, this.Context.WorkingTreePath, StringComparison.OrdinalIgnoreCase)
                ? null
                : Path.GetDirectoryName(searchDirectory);
            string versionTxtPath = Path.Combine(searchDirectory, TxtFileName);
            if (File.Exists(versionTxtPath))
            {
                using var sr = new StreamReader(File.OpenRead(versionTxtPath));
                VersionOptions? result = TryReadVersionFile(sr);
                if (result is object)
                {
                    actualDirectory = searchDirectory;
                    return result;
                }
            }

            string versionJsonPath = Path.Combine(searchDirectory, JsonFileName);
            if (File.Exists(versionJsonPath))
            {
                string versionJsonContent = File.ReadAllText(versionJsonPath);

                string? repoRelativeBaseDirectory = this.Context.GetRepoRelativePath(searchDirectory);
                VersionOptions? result =
                    TryReadVersionJsonContent(versionJsonContent, repoRelativeBaseDirectory);
                if (result?.Inherit ?? false)
                {
                    if (parentDirectory is object)
                    {
                        result = this.GetWorkingCopyVersion(parentDirectory, out _);
                        if (result is object)
                        {
                            JsonConvert.PopulateObject(
                                versionJsonContent,
                                result,
                                VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: repoRelativeBaseDirectory));
                            actualDirectory = searchDirectory;
                            return result;
                        }
                    }

                    throw new InvalidOperationException(
                        $"\"{versionJsonPath}\" inherits from a parent directory version.json file but none exists.");
                }
                else if (result is object)
                {
                    actualDirectory = searchDirectory;
                    return result;
                }
            }

            searchDirectory = parentDirectory;
        }

        actualDirectory = null;
        return null;
    }
}
