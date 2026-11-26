// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers;
using System.Diagnostics;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Reads a <see cref="GitCommit"/> object.
/// </summary>
public static class GitCommitReader
{
    private static readonly byte[] TreeStart = GitRepository.Encoding.GetBytes("tree ");
    private static readonly byte[] ParentStart = GitRepository.Encoding.GetBytes("parent ");
    private static readonly byte[] AuthorStart = GitRepository.Encoding.GetBytes("author ");
    private static readonly byte[] CommitterStart = GitRepository.Encoding.GetBytes("committer ");

    /// <summary>
    /// Reads a <see cref="GitCommit"/> object from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">
    /// A <see cref="Stream"/> which contains the <see cref="GitCommit"/> in its text representation.
    /// </param>
    /// <param name="sha">
    /// The <see cref="GitObjectId"/> of the commit.
    /// </param>
    /// <param name="readAuthor">
    /// A value indicating whether to populate the <see cref="GitCommit.Author"/> and <see cref="GitCommit.Committer"/> fields.
    /// </param>
    /// <returns>
    /// The <see cref="GitCommit"/>.
    /// </returns>
    public static GitCommit Read(Stream stream, GitObjectId sha, bool readAuthor = false)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);

        try
        {
            Span<byte> span = buffer.AsSpan(0, (int)stream.Length);
            stream.ReadAll(span);

            return Read(span, sha, readAuthor);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads a <see cref="GitCommit"/> object from a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    /// <param name="commit">
    /// A <see cref="ReadOnlySpan{T}"/> which contains the <see cref="GitCommit"/> in its text representation.
    /// </param>
    /// <param name="sha">
    /// The <see cref="GitObjectId"/> of the commit.
    /// </param>
    /// <param name="readAuthor">
    /// A value indicating whether to populate the <see cref="GitCommit.Author"/> and <see cref="GitCommit.Committer"/> fields.
    /// </param>
    /// <returns>
    /// The <see cref="GitCommit"/>.
    /// </returns>
    public static GitCommit Read(ReadOnlySpan<byte> commit, GitObjectId sha, bool readAuthor = false)
    {
        ReadOnlySpan<byte> buffer = commit;

        GitObjectId tree = ReadTree(buffer, out int treeLineLength);

        buffer = buffer.Slice(treeLineLength);

        GitObjectId? firstParent = null, secondParent = null;
        List<GitObjectId>? additionalParents = null;
        while (TryReadParent(buffer, out GitObjectId parent, out int parentLineLength))
        {
            if (!firstParent.HasValue)
            {
                firstParent = parent;
            }
            else if (!secondParent.HasValue)
            {
                secondParent = parent;
            }
            else
            {
                additionalParents ??= new List<GitObjectId>();
                additionalParents.Add(parent);
            }

            buffer = buffer.Slice(parentLineLength);
        }

        GitSignature author = default;
        GitSignature committer = default;

        if (readAuthor)
        {
            if (!TryReadAuthor(buffer, out author, out int lineLength))
            {
                throw new GitException();
            }

            buffer = buffer.Slice(lineLength);

            if (!TryReadCommitter(buffer, out committer))
            {
                throw new GitException();
            }
        }

        return new GitCommit()
        {
            Sha = sha,
            FirstParent = firstParent,
            SecondParent = secondParent,
            AdditionalParents = additionalParents,
            Tree = tree,
            Author = readAuthor ? author : null,
            Committer = readAuthor ? committer : null,
        };
    }

    private static GitObjectId ReadTree(ReadOnlySpan<byte> line, out int lineLength)
    {
        // Format: tree <hex-hash>\n
        // SHA-1: 46 bytes (5 + 40 + 1); SHA-256: 70 bytes (5 + 64 + 1)
        Debug.Assert(line.Slice(0, TreeStart.Length).SequenceEqual(TreeStart));
        int lineEnd = line.IndexOf((byte)'\n');
        Debug.Assert(lineEnd >= TreeStart.Length);

        lineLength = lineEnd + 1;
        return GitObjectId.ParseHex(line.Slice(TreeStart.Length, lineEnd - TreeStart.Length));
    }

    private static bool TryReadParent(ReadOnlySpan<byte> line, out GitObjectId parent, out int lineLength)
    {
        // Format: "parent <hex-hash>\n"
        parent = GitObjectId.Empty;
        lineLength = 0;

        if (line.Length < ParentStart.Length || !line.Slice(0, ParentStart.Length).SequenceEqual(ParentStart))
        {
            return false;
        }

        int lineEnd = line.IndexOf((byte)'\n');
        if (lineEnd < 0)
        {
            return false;
        }

        int hashLength = lineEnd - ParentStart.Length;
        if (hashLength != GitObjectId.Sha1HexLength && hashLength != GitObjectId.Sha256HexLength)
        {
            return false;
        }

        parent = GitObjectId.ParseHex(line.Slice(ParentStart.Length, hashLength));
        lineLength = lineEnd + 1;
        return true;
    }

    private static bool TryReadAuthor(ReadOnlySpan<byte> line, out GitSignature signature, out int lineLength)
    {
        return TryReadSignature(line, AuthorStart, out signature, out lineLength);
    }

    private static bool TryReadCommitter(ReadOnlySpan<byte> line, out GitSignature signature)
    {
        return TryReadSignature(line, CommitterStart, out signature, out _);
    }

    private static bool TryReadSignature(ReadOnlySpan<byte> line, byte[] signatureStart, out GitSignature signature, out int lineLength)
    {
        signature = default;
        lineLength = default;

        if (!line.Slice(0, signatureStart.Length).SequenceEqual(signatureStart))
        {
            return false;
        }

        line = line.Slice(signatureStart.Length);

        int emailStart = line.IndexOf((byte)'<');
        int emailEnd = line.IndexOf((byte)'>');
        int lineEnd = line.IndexOf((byte)'\n');

        ReadOnlySpan<byte> name = line.Slice(0, emailStart - 1);
        ReadOnlySpan<byte> email = line.Slice(emailStart + 1, emailEnd - emailStart - 1);
        ReadOnlySpan<byte> time = line.Slice(emailEnd + 2, lineEnd - emailEnd - 2);

        signature.Name = GitRepository.GetString(name);
        signature.Email = GitRepository.GetString(email);

        int offsetStart = time.IndexOf((byte)' ');
        long ticks = long.Parse(GitRepository.GetString(time.Slice(0, offsetStart)));
        signature.Date = DateTimeOffset.FromUnixTimeSeconds(ticks);

        lineLength = signatureStart.Length + lineEnd + 1;
        return true;
    }
}
