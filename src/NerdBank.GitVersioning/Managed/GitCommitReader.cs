using System;
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

        private const int TreeLineLength = 46;
        private const int ParentLineLength = 48;

        public static GitObjectId ReadTree(Stream stream)
        {
            // Format: tree d8329fc1cc938780ffdd9f94e0d364e0ea74f579\n
            // 47 bytes: 
            //  tree: 5 bytes
            //  space: 1 byte
            //  hash: 40 bytes
            //  \n: 1 byte

            Span<byte> line = stackalloc byte[TreeLineLength];
            stream.ReadAll(line);

            Debug.Assert(line.Slice(0, TreeStart.Length).SequenceEqual(TreeStart));
            Debug.Assert(line[TreeLineLength - 1] == (byte)'\n');

            return GitObjectId.ParseHex(line.Slice(TreeStart.Length, 40));
        }

        public static bool TryReadParent(Stream stream, out GitObjectId parent)
        {
            // Format: "parent ef079ebcca375f6fd54aa0cb9f35e3ecc2bb66e7\n"
            parent = GitObjectId.Empty;

            Span<byte> line = stackalloc byte[ParentLineLength];
            if (stream.Read(line) != ParentLineLength)
            {
                return false;
            }

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

        public static GitCommit Read(Stream stream, GitObjectId sha)
        {
            var tree = ReadTree(stream);

            List<GitObjectId> parents = new List<GitObjectId>();
            while (TryReadParent(stream, out GitObjectId parent))
            {
                parents.Add(parent);
            }

            return new GitCommit()
            {
                Sha = sha,
                Parents = parents,
                Tree = tree,
            };
        }
    }
}
