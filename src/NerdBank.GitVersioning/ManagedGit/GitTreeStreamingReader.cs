// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Reads git tree objects.
/// </summary>
public class GitTreeStreamingReader
{
    /// <summary>
    /// Finds a specific node in a git tree.
    /// </summary>
    /// <param name="stream">
    /// A <see cref="Stream"/> which represents the git tree.
    /// </param>
    /// <param name="name">
    /// The name of the node to find, in it UTF-8 representation.
    /// </param>
    /// <returns>
    /// The <see cref="GitObjectId"/> of the requested node.
    /// </returns>
    public static GitObjectId FindNode(Stream stream, ReadOnlySpan<byte> name)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
        var contents = new Span<byte>(buffer, 0, (int)stream.Length);

        stream.ReadAll(contents);

        GitObjectId value = GitObjectId.Empty;

        while (contents.Length > 0)
        {
            // Format: [mode] [file/ folder name]\0[SHA - 1 of referencing blob or tree]
            // Mode is either 6-bytes long (directory) or 7-bytes long (file).
            // If the entry is a file, the first byte is '1'
            int fileNameEnds = contents.IndexOf((byte)0);
            bool isFile = contents[0] == (byte)'1';
            int modeLength = isFile ? 7 : 6;

            Span<byte> currentName = contents.Slice(modeLength, fileNameEnds - modeLength);

            if (currentName.SequenceEqual(name))
            {
                value = GitObjectId.Parse(contents.Slice(fileNameEnds + 1, 20));
                break;
            }
            else
            {
                contents = contents.Slice(fileNameEnds + 1 + 20);
            }
        }

        ArrayPool<byte>.Shared.Return(buffer);

        return value;
    }
}
