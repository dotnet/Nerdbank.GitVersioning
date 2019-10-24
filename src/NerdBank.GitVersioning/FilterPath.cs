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
            if (path[0] == '/')
            {
                return path.Substring(1);
            }

            var combined = relativeTo == null ? path : relativeTo + '/' + path;
            return string.Join(Path.DirectorySeparatorChar.ToString(),
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

        public FilterPath(string pathSpec, string relativeTo, Configuration config) : this(pathSpec, relativeTo,
            config?.Get<bool>("core.ignorecase")?.Value ?? false)
        {
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
        /// Path (relative to the root of the repository) that <paramref name="paramRef"/> is relative to.
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
                else if (pathSpec.Length > 1 && pathSpec[1] == '/')
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
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar);
        }

        public static IReadOnlyList<FilterPath> FromVersionOptions(VersionOptions versionOptions,
            string relativeRepoProjectDirectory,
            IRepository repository)
        {
            Requires.NotNull(versionOptions, nameof(versionOptions));
            return versionOptions.PathFilters
                ?.Select(pathSpec => new FilterPath(pathSpec, relativeRepoProjectDirectory,
                    repository?.Config))
                .ToList();
        }

        /// <summary>
        /// Determines if <paramref name="repoRelativePath"/> should be excluded by this <see cref="FilterPath"/>.
        /// </summary>
        /// <param name="repoRelativePath">Path (repo relative). Slashes should be canonical for the OS.</param>
        /// <returns>
        /// True if this <see cref="FilterPath"/> is an excluding filter that matches
        /// <paramref name="repoRelativePath"/>, otherwise false.
        /// </returns>
        public bool Excludes(string repoRelativePath)
        {
            if (!this.IsExclude) return false;

            return this.RepoRelativePath.Equals(repoRelativePath, this.stringComparison) ||
                   repoRelativePath.StartsWith(this.RepoRelativePath + Path.DirectorySeparatorChar,
                       this.stringComparison);
        }
    }
}