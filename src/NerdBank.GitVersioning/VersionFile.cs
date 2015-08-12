namespace Nerdbank.GitVersioning
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
        /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <returns>The version information read from the file.</returns>
        public static SemanticVersion GetVersion(LibGit2Sharp.Commit commit, string repoRelativeProjectDirectory = null)
        {
            if (commit == null)
            {
                return null;
            }

            using (var content = GetVersionFileReader(commit, repoRelativeProjectDirectory))
            {
                return content != null
                    ? ReadVersionFile(content)
                    : null;
            }
        }

        /// <summary>
        /// Reads the version.txt file and returns the <see cref="Version"/> and prerelease tag from it.
        /// </summary>
        /// <param name="projectDirectory">The path to the directory which may (or its ancestors may) define the version.txt file.</param>
        /// <returns>The version information read from the file, or <c>null</c> if the file wasn't found.</returns>
        public static SemanticVersion GetVersion(string projectDirectory)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));

            string searchDirectory = projectDirectory;
            while (searchDirectory != null)
            {
                string versionTxtPath = Path.Combine(searchDirectory, FileName);
                if (File.Exists(versionTxtPath))
                {
                    using (var sr = new StreamReader(versionTxtPath))
                    {
                        return ReadVersionFile(sr);
                    }
                }

                searchDirectory = Path.GetDirectoryName(searchDirectory);
            }

            return null;
        }

        /// <summary>
        /// Checks whether the version.txt file is defined in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to search.</param>
        /// <param name="projectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <returns><c>true</c> if the version.txt file is found; otherwise <c>false</c>.</returns>
        public static bool IsVersionDefined(LibGit2Sharp.Commit commit, string projectDirectory = null)
        {
            return GetVersion(commit, projectDirectory) != null;
        }

        /// <summary>
        /// Checks whether the version.txt file is defined in the specified project directory
        /// or one of its ancestors.
        /// </summary>
        /// <param name="projectDirectory">The directory to start searching within.</param>
        /// <returns><c>true</c> if the version.txt file is found; otherwise <c>false</c>.</returns>
        public static bool IsVersionDefined(string projectDirectory)
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));

            return GetVersion(projectDirectory) != null;
        }

        /// <summary>
        /// Writes the version.txt file to the root of a repo with the specified version information.
        /// </summary>
        /// <param name="projectDirectory">
        /// The path to the directory in which to write the version.txt file.
        /// The file's impact will be all descendent projects and directories from this specified directory,
        /// except where any of those directories have their own version.txt file.
        /// </param>
        /// <param name="version">The version information to write to the file.</param>
        /// <param name="prerelease">The prerelease tag, starting with a hyphen per semver rules. May be the empty string or null.</param>
        /// <returns>The path to the file written.</returns>
        public static string SetVersion(string projectDirectory, Version version, string prerelease = "")
        {
            Requires.NotNullOrEmpty(projectDirectory, nameof(projectDirectory));
            Requires.NotNull(version, nameof(version));

            string versionTxtPath = Path.Combine(projectDirectory, FileName);
            File.WriteAllLines(
                versionTxtPath,
                new[] { version.ToString(), prerelease });
            return Path.Combine(projectDirectory, FileName);
        }

        /// <summary>
        /// Reads the version.txt file that is in the specified commit.
        /// </summary>
        /// <param name="commit">The commit to read the version file from.</param>
        /// <param name="repoRelativeProjectDirectory">The directory to consider when searching for the version.txt file.</param>
        /// <returns>A text reader with the content of the version.txt file.</returns>
        private static TextReader GetVersionFileReader(LibGit2Sharp.Commit commit, string repoRelativeProjectDirectory)
        {
            string searchDirectory = repoRelativeProjectDirectory ?? string.Empty;
            while (searchDirectory != null)
            {
                string candidatePath = Path.Combine(searchDirectory, FileName);
                var versionTxtBlob = commit.Tree[candidatePath]?.Target as LibGit2Sharp.Blob;
                if (versionTxtBlob != null)
                {
                    return new StreamReader(versionTxtBlob.GetContentStream());
                }

                searchDirectory = searchDirectory.Length > 0 ? Path.GetDirectoryName(searchDirectory) : null;
            }

            return null;
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
