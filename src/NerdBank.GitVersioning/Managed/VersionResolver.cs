#nullable enable

using System;
using System.IO;
using System.Linq;
using Nerdbank.GitVersioning;

namespace NerdBank.GitVersioning.Managed
{
    internal abstract class VersionResolver
    {
        protected readonly GitRepository gitRepository;
        protected readonly string versionPath;

        public VersionResolver(GitRepository gitRepository, string versionPath)
        {
            this.gitRepository = gitRepository ?? throw new ArgumentNullException(nameof(gitRepository));
            this.versionPath = versionPath;

            if (IsPathFullyQualified(versionPath))
            {
                this.versionPath = GetRelativePath(this.gitRepository.WorkingDirectory, versionPath);
            }
        }

        public abstract int GetGitHeight(Func<GitCommit, bool> continueStepping);

        /// <summary>
        /// The placeholder that may appear in the <see cref="Version"/> property's <see cref="SemanticVersion.Prerelease"/>
        /// to specify where the version height should appear in a computed semantic version.
        /// </summary>
        /// <remarks>
        /// When this macro does not appear in the string, the version height is set as the first unspecified integer of the 4-integer version.
        /// If all 4 integers in a version are specified, and the macro does not appear, the version height isn't inserted anywhere.
        /// </remarks>
        public const string VersionHeightPlaceholder = "{height}";

        public const char SuffixDelimiter = '-';
        public const char DigitDelimiter = '.';

        public string GetVersion(string version, int gitHeight)
        {
            if (version.Contains(VersionHeightPlaceholder))
            {
                return version.Replace(VersionHeightPlaceholder, gitHeight.ToString());
            }
            else if (version.Contains(SuffixDelimiter))
            {
                var suffixOffset = version.IndexOf(SuffixDelimiter);

                if (version.Take(suffixOffset).Count(c => c == DigitDelimiter) >= 2)
                {
                    return version;
                }
                else
                {
                    return $"{version.Substring(0, suffixOffset)}.{gitHeight}{version.Substring(suffixOffset)}";
                }
            }
            else
            {
                return $"{version}.{gitHeight}";
            }
        }

        protected static byte[][] GetPathComponents(string? path)
        {
            if (path == null)
            {
                return new byte[][] { };
            }

            return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Select(p => GitRepository.Encoding.GetBytes(p))
                .ToArray();
        }

        private static bool IsPathFullyQualified(string path)
        {
#if NETSTANDARD
            return Path.GetFullPath(path) == path;
#else
            return Path.IsPathFullyQualified(path);
#endif
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
#if NETSTANDARD
            if (!IsPathFullyQualified(path))
            {
                return path;
            }

            Uri relativeToUri = new Uri($"file:///{relativeTo}");
            Uri pathUri = new Uri($"file:///{path}");

            Uri relativeUri = relativeToUri.MakeRelativeUri(pathUri);
            return relativeUri.ToString();
#else
            return Path.GetRelativePath(relativeTo, path);
#endif
        }
    }
}
