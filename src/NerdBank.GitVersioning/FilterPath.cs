using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Validation;

namespace Nerdbank.GitVersioning
{
    /// <summary>
    /// A filter (include or exclude) representing a repo relative path.
    /// </summary>
    internal class FilterPath
    {
        private readonly StringComparison stringComparison;

        /// <summary>
        /// True if this <see cref="FilterPath"/> represents an exclude filter.
        /// </summary>
        internal bool IsExclude { get; }

        /// <summary>
        /// Path relative to the repository root that this <see cref="FilterPath"/> represents.
        /// Slashes are canonical for this OS.
        /// </summary>
        internal string RepoRelativePath { get; }

        /// <summary>
        /// True if this <see cref="FilterPath"/> represents the root of the repository.
        /// </summary>
        internal bool IsRoot => this.RepoRelativePath == "";

        /// <summary>
        /// Parses a pathspec-like string into a root-relative path.
        /// </summary>
        /// <param name="path">
        /// See <see cref="FilterPath(string, string, bool)"/> for supported
        /// formats of pathspecs.
        /// </param>
        /// <param name="relativeTo">
        /// Path that <paramref name="path"/> is relative to.
        /// Can be <c>null</c> - which indicates <paramref name="path"/> is
        /// relative to the root of the repository.
        /// </param>
        /// <returns>
        /// Forward slash delimited string representing the root-relative path.
        /// </returns>
        private static string ParsePath(string path, string relativeTo)
        {
            // Path is absolute, nothing to do here
            if (path[0] == '/' || path[0] == '\\')
            {
                return path.Substring(1);
            }

            var combined = relativeTo == null ? path : relativeTo + '/' + path;

            return string.Join("/",
                combined
                    .Split(new[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar},
                        StringSplitOptions.RemoveEmptyEntries)
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
                                    throw new FormatException($"Too many '..' in path '{combined}' - would escape the root of the repository.");

                                parts.Pop();
                                return parts;

                            // Otherwise it's a directory/file name - add it to the stack
                            default:
                                parts.Push(segment);
                                return parts;
                        }
                    })
                    // Reverse the stack, so it iterates root -> leaf
                    .Reverse()
            );
        }

        /// <summary>
        /// Construct a <see cref="FilterPath"/> from a pathspec-like string and a
        /// relative path within the repository.
        /// </summary>
        /// <param name="pathSpec">
        /// A string that supports some pathspec features.
        /// This path is relative to <paramref name="relativeTo"/>.
        ///
        /// Examples:
        /// - <c>../relative/inclusion.txt</c>
        /// - <c>:/absolute/inclusion.txt</c>
        /// - <c>:!relative/exclusion.txt</c>
        /// - <c>:^relative/exclusion.txt</c>
        /// - <c>:^/absolute/exclusion.txt</c>
        /// </param>
        /// <param name="relativeTo">
        /// Path (relative to the root of the repository) that <paramref name="pathSpec"/> is relative to.
        /// </param>
        /// <param name="ignoreCase">Whether case should be ignored by <see cref="Excludes"/></param>
        /// <exception cref="FormatException">Invalid path spec.</exception>
        internal FilterPath(string pathSpec, string relativeTo, bool ignoreCase = false)
        {
            Requires.NotNullOrEmpty(pathSpec, nameof(pathSpec));

            if (pathSpec[0] == ':')
            {
                if (pathSpec.Length > 1 && (pathSpec[1] == '^' || pathSpec[1] == '!'))
                {
                    this.IsExclude = true;
                    this.stringComparison = ignoreCase
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                    this.RepoRelativePath = ParsePath(pathSpec.Substring(2), relativeTo);
                }
                else if (pathSpec.Length > 1 && pathSpec[1] == '/' || pathSpec[1] == '\\')
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
                this.RepoRelativePath = ParsePath(pathSpec, relativeTo);
            }

            this.RepoRelativePath =
                this.RepoRelativePath
                    .Replace('\\', '/')
                    .TrimEnd('/');
        }

        /// <summary>
        /// Calculate the <see cref="FilterPath"/>s for a given project within a repository.
        /// </summary>
        /// <param name="versionOptions">Version options for the project.</param>
        /// <param name="relativeRepoProjectDirectory">
        /// Path to the project directory, relative to the root of the repository.
        /// If <c>null</c>, assumes root of repository.
        /// </param>
        /// <param name="repository">Git repository containing the project.</param>
        /// <returns>
        /// <c>null</c> if no path filters are set. Otherwise, returns a list of
        /// <see cref="FilterPath"/> instances.
        /// </returns>
        internal static IReadOnlyList<FilterPath> FromVersionOptions(VersionOptions versionOptions,
            string relativeRepoProjectDirectory,
            IRepository repository)
        {
            Requires.NotNull(versionOptions, nameof(versionOptions));

            var ignoreCase = repository?.Config.Get<bool>("core.ignorecase")?.Value ?? false;

            return versionOptions.PathFilters
                ?.Select(pathSpec => new FilterPath(pathSpec, relativeRepoProjectDirectory,
                    ignoreCase))
                .ToList();
        }

        /// <summary>
        /// Determines if <paramref name="repoRelativePath"/> should be excluded by this <see cref="FilterPath"/>.
        /// </summary>
        /// <param name="repoRelativePath">Forward-slash delimited path (repo relative).</param>
        /// <returns>
        /// True if this <see cref="FilterPath"/> is an excluding filter that matches
        /// <paramref name="repoRelativePath"/>, otherwise false.
        /// </returns>
        internal bool Excludes(string repoRelativePath)
        {
            if (repoRelativePath is null)
                throw new ArgumentNullException(nameof(repoRelativePath));

            if (!this.IsExclude) return false;

            return this.RepoRelativePath.Equals(repoRelativePath, this.stringComparison) ||
                   repoRelativePath.StartsWith(this.RepoRelativePath + "/",
                       this.stringComparison);
        }
    }
}