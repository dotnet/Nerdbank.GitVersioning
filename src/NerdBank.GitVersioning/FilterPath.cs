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
    public class FilterPath
    {
        private readonly StringComparison stringComparison;

        /// <summary>
        /// True if this <see cref="FilterPath"/> represents an exclude filter.
        /// </summary>
        public bool IsExclude { get; }

        /// <summary>
        /// Path relative to the repository root that this <see cref="FilterPath"/> represents.
        /// Slashes are canonical for this OS.
        /// </summary>
        public string RepoRelativePath { get; }

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
                    .Aggregate(new Stack<string>(), (parts, segment) =>
                    {
                        switch (segment)
                        {
                            case ".":
                                return parts;
                            case "..":
                                parts.Pop();
                                return parts;
                            default:
                                parts.Push(segment);
                                return parts;
                        }
                    })
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
        public FilterPath(string pathSpec, string relativeTo, bool ignoreCase = false)
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
        public static IReadOnlyList<FilterPath> FromVersionOptions(VersionOptions versionOptions,
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
        public bool Excludes(string repoRelativePath)
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