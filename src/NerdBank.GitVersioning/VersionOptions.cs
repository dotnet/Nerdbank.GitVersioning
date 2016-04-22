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
        /// Gets or sets the options around cloud build.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public CloudBuildOptions CloudBuild { get; set; }

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
                && EqualityComparer<CloudBuildOptions>.Default.Equals(this.CloudBuild ?? CloudBuildOptions.DefaultInstance, other.CloudBuild ?? CloudBuildOptions.DefaultInstance)
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

        internal bool ShouldSerializeCloudBuild() => !(this.CloudBuild?.IsDefault ?? true);

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
        /// Options that are applicable specifically to cloud builds (e.g. VSTS, AppVeyor, TeamCity)
        /// </summary>
        public class CloudBuildOptions : IEquatable<CloudBuildOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly CloudBuildOptions DefaultInstance = new CloudBuildOptions();

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildOptions"/> class.
            /// </summary>
            public CloudBuildOptions()
            {
            }

            /// <summary>
            /// Gets or sets a value indicating whether to elevate certain calculated version build properties to cloud build variables.
            /// </summary>
            public bool SetVersionVariables { get; set; } = true;

            /// <summary>
            /// Override the build number preset by the cloud build with one enriched with version information.
            /// </summary>
            public CloudBuildNumberOptions BuildNumber { get; set; }

            internal bool ShouldSerializeSetVersionVariables() => this.SetVersionVariables != DefaultInstance.SetVersionVariables;

            /// <inheritdoc />
            public override bool Equals(object obj) => this.Equals(obj as CloudBuildOptions);

            /// <inheritdoc />
            public bool Equals(CloudBuildOptions other)
            {
                return other != null
                    && this.SetVersionVariables == other.SetVersionVariables
                    && EqualityComparer<CloudBuildNumberOptions>.Default.Equals(this.BuildNumber ?? CloudBuildNumberOptions.DefaultInstance, other.BuildNumber ?? CloudBuildNumberOptions.DefaultInstance);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return this.SetVersionVariables ? 1 : 0
                    + this.BuildNumber?.GetHashCode() ?? 0;
            }

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);
        }

        /// <summary>
        /// Override the build number preset by the cloud build with one enriched with version information.
        /// </summary>
        public class CloudBuildNumberOptions : IEquatable<CloudBuildNumberOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly CloudBuildNumberOptions DefaultInstance = new CloudBuildNumberOptions();

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildNumberOptions"/> class.
            /// </summary>
            public CloudBuildNumberOptions()
            {
            }

            /// <summary>
            /// Gets or sets a value indicating whether to override the build number preset by the cloud build.
            /// </summary>
            public bool Enabled { get; set; }

            /// <summary>
            /// Getse or sets when and where to include information about the git commit being built.
            /// </summary>
            public CloudBuildNumberCommitIdOptions IncludeCommitId { get; set; }

            internal bool ShouldSerializeIncludeCommitId() => !(this.IncludeCommitId?.IsDefault ?? true);

            /// <inheritdoc />
            public override bool Equals(object obj) => this.Equals(obj as CloudBuildNumberOptions);

            /// <inheritdoc />
            public bool Equals(CloudBuildNumberOptions other)
            {
                return other != null
                    && this.Enabled == other.Enabled
                    && EqualityComparer<CloudBuildNumberCommitIdOptions>.Default.Equals(this.IncludeCommitId ?? CloudBuildNumberCommitIdOptions.DefaultInstance, other.IncludeCommitId ?? CloudBuildNumberCommitIdOptions.DefaultInstance);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return this.Enabled ? 1 : 0
                    + this.IncludeCommitId?.GetHashCode() ?? 0;
            }

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);
        }

        /// <summary>
        /// Describes when and where to include information about the git commit being built.
        /// </summary>
        public class CloudBuildNumberCommitIdOptions : IEquatable<CloudBuildNumberCommitIdOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly CloudBuildNumberCommitIdOptions DefaultInstance = new CloudBuildNumberCommitIdOptions();

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildNumberCommitIdOptions"/> class.
            /// </summary>
            public CloudBuildNumberCommitIdOptions()
            {
            }

            /// <summary>
            /// Gets or sets the conditions when the commit ID is included in the build number.
            /// </summary>
            public CloudBuildNumberCommitWhen When { get; set; } = CloudBuildNumberCommitWhen.NonPublicReleaseOnly;

            /// <summary>
            /// Gets or sets the position to include the commit ID information.
            /// </summary>
            public CloudBuildNumberCommitWhere Where { get; set; } = CloudBuildNumberCommitWhere.BuildMetadata;

            internal bool ShouldSerializeWhen() => this.When != DefaultInstance.When;

            internal bool ShouldSerializeWhere() => this.Where != DefaultInstance.Where;

            /// <inheritdoc />
            public override bool Equals(object obj) => this.Equals(obj as CloudBuildNumberCommitIdOptions);

            /// <inheritdoc />
            public bool Equals(CloudBuildNumberCommitIdOptions other)
            {
                return other != null
                    && this.When == other.When
                    && this.Where == other.Where;
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return (int)this.Where + (int)this.When * 0x10;
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

        /// <summary>
        /// The conditions a commit ID is included in a cloud build number.
        /// </summary>
        public enum CloudBuildNumberCommitWhen
        {
            /// <summary>
            /// Always include the commit information in the cloud Build Number.
            /// </summary>
            Always,

            /// <summary>
            /// Only include the commit information when building a non-PublicRelease.
            /// </summary>
            NonPublicReleaseOnly,

            /// <summary>
            /// Never include the commit information.
            /// </summary>
            Never,
        }

        /// <summary>
        /// The position a commit ID can appear in a cloud build number.
        /// </summary>
        public enum CloudBuildNumberCommitWhere
        {
            /// <summary>
            /// The commit ID appears in build metadata (e.g. +ga1b2c3).
            /// </summary>
            BuildMetadata,

            /// <summary>
            /// The commit ID appears as the 4th integer in the version (e.g. 1.2.3.23523).
            /// </summary>
            FourthVersionComponent,
        }
    }
}
