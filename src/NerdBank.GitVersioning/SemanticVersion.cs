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
        /// The regex pattern that a prerelease must match.
        /// </summary>
        private static readonly Regex PrereleasePattern = new Regex(@"^-((?:[0-9A-Za-z-]+)(?:\.[0-9A-Za-z-]+)*)$");

        /// <summary>
        /// The regex pattern that build metadata must match.
        /// </summary>
        private static readonly Regex BuildMetadataPattern = new Regex(@"^\+((?:[0-9A-Za-z-]+)(?:\.[0-9A-Za-z-]+)*)$");

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
