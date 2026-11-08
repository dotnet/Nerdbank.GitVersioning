// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using Newtonsoft.Json;

#nullable enable

namespace Nerdbank.GitVersioning.Tool;

/// <summary>
/// Defines the stable JSON contract emitted for external build adapters.
/// </summary>
internal sealed class AdapterVersionInfo
{
    /// <summary>
    /// The current adapter JSON schema version.
    /// </summary>
    internal const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdapterVersionInfo"/> class.
    /// </summary>
    /// <param name="oracle">The source of calculated version information.</param>
    internal AdapterVersionInfo(VersionOracle oracle)
    {
        if (oracle is null)
        {
            throw new ArgumentNullException(nameof(oracle));
        }

        this.SemVer2 = oracle.SemVer2;
        this.Version = oracle.Version.ToString();
        this.VersionHeight = oracle.VersionHeight;
        this.GitCommitId = oracle.GitCommitId;
        this.GitCommitIdShort = oracle.GitCommitIdShort;
        this.GitCommitDate = oracle.GitCommitDate?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        this.PublicRelease = oracle.PublicRelease;
    }

    /// <summary>
    /// Gets the adapter JSON schema version.
    /// </summary>
    [JsonProperty("schemaVersion", Order = 0)]
    internal int SchemaVersion => CurrentSchemaVersion;

    /// <summary>
    /// Gets the SemVer 2.0 version.
    /// </summary>
    [JsonProperty("semVer2", Order = 1)]
    internal string SemVer2 { get; }

    /// <summary>
    /// Gets the calculated numeric version.
    /// </summary>
    [JsonProperty("version", Order = 2)]
    internal string Version { get; }

    /// <summary>
    /// Gets the version height.
    /// </summary>
    [JsonProperty("versionHeight", Order = 3)]
    internal int VersionHeight { get; }

    /// <summary>
    /// Gets the full Git commit ID, or <see langword="null"/> when the selected Git engine cannot provide it.
    /// </summary>
    [JsonProperty("gitCommitId", Order = 4)]
    internal string? GitCommitId { get; }

    /// <summary>
    /// Gets the abbreviated Git commit ID, or <see langword="null"/> when the selected Git engine cannot provide it.
    /// </summary>
    [JsonProperty("gitCommitIdShort", Order = 5)]
    internal string? GitCommitIdShort { get; }

    /// <summary>
    /// Gets the Git commit time as a UTC RFC 3339 string, or <see langword="null"/> when unavailable.
    /// </summary>
    [JsonProperty("gitCommitDate", Order = 6)]
    internal string? GitCommitDate { get; }

    /// <summary>
    /// Gets a value indicating whether this is a public release.
    /// </summary>
    [JsonProperty("publicRelease", Order = 7)]
    internal bool PublicRelease { get; }
}
