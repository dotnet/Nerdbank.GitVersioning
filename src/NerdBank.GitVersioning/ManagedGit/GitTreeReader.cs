// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers;

namespace Nerdbank.GitVersioning.ManagedGit;

internal static class GitTreeReader
{
    public static GitTree Read(Stream stream, GitObjectId objectId)
        => Read(stream, objectId, objectId.ByteLength == GitObjectId.Sha256ByteLength ? GitObjectFormat.Sha256 : GitObjectFormat.Sha1);

    public static GitTree Read(Stream stream, GitObjectId objectId, GitObjectFormat objectFormat)
    {
        int hashByteLength = GitObjectId.GetByteLength(objectFormat);
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
#if DEBUG
        Array.Clear(buffer, 0, buffer.Length);
#endif

        var value = new GitTree()
        {
            Sha = objectId,
        };

        try
        {
            Span<byte> contents = buffer.AsSpan(0, (int)stream.Length);
            stream.ReadAll(contents);

            while (contents.Length > 0)
            {
                // Format: [mode] [file/ folder name]\0[hash of referencing blob or tree]
                // Mode is either 6-bytes long (directory) or 7-bytes long (file).
                // If the entry is a file, the first byte is '1'
                int fileNameEnds = contents.IndexOf((byte)0);
                bool isFile = contents[0] == (byte)'1';
                int modeLength = isFile ? 7 : 6;
                int mode = 0;
                for (int i = 0; i < modeLength - 1; i++)
                {
                    mode = (mode * 8) + (contents[i] - '0');
                }

                Span<byte> currentName = contents.Slice(modeLength, fileNameEnds - modeLength);
                var currentObjectId = GitObjectId.Parse(contents.Slice(fileNameEnds + 1, hashByteLength));

                string? name = GitRepository.GetString(currentName);

                value.Children.Add(
                    name,
                    new GitTreeEntry(name, isFile, currentObjectId, mode));

                contents = contents.Slice(fileNameEnds + 1 + hashByteLength);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return value;
    }
}
