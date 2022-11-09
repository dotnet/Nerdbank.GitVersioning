// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Validation;

namespace Nerdbank.GitVersioning;

/// <summary>
/// A filter (include or exclude) representing a repo relative path.
/// </summary>
public class FilterPath
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilterPath"/> class
    /// from a pathspec-like string and a relative path within the repository.
    /// </summary>
    /// <param name="pathSpec">
    /// A string that supports some pathspec features.
    /// This path is relative to <paramref name="relativeTo"/>.
    ///
    /// Examples:
    /// <list type="bullet">
    /// <item><c>../relative/inclusion.txt</c></item>
    /// <item><c>:/absolute/inclusion.txt</c></item>
    /// <item><c>:!relative/exclusion.txt</c></item>
    /// <item><c>:^relative/exclusion.txt</c></item>
    /// <item><c>:^/absolute/exclusion.txt</c></item>
    /// </list>
    /// </param>
    /// <param name="relativeTo">
    /// Path (relative to the root of the repository) that <paramref name="pathSpec"/> is relative to.
    /// Can be empty - which indicates <paramref name="pathSpec"/> is
    /// relative to the root of the repository.
    /// </param>
    /// <exception cref="FormatException">Invalid path spec.</exception>
    public FilterPath(string pathSpec, string relativeTo)
    {
        Requires.NotNullOrEmpty(pathSpec, nameof(pathSpec));
        Requires.NotNull(relativeTo, nameof(relativeTo));

        if (pathSpec[0] == ':')
        {
            if (pathSpec.Length > 1 && (pathSpec[1] == '^' || pathSpec[1] == '!'))
            {
                this.IsExclude = true;
                (this.IsRelative, this.RepoRelativePath) = Normalize(pathSpec.Substring(2), relativeTo);
            }
            else if ((pathSpec.Length > 1 && pathSpec[1] == '/') || pathSpec[1] == '\\')
            {
                this.RepoRelativePath = pathSpec.Substring(2);
            }
            else
            {
                throw new FormatException($"Unrecognized path spec '{pathSpec}'");
            }
        }
        else
        {
            (this.IsRelative, this.RepoRelativePath) = Normalize(pathSpec, relativeTo);
        }

        this.RepoRelativePath =
            this.RepoRelativePath
                .Replace('\\', '/')
                .TrimEnd('/');
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="FilterPath"/> represents an exclude filter.
    /// </summary>
    public bool IsExclude { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="FilterPath"/> represents an include filter.
    /// </summary>
    public bool IsInclude => !this.IsExclude;

    /// <summary>
    /// Gets the path that this <see cref="FilterPath"/> represents, relative to the repository root.
    /// Directories are delimited with forward slashes.
    /// </summary>
    public string RepoRelativePath { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="FilterPath"/> represents the root of the repository.
    /// </summary>
    public bool IsRoot => this.RepoRelativePath == string.Empty;

    /// <summary>
    /// Gets a value indicating whether the original pathspec was parsed as a relative path.
    /// </summary>
    internal bool IsRelative { get; }

    /// <summary>
    /// Determines if <paramref name="repoRelativePath"/> should be excluded by this <see cref="FilterPath"/>.
    /// </summary>
    /// <param name="repoRelativePath">Forward-slash delimited path (repo relative).</param>
    /// <param name="ignoreCase">
    /// Whether paths should be compared case insensitively.
    /// Should be the 'core.ignorecase' config value for the repository.
    /// </param>
    /// <returns>
    /// True if this <see cref="FilterPath"/> is an excluding filter that matches
    /// <paramref name="repoRelativePath"/>, otherwise false.
    /// </returns>
    public bool Excludes(string repoRelativePath, bool ignoreCase)
    {
        if (repoRelativePath is null)
        {
            throw new ArgumentNullException(nameof(repoRelativePath));
        }

        if (!this.IsExclude)
        {
            return false;
        }

        StringComparison stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return this.RepoRelativePath.Equals(repoRelativePath, stringComparison) ||
               repoRelativePath.StartsWith(this.RepoRelativePath + "/", stringComparison);
    }

    /// <summary>
    /// Determines if <paramref name="repoRelativePath"/> should be included by this <see cref="FilterPath"/>.
    /// </summary>
    /// <param name="repoRelativePath">Forward-slash delimited path (repo relative).</param>
    /// <param name="ignoreCase">
    /// Whether paths should be compared case insensitively.
    /// Should be the 'core.ignorecase' config value for the repository.
    /// </param>
    /// <returns>
    /// True if this <see cref="FilterPath"/> is an including filter that matches
    /// <paramref name="repoRelativePath"/>, otherwise false.
    /// </returns>
    public bool Includes(string repoRelativePath, bool ignoreCase)
    {
        if (repoRelativePath is null)
        {
            throw new ArgumentNullException(nameof(repoRelativePath));
        }

        if (!this.IsInclude)
        {
            return false;
        }

        if (this.IsRoot)
        {
            return true;
        }

        StringComparison stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return this.RepoRelativePath.Equals(repoRelativePath, stringComparison) ||
               repoRelativePath.StartsWith(this.RepoRelativePath + "/", stringComparison);
    }

    /// <summary>
    /// Determines if children of <paramref name="repoRelativePath"/> may be included
    /// by this <see cref="FilterPath"/>.
    /// </summary>
    /// <param name="repoRelativePath">Forward-slash delimited path (repo relative).</param>
    /// <param name="ignoreCase">
    /// Whether paths should be compared case insensitively.
    /// Should be the 'core.ignorecase' config value for the repository.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if this <see cref="FilterPath"/> is an including filter that may match
    /// children of <paramref name="repoRelativePath"/>, otherwise <see langword="false"/>.
    /// </returns>
    public bool IncludesChildren(string repoRelativePath, bool ignoreCase)
    {
        if (repoRelativePath is null)
        {
            throw new ArgumentNullException(nameof(repoRelativePath));
        }

        if (!this.IsInclude)
        {
            return false;
        }

        if (this.IsRoot)
        {
            return true;
        }

        StringComparison stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return this.RepoRelativePath.StartsWith(repoRelativePath + "/", stringComparison);
    }

    /// <summary>
    /// Convert this path filter to a pathspec.
    /// </summary>
    /// <param name="repoRelativeBaseDirectory">
    /// Repo-relative directory that relative pathspecs should be relative to.
    /// Can be empty - which indicates this <c>FilterPath</c> is
    /// relative to the root of the repository.
    /// </param>
    /// <returns>String representation of a path filter (a pathspec).</returns>
    public string ToPathSpec(string repoRelativeBaseDirectory)
    {
        Requires.NotNull(repoRelativeBaseDirectory, nameof(repoRelativeBaseDirectory));

        var pathSpec = new StringBuilder(this.RepoRelativePath.Length + 2);
        (bool _, string normalizedBaseDirectory) =
            Normalize(repoRelativeBaseDirectory == string.Empty ? "." : repoRelativeBaseDirectory, null);

        if (this.IsExclude)
        {
            pathSpec.Append(":!");
        }

        if (this.IsRelative)
        {
            (int dirsAscended, StringBuilder relativePath) = GetRelativePath(this.RepoRelativePath, normalizedBaseDirectory);
            if (dirsAscended == 0 && !this.IsExclude)
            {
                pathSpec.Append("./");
            }

            pathSpec.Append(relativePath);
        }
        else
        {
            pathSpec.Append('/');
            pathSpec.Append(this.RepoRelativePath);
        }

        return pathSpec.ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.RepoRelativePath;
    }

    /// <summary>
    /// Normalizes a pathspec-like string into a root-relative path.
    /// </summary>
    /// <param name="path">
    /// See <see cref="FilterPath(string, string)"/> for supported
    /// formats of pathspecs.
    /// </param>
    /// <param name="relativeTo">
    /// Path that <paramref name="path"/> is relative to.
    /// Can be empty - which indicates <paramref name="path"/> is
    /// relative to the root of the repository.
    /// </param>
    /// <returns>
    /// Forward slash delimited string representing the root-relative path.
    /// </returns>
    private static (bool IsRelative, string Normalized) Normalize(string path, string relativeTo)
    {
        // Path is absolute, nothing to do here
        if (path[0] == '/' || path[0] == '\\')
        {
            return (false, path.Substring(1));
        }

        string combined = relativeTo == string.Empty ? path : relativeTo + '/' + path;

        return (true, string.Join(
            "/",
            combined
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)

                // Loop through each path segment...
                .Aggregate(new Stack<string>(), (parts, segment) =>
                {
                    switch (segment)
                    {
                        // If it refers to the current directory, skip it
                        case ".":
                            return parts;

                        // If it refers to the parent directory, pop the most recent directory
                        case "..":
                            if (parts.Count == 0)
                            {
                                throw new FormatException($"Too many '..' in path '{combined}' - would escape the root of the repository.");
                            }

                            parts.Pop();
                            return parts;

                        // Otherwise it's a directory/file name - add it to the stack
                        default:
                            parts.Push(segment);
                            return parts;
                    }
                })

                // Reverse the stack, so it iterates root -> leaf
                .Reverse()));
    }

    private static (int DirsToAscend, StringBuilder Result) GetRelativePath(string path, string relativeTo)
    {
        string[] pathParts = path.Split('/');
        string[] baseDirParts = relativeTo.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        int commonParts;
        for (commonParts = 0;
            commonParts < Math.Min(pathParts.Length, baseDirParts.Length) &&
            pathParts[commonParts].Equals(baseDirParts[commonParts], StringComparison.OrdinalIgnoreCase);
            ++commonParts)
        {
        }

        int dirsToAscend = baseDirParts.Length - commonParts;

        var result = new StringBuilder(path.Length + (dirsToAscend * 3));
        result.Insert(0, "../", dirsToAscend);
        result.Append(string.Join("/", pathParts.Skip(commonParts)));
        return (dirsToAscend, result);
    }
}
