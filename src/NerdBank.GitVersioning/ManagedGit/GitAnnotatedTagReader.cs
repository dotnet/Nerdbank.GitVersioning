// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers;
using System.Diagnostics;
using System.Xml.Linq;
using LibGit2Sharp;
using Validation;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Reads a <see cref="GitAnnotatedTag"/> object.
/// </summary>
public static class GitAnnotatedTagReader
{
    private static readonly byte[] ObjectStart = GitRepository.Encoding.GetBytes("object ");
    private static readonly byte[] TypeStart = GitRepository.Encoding.GetBytes("type ");
    private static readonly byte[] TagStart = GitRepository.Encoding.GetBytes("tag ");

    /// <summary>
    /// Reads a <see cref="GitAnnotatedTag"/> object from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">
    /// A <see cref="Stream"/> which contains the <see cref="GitAnnotatedTag"/> in its text representation.
    /// </param>
    /// <param name="sha">
    /// The <see cref="GitObjectId"/> of the commit.
    /// </param>
    /// <returns>
    /// The <see cref="GitAnnotatedTag"/>.
    /// </returns>
    public static GitAnnotatedTag Read(Stream stream, GitObjectId sha)
    {
        Requires.NotNull(stream, nameof(stream));

        byte[] buffer = ArrayPool<byte>.Shared.Rent(checked((int)stream.Length));

        try
        {
            Span<byte> span = buffer.AsSpan(0, (int)stream.Length);
            stream.ReadAll(span);

            return Read(span, sha);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads a <see cref="GitAnnotatedTag"/> object from a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    /// <param name="tag">
    /// A <see cref="ReadOnlySpan{T}"/> which contains the <see cref="GitAnnotatedTag"/> in its text representation.
    /// </param>
    /// <param name="sha">
    /// The <see cref="GitObjectId"/> of the annotated tag.
    /// </param>
    /// <returns>
    /// The <see cref="GitAnnotatedTag"/>.
    /// </returns>
    public static GitAnnotatedTag Read(ReadOnlySpan<byte> tag, GitObjectId sha)
    {
        ReadOnlySpan<byte> buffer = tag;

        (GitObjectId obj, int objLen) = ReadObject(buffer);
        buffer = buffer.Slice(objLen);

        (string type, int typeLen) = ReadType(buffer);
        buffer = buffer.Slice(typeLen);

        (string tagName, _) = ReadTag(buffer);

        return new GitAnnotatedTag
        {
            Sha = sha,
            Object = obj,
            Type = type,
            Tag = tagName,
        };
    }

    private static (GitObjectId ObjectId, int BytesRead) ReadObject(ReadOnlySpan<byte> remaining)
    {
        // Format: object <hex-hash>\n
        // SHA-1: 48 bytes (7 + 40 + 1); SHA-256: 72 bytes (7 + 64 + 1)
        Debug.Assert(remaining.Slice(0, ObjectStart.Length).SequenceEqual(ObjectStart));

        int lineEnd = remaining.IndexOf((byte)'\n');
        Debug.Assert(lineEnd >= ObjectStart.Length);

        ReadOnlySpan<byte> hash = remaining.Slice(ObjectStart.Length, lineEnd - ObjectStart.Length);
        return (GitObjectId.ParseHex(hash), lineEnd + 1);
    }

    private static (string Content, int BytesRead) ReadPrefixedString(ReadOnlySpan<byte> remaining, byte[] prefix)
    {
        Debug.Assert(remaining.Slice(0, prefix.Length).SequenceEqual(prefix));

        int lineEnd = remaining.IndexOf((byte)'\n');
        ReadOnlySpan<byte> type = remaining.Slice(prefix.Length, lineEnd - prefix.Length);
        return (GitRepository.GetString(type), lineEnd + 1);
    }

    private static (string Content, int BytesRead) ReadType(ReadOnlySpan<byte> remaining)
    {
        // Format: type commit\n
        // <variable> bytes:
        //  type: 4 bytes
        //  space: 1 byte
        //  <type e.g. commit>: <variable> bytes
        //  \n: 1 byte
        return ReadPrefixedString(remaining, TypeStart);
    }

    private static (string Content, int BytesRead) ReadTag(ReadOnlySpan<byte> remaining)
    {
        // Format: tag someAnnotatedTag\n
        // <variable> bytes:
        //  tag: 3 bytes
        //  space: 1 byte
        //  <tag name e.g. someAnnotatedTag>: <variable> bytes
        //  \n: 1 byte
        return ReadPrefixedString(remaining, TagStart);
    }
}
