using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Nerdbank.GitVersioning;
using Newtonsoft.Json;
using Validation;

namespace NerdBank.GitVersioning.Managed
{
    internal static class VersionFile
    {
        /// <summary>
        /// The filename of the version.json file.
        /// </summary>
        public const string JsonFileName = "version.json";

        const bool ClearArray =
#if DEBUG
            true;
#else
            false;
#endif

        public static (string, VersionOptions) GetVersionOptions(GitRepository repository, GitCommit? commit, string relativeRepoProjectDirectory)
        {
            if (commit == null)
            {
                return (null, null);
            }

            Stack<string> directories = new Stack<string>();
            string currentDirectory = relativeRepoProjectDirectory;

            while (!string.IsNullOrEmpty(currentDirectory))
            {
                directories.Push(Path.GetFileName(currentDirectory));
                currentDirectory = Path.GetDirectoryName(currentDirectory);
            }

            Stack<VersionOptions> versionOptions = new Stack<VersionOptions>();
            Stack<string> versionFileNames = new Stack<string>();

            GitObjectId tree = commit.Value.Tree;
            VersionOptions result;
            string versionFileName = "";

            while (tree != GitObjectId.Empty)
            {
                using (Stream treeStream = repository.GetObjectBySha(tree, "tree"))
                {
                    var versionObject = GitTreeStreamingReader.FindNode(treeStream, Encoding.UTF8.GetBytes(JsonFileName));

                    if (versionObject != GitObjectId.Empty)
                    {
                        using (Stream optionsStream = repository.GetObjectBySha(versionObject, "blob"))
                        using (StreamReader optionsReader = new StreamReader(optionsStream))
                        {
                            var versionJsonContent = optionsReader.ReadToEnd();

                            try
                            {
                                result =
                                    TryReadVersionJsonContent(versionJsonContent, repoRelativeBaseDirectory: currentDirectory ?? string.Empty);
                            }
                            catch (Exception ex)
                            {
                                throw new FormatException(
                                    $"Failure while reading {JsonFileName} from commit {commit.Value.Sha}. " +
                                    "Fix this commit with rebase if this is an error, or review this doc on how to migrate to Nerdbank.GitVersioning: " +
                                    "https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/migrating.md", ex);
                            }

                            versionOptions.Push(result);
                            versionFileNames.Push(Path.Combine(versionFileName, JsonFileName));
                        }
                    }
                }

                using (Stream treeStream = repository.GetObjectBySha(tree, "tree"))
                {
                    if (directories.Count > 0)
                    {
                        string directoryName = directories.Pop();
                        tree = GitTreeStreamingReader.FindNode(treeStream, Encoding.UTF8.GetBytes(directoryName));
                        versionFileName = Path.Combine(versionFileName, directoryName);
                    }
                    else
                    {
                        tree = GitObjectId.Empty;
                    }
                }
            }

            return versionOptions.Count > 0 ? (versionFileNames.Pop(), versionOptions.Pop()) : (null, null);
        }

        public static (string, VersionOptions) GetVersionOptions(string projectDirectory) =>
            GetVersionOptions(projectDirectory, out _);

        public static (string, VersionOptions) GetVersionOptions(string projectDirectory, out string actualDirectory)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));

            string searchDirectory = projectDirectory;
            while (searchDirectory != null)
            {
                string parentDirectory = Path.GetDirectoryName(searchDirectory);

                string versionJsonPath = Path.Combine(searchDirectory, JsonFileName);
                if (File.Exists(versionJsonPath))
                {
                    string versionJsonContent = File.ReadAllText(versionJsonPath);

                    string repoRelativeBaseDirectory = null; // repo?.GetRepoRelativePath(searchDirectory);
                    VersionOptions result =
                        TryReadVersionJsonContent(versionJsonContent, repoRelativeBaseDirectory);
                    if (result?.Inherit ?? false)
                    {
                        if (parentDirectory != null)
                        {
                            (_, result) = GetVersionOptions(parentDirectory);
                            if (result != null)
                            {
                                JsonConvert.PopulateObject(versionJsonContent, result,
                                    VersionOptions.GetJsonSettings(
                                        repoRelativeBaseDirectory: repoRelativeBaseDirectory));
                                actualDirectory = searchDirectory;
                                return (versionJsonPath, result);
                            }
                        }

                        throw new InvalidOperationException(
                            $"\"{versionJsonPath}\" inherits from a parent directory version.json file but none exists.");
                    }
                    else if (result != null)
                    {
                        actualDirectory = searchDirectory;
                        return (versionJsonPath, result);
                    }
                }

                searchDirectory = parentDirectory;
            }

            actualDirectory = null;
            return (null, null);
        }

        public static string GetVersion(string path)
        {
            if (FileHelpers.TryOpen(path, CreateFileFlags.FILE_ATTRIBUTE_NORMAL, out FileStream stream))
            {
                using (stream)
                {
                    return GetVersion(stream);
                }
            }
            else
            {
                return null;
            }
        }

        public static string GetVersion(Stream stream)
        {
            if (stream == null)
            {
                return null;
            }

            string value = null;

            byte[] data = ArrayPool<byte>.Shared.Rent((int)stream.Length);
            stream.Read(data);

            var span = data.AsSpan(0, (int)stream.Length);
            var reader = new Utf8JsonReader(span, isFinalBlock: true, default);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.ValueTextEquals("version"))
                {
                    reader.Read();
                    value = reader.GetString();
                    break;
                }
            }

            ArrayPool<byte>.Shared.Return(data, clearArray: ClearArray);

            return value;
        }

        public static VersionOptions TryReadVersion(string path, string repoRelativeBaseDirectory)
        {
            return TryReadVersionJsonContent(File.ReadAllText(path), repoRelativeBaseDirectory);
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
