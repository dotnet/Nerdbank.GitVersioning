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

        public static VersionOptions GetVersionOptions(GitRepository repository, GitCommit? commit, string relativeRepoProjectDirectory)
        {
            if (commit == null)
            {
                return null;
            }

            Stack<string> directories = new Stack<string>();
            string currentDirectory = relativeRepoProjectDirectory;

            while (!string.IsNullOrEmpty(currentDirectory))
            {
                directories.Push(Path.GetFileName(currentDirectory));
                currentDirectory = Path.GetDirectoryName(currentDirectory);
            }

            Stack<VersionOptions> versionOptions = new Stack<VersionOptions>();

            GitObjectId tree = commit.Value.Tree;

            while (tree != GitObjectId.Empty)
            {
                using (Stream treeStream = repository.GetObjectBySha(tree, "tree", false))
                {
                    var versionObject = GitTreeStreamingReader.FindNode(treeStream, Encoding.UTF8.GetBytes(JsonFileName));

                    if (versionObject != GitObjectId.Empty)
                    {
                        using (Stream optionsStream = repository.GetObjectBySha(versionObject, "blob", false))
                        using (StreamReader optionsReader = new StreamReader(optionsStream))
                        {
                            var versionJsonContent = optionsReader.ReadToEnd();
                            VersionOptions result =
                                TryReadVersionJsonContent(versionJsonContent, repoRelativeBaseDirectory: null);

                            versionOptions.Push(result);
                        }
                    }
                }

                using (Stream treeStream = repository.GetObjectBySha(tree, "tree", false))
                {
                    tree = directories.Count > 0
                        ? GitTreeStreamingReader.FindNode(treeStream, Encoding.UTF8.GetBytes(directories.Pop()))
                        : GitObjectId.Empty;
                }
            }

            return versionOptions.Count > 0 ? versionOptions.Pop() : null;
        }

        public static VersionOptions GetVersionOptions(string projectDirectory) =>
            GetVersionOptions(projectDirectory, out _);

        public static VersionOptions GetVersionOptions(string projectDirectory, out string actualDirectory)
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
                            result = GetVersionOptions(parentDirectory);
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

            actualDirectory = null;
            return null;
        }

        public static string GetVersion(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return GetVersion(stream);
            }
        }

        public static string GetVersion(Stream stream)
        {
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
