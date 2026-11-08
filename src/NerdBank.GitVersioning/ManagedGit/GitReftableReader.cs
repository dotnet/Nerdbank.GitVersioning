// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers.Binary;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Reads reference snapshots from Git's immutable reftable stacks.
/// </summary>
internal static class GitReftableReader
{
    /// <summary>
    /// Applies a consistent stack snapshot to a reference collection, including deletions.
    /// </summary>
    /// <param name="directory">The directory containing the stack's tables.list.</param>
    /// <param name="references">The references to update, in oldest-to-newest table order.</param>
    internal static void ReadStack(string directory, Dictionary<string, (object Value, GitObjectId? Peeled)> references)
    {
        string listPath = Path.Combine(directory, "tables.list");
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string[] names;
            using (var listStream = new FileStream(listPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(listStream))
            {
                var tableNames = new List<string>();
                while (reader.ReadLine() is string name)
                {
                    tableNames.Add(name);
                }

                names = tableNames.ToArray();
            }

            var streams = new List<FileStream>();
            try
            {
                foreach (string name in names)
                {
                    if (name.Length == 0 || Path.IsPathRooted(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name == "." || name == "..")
                    {
                        throw new GitException($"Invalid reftable filename in '{listPath}'.");
                    }

                    streams.Add(new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                }
            }
            catch (FileNotFoundException) when (attempt < 4)
            {
                // Compaction can remove a table between reading tables.list and opening it.
                continue;
            }
            finally
            {
                if (streams.Count != names.Length)
                {
                    foreach (FileStream stream in streams)
                    {
                        stream.Dispose();
                    }
                }
            }

            try
            {
                foreach (FileStream stream in streams)
                {
                    ReadTable(stream, references);
                }

                return;
            }
            finally
            {
                foreach (FileStream stream in streams)
                {
                    stream.Dispose();
                }
            }
        }
    }

    private static void ReadTable(FileStream stream, Dictionary<string, (object Value, GitObjectId? Peeled)> references)
    {
        Span<byte> header = stackalloc byte[28];
        if (stream.Length < 24 + 68)
        {
            throw InvalidTable(stream);
        }

        stream.ReadAll(header.Slice(0, 24));
        if (!header.Slice(0, 4).SequenceEqual("REFT"u8))
        {
            throw InvalidTable(stream);
        }

        int headerLength;
        switch (header[4])
        {
            case 1:
                headerLength = 24;
                break;
            case 2:
                headerLength = 28;
                stream.ReadAll(header.Slice(24, 4));
                if (!header.Slice(24, 4).SequenceEqual("sha1"u8))
                {
                    throw new GitException($"Unsupported reftable object format in '{stream.Name}'. Only SHA-1 repositories are supported.");
                }

                break;
            default:
                throw new GitException($"Unsupported reftable version {header[4]} in '{stream.Name}'.");
        }

        int footerLength = headerLength + 44;
        long footerPosition = stream.Length - footerLength;
        if (footerPosition < headerLength)
        {
            throw InvalidTable(stream);
        }

        Span<byte> footer = stackalloc byte[72];
        footer = footer.Slice(0, footerLength);
        stream.Position = footerPosition;
        stream.ReadAll(footer);
        if (!footer.Slice(0, headerLength).SequenceEqual(header.Slice(0, headerLength))
            || ComputeCrc32(footer.Slice(0, footerLength - 4)) != BinaryPrimitives.ReadUInt32BigEndian(footer.Slice(footerLength - 4)))
        {
            throw InvalidTable(stream);
        }

        int blockSize = ReadUInt24(header.Slice(5, 3));
        ulong minimumUpdate = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
        ulong maximumUpdate = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(16, 8));
        if (minimumUpdate > maximumUpdate)
        {
            throw InvalidTable(stream);
        }

        long blockPosition = 0;
        int headerOffset = headerLength;
        Span<byte> blockHeader = stackalloc byte[4];
        while (blockPosition + headerOffset < footerPosition)
        {
            stream.Position = blockPosition + headerOffset;
            if (footerPosition - stream.Position < 4)
            {
                throw InvalidTable(stream);
            }

            stream.ReadAll(blockHeader);
            if (blockHeader[0] != (byte)'r')
            {
                if (blockHeader[0] == (byte)'i' || blockHeader[0] == (byte)'o' || blockHeader[0] == (byte)'g')
                {
                    // Object indexes and reflogs are not needed for reference resolution.
                    break;
                }

                throw InvalidTable(stream);
            }

            int blockLength = ReadUInt24(blockHeader.Slice(1));
            if (blockLength < headerOffset + 9 || blockPosition + blockLength > footerPosition
                || (blockSize != 0 && blockLength > blockSize))
            {
                throw InvalidTable(stream);
            }

            byte[] block = new byte[blockLength];
            blockHeader.CopyTo(block.AsSpan(headerOffset));
            stream.ReadAll(block.AsSpan(headerOffset + 4));
            ReadReferences(block, headerOffset + 4, maximumUpdate - minimumUpdate, references, stream);

            long nextPosition = blockPosition + blockLength;
            if (blockSize != 0 && nextPosition < footerPosition)
            {
                stream.Position = nextPosition;
                if (stream.ReadByte() == 0)
                {
                    nextPosition = blockPosition + blockSize;
                    if (nextPosition > footerPosition)
                    {
                        throw InvalidTable(stream);
                    }
                }
            }

            blockPosition = nextPosition;
            headerOffset = 0;
        }
    }

    private static void ReadReferences(
        ReadOnlySpan<byte> block,
        int position,
        ulong maximumDelta,
        Dictionary<string, (object Value, GitObjectId? Peeled)> references,
        FileStream stream)
    {
        int restartCount = BinaryPrimitives.ReadUInt16BigEndian(block.Slice(block.Length - 2));
        int recordsEnd = block.Length - 2 - (restartCount * 3);
        if (restartCount == 0 || recordsEnd < position)
        {
            throw InvalidTable(stream);
        }

        ReadOnlySpan<byte> records = block.Slice(0, recordsEnd);
        byte[] previousName = Array.Empty<byte>();
        int restartIndex = 0;
        while (position < recordsEnd)
        {
            int recordPosition = position;
            ulong prefixLength = ReadVarInt(records, ref position, stream);
            ulong suffixAndType = ReadVarInt(records, ref position, stream);
            ulong suffixLength = suffixAndType >> 3;
            if (prefixLength > (ulong)previousName.Length || suffixLength > (ulong)(recordsEnd - position)
                || prefixLength + suffixLength > int.MaxValue)
            {
                throw InvalidTable(stream);
            }

            if (restartIndex < restartCount)
            {
                int restartOffset = ReadUInt24(block.Slice(recordsEnd + (restartIndex * 3), 3));
                if (restartOffset < recordPosition || (restartIndex == 0 && restartOffset != recordPosition))
                {
                    throw InvalidTable(stream);
                }

                if (restartOffset == recordPosition)
                {
                    if (prefixLength != 0)
                    {
                        throw InvalidTable(stream);
                    }

                    restartIndex++;
                }
            }

            byte[] name = new byte[(int)(prefixLength + suffixLength)];
            previousName.AsSpan(0, (int)prefixLength).CopyTo(name);
            ReadBytes(records, ref position, (int)suffixLength, stream).CopyTo(name.AsSpan((int)prefixLength));
            if (name.Length == 0 || (previousName.Length != 0 && previousName.AsSpan().SequenceCompareTo(name) >= 0)
                || ReadVarInt(records, ref position, stream) > maximumDelta)
            {
                throw InvalidTable(stream);
            }

            string referenceName = GitRepository.GetString(name);
            object? value;
            GitObjectId? peeled = null;
            switch (suffixAndType & 7)
            {
                case 0:
                    value = null;
                    break;
                case 1:
                case 2:
                    value = GitObjectId.Parse(ReadBytes(records, ref position, 20, stream));
                    if ((suffixAndType & 7) == 2)
                    {
                        peeled = GitObjectId.Parse(ReadBytes(records, ref position, 20, stream));
                    }

                    break;
                case 3:
                    ulong targetLength = ReadVarInt(records, ref position, stream);
                    if (targetLength == 0 || targetLength > (ulong)(recordsEnd - position))
                    {
                        throw InvalidTable(stream);
                    }

                    value = GitRepository.GetString(ReadBytes(records, ref position, (int)targetLength, stream));
                    break;
                default:
                    throw InvalidTable(stream);
            }

            if (value is null)
            {
                references.Remove(referenceName);
            }
            else
            {
                references[referenceName] = (value, peeled);
            }

            previousName = name;
        }

        if (restartIndex != restartCount)
        {
            throw InvalidTable(stream);
        }
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> bytes, ref int position, int count, FileStream stream)
    {
        if (count < 0 || count > bytes.Length - position)
        {
            throw InvalidTable(stream);
        }

        ReadOnlySpan<byte> result = bytes.Slice(position, count);
        position += count;
        return result;
    }

    private static ulong ReadVarInt(ReadOnlySpan<byte> bytes, ref int position, FileStream stream)
    {
        byte current = ReadBytes(bytes, ref position, 1, stream)[0];
        ulong value = (ulong)(current & 0x7f);
        while ((current & 0x80) != 0)
        {
            if (value >= (ulong.MaxValue >> 7))
            {
                throw InvalidTable(stream);
            }

            current = ReadBytes(bytes, ref position, 1, stream)[0];
            value = ((value + 1) << 7) | ((uint)current & 0x7fU);
        }

        return value;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> bytes) => (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0);
            }
        }

        return ~crc;
    }

    private static GitException InvalidTable(FileStream stream) => new GitException($"Invalid reftable data in '{stream.Name}'.");
}
