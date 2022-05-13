// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Base class for classes which support reading data stored in a Git Pack file.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format"/>
public abstract class GitPackIndexReader : IDisposable
{
    /// <summary>
    /// The header of the index file.
    /// </summary>
    protected static readonly byte[] Header = new byte[] { 0xff, 0x74, 0x4f, 0x63 };

    /// <summary>
    /// Gets the offset of a Git object in the index file.
    /// </summary>
    /// <param name="objectId">
    /// The Git object Id of the Git object for which to get the offset.
    /// </param>
    /// <returns>
    /// If found, the offset of the Git object in the index file; otherwise,
    /// <see langword="null"/>.
    /// </returns>
    public long? GetOffset(GitObjectId objectId)
    {
        Span<byte> name = stackalloc byte[20];
        objectId.CopyTo(name);
        (long? offset, GitObjectId? _) = this.GetOffset(name);
        return offset;
    }

    /// <summary>
    /// Gets the offset of a Git object in the index file.
    /// </summary>
    /// <param name="objectId">
    /// A partial or full Git object id, in its binary representation.
    /// </param>
    /// <param name="endsWithHalfByte"><see langword="true"/> if <paramref name="objectId"/> ends with a byte whose last 4 bits are all zeros and not intended for inclusion in the search; <see langword="false"/> otherwise.</param>
    /// <returns>
    /// If found, the offset of the Git object in the index file; otherwise,
    /// <see langword="null"/>.
    /// </returns>
    public abstract (long? Offset, GitObjectId? ObjectId) GetOffset(Span<byte> objectId, bool endsWithHalfByte = false);

    /// <inheritdoc/>
    public abstract void Dispose();
}
