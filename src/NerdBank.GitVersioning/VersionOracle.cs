﻿// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

#nullable enable

namespace Nerdbank.GitVersioning;

/// <summary>
/// Assembles version information in a variety of formats.
/// </summary>
public class VersionOracle
{
    /// <summary>
    /// The 0.0 version.
    /// </summary>
    private protected static readonly Version Version0 = new Version(0, 0);

    private const bool UseLibGit2 = false;

    private readonly GitContext context;

    private readonly ICloudBuild? cloudBuild;

    /// <summary>
    /// The number of version components (up to the 4 integers) to include in <see cref="AssemblyInformationalVersion"/>.
    /// </summary>
    private readonly int assemblyInformationalVersionComponentCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionOracle"/> class.
    /// </summary>
    /// <param name="context">The git context from which to calculate version data.</param>
    /// <param name="cloudBuild">An optional cloud build provider that may offer additional context. Typically set to <see cref="CloudBuild.Active"/>.</param>
    /// <param name="overrideVersionHeightOffset">An optional value to override the version height offset.</param>
    public VersionOracle(GitContext context, ICloudBuild? cloudBuild = null, int? overrideVersionHeightOffset = null)
    {
        this.context = context;
        this.cloudBuild = cloudBuild;

        this.CommittedVersion = context.VersionFile.GetVersion();

        // Consider the working version only if the commit being inspected is HEAD.
        // Otherwise we're looking at historical data and should not consider the state of the working tree at all.
        this.WorkingVersion = context.IsHead ? context.VersionFile.GetWorkingCopyVersion() : this.CommittedVersion;

        if (overrideVersionHeightOffset.HasValue)
        {
            if (this.CommittedVersion is object)
            {
                this.CommittedVersion.VersionHeightOffset = overrideVersionHeightOffset.Value;
            }

            if (this.WorkingVersion is object)
            {
                this.WorkingVersion.VersionHeightOffset = overrideVersionHeightOffset.Value;
            }
        }

        this.BuildingRef = cloudBuild?.BuildingTag ?? cloudBuild?.BuildingBranch ?? context.HeadCanonicalName;

        try
        {
            this.VersionHeight = context.CalculateVersionHeight(this.CommittedVersion, this.WorkingVersion);
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

        static Exception ThrowShallowClone(Exception inner) => throw new GitException("Shallow clone lacks the objects required to calculate version height. Use full clones or clones with a history at least as deep as the last version height resetting change.", inner) { IsShallowClone = true, ErrorCode = GitException.ErrorCodes.ObjectNotFound };

        this.VersionOptions = this.CommittedVersion ?? this.WorkingVersion;
        this.Version = this.VersionOptions?.Version?.Version ?? Version0;
        this.assemblyInformationalVersionComponentCount = this.VersionOptions?.VersionHeightPosition == SemanticVersion.Position.Revision ? 4 : 3;

        // Override the typedVersion with the special build number and revision components, when available.
        if (context.IsRepository)
        {
            this.Version = context.GetIdAsVersion(this.CommittedVersion, this.WorkingVersion, this.VersionHeight);
        }

        this.CloudBuildNumberOptions = this.VersionOptions?.CloudBuild?.BuildNumberOrDefault ?? VersionOptions.CloudBuildNumberOptions.DefaultInstance;

        // get the commit id abbreviation only if the commit id is set
        if (!string.IsNullOrEmpty(this.GitCommitId))
        {
            int gitCommitIdShortFixedLength = this.VersionOptions?.GitCommitIdShortFixedLength ?? VersionOptions.DefaultGitCommitIdShortFixedLength;
            int gitCommitIdShortAutoMinimum = this.VersionOptions?.GitCommitIdShortAutoMinimum ?? 0;

            // Get it from the git repository if there is a repository present and it is enabled.
            this.GitCommitIdShort = this.GitCommitId is object && gitCommitIdShortAutoMinimum > 0
                ? this.context.GetShortUniqueCommitId(gitCommitIdShortAutoMinimum)
                : this.GitCommitId!.Substring(0, gitCommitIdShortFixedLength);
        }

        if (this.VersionOptions?.PublicReleaseRefSpec?.Count > 0)
        {
            if (this.BuildingRef is not null)
            {
                this.PublicRelease = this.VersionOptions.PublicReleaseRefSpec.Any(
                    expr => Regex.IsMatch(this.BuildingRef, expr));
            }

            if (!this.PublicRelease && this.VersionOptions.PublicReleaseRefSpec.Any(expr => expr.StartsWith("^refs/tags/", StringComparison.Ordinal)) && this.Tags is not null)
            {
                this.PublicRelease = this.VersionOptions.PublicReleaseRefSpec.Any(
                    expr => this.Tags.Any(cand => Regex.IsMatch(cand, expr)));
            }
        }
    }

    /// <summary>
    /// Gets the BuildNumber to set the cloud build to (if applicable).
    /// </summary>
    public string CloudBuildNumber
    {
        get
        {
            VersionOptions.CloudBuildNumberCommitIdOptions? commitIdOptions = this.CloudBuildNumberOptions.IncludeCommitIdOrDefault;
            bool includeCommitInfo = commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.Always ||
                (commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.NonPublicReleaseOnly && !this.PublicRelease);
            bool commitIdInBuildMetadata = includeCommitInfo && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.BuildMetadata;

            // Include the revision in the build number if, either
            // - The commit id is configured to be included as a revision or
            // - 3 version fields are configured in version.json (and thus the version height is encoded as revision) or
            // - 4 version fields are configured in version.json.
            bool includeRevision = (includeCommitInfo && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.FourthVersionComponent) ||
                                   this.VersionOptions?.Version?.VersionHeightPosition == SemanticVersion.Position.Revision ||
                                   this.VersionOptions?.Version?.Version.Revision != -1;

            string buildNumberMetadata = FormatBuildMetadata(commitIdInBuildMetadata ? this.BuildMetadataWithCommitId : this.BuildMetadata);

            Version buildNumberVersion = includeRevision ? this.Version : this.SimpleVersion;
            return $"{buildNumberVersion}{this.PrereleaseVersion}{buildNumberMetadata}";
        }
    }

    /// <summary>
    /// Gets a value indicating whether the cloud build number should be set.
    /// </summary>
    [Ignore]
    public bool CloudBuildNumberEnabled => this.CloudBuildNumberOptions.EnabledOrDefault;

    /// <summary>
    /// Gets the build metadata identifiers, including the git commit ID as the first identifier if appropriate.
    /// </summary>
    [Ignore]
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
    public bool VersionFileFound => this.VersionOptions is object;

    /// <summary>
    /// Gets the version options used to initialize this instance.
    /// </summary>
    [Ignore]
    public VersionOptions? VersionOptions { get; }

    /// <summary>
    /// Gets the version string to use for the <see cref="System.Reflection.AssemblyVersionAttribute"/>.
    /// </summary>
    public Version AssemblyVersion => GetAssemblyVersion(this.Version, this.VersionOptions).EnsureNonNegativeComponents();

    /// <summary>
    /// Gets the version string to use for the <see cref="System.Reflection.AssemblyFileVersionAttribute"/>.
    /// </summary>
    public Version AssemblyFileVersion => this.Version;

    /// <summary>
    /// Gets the version string to use for the <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>.
    /// </summary>
    public string AssemblyInformationalVersion =>
        $"{this.Version.ToStringSafe(this.assemblyInformationalVersionComponentCount)}{this.PrereleaseVersion}{FormatBuildMetadata(this.BuildMetadataWithCommitId)}";

    /// <summary>
    /// Gets or sets a value indicating whether the project is building
    /// in PublicRelease mode.
    /// </summary>
    public bool PublicRelease { get; set; }

    /// <summary>
    /// Gets the prerelease version information, including a leading hyphen.
    /// </summary>
    public string PrereleaseVersion => this.ReplaceMacros(this.VersionOptions?.Version?.Prerelease ?? string.Empty);

    /// <summary>
    /// Gets the prerelease version information, omitting the leading hyphen, if any.
    /// </summary>
    public string? PrereleaseVersionNoLeadingHyphen => this.PrereleaseVersion?.TrimStart('-');

    /// <summary>
    /// Gets the version information without a Revision component.
    /// </summary>
    public Version SimpleVersion => this.Version.Build >= 0
            ? new Version(this.Version.Major, this.Version.Minor, this.Version.Build)
            : new Version(this.Version.Major, this.Version.Minor);

    /// <summary>
    /// Gets the build number (i.e. third integer, or PATCH) for this version.
    /// </summary>
    public int BuildNumber => Math.Max(0, this.Version.Build);

    /// <summary>
    /// Gets the <see cref="Version.Revision"/> component of the <see cref="Version"/>.
    /// </summary>
    public int VersionRevision => this.Version.Revision;

    /// <summary>
    /// Gets the major.minor version string.
    /// </summary>
    /// <value>
    /// The x.y string (no build number or revision number).
    /// </value>
    public Version MajorMinorVersion => new Version(this.Version.Major, this.Version.Minor);

    /// <summary>
    /// Gets the <see cref="Version.Major"/> component of the <see cref="Version"/>.
    /// </summary>
    public int VersionMajor => this.Version.Major;

    /// <summary>
    /// Gets the <see cref="Version.Minor"/> component of the <see cref="Version"/>.
    /// </summary>
    public int VersionMinor => this.Version.Minor;

    /// <summary>
    /// Gets the Git revision control commit id for HEAD (the current source code version).
    /// </summary>
    public string? GitCommitId => this.context.GitCommitId ?? this.cloudBuild?.GitCommitId;

    /// <summary>
    /// Gets the first several characters of the Git revision control commit id for HEAD (the current source code version).
    /// </summary>
    public string? GitCommitIdShort { get; }

    /// <summary>
    /// Gets the Git revision control commit date for HEAD (the current source code version).
    /// </summary>
    public DateTimeOffset? GitCommitDate => this.context.GitCommitDate;

    /// <summary>
    /// Gets or sets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive)
    /// that set the version to the value at HEAD.
    /// </summary>
    public int VersionHeight { get; protected set; }

    /// <summary>
    /// Gets the offset to add to the <see cref="VersionHeight"/>
    /// when calculating the integer to use as the <see cref="BuildNumber"/>
    /// or elsewhere that the {height} macro is used.
    /// </summary>
    public int VersionHeightOffset => this.VersionOptions?.VersionHeightOffsetOrDefault ?? 0;

    /// <summary>
    /// Gets or sets the ref (branch or tag) being built.
    /// </summary>
    /// <remarks>
    /// Just contains a tag if it is known that explicitly this tag is built, e.g. in a cloud build context.
    /// </remarks>
    public string? BuildingRef { get; protected set; }

    /// <summary>
    /// Gets a collection of the tags that reference HEAD.
    /// </summary>
    [Ignore]
    public IReadOnlyCollection<string>? Tags => this.context.HeadTags;

    /// <summary>
    /// Gets or sets the version for this project, with up to 4 components.
    /// </summary>
    public Version Version { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether to set all cloud build variables prefaced with "NBGV_".
    /// </summary>
    [Ignore]
    public bool CloudBuildAllVarsEnabled => this.VersionOptions?.CloudBuildOrDefault.SetAllVariablesOrDefault
        ?? VersionOptions.CloudBuildOptions.DefaultInstance.SetAllVariablesOrDefault;

    /// <summary>
    /// Gets a dictionary of all cloud build variables that applies to this project,
    /// regardless of the current setting of <see cref="CloudBuildAllVarsEnabled"/>.
    /// </summary>
    [Ignore]
    public IDictionary<string, string> CloudBuildAllVars
    {
        get
        {
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            PropertyInfo[] properties = this.GetType().GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (property.GetCustomAttribute<IgnoreAttribute>() is not null)
                {
                    continue;
                }

                object? propertyValue = property.GetValue(this);
                if (propertyValue is null)
                {
                    continue;
                }

                string value = propertyValue switch
                {
                    DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("o", CultureInfo.InvariantCulture),
                    _ => Convert.ToString(propertyValue, CultureInfo.InvariantCulture) ?? string.Empty,
                };

                variables.Add($"NBGV_{property.Name}", value);
            }

            return variables;
        }
    }

    /// <summary>
    /// Gets a value indicating whether to set cloud build version variables.
    /// </summary>
    [Ignore]
    public bool CloudBuildVersionVarsEnabled => this.VersionOptions?.CloudBuildOrDefault.SetVersionVariablesOrDefault
        ?? VersionOptions.CloudBuildOptions.DefaultInstance.SetVersionVariablesOrDefault;

    /// <summary>
    /// Gets a dictionary of cloud build variables that applies to this project,
    /// regardless of the current setting of <see cref="CloudBuildVersionVarsEnabled"/>.
    /// </summary>
    [Ignore]
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
    [Ignore]
    public List<string> BuildMetadata { get; } = new List<string>();

    /// <summary>
    /// Gets the +buildMetadata fragment for the semantic version.
    /// </summary>
    public string BuildMetadataFragment => FormatBuildMetadata(this.BuildMetadataWithCommitId);

    /// <summary>
    /// Gets the version to use for NuGet packages.
    /// </summary>
    public string NuGetPackageVersion => this.VersionOptions?.NuGetPackageVersionOrDefault.SemVerOrDefault == 1 ? this.NuGetSemVer1 : this.NuGetSemVer2;

    /// <summary>
    /// Gets the version to use for Chocolatey packages.
    /// </summary>
    /// <remarks>
    /// This always returns the NuGet subset of SemVer 1.0.
    /// </remarks>
    public string ChocolateyPackageVersion => this.NuGetSemVer1;

    /// <summary>
    /// Gets the version to use for NPM packages.
    /// </summary>
    public string NpmPackageVersion => $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}";

