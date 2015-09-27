namespace Nerdbank.GitVersioning
{
    using System;
    using System.Diagnostics;
    using Validation;

    /// <summary>
    /// Describes a version with an optional unstable tag.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class SemanticVersion : IEquatable<SemanticVersion>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticVersion"/> class.
        /// </summary>
        /// <param name="version"></param>
        /// <param name="unstableTag"></param>
        public SemanticVersion(Version version, string unstableTag = null)
        {
            Requires.NotNull(version, nameof(version));
            VerifyValidPrereleaseVersion(unstableTag, nameof(unstableTag));

            this.Version = version;
            this.UnstableTag = unstableTag ?? string.Empty;
        }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public Version Version { get; private set; }

        /// <summary>
        /// Gets an unstable tag (with the leading hyphen), if applicable.
        /// </summary>
        /// <value>A string with a leading hyphen or the empty string.</value>
        public string UnstableTag { get; private set; }

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
            return this.Version.GetHashCode() + this.UnstableTag.GetHashCode();
        }

        /// <summary>
        /// Prints this instance as a string.
        /// </summary>
        /// <returns>A string representation of this object.</returns>
        public override string ToString()
        {
            return this.Version + this.UnstableTag;
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
                && this.UnstableTag == other.UnstableTag;
        }

        /// <summary>
        /// Verifies that the prerelease tag follows semver rules.
        /// </summary>
        /// <param name="prerelease">The prerelease tag to verify.</param>
        /// <param name="parameterName">The name of the parameter to report as not conforming.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if the <paramref name="prerelease"/> does not follow semver rules.
        /// </exception>
        private static void VerifyValidPrereleaseVersion(string prerelease, string parameterName)
        {
            if (string.IsNullOrEmpty(prerelease))
            {
                return;
            }

            if (prerelease[0] != '-')
            {
                throw new ArgumentOutOfRangeException(parameterName, "The prerelease string must begin with a hyphen.");
            }

            for (int i = 1; i < prerelease.Length; i++)
            {
                if (!char.IsLetterOrDigit(prerelease[i]))
                {
                    throw new ArgumentOutOfRangeException(parameterName, "The prerelease string must be alphanumeric.");
                }
            }
        }
    }
}
