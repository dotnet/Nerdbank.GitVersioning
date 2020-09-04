namespace Nerdbank.GitVersioning
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using Newtonsoft.Json.Linq;
    using Newtonsoft.Json.Serialization;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Validation;

    /// <summary>
    /// Extension methods for interacting with the version.txt file.
    /// </summary>
    public static class VersionFile
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
        /// A sequence of possible filenames for the version file in preferred order.
        /// </summary>
        public static readonly IReadOnlyList<string> PreferredFileNames = new[] { JsonFileName, TxtFileName };

        /// <summary>
        /// Reads the version.json file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <param name="blobVersionCache">An optional blob cache for storing the raw parse results of a version.txt or version.json file (before any inherit merge operations are applied).</param>
        /// <returns>The version information read from the file.</returns>
        public static VersionOptions GetVersion(LibGit2Sharp.Commit commit, string repoRelativeProjectDirectory = null, Dictionary<LibGit2Sharp.ObjectId, VersionOptions> blobVersionCache = null)
        {
            if (commit == null)
            {
                return null;
            }

            string searchDirectory = repoRelativeProjectDirectory ?? string.Empty;
            while (searchDirectory != null)
            {
                string parentDirectory = searchDirectory.Length > 0 ? Path.GetDirectoryName(searchDirectory) : null;

                string candidatePath = Path.Combine(searchDirectory, TxtFileName).Replace('\\', '/');
                var versionTxtBlob = commit.Tree[candidatePath]?.Target as LibGit2Sharp.Blob;
                if (versionTxtBlob != null)
                {
                    if (blobVersionCache is null || !blobVersionCache.TryGetValue(versionTxtBlob.Id, out VersionOptions result))
                    {
                        result = TryReadVersionFile(new StreamReader(versionTxtBlob.GetContentStream()));
                        if (blobVersionCache is object)
                        {
                            blobVersionCache.Add(versionTxtBlob.Id, result);
                        }
                    }

                    if (result != null)
                    {
                        return result;
                    }
                }

                candidatePath = Path.Combine(searchDirectory, JsonFileName).Replace('\\', '/');
                var versionJsonBlob = commit.Tree[candidatePath]?.Target as LibGit2Sharp.Blob;
                if (versionJsonBlob != null)
                {
                    string versionJsonContent = null;
                    if (blobVersionCache is null || !blobVersionCache.TryGetValue(versionJsonBlob.Id, out VersionOptions result))
                    {
                        using (var sr = new StreamReader(versionJsonBlob.GetContentStream()))
                        {
                            versionJsonContent = sr.ReadToEnd();
                        }

                        try
                        {
                            result = TryReadVersionJsonContent(versionJsonContent, searchDirectory);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException(
                                $"Failure while reading {JsonFileName} from commit {commit.Sha}. " +
                                "Fix this commit with rebase if this is an error, or review this doc on how to migrate to Nerdbank.GitVersioning: " +
                                "https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/migrating.md", ex);
                        }

                        if (blobVersionCache is object)
                        {
                            blobVersionCache.Add(versionJsonBlob.Id, result);
                        }
                    }

                    if (result?.Inherit ?? false)
                    {
                        if (parentDirectory != null)
                        {
                            result = GetVersion(commit, parentDirectory, blobVersionCache);
                            if (result != null)
                            {
                                if (versionJsonContent is null)
                                {
                                    // We reused a cache VersionOptions, but now we need the actual JSON string.
                                    using (var sr = new StreamReader(versionJsonBlob.GetContentStream()))
                                    {
                                        versionJsonContent = sr.ReadToEnd();
                                    }
                                }

                                JsonConvert.PopulateObject(versionJsonContent, result, VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: searchDirectory));
                                return result;
                            }
                        }

                        throw new InvalidOperationException($"\"{candidatePath}\" inherits from a parent directory version.json file but none exists.");
                    }
                    else if (result != null)
                    {
                        return result;
                    }
                }

                searchDirectory = parentDirectory;
            }

            return null;
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="repo">The repo to read the version file from.</param>
        /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <returns>The version information read from the file.</returns>
        public static VersionOptions GetVersion(LibGit2Sharp.Repository repo, string repoRelativeProjectDirectory = null)
        {
            if (repo == null)
            {
                return null;
            }

            if (!repo.Info.IsBare)
            {
                string fullDirectory = Path.Combine(repo.Info.WorkingDirectory, repoRelativeProjectDirectory ?? string.Empty);
                var workingCopyVersion = GetVersion(fullDirectory);
                return workingCopyVersion;
            }

            return GetVersion(repo.Head.Tip, repoRelativeProjectDirectory);
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="projectDirectory">The path to the directory which may (or its ancestors may) define the version.txt file.</param>
        /// <returns>The version information read from the file, or <c>null</c> if the file wasn't found.</returns>
        public static VersionOptions GetVersion(string projectDirectory) => GetVersion(projectDirectory, out string _);

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="projectDirectory">The path to the directory which may (or its ancestors may) define the version.txt file.</param>
        /// <param name="actualDirectory">Set to the actual directory that the version file was found in, which may be <paramref name="projectDirectory"/> or one of its ancestors.</param>
        /// <returns>The version information read from the file, or <c>null</c> if the file wasn't found.</returns>
        public static VersionOptions GetVersion(string projectDirectory, out string actualDirectory)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));
            using (var repo = GitExtensions.OpenGitRepo(projectDirectory))
            {
                string searchDirectory = projectDirectory;
                while (searchDirectory != null)
                {
                    string parentDirectory = Path.GetDirectoryName(searchDirectory);
                    string versionTxtPath = Path.Combine(searchDirectory, TxtFileName);
                    if (File.Exists(versionTxtPath))
                    {
                        using (var sr = new StreamReader(File.OpenRead(versionTxtPath)))
                        {
                            var result = TryReadVersionFile(sr);
                            if (result != null)
                            {
                                actualDirectory = searchDirectory;
                                return result;
                            }
                        }
                    }

                    string versionJsonPath = Path.Combine(searchDirectory, JsonFileName);
                    if (File.Exists(versionJsonPath))
                    {
                        string versionJsonContent = File.ReadAllText(versionJsonPath);

                        var repoRelativeBaseDirectory = repo?.GetRepoRelativePath(searchDirectory);
                        VersionOptions result =
                            TryReadVersionJsonContent(versionJsonContent, repoRelativeBaseDirectory);
                        if (result?.Inherit ?? false)
                        {
                            if (parentDirectory != null)
                            {
                                result = GetVersion(parentDirectory);
                                if (result != null)
                                {
                                    JsonConvert.PopulateObject(versionJsonContent, result,
                                        VersionOptions.GetJsonSettings(
                                            repoRelativeBaseDirectory: repoRelativeBaseDirectory));
                                    actualDirectory = searchDirectory;
                                    return result;
                                }
                            }

                            throw new InvalidOperationException(
                                $"\"{versionJsonPath}\" inherits from a parent directory version.json file but none exists.");
                        }
                        else if (result != null)
                        {
                            actualDirectory = searchDirectory;
                            return result;
                        }
                    }

                    searchDirectory = parentDirectory;
                }
            }

            actualDirectory = null;
            return null;
        }

        /// <summary>
        /// Checks whether the version.txt file is defined in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to search.</param>
        /// <param name="projectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <returns><c>true</c> if the version.txt file is found; otherwise <c>false</c>.</returns>
        public static bool IsVersionDefined(LibGit2Sharp.Commit commit, string projectDirectory = null)
        {
            return GetVersion(commit, projectDirectory) != null;
        }

        /// <summary>
        /// Checks whether the version.txt file is defined in the specified project directory
        /// or one of its ancestors.
        /// </summary>
        /// <param name="projectDirectory">The directory to start searching within.</param>
        /// <returns><c>true</c> if the version.txt file is found; otherwise <c>false</c>.</returns>
        public static bool IsVersionDefined(string projectDirectory)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));

            return GetVersion(projectDirectory) != null;
        }

        /// <summary>
        /// Writes the version.json file to a directory within a repo with the specified version information.
        /// The $schema property is included.
        /// </summary>
        /// <param name="projectDirectory">
        /// The path to the directory in which to write the version.json file.
        /// The file's impact will be all descendent projects and directories from this specified directory,
        /// except where any of those directories have their own version.json file.
        /// </param>
        /// <param name="version">The version information to write to the file.</param>
        /// <returns>The path to the file written.</returns>
        public static string SetVersion(string projectDirectory, VersionOptions version) => SetVersion(projectDirectory, version, includeSchemaProperty: true);

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
        public static string SetVersion(string projectDirectory, VersionOptions version, bool includeSchemaProperty)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));
            Requires.NotNull(version, nameof(version));
            Requires.Argument(version.Version != null || version.Inherit, nameof(version), $"{nameof(VersionOptions.Version)} must be set for a root-level version.json file.");

            Directory.CreateDirectory(projectDirectory);

            string versionTxtPath = Path.Combine(projectDirectory, TxtFileName);
            if (File.Exists(versionTxtPath))
            {
                if (version.IsDefaultVersionTheOnlyPropertySet)
                {
                    File.WriteAllLines(
                        versionTxtPath,
                        new[] { version.Version.Version.ToString(), version.Version.Prerelease });
                    return versionTxtPath;
                }
                else
                {
                    // The file must be upgraded to use the more descriptive JSON format.
                    File.Delete(versionTxtPath);
                }
            }

            using (var repo = GitExtensions.OpenGitRepo(projectDirectory))
            {
                string repoRelativeProjectDirectory = repo?.GetRepoRelativePath(projectDirectory);
                string versionJsonPath = Path.Combine(projectDirectory, JsonFileName);
                var jsonContent = JsonConvert.SerializeObject(version,
                    VersionOptions.GetJsonSettings(version.Inherit, includeSchemaProperty,
                        repoRelativeProjectDirectory));
                File.WriteAllText(versionJsonPath, jsonContent);
                return versionJsonPath;
            }
        }

        /// <summary>
        /// Writes the version.txt file to a directory within a repo with the specified version information.
        /// </summary>
        /// <param name="projectDirectory">
        /// The path to the directory in which to write the version.txt file.
        /// The file's impact will be all descendent projects and directories from this specified directory,
        /// except where any of those directories have their own version.txt file.
        /// </param>
        /// <param name="version">The version information to write to the file.</param>
        /// <param name="unstableTag">The optional unstable tag to include in the file.</param>
        /// <returns>The path to the file written.</returns>
        public static string SetVersion(string projectDirectory, Version version, string unstableTag = null)
        {
            return SetVersion(projectDirectory, VersionOptions.FromVersion(version, unstableTag), includeSchemaProperty: false);
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="versionTextContent">The content of the version.txt file to read.</param>
        /// <returns>The version information read from the file; or <c>null</c> if a deserialization error occurs.</returns>
        private static VersionOptions TryReadVersionFile(TextReader versionTextContent)
        {
            string versionLine = versionTextContent.ReadLine();
            string prereleaseVersion = versionTextContent.ReadLine();
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
        /// Tries to read a version.json file from the specified string, but favors returning null instead of throwing a <see cref="JsonSerializationException"/>.
        /// </summary>
        /// <param name="jsonContent">The content of the version.json file.</param>
        /// <param name="repoRelativeBaseDirectory">Directory that this version.json file is relative to the root of the repository.</param>
        /// <returns>The deserialized <see cref="VersionOptions"/> object, if deserialization was successful.</returns>
        private static VersionOptions TryReadVersionJsonContent(string jsonContent, string repoRelativeBaseDirectory)
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
    }
}
