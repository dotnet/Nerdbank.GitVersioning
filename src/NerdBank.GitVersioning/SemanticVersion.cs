namespace Nerdbank.GitVersioning
{
    using System;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using Validation;

    /// <summary>
    /// Describes a version with an optional unstable tag.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class SemanticVersion : IEquatable<SemanticVersion>
    {
        /// <summary>
        /// The regular expression with capture groups for semantic versioning.
        /// It considers PATCH to be optional.
        /// </summary>
        /// <remarks>
        /// Parts of this regex inspired by https://github.com/sindresorhus/semver-regex/blob/master/index.js
        /// </remarks>
        private static readonly Regex FullSemVerPattern = new Regex(@"^v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)(?:\.(?<patch>0|[1-9][0-9]*))?(?<prerelease>-[\da-z\-]+(?:\.[\da-z\-]+)*)?(?<buildMetadata>\+[\da-z\-]+(?:\.[\da-z\-]+)*)?$", RegexOptions.IgnoreCase);

        /// <summary>
        /// The regex pattern that a prerelease must match.
        /// </summary>
        private static readonly Regex PrereleasePattern = new Regex(@"^-(?:[0-9A-Za-z-]+)(?:\.[0-9A-Za-z-]+)*$");

        /// <summary>
        /// The regex pattern that build metadata must match.
        /// </summary>
        private static readonly Regex BuildMetadataPattern = new Regex(@"^\+(?:[0-9A-Za-z-]+)(?:\.[0-9A-Za-z-]+)*$");

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticVersion"/> class.
        /// </summary>
        /// <param name="version">The numeric version.</param>
        /// <param name="prerelease">The prerelease, with leading - character.</param>
        /// <param name="buildMetadata">The build metadata, with leading + character.</param>
        public SemanticVersion(Version version, string prerelease = null, string buildMetadata = null)
        {
            Requires.NotNull(version, nameof(version));
            VerifyPatternMatch(prerelease, PrereleasePattern, nameof(prerelease));
            VerifyPatternMatch(buildMetadata, BuildMetadataPattern, nameof(buildMetadata));

            this.Version = version;
            this.Prerelease = prerelease ?? string.Empty;
            this.BuildMetadata = buildMetadata ?? string.Empty;
        }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public Version Version { get; private set; }

        /// <summary>
        /// Gets an unstable tag (with the leading hyphen), if applicable.
        /// </summary>
        /// <value>A string with a leading hyphen or the empty string.</value>
        public string Prerelease { get; private set; }

        /// <summary>
        /// Gets the build metadata (with the leading plus), if applicable.
        /// </summary>
        /// <value>A string with a leading plus or the empty string.</value>
        public string BuildMetadata { get; private set; }

        /// <summary>
        /// Gets the debugger display for this instance.
        /// </summary>
        private string DebuggerDisplay => this.ToString();

        /// <summary>
        /// Parses a semantic version from the given string.
        /// </summary>
        /// <param name="semanticVersion">The value which must wholly constitute a semantic version to succeed.</param>
        /// <param name="version">Receives the semantic version, if found.</param>
        /// <returns><c>true</c> if a semantic version is found; <c>false</c> otherwise.</returns>
        public static bool TryParse(string semanticVersion, out SemanticVersion version)
        {
            Requires.NotNullOrEmpty(semanticVersion, nameof(semanticVersion));

            Match m = FullSemVerPattern.Match(semanticVersion);
            if (m.Success)
            {
                var major = int.Parse(m.Groups["major"].Value);
                var minor = int.Parse(m.Groups["minor"].Value);
                var patch = m.Groups["patch"].Value;
                var systemVersion = patch.Length > 0 ? new Version(major, minor, int.Parse(patch)) : new Version(major, minor);
                var prerelease = m.Groups["prerelease"].Value;
                var buildMetadata = m.Groups["buildmetadata"].Value;
                version = new SemanticVersion(systemVersion, prerelease, buildMetadata);
                return true;
            }

            version = null;
            return false;
        }

        /// <summary>
        /// Checks equality against another object.
        /// </summary>
        /// <param name="obj">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as SemanticVersion);
        }

        /// <summary>
        /// Gets a hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return this.Version.GetHashCode() + this.Prerelease.GetHashCode();
        }

        /// <summary>
        /// Prints this instance as a string.
        /// </summary>
        /// <returns>A string representation of this object.</returns>
        public override string ToString()
        {
            return this.Version + this.Prerelease + this.BuildMetadata;
        }

        /// <summary>
        /// Checks equality against another instance of this class.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public bool Equals(SemanticVersion other)
        {
            if (other == null)
            {
                return false;
            }

            return this.Version == other.Version
                && this.Prerelease == other.Prerelease
                && this.BuildMetadata == other.BuildMetadata;
        }

        /// <summary>
        /// Verifies that the prerelease tag follows semver rules.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="pattern">The regex that the string must conform to.</param>
        /// <param name="parameterName">The name of the parameter supplying the <paramref name="input"/>.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if the <paramref name="input"/> does not match the required <paramref name="pattern"/>.
        /// </exception>
        private static void VerifyPatternMatch(string input, Regex pattern, string parameterName)
        {
            Requires.NotNull(pattern, nameof(pattern));

            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            Requires.Argument(pattern.IsMatch(input), parameterName, $"The prerelease must match the pattern \"{pattern}\".");
        }
    }
}
