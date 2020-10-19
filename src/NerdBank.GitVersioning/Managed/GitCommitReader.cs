using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Reads a <see cref="GitCommit"/> object.
    /// </summary>
    public static class GitCommitReader
    {
        private static readonly byte[] TreeStart = GitRepository.Encoding.GetBytes("tree ");
        private static readonly byte[] ParentStart = GitRepository.Encoding.GetBytes("parent ");
        private static readonly byte[] AuthorStart = GitRepository.Encoding.GetBytes("author ");

        private const int TreeLineLength = 46;
        private const int ParentLineLength = 48;

        /// <summary>
        /// Reads a <see cref="GitCommit"/> object from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">
        /// A <see cref="Stream"/> which contains the <see cref="GitCommit"/> in its text representation.
        /// </param>
        /// <param name="sha">
        /// The <see cref="GitObjectId"/> of the commit.
        /// </param>
        /// <returns>
        /// The <see cref="GitCommit"/>.
        /// </returns>
        public static GitCommit Read(Stream stream, GitObjectId sha)
        {
            byte[] buffer = null;

            try
            {
                buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                Span<byte> span = buffer.AsSpan(0, (int)stream.Length);
                stream.ReadAll(span);

                return Read(span, sha);
            }
            finally
            {
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
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
        /// <returns>
        /// The <see cref="GitCommit"/>.
        /// </returns>
        public static GitCommit Read(ReadOnlySpan<byte> commit, GitObjectId sha)
        {
            var buffer = commit;

            var tree = ReadTree(buffer.Slice(0, TreeLineLength));

            buffer = buffer.Slice(TreeLineLength);

            List<GitObjectId> parents = new List<GitObjectId>();
            while (TryReadParent(buffer, out GitObjectId parent))
            {
                parents.Add(parent);
                buffer = buffer.Slice(ParentLineLength);
            }

            if (!TryReadAuthor(buffer, out GitSignature signature))
            {
                throw new GitException();
            }

            return new GitCommit()
            {
                Sha = sha,
                Parents = parents,
                Tree = tree,
                Author = signature,
            };
        }

        private static GitObjectId ReadTree(ReadOnlySpan<byte> line)
        {
            // Format: tree d8329fc1cc938780ffdd9f94e0d364e0ea74f579\n
            // 47 bytes: 
            //  tree: 5 bytes
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
            var lineEnd = line.IndexOf((byte)'\n');

            var name = line.Slice(0, emailStart - 1);
            var email = line.Slice(emailStart + 1, emailEnd - emailStart - 1);
            var time = line.Slice(emailEnd + 2, lineEnd - emailEnd - 2);

            signature.Name = GitRepository.Encoding.GetString(name);
            signature.Email = GitRepository.Encoding.GetString(email);

            var offsetStart = time.IndexOf((byte)' ');
            var ticks = long.Parse(GitRepository.Encoding.GetString(time.Slice(0, offsetStart)));
            signature.Date = DateTimeOffset.FromUnixTimeSeconds(ticks);

            return true;
        }
    }
}
