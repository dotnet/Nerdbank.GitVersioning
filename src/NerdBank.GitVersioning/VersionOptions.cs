namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;

    /// <summary>
    /// Describes the various versions and options required for the build.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class VersionOptions : IEquatable<VersionOptions>
    {
        /// <summary>
        /// Gets or sets the default version to use.
        /// </summary>
        public SemanticVersion Version { get; set; }

        /// <summary>
        /// Gets or sets the version to use particularly for the <see cref="AssemblyVersionAttribute"/>
        /// instead of the default <see cref="Version"/>.
        /// </summary>
        /// <value>An instance of <see cref="System.Version"/> or <c>null</c> to simply use the default <see cref="Version"/>.</value>
        public Version AssemblyVersion { get; set; }

        /// <summary>
        /// Gets the debugger display for this instance.
        /// </summary>
        private string DebuggerDisplay => this.Version?.ToString();

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOptions"/> class
        /// with <see cref="Version"/> initialized with the specified parameters.
        /// </summary>
        /// <param name="version">The version number.</param>
        /// <param name="unstableTag">The prerelease tag, if any.</param>
        /// <returns>The new instance of <see cref="VersionOptions"/>.</returns>
        public static VersionOptions FromVersion(Version version, string unstableTag = null)
        {
            return new VersionOptions
            {
                Version = new SemanticVersion(version, unstableTag),
            };
        }

        /// <summary>
        /// Checks equality against another object.
        /// </summary>
        /// <param name="obj">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as VersionOptions);
        }

        /// <summary>
        /// Gets a hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return this.Version?.GetHashCode() ?? 0;
        }

        /// <summary>
        /// Checks equality against another instance of this class.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public bool Equals(VersionOptions other)
        {
            if (other == null)
            {
                return false;
            }

            return EqualityComparer<SemanticVersion>.Default.Equals(this.Version, other.Version);
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="Version"/> is
        /// set and the only property on this class that is set.
        /// </summary>
        internal bool IsDefaultVersionTheOnlyPropertySet
        {
            get
            {
                return this.Version != null
                    && this.AssemblyVersion == null;
            }
        }
    }
}
