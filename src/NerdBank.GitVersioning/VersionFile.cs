using Validation;
namespace NerdBank.GitVersioning
{
    using System;
    using System.IO;
    using Validation;

    /// <summary>
    /// Extension methods for interacting with the version.txt file.
    /// </summary>
    public static class VersionFile
    {
        /// <summary>
        /// The filename of the version.txt file.
        /// </summary>
        public const string FileName = "version.txt";

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <returns>The version information read from the file.</returns>
        public static SemanticVersion GetVersionFromFile(LibGit2Sharp.Commit commit)
        {
            if (commit == null)
            {
                return null;
            }

            using (var content = GetVersionFileReader(commit))
            {
                return content != null
                    ? ReadVersionFile(content)
                    : null;
            }
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="repoRoot">The path to the directory in which to find the version.txt file.</param>
        /// <returns>The version information read from the file, or <c>null</c> if the file wasn't found.</returns>
        public static SemanticVersion GetVersionFromFile(string repoRoot)
        {
            Requires.NotNullOrEmpty(repoRoot, nameof(repoRoot));

            string versionTxtPath = Path.Combine(repoRoot, FileName);
            if (File.Exists(versionTxtPath))
            {
                using (var sr = new StreamReader(versionTxtPath))
                {
                    return ReadVersionFile(sr);
                }
            }

            return null;
        }

        /// <summary>
        /// Checks whether the version.txt file is defined in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to search.</param>
        /// <returns><c>true</c> if the version.txt file is found; otherwise <c>false</c>.</returns>
        public static bool IsVersionFilePresent(LibGit2Sharp.Commit commit)
        {
            return commit?.Tree[FileName] != null;
        }

        /// <summary>
        /// Writes the version.txt file to the root of a repo with the specified version information.
        /// </summary>
        /// <param name="repoRoot">The path to the root of the repo.</param>
        /// <param name="version">The version information to write to the file.</param>
        /// <param name="prerelease">The prerelease tag, starting with a hyphen per semver rules. May be the empty string or null.</param>
        /// <returns>The path to the file written.</returns>
        public static string WriteVersionFile(string repoRoot, Version version, string prerelease = "")
        {
            Requires.NotNullOrEmpty(repoRoot, nameof(repoRoot));
            Requires.NotNull(version, nameof(version));

            string versionTxtPath = Path.Combine(repoRoot, FileName);
            File.WriteAllLines(
                versionTxtPath,
                new[] { version.ToString(), prerelease });
            return Path.Combine(repoRoot, FileName);
        }

        /// <summary>
        /// Reads the version.txt file that is in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <returns>A text reader with the content of the version.txt file.</returns>
        private static TextReader GetVersionFileReader(LibGit2Sharp.Commit commit)
        {
            var versionTxtBlob = commit.Tree[FileName]?.Target as LibGit2Sharp.Blob;
            return versionTxtBlob != null ? new StreamReader(versionTxtBlob.GetContentStream()) : null;
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="versionTextContent">The content of the version.txt file to read.</param>
        /// <returns>The version information read from the file.</returns>
        private static SemanticVersion ReadVersionFile(TextReader versionTextContent)
        {
            string versionLine = versionTextContent.ReadLine();
            string prereleaseVersion = versionTextContent.ReadLine();
            if (!string.IsNullOrEmpty(prereleaseVersion))
            {
                if (!prereleaseVersion.StartsWith("-"))
                {
                    // SemVer requires that prerelease suffixes begin with a hyphen, so add one if it's missing.
                    prereleaseVersion = "-" + prereleaseVersion;
                }

                VerifyValidPrereleaseVersion(prereleaseVersion);
            }

            return new SemanticVersion(new Version(versionLine), prereleaseVersion);
        }

        /// <summary>
        /// Verifies that the prerelease tag follows semver rules.
        /// </summary>
        /// <param name="prerelease">The prerelease tag to verify.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if the <paramref name="prerelease"/> does not follow semver rules.
        /// </exception>
        private static void VerifyValidPrereleaseVersion(string prerelease)
        {
            Requires.NotNullOrEmpty(prerelease, nameof(prerelease));

            if (prerelease[0] != '-')
            {
                throw new ArgumentOutOfRangeException("The prerelease string must begin with a hyphen.");
            }

            for (int i = 1; i < prerelease.Length; i++)
            {
                if (!char.IsLetterOrDigit(prerelease[i]))
                {
                    throw new ArgumentOutOfRangeException("The prerelease string must be alphanumeric.");
                }
            }
        }
    }
}
