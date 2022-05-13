// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Nerdbank.GitVersioning.ManagedGit;
using Newtonsoft.Json;

#nullable enable

namespace Nerdbank.GitVersioning.Managed;

/// <summary>
/// Exposes queries and mutations on a version.json or version.txt file,
/// implemented in terms of our private managed git implementation.
/// </summary>
internal class ManagedVersionFile : VersionFile
{
    /// <summary>
    /// The filename of the version.txt file, as a byte array.
    /// </summary>
    private static readonly byte[] TxtFileNameBytes = Encoding.ASCII.GetBytes(TxtFileName);

    /// <summary>
    /// The filename of the version.json file, as a byte array.
    /// </summary>
    private static readonly byte[] JsonFileNameBytes = Encoding.ASCII.GetBytes(JsonFileName);

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedVersionFile"/> class.
    /// </summary>
    /// <param name="context"><inheritdoc /></param>
    public ManagedVersionFile(GitContext context)
        : base(context)
    {
    }

    protected new ManagedGitContext Context => (ManagedGitContext)base.Context;

    /// <summary>
    /// Reads the version.json file and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="commit">The commit to read from.</param>
    /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
    /// <param name="blobVersionCache">An optional blob cache for storing the raw parse results of a version.txt or version.json file (before any inherit merge operations are applied).</param>
    /// <param name="actualDirectory">Receives the full path to the directory in which the version file was found.</param>
    /// <returns>The version information read from the file.</returns>
    internal VersionOptions? GetVersion(GitCommit commit, string repoRelativeProjectDirectory, Dictionary<GitObjectId, VersionOptions?>? blobVersionCache, out string? actualDirectory)
    {
        var directories = new Stack<string>();

        string? currentDirectory = repoRelativeProjectDirectory;

        while (!string.IsNullOrEmpty(currentDirectory))
        {
            directories.Push(Path.GetFileName(currentDirectory));
            currentDirectory = Path.GetDirectoryName(currentDirectory);
        }

        GitObjectId tree = commit.Tree;
        string? searchDirectory = string.Empty;
        string? parentDirectory = null;

        VersionOptions? finalResult = null;
        actualDirectory = null;

        while (tree != GitObjectId.Empty)
        {
            GitObjectId versionTxtBlob = this.Context.Repository.GetTreeEntry(tree, TxtFileNameBytes);
            if (versionTxtBlob != GitObjectId.Empty)
            {
                if (blobVersionCache is null || !blobVersionCache.TryGetValue(versionTxtBlob, out VersionOptions? result))
                {
                    result = TryReadVersionFile(new StreamReader(this.Context.Repository.GetObjectBySha(versionTxtBlob, "blob")!));
                    if (blobVersionCache is object)
                    {
                        result?.Freeze();
                        blobVersionCache.Add(versionTxtBlob, result);
                    }
                }

                if (result is object)
                {
                    finalResult = result;
                    actualDirectory = Path.Combine(this.Context.WorkingTreePath, searchDirectory);
                }
            }

            GitObjectId versionJsonBlob = this.Context.Repository.GetTreeEntry(tree, JsonFileNameBytes);
            if (versionJsonBlob != GitObjectId.Empty)
            {
                string? versionJsonContent = null;
                if (blobVersionCache is null || !blobVersionCache.TryGetValue(versionJsonBlob, out VersionOptions? result))
                {
                    using (var sr = new StreamReader(this.Context.Repository.GetObjectBySha(versionJsonBlob, "blob")!))
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
                            $"Failure while reading {JsonFileName} from commit {this.Context.GitCommitId}. " +
                            "Fix this commit with rebase if this is an error, or review this doc on how to migrate to Nerdbank.GitVersioning: " +
                            "https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/doc/migrating.md",
                            ex);
                    }

                    if (blobVersionCache is object)
                    {
                        result?.Freeze();
                        blobVersionCache.Add(versionJsonBlob, result);
                    }
                }

                if (result?.Inherit ?? false)
                {
                    if (parentDirectory is object)
                    {
                        result = this.GetVersion(commit, parentDirectory, blobVersionCache, out string? resultingDirectory);
                        if (result is object)
                        {
                            if (versionJsonContent is null)
                            {
                                // We reused a cache VersionOptions, but now we need the actual JSON string.
                                using var sr = new StreamReader(this.Context.Repository.GetObjectBySha(versionJsonBlob, "blob")!);
                                versionJsonContent = sr.ReadToEnd();
                            }

                            if (result.IsFrozen)
                            {
                                result = new VersionOptions(result);
                            }

                            JsonConvert.PopulateObject(versionJsonContent, result, VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: searchDirectory));
                            finalResult = result;
                        }
                        else
                        {
                            string? candidatePath = Path.Combine(searchDirectory, JsonFileName);
                            throw new InvalidOperationException($"\"{candidatePath}\" inherits from a parent directory version.json file but none exists.");
                        }
                    }
                    else
                    {
                        string? candidatePath = Path.Combine(searchDirectory, JsonFileName);
                        throw new InvalidOperationException($"\"{candidatePath}\" inherits from a parent directory version.json file but none exists.");
                    }
                }

                if (result is object)
                {
                    actualDirectory = Path.Combine(this.Context.WorkingTreePath, searchDirectory);
                    finalResult = result;
                }
            }

            if (directories.Count > 0)
            {
                string? directoryName = directories.Pop();
                tree = this.Context.Repository.GetTreeEntry(tree, GitRepository.Encoding.GetBytes(directoryName));
                parentDirectory = searchDirectory;
                searchDirectory = Path.Combine(searchDirectory, directoryName);
            }
            else
            {
                tree = GitObjectId.Empty;
                parentDirectory = null;
                searchDirectory = null;
                break;
            }
        }

        return finalResult;
    }

    /// <inheritdoc/>
    protected override VersionOptions? GetVersionCore(out string? actualDirectory) => this.GetVersion(this.Context.Commit!.Value, this.Context.RepoRelativeProjectDirectory, null, out actualDirectory);
}
