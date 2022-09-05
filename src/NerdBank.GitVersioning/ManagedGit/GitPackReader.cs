// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers.Binary;
using System.Diagnostics;

namespace Nerdbank.GitVersioning.ManagedGit;

internal static class GitPackReader
{
    private static readonly byte[] Signature = GitRepository.Encoding.GetBytes("PACK");

    public static Stream GetObject(GitPack pack, Stream stream, long offset, string objectType, GitPackObjectType packObjectType)
    {
        if (pack is null)
        {
            throw new ArgumentNullException(nameof(pack));
        }

        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        // Read the signature
#if DEBUG
        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> buffer = stackalloc byte[12];
        stream.ReadAll(buffer);

        Debug.Assert(buffer.Slice(0, 4).SequenceEqual(Signature));

        int versionNumber = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(4, 4));
        Debug.Assert(versionNumber == 2);

        int numberOfObjects = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(8, 4));
#endif

        stream.Seek(offset, SeekOrigin.Begin);

        (GitPackObjectType type, long decompressedSize) = ReadObjectHeader(stream);

        if (type == GitPackObjectType.OBJ_OFS_DELTA)
        {
            long baseObjectRelativeOffset = ReadVariableLengthInteger(stream);
            long baseObjectOffset = offset - baseObjectRelativeOffset;

            var deltaStream = new ZLibStream(stream, decompressedSize);
            Stream? baseObjectStream = pack.GetObject(baseObjectOffset, objectType);

            return new GitPackDeltafiedStream(baseObjectStream, deltaStream);
        }
        else if (type == GitPackObjectType.OBJ_REF_DELTA)
        {
            Span<byte> baseObjectId = stackalloc byte[20];
            stream.ReadAll(baseObjectId);

            Stream baseObject = pack.GetObjectFromRepository(GitObjectId.Parse(baseObjectId), objectType)!;
            var seekableBaseObject = new GitPackMemoryCacheStream(baseObject);

            var deltaStream = new ZLibStream(stream, decompressedSize);

            return new GitPackDeltafiedStream(seekableBaseObject, deltaStream);
        }

        // Tips for handling deltas: https://github.com/choffmeister/gitnet/blob/4d907623d5ce2d79a8875aee82e718c12a8aad0b/src/GitNet/GitPack.cs
        if (type != packObjectType)
        {
            throw new GitException($"An object of type {objectType} could not be located at offset {offset}.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
        }

        return new ZLibStream(stream, decompressedSize);
    }

    private static (GitPackObjectType ObjectType, long Length) ReadObjectHeader(Stream stream)
    {
        Span<byte> value = stackalloc byte[1];
        stream.Read(value);

        var type = (GitPackObjectType)((value[0] & 0b0111_0000) >> 4);
        long length = value[0] & 0b_1111;

        if ((value[0] & 0b1000_0000) == 0)
        {
            return (type, length);
        }

        int shift = 4;

        do
        {
            stream.Read(value);
            length = length | ((value[0] & 0b0111_1111L) << shift);
            shift += 7;
        }
        while ((value[0] & 0b1000_0000) != 0);

        return (type, length);
    }

    private static long ReadVariableLengthInteger(Stream stream)
    {
        long offset = -1;
        int b;

        do
        {
            offset++;
            b = stream.ReadByte();
            offset = (offset << 7) + (b & 127);
        }
        while ((b & 128) != 0);

        return offset;
    }
}
