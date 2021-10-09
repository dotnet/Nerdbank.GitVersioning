#nullable enable

namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json;
    using Validation;

    /// <summary>
    /// Assembles version information in a variety of formats.
    /// </summary>
    public class VersionOracle
    {
        private const bool UseLibGit2 = false;

        /// <summary>
        /// The 0.0 version.
        /// </summary>
        private protected static readonly Version Version0 = new Version(0, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class.
        /// </summary>
        /// <param name="context">The git context from which to calculate version data.</param>
        /// <param name="cloudBuild">An optional cloud build provider that may offer additional context. Typically set to <see cref="CloudBuild.Active"/>.</param>
        /// <param name="overrideVersionHeightOffset">An optional value to override the version height offset.</param>
        public VersionOracle(GitContext context, ICloudBuild? cloudBuild = null, int? overrideVersionHeightOffset = null)
        {
            Requires.NotNull(context, nameof(context));

            this.RepoRelativeBaseDirectory = context.RepoRelativeProjectDirectory;
            this.GitCommitId = context.GitCommitId ?? cloudBuild?.GitCommitId;
            this.GitCommitDate = context.GitCommitDate;

            VersionOptions? committedVersion = context.VersionFile.GetVersion();

            // Consider the working version only if the commit being inspected is HEAD.
            // Otherwise we're looking at historical data and should not consider the state of the working tree at all.
            VersionOptions? workingVersion = context.IsHead ? context.VersionFile.GetWorkingCopyVersion() : committedVersion;

            if (overrideVersionHeightOffset.HasValue)
            {
                if (committedVersion is object)
                {
                    committedVersion.VersionHeightOffset = overrideVersionHeightOffset.Value;
                }

                if (workingVersion is object)
                {
                    workingVersion.VersionHeightOffset = overrideVersionHeightOffset.Value;
                }
            }

            this.BuildingRef = cloudBuild?.BuildingTag ?? cloudBuild?.BuildingBranch ?? context.HeadCanonicalName;
            try
            {
                this.VersionHeight = context.CalculateVersionHeight(committedVersion, workingVersion);
            }
            catch (GitException ex) when (context.IsShallow && ex.ErrorCode == GitException.ErrorCodes.ObjectNotFound)
            {
                // Our managed git implementation throws this on shallow clones.
                throw ThrowShallowClone(ex);
            }
            catch (InvalidOperationException ex) when (context.IsShallow && (ex.InnerException is NullReferenceException || ex.InnerException is LibGit2Sharp.NotFoundException))
            {
                // Libgit2 throws this on shallow clones.
                throw ThrowShallowClone(ex);
            }

            static Exception ThrowShallowClone(Exception inner) => throw new GitException("Shallow clone lacks the objects required to calculate version height. Use full clones or clones with a history at least as deep as the last version height resetting change.", inner) { iSShallowClone = true, ErrorCode = GitException.ErrorCodes.ObjectNotFound };

            this.VersionOptions = committedVersion ?? workingVersion;
            this.Version = this.VersionOptions?.Version?.Version ?? Version0;

            // Override the typedVersion with the special build number and revision components, when available.
            if (context.IsRepository)
            {
                this.Version = context.GetIdAsVersion(committedVersion, workingVersion, this.VersionHeight);
            }

            // get the commit id abbreviation only if the commit id is set
            if (!string.IsNullOrEmpty(this.GitCommitId))
            {
                var gitCommitIdShortFixedLength = this.VersionOptions?.GitCommitIdShortFixedLength ?? VersionOptions.DefaultGitCommitIdShortFixedLength;
                var gitCommitIdShortAutoMinimum = this.VersionOptions?.GitCommitIdShortAutoMinimum ?? 0;

                // Get it from the git repository if there is a repository present and it is enabled.
                this.GitCommitIdShort = this.GitCommitId is object && gitCommitIdShortAutoMinimum > 0
                    ? context.GetShortUniqueCommitId(gitCommitIdShortAutoMinimum)
                    : this.GitCommitId!.Substring(0, gitCommitIdShortFixedLength);
            }

            if (!string.IsNullOrEmpty(this.BuildingRef) && this.VersionOptions?.PublicReleaseRefSpec?.Count > 0)
            {
                this.PublicRelease = this.VersionOptions.PublicReleaseRefSpec.Any(
                    expr => Regex.IsMatch(this.BuildingRef, expr));
            }
        }

        [JsonConstructor]
        private VersionOracle(VersionOptions? versionOptions, bool publicRelease, string? gitCommitId, string? gitCommitIdShort, DateTimeOffset? gitCommitDate, int versionHeight, string? buildingRef, Version version)
        {
            this.VersionOptions = versionOptions;
            this.PublicRelease = publicRelease;
            this.GitCommitId = gitCommitId;
            this.GitCommitIdShort = gitCommitIdShort;
            this.GitCommitDate = gitCommitDate;
            this.VersionHeight = versionHeight;
            this.BuildingRef = buildingRef;
            this.Version = version;
        }

        /// <summary>
        /// Gets the BuildNumber to set the cloud build to (if applicable).
        /// </summary>
        [IgnoreDataMember]
        public string CloudBuildNumber
        {
            get
            {
                var commitIdOptions = this.CloudBuildNumberOptions.IncludeCommitIdOrDefault;
                bool includeCommitInfo = commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.Always ||
                    (commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.NonPublicReleaseOnly && !this.PublicRelease);
                bool commitIdInRevision = includeCommitInfo && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.FourthVersionComponent;
                bool commitIdInBuildMetadata = includeCommitInfo && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.BuildMetadata;
                Version buildNumberVersion = commitIdInRevision ? this.Version : this.SimpleVersion;
                string buildNumberMetadata = FormatBuildMetadata(commitIdInBuildMetadata ? this.BuildMetadataWithCommitId : this.BuildMetadata);
                return buildNumberVersion + this.PrereleaseVersion + buildNumberMetadata;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the cloud build number should be set.
        /// </summary>
        [Ignore, IgnoreDataMember]
        public bool CloudBuildNumberEnabled => this.CloudBuildNumberOptions.EnabledOrDefault;

        /// <summary>
        /// Gets the build metadata identifiers, including the git commit ID as the first identifier if appropriate.
        /// </summary>
        [Ignore, IgnoreDataMember]
        public IEnumerable<string> BuildMetadataWithCommitId
        {
            get
            {
                if (!string.IsNullOrEmpty(this.GitCommitIdShort))
                {
                    yield return this.GitCommitIdShort!;
                }

                foreach (string identifier in this.BuildMetadata)
                {
                    yield return identifier;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether a version.json or version.txt file was found.
        /// </summary>
        [IgnoreDataMember]
        public bool VersionFileFound => this.VersionOptions is object;

        /// <summary>
        /// Gets the version options used to initialize this instance.
        /// </summary>
        [DataMember]
        public VersionOptions? VersionOptions { get; }

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyVersionAttribute"/>.
        /// </summary>
        [IgnoreDataMember]
        public Version AssemblyVersion => GetAssemblyVersion(this.Version, this.VersionOptions).EnsureNonNegativeComponents();

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyFileVersionAttribute"/>.
        /// </summary>
        [IgnoreDataMember]
        public Version AssemblyFileVersion => this.Version;

        /// <summary>
        /// Gets the version string to use for the <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
        /// </summary>
        [IgnoreDataMember]
        public string AssemblyInformationalVersion =>
            $"{this.Version.ToStringSafe(this.AssemblyInformationalVersionComponentCount)}{this.PrereleaseVersion}{FormatBuildMetadata(this.BuildMetadataWithCommitId)}";

        /// <summary>
        /// Gets or sets a value indicating whether the project is building
        /// in PublicRelease mode.
        /// </summary>
        [DataMember]
        public bool PublicRelease { get; set; }

        /// <summary>
        /// Gets the prerelease version information, including a leading hyphen.
        /// </summary>
        [IgnoreDataMember]
        public string PrereleaseVersion => this.ReplaceMacros(this.VersionOptions?.Version?.Prerelease ?? string.Empty);

        /// <summary>
        /// Gets the prerelease version information, omitting the leading hyphen, if any.
        /// </summary>
        [IgnoreDataMember]
        public string? PrereleaseVersionNoLeadingHyphen => this.PrereleaseVersion?.TrimStart('-');

        /// <summary>
        /// Gets the version information without a Revision component.
        /// </summary>
        [IgnoreDataMember]
        public Version SimpleVersion => this.Version.Build >= 0
                ? new Version(this.Version.Major, this.Version.Minor, this.Version.Build)
                : new Version(this.Version.Major, this.Version.Minor);

        /// <summary>
        /// Gets the build number (i.e. third integer, or PATCH) for this version.
        /// </summary>
        [IgnoreDataMember]
        public int BuildNumber => Math.Max(0, this.Version.Build);

        /// <summary>
        /// Gets the <see cref="Version.Revision"/> component of the <see cref="Version"/>.
        /// </summary>
        [IgnoreDataMember]
        public int VersionRevision => this.Version.Revision;

        /// <summary>
        /// Gets the major.minor version string.
        /// </summary>
        /// <value>
        /// The x.y string (no build number or revision number).
        /// </value>
        [IgnoreDataMember]
        public Version MajorMinorVersion => new Version(this.Version.Major, this.Version.Minor);

        /// <summary>
        /// Gets the <see cref="Version.Major"/> component of the <see cref="Version"/>.
        /// </summary>
        [IgnoreDataMember]
        public int VersionMajor => this.Version.Major;

        /// <summary>
        /// Gets the <see cref="Version.Minor"/> component of the <see cref="Version"/>.
        /// </summary>
        [IgnoreDataMember]
        public int VersionMinor => this.Version.Minor;

        /// <summary>
        /// Gets the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        [DataMember]
        public string? GitCommitId { get; }

        /// <summary>
        /// Gets the first several characters of the Git revision control commit id for HEAD (the current source code version).
        /// </summary>
        [DataMember]
        public string? GitCommitIdShort { get; }

        /// <summary>
        /// Gets the Git revision control commit date for HEAD (the current source code version).
        /// </summary>
        [DataMember]
        public DateTimeOffset? GitCommitDate { get; }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at HEAD.
        /// </summary>
        [DataMember]
        public int VersionHeight { get; protected set; }

        /// <summary>
        /// The offset to add to the <see cref="VersionHeight"/>
        /// when calculating the integer to use as the <see cref="BuildNumber"/>
        /// or elsewhere that the {height} macro is used.
        /// </summary>
        [IgnoreDataMember]
        public int VersionHeightOffset => this.VersionOptions?.VersionHeightOffsetOrDefault ?? 0;

        /// <summary>
        /// Gets the ref (branch or tag) being built.
        /// </summary>
        [DataMember]
        public string? BuildingRef { get; protected set; }

        /// <summary>
        /// Gets the version for this project, with up to 4 components.
        /// </summary>
        [DataMember]
        public Version Version { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether to set all cloud build variables prefaced with "NBGV_".
        /// </summary>
        [Ignore, IgnoreDataMember]
        public bool CloudBuildAllVarsEnabled => this.VersionOptions?.CloudBuildOrDefault.SetAllVariablesOrDefault
            ?? VersionOptions.CloudBuildOptions.DefaultInstance.SetAllVariablesOrDefault;

        /// <summary>
        /// Gets a dictionary of all cloud build variables that applies to this project,
        /// regardless of the current setting of <see cref="CloudBuildAllVarsEnabled"/>.
        /// </summary>
        [Ignore, IgnoreDataMember]
        public IDictionary<string, string> CloudBuildAllVars
        {
            get
            {
                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var properties = this.GetType().GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    if (property.GetCustomAttribute<IgnoreAttribute>() is null)
                    {
                        var value = property.GetValue(this);
                        if (value is object)
                        {
                            variables.Add($"NBGV_{property.Name}", value.ToString() ?? string.Empty);
                        }
                    }
                }

                return variables;
            }
        }

        /// <summary>
        /// Gets a value indicating whether to set cloud build version variables.
        /// </summary>
        [Ignore, IgnoreDataMember]
        public bool CloudBuildVersionVarsEnabled => this.VersionOptions?.CloudBuildOrDefault.SetVersionVariablesOrDefault
            ?? VersionOptions.CloudBuildOptions.DefaultInstance.SetVersionVariablesOrDefault;

        /// <summary>
        /// Gets a dictionary of cloud build variables that applies to this project,
        /// regardless of the current setting of <see cref="CloudBuildVersionVarsEnabled"/>.
        /// </summary>
        [Ignore, IgnoreDataMember]
        public IDictionary<string, string> CloudBuildVersionVars
        {
            get
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "GitAssemblyInformationalVersion", this.AssemblyInformationalVersion },
                    { "GitBuildVersion", this.Version.ToString() },
                    { "GitBuildVersionSimple", this.SimpleVersion.ToString() },
                };
            }
        }

        /// <summary>
        /// Gets the list of build metadata identifiers to include in semver version strings.
        /// </summary>
        [Ignore, IgnoreDataMember]
        public List<string> BuildMetadata { get; } = new List<string>();

        /// <summary>
        /// Gets the +buildMetadata fragment for the semantic version.
        /// </summary>
        [IgnoreDataMember]
        public string BuildMetadataFragment => FormatBuildMetadata(this.BuildMetadataWithCommitId);

        /// <summary>
        /// Gets the version to use for NuGet packages.
        /// </summary>
        [IgnoreDataMember]
        public string NuGetPackageVersion => this.VersionOptions?.NuGetPackageVersionOrDefault.SemVerOrDefault == 1 ? this.NuGetSemVer1 : this.SemVer2;

        /// <summary>
        /// Gets the version to use for Chocolatey packages.
        /// </summary>
        /// <remarks>
        /// This always returns the NuGet subset of SemVer 1.0.
        /// </remarks>
        [IgnoreDataMember]
        public string ChocolateyPackageVersion => this.NuGetSemVer1;

        /// <summary>
        /// Gets the version to use for NPM packages.
        /// </summary>
        [IgnoreDataMember]
        public string NpmPackageVersion => this.SemVer2;

        /// <summary>
        /// Gets a SemVer 1.0 compliant string that represents this version, including the -COMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        [IgnoreDataMember]
        public string SemVer1 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersionSemVer1}{this.SemVer1BuildMetadata}";

        /// <summary>
        /// Gets a SemVer 2.0 compliant string that represents this version, including a +COMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        [IgnoreDataMember]
        public string SemVer2 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}{this.SemVer2BuildMetadata}";

        /// <summary>
        /// Gets the minimum number of digits to use for numeric identifiers in SemVer 1.
        /// </summary>
        [IgnoreDataMember]
        public int SemVer1NumericIdentifierPadding => this.VersionOptions?.SemVer1NumericIdentifierPaddingOrDefault ?? 4;

        /// <inheritdoc cref="GitContext.RepoRelativeProjectDirectory"/>
        /// <devremarks>
        /// We do not serialize this value because we need it in order to deserialize path filters properly, so being in the serialized form is too late.
        /// </devremarks>
        [Ignore, IgnoreDataMember]
        public string? RepoRelativeBaseDirectory { get; private set; }

        /// <summary>
        /// Gets or sets the <see cref="VersionOracle.CloudBuildNumberOptions"/>.
        /// </summary>
        [IgnoreDataMember]
        protected VersionOptions.CloudBuildNumberOptions CloudBuildNumberOptions => this.VersionOptions?.CloudBuild?.BuildNumberOrDefault ?? VersionOptions.CloudBuildNumberOptions.DefaultInstance;

        /// <summary>
        /// Gets the build metadata, compliant to the NuGet-compatible subset of SemVer 1.0.
        /// </summary>
        /// <remarks>
        /// When adding the git commit ID in a -prerelease tag, prefix a `g` because
        /// older NuGet clients (the ones that support only a subset of semver 1.0)
        /// cannot handle prerelease tags that begin with a number (which a git commit ID might).
        /// See <see href="https://github.com/dotnet/Nerdbank.GitVersioning/issues/260#issuecomment-445511898">this discussion</see>.
        /// </remarks>
        [IgnoreDataMember]
        private string NuGetSemVer1BuildMetadata =>
            this.PublicRelease ? string.Empty : $"-{this.VersionOptions?.GitCommitIdPrefix ?? "g"}{this.GitCommitIdShort}";

        /// <summary>
        /// Gets the number of version components (up to the 4 integers) to include in <see cref="AssemblyInformationalVersion"/>.
        /// </summary>
        private int AssemblyInformationalVersionComponentCount => this.VersionOptions?.VersionHeightPosition == SemanticVersion.Position.Revision ? 4 : 3;

        /// <summary>
        /// Gets the build metadata, compliant to SemVer 1.0.
        /// </summary>
        [IgnoreDataMember]
        private string SemVer1BuildMetadata =>
            this.PublicRelease ? string.Empty : $"-{this.GitCommitIdShort}";

        /// <summary>
        /// Gets a SemVer 1.0 compliant string that represents this version, including the -gCOMMITID suffix
        /// when <see cref="PublicRelease"/> is <c>false</c>.
        /// </summary>
        [IgnoreDataMember]
        private string NuGetSemVer1 =>
            $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersionSemVer1}{this.NuGetSemVer1BuildMetadata}";

        /// <summary>
        /// Gets the build metadata that is appropriate for SemVer2 use.
        /// </summary>
        /// <remarks>
        /// We always put the commit ID in the -prerelease tag for non-public releases.
        /// But for public releases, we don't include it in the +buildMetadata section since it may be confusing for NuGet.
        /// See https://github.com/dotnet/Nerdbank.GitVersioning/pull/132#issuecomment-307208561
        /// </remarks>
        [IgnoreDataMember]
        private string SemVer2BuildMetadata =>
            (this.PublicRelease ? string.Empty : this.GitCommitIdShortForNonPublicPrereleaseTag) + FormatBuildMetadata(this.BuildMetadata);

        [IgnoreDataMember]
        private string PrereleaseVersionSemVer1 => SemanticVersionExtensions.MakePrereleaseSemVer1Compliant(this.PrereleaseVersion, this.SemVer1NumericIdentifierPadding);

        /// <summary>
        /// Gets the -gc0ffee or .gc0ffee suffix for the version.
        /// The g in the prefix might be changed if <see cref="VersionOptions.GitCommitIdPrefix"/> is set.
        /// </summary>
        /// <remarks>
        /// The prefix to the commit ID is to remain SemVer2 compliant particularly when the partial commit ID we use is made up entirely of numerals.
        /// SemVer2 forbids numerals to begin with leading zeros, but a git commit just might, so we begin with prefix always to avoid failures when the commit ID happens to be problematic.
        /// </remarks>
        [IgnoreDataMember]
        private string GitCommitIdShortForNonPublicPrereleaseTag => (string.IsNullOrEmpty(this.PrereleaseVersion) ? "-" : ".") + (this.VersionOptions?.GitCommitIdPrefix ?? "g") + this.GitCommitIdShort;

        [IgnoreDataMember]
        private int VersionHeightWithOffset => this.VersionHeight + this.VersionHeightOffset;

        /// <inheritdoc cref="Deserialize(TextReader, string?)"/>
        /// <param name="filePath">The path to the file to be read from.</param>
        /// <param name="repoRelativeBaseDirectory"><inheritdoc cref="Deserialize(TextReader, string?)" path="/param[@name='repoRelativeBaseDirectory" /></param>
        public static VersionOracle Deserialize(string filePath, string? repoRelativeBaseDirectory)
        {
            using StreamReader sr = new(File.OpenRead(filePath));
            return Deserialize(sr, repoRelativeBaseDirectory);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOracle"/> class
        /// based on data previously cached with <see cref="Serialize(TextWriter)"/>.
        /// </summary>
        /// <param name="reader">The data to read from.</param>
        /// <param name="repoRelativeBaseDirectory">The path to the repo-relative base directory.</param>
        /// <returns>The deserialized instance of <see cref="VersionOracle"/>.</returns>
        public static VersionOracle Deserialize(TextReader reader, string? repoRelativeBaseDirectory)
        {
            JsonSerializer serializer = JsonSerializer.Create(VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: repoRelativeBaseDirectory));
            using JsonReader jsonReader = new JsonTextReader(reader);
            VersionOracle oracle = serializer.Deserialize<VersionOracle>(jsonReader);
            oracle.RepoRelativeBaseDirectory = repoRelativeBaseDirectory;
            return oracle;
        }

        /// <inheritdoc cref="Serialize(TextWriter)"/>
        /// <param name="filePath">The path to the file to write to.</param>
        public void Serialize(string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using StreamWriter sw = new(File.OpenWrite(filePath), Encoding.UTF8);
            this.Serialize(sw);
        }

        /// <summary>
        /// Writes the fields of this instance out for later deserialization.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        public void Serialize(TextWriter writer)
        {
            JsonSerializer serializer = JsonSerializer.Create(VersionOptions.GetJsonSettings(repoRelativeBaseDirectory: this.RepoRelativeBaseDirectory));
            serializer.Serialize(writer, this);
        }

        private static string FormatBuildMetadata(IEnumerable<string> identifiers) =>
            (identifiers?.Any() ?? false) ? "+" + string.Join(".", identifiers) : string.Empty;

        private static Version GetAssemblyVersion(Version version, VersionOptions? versionOptions)
        {
            // If there is no repo, "version" could have uninitialized components (-1).
            version = version.EnsureNonNegativeComponents();

            var assemblyVersion = versionOptions?.AssemblyVersionOrDefault.Version ?? new System.Version(version.Major, version.Minor);
            var precision = versionOptions?.AssemblyVersionOrDefault.PrecisionOrDefault;

            assemblyVersion = new System.Version(
                assemblyVersion.Major,
                precision >= VersionOptions.VersionPrecision.Minor ? assemblyVersion.Minor : 0,
                precision >= VersionOptions.VersionPrecision.Build ? version.Build : 0,
                precision >= VersionOptions.VersionPrecision.Revision ? version.Revision : 0);
            return assemblyVersion.EnsureNonNegativeComponents(4);
        }

        /// <summary>
        /// Replaces any macros found in a prerelease or build metadata string.
        /// </summary>
        /// <param name="prereleaseOrBuildMetadata">The prerelease or build metadata.</param>
        /// <returns>The specified string, with macros substituted for actual values.</returns>
        private string ReplaceMacros(string prereleaseOrBuildMetadata) => prereleaseOrBuildMetadata.Replace(VersionOptions.VersionHeightPlaceholder, this.VersionHeightWithOffset.ToString(CultureInfo.InvariantCulture));

        [AttributeUsage(AttributeTargets.Property)]
        private class IgnoreAttribute : Attribute
        {
        }
    }
}
