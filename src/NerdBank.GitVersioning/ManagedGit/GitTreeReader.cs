#nullable enable

using System;
using System.Buffers;
using System.IO;

namespace Nerdbank.GitVersioning.ManagedGit
{
    internal static class GitTreeReader
    {
        public static GitTree Read(Stream stream, GitObjectId objectId)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
#if DEBUG
            Array.Clear(buffer, 0, buffer.Length);
#endif

            GitTree value = new GitTree()
            {
                Sha = objectId,
            };

            try
            {
                Span<byte> contents = buffer.AsSpan(0, (int)stream.Length);
                stream.ReadAll(contents);

                while (contents.Length > 0)
                {
                    // Format: [mode] [file/ folder name]\0[SHA - 1 of referencing blob or tree]
                    // Mode is either 6-bytes long (directory) or 7-bytes long (file).
                    // If the entry is a file, the first byte is '1'
                    var fileNameEnds = contents.IndexOf((byte)0);
                    bool isFile = contents[0] == (byte)'1';
                    var modeLength = isFile ? 7 : 6;

                    var currentName = contents.Slice(modeLength, fileNameEnds - modeLength);
                    var currentObjectId = GitObjectId.Parse(contents.Slice(fileNameEnds + 1, 20));

                    var name = GitRepository.GetString(currentName);

                    value.Children.Add(
                        name,
                        new GitTreeEntry(name, isFile, currentObjectId));

                    contents = contents.Slice(fileNameEnds + 1 + 20);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return value;
        }
    }
}
