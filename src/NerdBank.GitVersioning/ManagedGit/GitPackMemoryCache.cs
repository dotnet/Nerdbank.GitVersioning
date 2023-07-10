// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// <para>
///   The <see cref="GitPackMemoryCache"/> implements the <see cref="GitPackCache"/> abstract class.
///   When a <see cref="Stream"/> is added to the <see cref="GitPackMemoryCache"/>, it is wrapped in a
///   <see cref="GitPackMemoryCacheStream"/>. This stream allows for just-in-time, random, read-only
///   access to the underlying data (which may deltafied and/or compressed).
/// </para>
/// <para>
///   Whenever data is read from a <see cref="GitPackMemoryCacheStream"/>, the call is forwarded to the
///   underlying <see cref="Stream"/> and cached in a <see cref="MemoryStream"/>. If the same data is read
///   twice, it is read from the <see cref="MemoryStream"/>, rather than the underlying <see cref="Stream"/>.
/// </para>
/// <para>
///   <see cref="Add(long, Stream, string)"/> and <see cref="TryOpen(long, out ValueTuple{Stream, string}?)"/> return <see cref="Stream"/>
///   objects which may operate on the same underlying <see cref="Stream"/>, but independently maintain
///   their state.
/// </para>
/// </summary>
public class GitPackMemoryCache : GitPackCache
{
    private readonly Dictionary<long, (GitPackMemoryCacheStream, string)> cache = new();

    /// <inheritdoc/>
    public override Stream Add(long offset, Stream stream, string objectType)
    {
        var cacheStream = new GitPackMemoryCacheStream(stream);
        this.cache.Add(offset, (cacheStream, objectType));
        return new GitPackMemoryCacheViewStream(cacheStream);
    }

    /// <inheritdoc/>
    public override bool TryOpen(long offset, [NotNullWhen(true)] out (Stream ContentStream, string ObjectType)? hit)
    {
        if (this.cache.TryGetValue(offset, out (GitPackMemoryCacheStream Stream, string ObjectType) tuple))
        {
            hit = (new GitPackMemoryCacheViewStream(tuple.Stream), tuple.ObjectType);
            return true;
        }

        hit = null;
        return false;
    }

    /// <inheritdoc/>
    public override void GetCacheStatistics(StringBuilder builder)
    {
        builder.AppendLine($"{this.cache.Count} items in cache");
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            while (this.cache.Count > 0)
            {
                var key = this.cache.Keys.First();
                (GitPackMemoryCacheStream? stream, _) = this.cache[key];
                stream.Dispose();
                this.cache.Remove(key);
            }
        }

        base.Dispose(disposing);
    }
}
