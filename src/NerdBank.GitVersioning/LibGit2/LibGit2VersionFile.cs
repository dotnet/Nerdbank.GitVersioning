// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LibGit2Sharp;
using Newtonsoft.Json;

#nullable enable

namespace Nerdbank.GitVersioning.LibGit2;

/// <summary>
/// Exposes queries and mutations on a version.json or version.txt file,
/// implemented in terms of libgit2sharp.
/// </summary>
internal class LibGit2VersionFile : VersionFile
{
    /// <summary>
    /// A sequence of possible filenames for the version file in preferred order.
    /// </summary>
    public static readonly IReadOnlyList<string> PreferredFileNames = new[] { JsonFileName, TxtFileName };

    internal LibGit2VersionFile(LibGit2Context context)
        : base(context)
    {
    }

    protected new LibGit2Context Context => (LibGit2Context)base.Context;

    /// <summary>
    /// Reads the version.json file and returns the <see cref="VersionOptions"/> deserialized from it.
    /// </summary>
    /// <param name="commit">The commit to read from.</param>
    /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
    /// <param name="blobVersionCache">An optional blob cache for storing the raw parse results of a version.txt or version.json file (before any inherit merge operations are applied).</param>
    /// <param name="requirements"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='requirements']" /></param>
    /// <param name="locations"><inheritdoc cref="GetVersionCore(VersionFileRequirements, out VersionFileLocations)" path="/param[@name='locations']" /></param>
    /// <returns>The version information read from the file.</returns>
    internal VersionOptions? GetVersion(Commit commit, string repoRelativeProjectDirectory, Dictionary<ObjectId, VersionOptions?>? blobVersionCache, VersionFileRequirements requirements, out VersionFileLocations locations)
    {
        repoRelativeProjectDirectory = TrimTrailingPathSeparator(repoRelativeProjectDirectory);
        locations = default;

        string? searchDirectory = repoRelativeProjectDirectory ?? string.Empty;
        while (searchDirectory is object)
        {
            string? parentDirectory = searchDirectory.Length > 0 ? Path.GetDirectoryName(searchDirectory) : null;

            string candidatePath = Path.Combine(searchDirectory, TxtFileName).Replace('\\', '/');
            var versionTxtBlob = commit.Tree[candidatePath]?.Target as Blob;
            if (versionTxtBlob is object)
            {
                if (blobVersionCache is null || !blobVersionCache.TryGetValue(versionTxtBlob.Id, out VersionOptions? result))
                {
                    result = TryReadVersionFile(new StreamReader(versionTxtBlob.GetContentStream()));
                    if (blobVersionCache is object)
                    {
                        result?.Freeze();
                        blobVersionCache.Add(versionTxtBlob.Id, result);
                    }
                }

                if (result is object)
                {
                    IBelongToARepository commitAsRepoMember = commit;
                    this.ApplyLocations(result, Path.Combine(commitAsRepoMember.Repository.Info.WorkingDirectory, searchDirectory), ref locations);
                    return VersionOptionsSatisfyRequirements(result, requirements) ? result : null;
                }
            }

            candidatePath = Path.Combine(searchDirectory, JsonFileName).Replace('\\', '/');
            var versionJsonBlob = commit.Tree[candidatePath]?.Target as LibGit2Sharp.Blob;
            if (versionJsonBlob is object)
            {
                string? versionJsonContent = null;
                if (blobVersionCache is null || !blobVersionCache.TryGetValue(versionJsonBlob.Id, out VersionOptions? result))
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
                            $"Failure while reading {JsonFileName} from commit {this.Context.GitCommitId}. " +
                            "Fix this commit with rebase if this is an error, or review this doc on how to migrate to Nerdbank.GitVersioning: " +
                            "https://dotnet.github.io/Nerdbank.GitVersioning/docs/migrating.html",
                            ex);
                    }

                    if (blobVersionCache is object)
                    {
                        result?.Freeze();
                        blobVersionCache.Add(versionJsonBlob.Id, result);
                    }
                }

                this.ApplyLocations(result, Path.Combine(this.Context.WorkingTreePath, searchDirectory), ref locations);
                if (VersionOptionsSatisfyRequirements(result, requirements))
                {
                    return result;
                }

                if (result?.Inherit is true)
                {
                    if (parentDirectory is object)
                    {
                        result = this.GetVersion(commit, parentDirectory, blobVersionCache, requirements, out VersionFileLocations parentLocations);
                        this.MergeLocations(ref locations, parentLocations);
                        if (!requirements.HasFlag(VersionFileRequirements.NonMergedResult) && result is not null)
                        {
                            if (versionJsonContent is null)
                            {
                                // We reused a cache VersionOptions, but now we need the actual JSON string.
                                using var sr = new StreamReader(versionJsonBlob.GetContentStream());
                                versionJsonContent = sr.ReadToEnd();
                            }

                            if (result.IsFrozen)
                            {
                                result = new VersionOptions(result);
                            }

                            JsonConvert.PopulateObject(versionJsonContent, result, VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: searchDirectory));
                            result.Inherit = false;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"\"{candidatePath}\" inherits from a parent directory version.json file but none exists.");
                    }

                    return VersionOptionsSatisfyRequirements(result, requirements) ? result : null;
                }
                else if (result is object)
                {
                    IBelongToARepository commitAsRepoMember = commit;
                    return VersionOptionsSatisfyRequirements(result, requirements) ? result : null;
                }
            }

            searchDirectory = parentDirectory;
        }

        locations = default;
        return null;
    }

    /// <inheritdoc/>
    protected override VersionOptions? GetVersionCore(VersionFileRequirements requirements, out VersionFileLocations locations) => this.GetVersion(this.Context.Commit!, this.Context.RepoRelativeProjectDirectory, null, requirements, out locations);
}
