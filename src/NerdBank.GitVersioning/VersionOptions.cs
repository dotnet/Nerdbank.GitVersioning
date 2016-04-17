namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    /// Describes the various versions and options required for the build.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class VersionOptions : IEquatable<VersionOptions>
    {
        /// <summary>
        /// The JSON serializer settings to use.
        /// </summary>
        public static JsonSerializerSettings JsonSettings => new JsonSerializerSettings
        {
            Converters = new JsonConverter[] {
                new VersionConverter(),
                new SemanticVersionJsonConverter(),
                new AssemblyVersionOptionsConverter(),
                new StringEnumConverter() { CamelCaseText = true },
            },
            ContractResolver = new VersionOptionsContractResolver(),
            Formatting = Formatting.Indented,
        };

        /// <summary>
        /// Gets or sets the default version to use.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public SemanticVersion Version { get; set; }

        /// <summary>
        /// Gets or sets the version to use particularly for the <see cref="AssemblyVersionAttribute"/>
        /// instead of the default <see cref="Version"/>.
        /// </summary>
        /// <value>An instance of <see cref="System.Version"/> or <c>null</c> to simply use the default <see cref="Version"/>.</value>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public AssemblyVersionOptions AssemblyVersion { get; set; }

        /// <summary>
        /// Gets or sets a number to add to the git height when calculating the <see cref="Version.Build"/> number.
        /// </summary>
        /// <value>Any integer (0, positive, or negative).</value>
        /// <remarks>
        /// An error will result if this value is negative with such a magnitude as to exceed the git height,
        /// resulting in a negative build number.
        /// </remarks>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int BuildNumberOffset { get; set; }

        /// <summary>
        /// Gets or sets an array of regular expressions that describes branch or tag names that should
        /// be built with PublicRelease=true as the default value on build servers.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string[] PublicReleaseRefSpec { get; set; }

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

            return EqualityComparer<SemanticVersion>.Default.Equals(this.Version, other.Version)
                && EqualityComparer<AssemblyVersionOptions>.Default.Equals(this.AssemblyVersion, other.AssemblyVersion)
                && this.BuildNumberOffset == other.BuildNumberOffset;
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

        internal bool ShouldSerializeAssemblyVersion() => !(this.AssemblyVersion?.IsDefault ?? true);

        /// <summary>
        /// Describes the details of how the AssemblyVersion value will be calculated.
        /// </summary>
        public class AssemblyVersionOptions : IEquatable<AssemblyVersionOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            private static readonly AssemblyVersionOptions DefaultInstance = new AssemblyVersionOptions();

            /// <summary>
            /// Initializes a new instance of the <see cref="AssemblyVersionOptions"/> class.
            /// </summary>
            public AssemblyVersionOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="AssemblyVersionOptions"/> class.
            /// </summary>
            /// <param name="version">The assembly version (with major.minor components).</param>
            /// <param name="precision">The additional version precision to add toward matching the AssemblyFileVersion.</param>
            public AssemblyVersionOptions(Version version, VersionPrecision precision = default(VersionPrecision))
            {
                this.Version = version;
                this.Precision = precision;
            }

            /// <summary>
            /// Gets or sets the major.minor components of the assembly version.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Version Version { get; set; }

            /// <summary>
            /// Gets or sets the additional version precision to add toward matching the AssemblyFileVersion.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public VersionPrecision Precision { get; set; }

            /// <inheritdoc />
            public override bool Equals(object obj) => this.Equals(obj as AssemblyVersionOptions);

            /// <inheritdoc />
            public bool Equals(AssemblyVersionOptions other)
            {
                return other != null
                    && EqualityComparer<Version>.Default.Equals(this.Version, other.Version)
                    && this.Precision == other.Precision;
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return (this.Version?.GetHashCode() ?? 0) + (int)this.Precision;
            }

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);
        }

        /// <summary>
        /// The last component to control in a 4 integer version.
        /// </summary>
        public enum VersionPrecision
        {
            /// <summary>
            /// The second integer is the last number set. The rest will be zeros.
            /// </summary>
            Minor,

            /// <summary>
            /// The third integer is the last number set. The fourth will be zero.
            /// </summary>
            Build,

            /// <summary>
            /// All four integers will be set.
            /// </summary>
            Revision,
        }
    }
}