    /// <summary>
    /// Gets a SemVer 1.0 compliant string that represents this version, including the -COMMITID suffix
    /// when <see cref="PublicRelease"/> is <see langword="false"/>.
    /// </summary>
    public string SemVer1 =>
        $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersionSemVer1}{this.SemVer1BuildMetadata}";

    /// <summary>
    /// Gets a SemVer 2.0 compliant string that represents this version, including a +COMMITID suffix
    /// when <see cref="PublicRelease"/> is <see langword="false"/>.
    /// </summary>
    public string SemVer2 =>
        $"{this.Version.ToStringSafe(3)}{this.PrereleaseVersion}{this.SemVer2BuildMetadata}";

    /// <summary>
    /// Gets the minimum number of digits to use for numeric identifiers in SemVer 1.
    /// </summary>
    public int SemVer1NumericIdentifierPadding => this.VersionOptions?.SemVer1NumericIdentifierPaddingOrDefault ?? 4;

    /// <summary>
    /// Gets or sets the <see cref="VersionOracle.CloudBuildNumberOptions"/>.
    /// </summary>
    protected VersionOptions.CloudBuildNumberOptions CloudBuildNumberOptions { get; set; }

    /// <summary>
    /// Gets the <see cref="VersionOptions"/> that were deserialized from the contextual commit, if any.
    /// </summary>
    protected VersionOptions? CommittedVersion { get; }

    /// <summary>
    /// Gets the <see cref="VersionOptions"/> that were deserialized from the working tree, if any.
    /// </summary>
    protected VersionOptions? WorkingVersion { get; }

    /// <summary>
    /// Gets the build metadata, compliant to the NuGet-compatible subset of SemVer 1.0.
    /// </summary>
    /// <remarks>
    /// When adding the git commit ID in a -prerelease tag, prefix a `g` because
    /// older NuGet clients (the ones that support only a subset of semver 1.0)
    /// cannot handle prerelease tags that begin with a number (which a git commit ID might).
    /// See <see href="https://github.com/dotnet/Nerdbank.GitVersioning/issues/260#issuecomment-445511898">this discussion</see>.
    /// </remarks>
    private string NuGetSemVer1BuildMetadata =>
        this.PublicRelease ? string.Empty : $"-{this.VersionOptions?.GitCommitIdPrefix ?? "g"}{this.GitCommitIdShort}";

    /// <summary>
    /// Gets the build metadata, compliant to SemVer 1.0.
    /// </summary>
    private string SemVer1BuildMetadata =>
        this.PublicRelease ? string.Empty : $"-{this.GitCommitIdShort}";

    /// <summary>
    /// Gets a SemVer 1.0 compliant string that represents this version, including the -gCOMMITID suffix
    /// when <see cref="PublicRelease"/> is <see langword="false"/>.
    /// </summary>
    private string NuGetSemVer1
    {
        get
        {
            VersionOptions.VersionPrecision precision = this.VersionOptions?.NuGetPackageVersionOrDefault.PrecisionOrDefault ?? VersionOptions.NuGetPackageVersionOptions.DefaultPrecision;
            Version? version = this.Version.EnsureNonNegativeComponents();
            version = ApplyVersionPrecision(version, precision);

            // If precision is set to include the 4th version component, return all 4 version fields, otherwise return 3 fields.
            int fieldCount = precision >= VersionOptions.VersionPrecision.Revision ? 4 : 3;

            return $"{version.ToStringSafe(fieldCount)}{this.PrereleaseVersionSemVer1}{this.NuGetSemVer1BuildMetadata}";
        }
    }

    /// <summary>
    /// Gets a SemVer 2.0 compliant string that represents this version, including the -gCOMMITID suffix
    /// when <see cref="PublicRelease"/> is <see langword="false"/>.
    /// </summary>
    private string NuGetSemVer2
    {
        get
        {
            VersionOptions.VersionPrecision precision = this.VersionOptions?.NuGetPackageVersionOrDefault.PrecisionOrDefault ?? VersionOptions.NuGetPackageVersionOptions.DefaultPrecision;
            Version? version = this.Version.EnsureNonNegativeComponents();
            version = ApplyVersionPrecision(version, precision);

            // If precision is set to include the 4th version component, return all 4 version fields, otherwise return 3 fields.
            int fieldCount = precision >= VersionOptions.VersionPrecision.Revision ? 4 : 3;

            return $"{version.ToStringSafe(fieldCount)}{this.PrereleaseVersion}{this.SemVer2BuildMetadata}";
        }
    }

    /// <summary>
    /// Gets the build metadata that is appropriate for SemVer2 use.
    /// </summary>
    /// <remarks>
    /// We always put the commit ID in the -prerelease tag for non-public releases.
    /// But for public releases, we don't include it in the +buildMetadata section since it may be confusing for NuGet.
    /// </remarks>
    /// <seealso href="https://github.com/dotnet/Nerdbank.GitVersioning/pull/132#issuecomment-307208561"/>
    private string SemVer2BuildMetadata =>
        (this.PublicRelease ? string.Empty : this.GitCommitIdShortForNonPublicPrereleaseTag) + FormatBuildMetadata(this.BuildMetadata);

    private string PrereleaseVersionSemVer1 => SemanticVersionExtensions.MakePrereleaseSemVer1Compliant(this.PrereleaseVersion, this.SemVer1NumericIdentifierPadding);

    /// <summary>
    /// Gets the -gc0ffee or .gc0ffee suffix for the version.
    /// The g in the prefix might be changed if <see cref="VersionOptions.GitCommitIdPrefix"/> is set.
    /// </summary>
    /// <remarks>
    /// The prefix to the commit ID is to remain SemVer2 compliant particularly when the partial commit ID we use is made up entirely of numerals.
    /// SemVer2 forbids numerals to begin with leading zeros, but a git commit just might, so we begin with prefix always to avoid failures when the commit ID happens to be problematic.
    /// </remarks>
    private string GitCommitIdShortForNonPublicPrereleaseTag => (string.IsNullOrEmpty(this.PrereleaseVersion) ? "-" : ".") + (this.VersionOptions?.GitCommitIdPrefix ?? "g") + this.GitCommitIdShort;

    private int VersionHeightWithOffset => this.VersionHeight + this.VersionHeightOffset;

    private static string FormatBuildMetadata(IEnumerable<string> identifiers) =>
        (identifiers?.Any() ?? false) ? "+" + string.Join(".", identifiers) : string.Empty;

    private static Version GetAssemblyVersion(Version version, VersionOptions? versionOptions)
    {
        // If there is no repo, "version" could have uninitialized components (-1).
        version = version.EnsureNonNegativeComponents();

        Version assemblyVersion;

        if (versionOptions?.AssemblyVersion?.Version is not null)
        {
            // When specified explicitly, use the assembly version as the user defines it.
            assemblyVersion = versionOptions.AssemblyVersion.Version;
        }
        else
        {
            // Otherwise consider precision to base the assembly version off of the main computed version.
            VersionOptions.VersionPrecision precision = versionOptions?.AssemblyVersion?.Precision ?? VersionOptions.DefaultVersionPrecision;
            assemblyVersion = ApplyVersionPrecision(version, precision);
        }

        return assemblyVersion.EnsureNonNegativeComponents(4);
    }

    private static Version ApplyVersionPrecision(Version version, VersionOptions.VersionPrecision precision)
    {
        return new Version(
            version.Major,
            precision >= VersionOptions.VersionPrecision.Minor ? version.Minor : 0,
            precision >= VersionOptions.VersionPrecision.Build ? version.Build : 0,
            precision >= VersionOptions.VersionPrecision.Revision ? version.Revision : 0);
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
