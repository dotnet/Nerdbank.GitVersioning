// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Represents a cache in which objects retrieved from a <see cref="GitPack"/>
/// are cached. Caching these objects can be of interest, because retrieving
/// data from a <see cref="GitPack"/> can be potentially expensive: the data is
/// compressed and can be deltified.
/// </summary>
public abstract class GitPackCache : IDisposable
{
    /// <summary>
    /// Attempts to retrieve a Git object from cache.
    /// </summary>
    /// <param name="offset">
    /// The offset of the Git object in the Git pack.
    /// </param>
    /// <param name="stream">
    /// A <see cref="Stream"/> which will be set to the cached Git object.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the object was found in cache; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public abstract bool TryOpen(long offset, [NotNullWhen(true)] out Stream? stream);

    /// <summary>
    /// Gets statistics about the cache usage.
    /// </summary>
    /// <param name="builder">
    /// A <see cref="StringBuilder"/> to which to write the statistics.
    /// </param>
    public abstract void GetCacheStatistics(StringBuilder builder);

    /// <summary>
    /// Adds a Git object to this cache.
    /// </summary>
    /// <param name="offset">
    /// The offset of the Git object in the Git pack.
    /// </param>
    /// <param name="stream">
    /// A <see cref="Stream"/> which represents the object to add. This stream
    /// will be copied to the cache.
    /// </param>
    /// <returns>
    /// A <see cref="Stream"/> which represents the cached entry.
    /// </returns>
    public abstract Stream Add(long offset, Stream stream);

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of native and managed resources associated by this object.
    /// </summary>
    /// <param name="disposing"><see langword="true" /> to dispose managed and native resources; <see langword="false" /> to only dispose of native resources.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
