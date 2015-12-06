namespace Nerdbank.GitVersioning
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using Newtonsoft.Json.Serialization;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Validation;
    using System.Linq;
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
        /// The JSON serializer settings to use.
        /// </summary>
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings()
        {
            Converters = new JsonConverter[] {
                new VersionConverter(),
                new SemanticVersionJsonConverter(),
            },
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
        };

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <returns>The version information read from the file.</returns>
        public static VersionOptions GetVersion(LibGit2Sharp.Commit commit, string repoRelativeProjectDirectory = null)
        {
            if (commit == null)
            {
                return null;
            }

            bool json;
            using (var content = GetVersionFileReader(commit, repoRelativeProjectDirectory, out json))
            {
                return content != null
                    ? ReadVersionFile(content, json)
                    : null;
            }
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
            }

            return GetVersion(repo.Head.Commits.FirstOrDefault(), repoRelativeProjectDirectory);
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="projectDirectory">The path to the directory which may (or its ancestors may) define the version.txt file.</param>
        /// <returns>The version information read from the file, or <c>null</c> if the file wasn't found.</returns>
        public static VersionOptions GetVersion(string projectDirectory)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));

            string searchDirectory = projectDirectory;
            while (searchDirectory != null)
            {
                string versionTxtPath = Path.Combine(searchDirectory, TxtFileName);
                if (File.Exists(versionTxtPath))
                {
                    using (var sr = new StreamReader(versionTxtPath))
                    {
                        return ReadVersionFile(sr, isJsonFile: false);
                    }
                }

                string versionJsonPath = Path.Combine(searchDirectory, JsonFileName);
                if (File.Exists(versionJsonPath))
                {
                    using (var sr = new StreamReader(versionJsonPath))
                    {
                        return ReadVersionFile(sr, isJsonFile: true);
                    }
                }

                searchDirectory = Path.GetDirectoryName(searchDirectory);
            }

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
        /// Writes the version.txt file to a directory within a repo with the specified version information.
        /// </summary>
        /// <param name="projectDirectory">
        /// The path to the directory in which to write the version.txt file.
        /// The file's impact will be all descendent projects and directories from this specified directory,
        /// except where any of those directories have their own version.txt file.
        /// </param>
        /// <param name="version">The version information to write to the file.</param>
        /// <returns>The path to the file written.</returns>
        public static string SetVersion(string projectDirectory, VersionOptions version)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));
            Requires.NotNull(version, nameof(version));
            Requires.Argument(version.Version != null, nameof(version), $"{nameof(VersionOptions.Version)} must be set.");

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

            string versionJsonPath = Path.Combine(projectDirectory, JsonFileName);
            var jsonContent = JsonConvert.SerializeObject(version, JsonSettings);
            File.WriteAllText(versionJsonPath, jsonContent);
            return versionJsonPath;
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
            return SetVersion(projectDirectory, VersionOptions.FromVersion(version, unstableTag));
        }

        /// <summary>
        /// Reads the version.txt file that is in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <param name="isJsonFile">Receives a value indicating whether the file found is a JSON file.</param>
        /// <returns>A text reader with the content of the version.txt file.</returns>
        private static TextReader GetVersionFileReader(LibGit2Sharp.Commit commit, string repoRelativeProjectDirectory, out bool isJsonFile)
        {
            string searchDirectory = repoRelativeProjectDirectory ?? string.Empty;
            while (searchDirectory != null)
            {
                string candidatePath = Path.Combine(searchDirectory, JsonFileName);
                var versionTxtBlob = commit.Tree[candidatePath]?.Target as LibGit2Sharp.Blob;
                if (versionTxtBlob != null)
                {
                    isJsonFile = true;
                    return new StreamReader(versionTxtBlob.GetContentStream());
                }

                candidatePath = Path.Combine(searchDirectory, TxtFileName);
                versionTxtBlob = commit.Tree[candidatePath]?.Target as LibGit2Sharp.Blob;
                if (versionTxtBlob != null)
                {
                    isJsonFile = false;
                    return new StreamReader(versionTxtBlob.GetContentStream());
                }

                searchDirectory = searchDirectory.Length > 0 ? Path.GetDirectoryName(searchDirectory) : null;
            }

            isJsonFile = false;
            return null;
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="versionTextContent">The content of the version.txt file to read.</param>
        /// <param name="isJsonFile"><c>true</c> if the file being read is a JSON file; <c>false</c> for the old-style text format.</param>
        /// <returns>The version information read from the file.</returns>
        private static VersionOptions ReadVersionFile(TextReader versionTextContent, bool isJsonFile)
        {
            if (isJsonFile)
            {
                string jsonContent = versionTextContent.ReadToEnd();
                return JsonConvert.DeserializeObject<VersionOptions>(jsonContent, JsonSettings);
            }

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
    }
}
