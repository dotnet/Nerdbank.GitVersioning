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
    private const int TreeLineLength = 46;
    private const int ParentLineLength = 48;

    private static readonly byte[] TreeStart = GitRepository.Encoding.GetBytes("tree ");
    private static readonly byte[] ParentStart = GitRepository.Encoding.GetBytes("parent ");
    private static readonly byte[] AuthorStart = GitRepository.Encoding.GetBytes("author ");

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
    /// A value indicating whether to populate the <see cref="GitCommit.Author"/> field.
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
    /// A value indicating whether to populate the <see cref="GitCommit.Author"/> field.
    /// </param>
    /// <returns>
    /// The <see cref="GitCommit"/>.
    /// </returns>
    public static GitCommit Read(ReadOnlySpan<byte> commit, GitObjectId sha, bool readAuthor = false)
    {
        ReadOnlySpan<byte> buffer = commit;

        GitObjectId tree = ReadTree(buffer.Slice(0, TreeLineLength));

        buffer = buffer.Slice(TreeLineLength);

        GitObjectId? firstParent = null, secondParent = null;
        List<GitObjectId>? additionalParents = null;
        var parents = new List<GitObjectId>();
        while (TryReadParent(buffer, out GitObjectId parent))
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

            buffer = buffer.Slice(ParentLineLength);
        }

        GitSignature signature = default;

        if (readAuthor && !TryReadAuthor(buffer, out signature))
        {
            throw new GitException();
        }

        return new GitCommit()
        {
            Sha = sha,
            FirstParent = firstParent,
            SecondParent = secondParent,
            AdditionalParents = additionalParents,
            Tree = tree,
            Author = readAuthor ? signature : null,
        };
    }

    private static GitObjectId ReadTree(ReadOnlySpan<byte> line)
    {
        // Format: tree d8329fc1cc938780ffdd9f94e0d364e0ea74f579\n
        // 46 bytes:
        //  tree: 4 bytes
        //  space: 1 byte
        //  hash: 40 bytes
        //  \n: 1 byte
        Debug.Assert(line.Slice(0, TreeStart.Length).SequenceEqual(TreeStart));
        Debug.Assert(line[TreeLineLength - 1] == (byte)'\n');

        return GitObjectId.ParseHex(line.Slice(TreeStart.Length, 40));
    }

    private static bool TryReadParent(ReadOnlySpan<byte> line, out GitObjectId parent)
    {
        // Format: "parent ef079ebcca375f6fd54aa0cb9f35e3ecc2bb66e7\n"
        parent = GitObjectId.Empty;

        if (!line.Slice(0, ParentStart.Length).SequenceEqual(ParentStart))
        {
            return false;
        }

        if (line[ParentLineLength - 1] != (byte)'\n')
        {
            return false;
        }

        parent = GitObjectId.ParseHex(line.Slice(ParentStart.Length, 40));
        return true;
    }

    private static bool TryReadAuthor(ReadOnlySpan<byte> line, out GitSignature signature)
    {
        signature = default;

        if (!line.Slice(0, AuthorStart.Length).SequenceEqual(AuthorStart))
        {
            return false;
        }

        line = line.Slice(AuthorStart.Length);

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

        return true;
    }
}
