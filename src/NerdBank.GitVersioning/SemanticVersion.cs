namespace Nerdbank.GitVersioning
{
    using System;
    using Validation;

    /// <summary>
    /// Describes a version with an optional unstable tag.
    /// </summary>
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

            this.Version = version;
            this.UnstableTag = unstableTag ?? string.Empty;
        }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Gets an unstable tag (with the leading hyphen), if applicable.
        /// </summary>
        /// <value>A string with a leading hyphen or the empty string.</value>
        public string UnstableTag { get; }

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
    }
}
