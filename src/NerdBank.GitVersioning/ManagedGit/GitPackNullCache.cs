// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// A no-op implementation of the <see cref="GitPackCache"/> class.
/// </summary>
public class GitPackNullCache : GitPackCache
{
    /// <summary>
    /// Gets the default instance of the <see cref="GitPackCache"/> class.
    /// </summary>
    public static GitPackNullCache Instance { get; } = new GitPackNullCache();

    /// <inheritdoc/>
    public override Stream Add(long offset, Stream stream)
    {
        return stream;
    }

    /// <inheritdoc/>
    public override bool TryOpen(long offset, [NotNullWhen(true)] out Stream? stream)
    {
        stream = null;
        return false;
    }

    /// <inheritdoc/>
    public override void GetCacheStatistics(StringBuilder builder)
    {
    }
}
