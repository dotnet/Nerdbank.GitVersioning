using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NerdBank.GitVersioning.Managed
{
    internal static class GitCommitReader
    {
        private static readonly byte[] TreeStart = GitRepository.Encoding.GetBytes("tree ");
        private static readonly byte[] ParentStart = GitRepository.Encoding.GetBytes("parent ");
        private static readonly byte[] AuthorStart = GitRepository.Encoding.GetBytes("author ");

        private const int TreeLineLength = 46;
        private const int ParentLineLength = 48;

        public static GitObjectId ReadTree(Span<byte> line)
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

        public static bool TryReadParent(Span<byte> line, out GitObjectId parent)
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

        public static bool TryReadAuthor(Span<byte> line, out GitSignature signature)
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
            var email = line.Slice(emailStart + 1, emailEnd - emailStart - 2);
            var time = line.Slice(emailEnd + 2, lineEnd - emailEnd - 2);

            signature.Name = GitRepository.Encoding.GetString(name);
            signature.Email = GitRepository.Encoding.GetString(email);

            var offsetStart = time.IndexOf((byte)'+');
            var ticks = long.Parse(GitRepository.Encoding.GetString(time.Slice(0, offsetStart - 1)));
            signature.Date = DateTimeOffset.FromUnixTimeSeconds(ticks);

            return true;
        }

        public static GitCommit Read(Stream stream, GitObjectId sha)
        {
            byte[] buffer = null;

            try
            {
                buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                Span<byte> span = buffer.AsSpan((int)stream.Length);
                stream.Read(span);

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

        public static GitCommit Read(Span<byte> commit, GitObjectId sha)
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

            if(!TryReadAuthor(buffer, out GitSignature signature))
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
    }
}
