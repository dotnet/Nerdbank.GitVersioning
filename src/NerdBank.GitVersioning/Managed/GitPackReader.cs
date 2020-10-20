using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    internal static class GitPackReader
    {
        private static readonly byte[] Signature = GitRepository.Encoding.GetBytes("PACK");

        public static Stream GetObject(GitPack pack, Stream stream, int offset, string objectType, GitPackObjectType packObjectType)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            // Read the signature
#if DEBUG
            stream.Seek(0, SeekOrigin.Begin);
            Span<byte> buffer = stackalloc byte[12];
            stream.ReadAll(buffer);

            Debug.Assert(buffer.Slice(0, 4).SequenceEqual(Signature));

            var versionNumber = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(4, 4));
            Debug.Assert(versionNumber == 2);

            var numberOfObjects = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(8, 4));
#endif

            stream.Seek(offset, SeekOrigin.Begin);

            var (type, decompressedSize) = ReadObjectHeader(stream);

            if (type == GitPackObjectType.OBJ_OFS_DELTA)
            {
                var baseObjectRelativeOffset = ReadVariableLengthInteger(stream);
                var baseObjectOffset = (int)(offset - baseObjectRelativeOffset);

                var deltaStream = new ZLibStream(stream, decompressedSize);

                int baseObjectlength = ReadMbsInt(deltaStream);
                int targetLength = ReadMbsInt(deltaStream);

                var baseObjectStream = pack.GetObject(baseObjectOffset, objectType);

                return new GitPackDeltafiedStream(baseObjectStream, deltaStream, targetLength);
            }
            else if (type == GitPackObjectType.OBJ_REF_DELTA)
            {
                Span<byte> baseObjectId = stackalloc byte[20];
                stream.ReadAll(baseObjectId);

                Stream baseObject = pack.GetObjectFromRepository(GitObjectId.Parse(baseObjectId), objectType, seekable: true);

                var deltaStream = new ZLibStream(stream, decompressedSize);

                int baseObjectlength = ReadMbsInt(deltaStream);
                int targetLength = ReadMbsInt(deltaStream);

                return new GitPackDeltafiedStream(baseObject, deltaStream, targetLength);
            }

            // Tips for handling deltas: https://github.com/choffmeister/gitnet/blob/4d907623d5ce2d79a8875aee82e718c12a8aad0b/src/GitNet/GitPack.cs
            if (type != packObjectType)
            {
                throw new GitException($"An object of type {objectType} could not be located at offset {offset}.");
            }

            return new ZLibStream(stream, decompressedSize);
        }

        private static (GitPackObjectType, int) ReadObjectHeader(Stream stream)
        {
            Span<byte> value = stackalloc byte[1];
            stream.Read(value);

            var type = (GitPackObjectType)((value[0] & 0b0111_0000) >> 4);
            int length = value[0] & 0b_1111;

            if ((value[0] & 0b1000_0000) == 0)
            {
                return (type, length);
            }

            int shift = 4;

            do
            {
                stream.Read(value);
                length = length | ((value[0] & 0b0111_1111) << shift);
                shift += 7;
            } while ((value[0] & 0b1000_0000) != 0);

            return (type, length);
        }

        private static int ReadVariableLengthInteger(Stream stream)
        {
            int offset = -1;
            int b;

            do
            {
                offset++;
                b = stream.ReadByte();
                offset = (offset << 7) + (b & 127);
            }
            while ((b & (byte)128) != 0);

            return offset;
        }

        private static int ReadMbsInt(Stream stream, int initialValue = 0, int initialBit = 0)
        {
            int value = initialValue;
            int currentBit = initialBit;
            int read;

            while (true)
            {
                read = stream.ReadByte();
                value |= (read & 0b_0111_1111) << currentBit;
                currentBit += 7;

                if (read < 128)
                {
                    break;
                }
            }

            return value;
        }
    }
}
