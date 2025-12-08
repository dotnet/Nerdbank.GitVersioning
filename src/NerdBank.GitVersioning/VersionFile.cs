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
    /// Gets a value indicating whether merging paths with <see cref="MergeLocations(ref VersionFileLocations, VersionFileLocations)"/> and <see cref="ApplyLocations(VersionOptions?, string, ref VersionFileLocations)"/>
    /// prefer the new locations over the old ones.
    /// </summary>
    protected virtual bool VersionSearchRootToBranch => false;

    /// <summary>
    /// Checks whether a version file is defined.
    /// </summary>
    /// <returns><see langword="true"/> if the version file is found; otherwise <see langword="false"/>.</returns>
    public bool IsVersionDefined() => this.GetVersion() is object;

    /// <inheritdoc cref="GetWorkingCopyVersion(VersionFileRequirements, out VersionFileLocations)"/>
    public VersionOptions? GetWorkingCopyVersion(VersionFileRequirements requirements) => this.GetWorkingCopyVersion(requirements, out _);

    /// <summary>
    /// Reads the version file from the working tree and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="requirements"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='requirements']" /></param>
    /// <param name="locations"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='locations']" /></param>
    /// <returns>The version information read from the file, or <see langword="null"/> if the file wasn't found.</returns>
    public VersionOptions? GetWorkingCopyVersion(VersionFileRequirements requirements, out VersionFileLocations locations) => this.GetWorkingCopyVersion(this.Context.AbsoluteProjectDirectory, requirements, out locations);

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

    /// <inheritdoc cref="GetVersion(VersionFileRequirements, out VersionFileLocations)"/>
    public VersionOptions? GetVersion() => this.GetVersion(VersionFileRequirements.Default, out _);

    /// <summary>
    /// Reads the version file from the selected git commit (or working copy if no commit is selected) and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="requirements"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='requirements']" /></param>
    /// <param name="locations"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='locations']" /></param>
    /// <returns>The version information read from the file, or <see langword="null"/> if the file wasn't found.</returns>
    public VersionOptions? GetVersion(VersionFileRequirements requirements, out VersionFileLocations locations)
    {
        return this.Context.GitCommitId is null ? this.GetWorkingCopyVersion(requirements, out locations) : this.GetVersionCore(requirements, out locations);
    }

    /// <summary>
    /// Tries to read a version.json file from the specified string, but favors returning null instead of throwing a <see cref="JsonException"/>.
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
        catch (JsonException)
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

    protected static bool VersionOptionsSatisfyRequirements(VersionOptions? options, VersionFileRequirements requirements)
    {
        Requires.Argument(
            !requirements.HasFlag(VersionFileRequirements.AcceptInheritingFile) || requirements.HasFlag(VersionFileRequirements.NonMergedResult),
            nameof(requirements),
            "Clients that accept an inheriting file must not want a merged result.");

        if (options is null)
        {
            return false;
        }

        if (options.Version is null && requirements.HasFlag(VersionFileRequirements.VersionSpecified))
        {
            return false;
        }

        if (options.Inherit && !requirements.HasFlag(VersionFileRequirements.AcceptInheritingFile))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the standalone <see cref="VersionOptions.Prerelease"/> property to the <see cref="VersionOptions.Version"/> property.
    /// </summary>
    /// <param name="options">The version options to modify.</param>
    /// <remarks>
    /// This method should be called after merging a child version.json with a parent version.json.
    /// If the child specified a <see cref="VersionOptions.Prerelease"/> property, this method will apply it to the version.
    /// </remarks>
    protected static void ApplyPrereleaseProperty(VersionOptions options)
    {
        Requires.NotNull(options, nameof(options));

        // Only apply if the Prerelease property was explicitly set
        if (options.Prerelease is null)
        {
            return;
        }

        // The version must exist to apply a prerelease tag
        if (options.Version is null)
        {
            throw new InvalidOperationException("The 'prerelease' property cannot be used without a 'version' property.");
        }

        // If prerelease is an empty string, it suppresses any inherited prerelease tag
        if (options.Prerelease == string.Empty)
        {
            // Remove any existing prerelease tag
            if (!string.IsNullOrEmpty(options.Version.Prerelease))
            {
                options.Version = new SemanticVersion(
                    options.Version.Version,
                    null,
                    options.Version.BuildMetadata);
            }

            options.Prerelease = null;
            return;
        }

        // Validate that the version doesn't already have a prerelease tag (non-empty prerelease being applied)
        if (!string.IsNullOrEmpty(options.Version.Prerelease))
        {
            throw new InvalidOperationException("The 'prerelease' property cannot be used when the 'version' property already includes a prerelease tag.");
        }

        // Apply the prerelease tag to the version
        string prereleaseTag = options.Prerelease;
        if (!prereleaseTag.StartsWith("-", StringComparison.Ordinal))
        {
            // Add the hyphen prefix if not present
            prereleaseTag = "-" + prereleaseTag;
        }

        // Create a new SemanticVersion with the prerelease tag applied
        options.Version = new SemanticVersion(
            options.Version.Version,
            prereleaseTag,
            options.Version.BuildMetadata);

        // Clear the Prerelease property since it has been applied
        options.Prerelease = null;
    }

    protected static string TrimTrailingPathSeparator(string path)
        => path.Length > 0 && (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar) ? path[..^1] : path;

    protected void MergeLocations(ref VersionFileLocations target, VersionFileLocations input)
    {
        if (this.VersionSearchRootToBranch && input.VersionSpecifyingVersionDirectory is not null)
        {
            target.VersionSpecifyingVersionDirectory = input.VersionSpecifyingVersionDirectory;
        }
        else
        {
            target.VersionSpecifyingVersionDirectory ??= input.VersionSpecifyingVersionDirectory;
        }

        if (this.VersionSearchRootToBranch && input.NonInheritingVersionDirectory is not null)
        {
            target.NonInheritingVersionDirectory = input.NonInheritingVersionDirectory;
        }
        else
        {
            target.NonInheritingVersionDirectory ??= input.NonInheritingVersionDirectory;
        }
    }

    protected void ApplyLocations(VersionOptions? options, string currentLocation, ref VersionFileLocations locations)
    {
        if (options is null)
        {
            return;
        }

        if (options.Version is not null)
        {
            if (this.VersionSearchRootToBranch)
            {
                locations.VersionSpecifyingVersionDirectory = currentLocation;
            }
            else
            {
                locations.VersionSpecifyingVersionDirectory ??= currentLocation;
            }
        }

        if (!options.Inherit)
        {
            if (this.VersionSearchRootToBranch)
            {
                locations.NonInheritingVersionDirectory = currentLocation;
            }
            else
            {
                locations.NonInheritingVersionDirectory ??= currentLocation;
            }
        }
    }

    /// <summary>
    /// Reads the version file from <see cref="GitContext.GitCommitId"/> in the <see cref="Context"/> and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="requirements"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='requirements']" /></param>
    /// <param name="locations"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='locations']" /></param>
    /// <returns>The version information read from the file, or <see langword="null"/> if the file wasn't found.</returns>
    /// <remarks>This method is only called if <see cref="GitContext.GitCommitId"/> is not null.</remarks>
    protected abstract VersionOptions? GetVersionCore(VersionFileRequirements requirements, out VersionFileLocations locations);

    /// <summary>
    /// Reads a version file from the working tree, without any regard to a git repo.
    /// </summary>
    /// <param name="startingDirectory">The path to start the search from.</param>
    /// <param name="requirements"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='requirements']" /></param>
    /// <param name="locations"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='locations']" /></param>
    /// <returns>The version options, if found.</returns>
    protected VersionOptions? GetWorkingCopyVersion(string startingDirectory, VersionFileRequirements requirements, out VersionFileLocations locations)
    {
        startingDirectory = TrimTrailingPathSeparator(startingDirectory);
        locations = default;

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
                    this.ApplyLocations(result, searchDirectory, ref locations);
                    return VersionOptionsSatisfyRequirements(result, requirements) ? result : null;
                }
            }

            string versionJsonPath = Path.Combine(searchDirectory, JsonFileName);
            if (File.Exists(versionJsonPath))
            {
                string versionJsonContent = File.ReadAllText(versionJsonPath);

                string? repoRelativeBaseDirectory = this.Context.GetRepoRelativePath(searchDirectory);
                VersionOptions? result = TryReadVersionJsonContent(versionJsonContent, repoRelativeBaseDirectory);

                this.ApplyLocations(result, searchDirectory, ref locations);
                if (VersionOptionsSatisfyRequirements(result, requirements))
                {
                    return result;
                }

                if (result?.Inherit is true)
                {
                    if (parentDirectory is object)
                    {
                        result = this.GetWorkingCopyVersion(parentDirectory, requirements, out VersionFileLocations parentLocations);
                        this.MergeLocations(ref locations, parentLocations);
                        if (!requirements.HasFlag(VersionFileRequirements.NonMergedResult) && result is not null)
                        {
                            JsonConvert.PopulateObject(
                                versionJsonContent,
                                result,
                                VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: repoRelativeBaseDirectory));
                            ApplyPrereleaseProperty(result);
                            result.Inherit = false;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"\"{searchDirectory}\" inherits from a parent directory version.json file but none exists.");
                    }

                    return VersionOptionsSatisfyRequirements(result, requirements) ? result : null;
                }
                else if (result is object)
                {
                    return VersionOptionsSatisfyRequirements(result, requirements) ? result : null;
                }
            }

            searchDirectory = parentDirectory;
        }

        return null;
    }
}
