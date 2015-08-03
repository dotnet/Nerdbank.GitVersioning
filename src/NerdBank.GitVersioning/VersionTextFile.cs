using Validation;
namespace NerdBank.GitVersioning
{
    using System;
    using System.IO;
    using Validation;

    /// <summary>
    /// Extension methods for interacting with the version.txt file.
    /// </summary>
    public static class VersionTextFile
    {
        /// <summary>
        /// The filename of the version.txt file.
        /// </summary>
        public const string FileName = "version.txt";

        /// <summary>
        /// Gets the version specified in the first line of the version.txt file
        /// as recorded in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to look up the version for.</param>
        /// <returns>The version, if a version.txt file was found; otherwise <c>null</c>.</returns>
        public static Version GetVersionFromTxtFile(this LibGit2Sharp.Commit commit)
        {
            Requires.NotNull(commit, nameof(commit));

            var versionTxtBlob = commit.Tree[FileName]?.Target as LibGit2Sharp.Blob;
            if (versionTxtBlob != null)
            {
                using (var versionTxtStream = new StreamReader(versionTxtBlob.GetContentStream()))
                {
                    return ReadVersionFromFile(versionTxtStream);
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <param name="typedVersion">Receives the version from the first line of the file.</param>
        /// <param name="prereleaseVersion">The prerelease tag from the second line of the file.</param>
        public static void GetVersionFromTxtFile(this LibGit2Sharp.Commit commit, out Version typedVersion, out string prereleaseVersion)
        {
            Requires.NotNull(commit, nameof(commit));

            using (var content = commit.ReadVersionTxt())
            {
                if (content != null)
                {
                    ReadVersionFromFile(content, out typedVersion, out prereleaseVersion);
                }
                else
                {
                    typedVersion = null;
                    prereleaseVersion = null;
                }
            }
        }

        /// <summary>
        /// Checks whether the version.txt file is defined in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to search.</param>
        /// <returns><c>true</c> if the version.txt file is found; otherwise <c>false</c>.</returns>
        public static bool IsVersionTxtPresent(this LibGit2Sharp.Commit commit)
        {
            return commit?.Tree[FileName] != null;
        }

        /// <summary>
        /// Writes the version.txt file to the root of a repo with the specified version information.
        /// </summary>
        /// <param name="repoRoot">The path to the root of the repo.</param>
        /// <param name="version">The version information to write to the file.</param>
        /// <param name="prerelease">The prerelease tag, starting with a hyphen per semver rules. May be the empty string or null.</param>
        public static void WriteVersionFile(string repoRoot, Version version, string prerelease = "")
        {
            Requires.NotNullOrEmpty(repoRoot, nameof(repoRoot));
            Requires.NotNull(version, nameof(version));

            File.WriteAllLines(
                Path.Combine(repoRoot, FileName),
                new[] { version.ToString(), prerelease });
        }

        /// <summary>
        /// Reads the version.txt file that is in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <returns>A text reader with the content of the version.txt file.</returns>
        private static TextReader ReadVersionTxt(this LibGit2Sharp.Commit commit)
        {
            var versionTxtBlob = commit.Tree[FileName]?.Target as LibGit2Sharp.Blob;
            return versionTxtBlob != null ? new StreamReader(versionTxtBlob.GetContentStream()) : null;
        }

        /// <summary>
        /// Reads the first line of the version.txt file and returns the <see cref="Version"/> from it.
        /// </summary>
        /// <param name="versionTextContent">The content of the version.txt file to read.</param>
        /// <returns>The deserialized <see cref="Version"/> instance.</returns>
        private static Version ReadVersionFromFile(TextReader versionTextContent)
        {
            Version result;
            string prerelease;
            ReadVersionFromFile(versionTextContent, out result, out prerelease);
            return result;
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="versionTextContent">The content of the version.txt file to read.</param>
        /// <param name="typedVersion">Receives the version from the first line of the file.</param>
        /// <param name="prereleaseVersion">The prerelease tag from the second line of the file.</param>
        private static void ReadVersionFromFile(TextReader versionTextContent, out Version typedVersion, out string prereleaseVersion)
        {
            string versionLine = versionTextContent.ReadLine();
            prereleaseVersion = versionTextContent.ReadLine();
            if (!string.IsNullOrEmpty(prereleaseVersion))
            {
                if (!prereleaseVersion.StartsWith("-"))
                {
                    // SemVer requires that prerelease suffixes begin with a hyphen, so add one if it's missing.
                    prereleaseVersion = "-" + prereleaseVersion;
                }

                VerifyValidPrereleaseVersion(prereleaseVersion);
            }

            typedVersion = new Version(versionLine);
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
