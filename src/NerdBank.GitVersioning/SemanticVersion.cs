namespace NerdBank.GitVersioning
{
    using System;
    using Validation;

    /// <summary>
    /// Describes a version with an optional unstable tag.
    /// </summary>
    public class SemanticVersion
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
        public Version Version { get; private set; }

        /// <summary>
        /// Gets an unstable tag (with the leading hyphen), if applicable.
        /// </summary>
        /// <value>A string with a leading hyphen or the empty string.</value>
        public string UnstableTag { get; private set; }
    }
}
