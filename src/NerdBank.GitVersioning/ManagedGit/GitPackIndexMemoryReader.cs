// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers.Binary;
using System.Diagnostics;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// A <see cref="GitPackIndexReader"/> that reads index data from memory instead of a memory-mapped file.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format"/>
public class GitPackIndexMemoryReader : GitPackIndexReader
{
    private readonly byte[] data;

    // The fanout table consists of
    // 256 4-byte network byte order integers.
    // The N-th entry of this table records the number of objects in the corresponding pack,
    // the first byte of whose object name is less than or equal to N.
    private readonly int[] fanoutTable = new int[257];

    private bool initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPackIndexMemoryReader"/> class.
    /// </summary>
    /// <param name="stream">
    /// A <see cref="FileStream"/> which points to the index file.
    /// </param>
    public GitPackIndexMemoryReader(FileStream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        stream.Position = 0;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        this.data = memory.ToArray();
    }

    /// <inheritdoc/>
    public override (long? Offset, GitObjectId? ObjectId) GetOffset(Span<byte> objectName, bool endsWithHalfByte = false)
    {
        this.Initialize();

        int packStart = this.fanoutTable[objectName[0]];
        int packEnd = this.fanoutTable[objectName[0] + 1];
        int objectCount = this.fanoutTable[256];

        int i = 0;
        int order = 0;

        int tableSize = 20 * (packEnd - packStart + 1);
        ReadOnlySpan<byte> table = this.GetSpan(4 + 4 + (256 * 4) + (20 * packStart), tableSize);

        int originalPackStart = packStart;

        packEnd -= originalPackStart;
        packStart = 0;

        Span<byte> buffer = stackalloc byte[20];
        while (packStart <= packEnd)
        {
            i = (packStart + packEnd) / 2;

            ReadOnlySpan<byte> comparand = table.Slice(20 * i, objectName.Length);
            if (endsWithHalfByte)
            {
                comparand.CopyTo(buffer);
                buffer[objectName.Length - 1] &= 0xf0;
                order = buffer.Slice(0, objectName.Length).SequenceCompareTo(objectName);
            }
            else
            {
                order = comparand.SequenceCompareTo(objectName);
            }

            if (order < 0)
            {
                packStart = i + 1;
            }
            else if (order > 0)
            {
                packEnd = i - 1;
            }
            else
            {
                break;
            }
        }

        if (order != 0)
        {
            return (null, null);
        }

        int offsetTableStart = 4 + 4 + (256 * 4) + (20 * objectCount) + (4 * objectCount);
        ReadOnlySpan<byte> offsetBuffer = this.GetSpan(offsetTableStart + (4 * (i + originalPackStart)), 4);
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(offsetBuffer);

        if (offsetBuffer[0] < 128)
        {
            return (offset, GitObjectId.Parse(table.Slice(20 * i, 20)));
        }
        else
        {
            offset = offset & 0x7FFFFFFF;

            offsetBuffer = this.GetSpan(offsetTableStart + (4 * objectCount) + (8 * (int)offset), 8);
            long offset64 = BinaryPrimitives.ReadInt64BigEndian(offsetBuffer);
            return (offset64, GitObjectId.Parse(table.Slice(20 * i, 20)));
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
    }

    private ReadOnlySpan<byte> GetSpan(int offset, int length) => new ReadOnlySpan<byte>(this.data, offset, length);

    private void Initialize()
    {
        if (!this.initialized)
        {
            const int fanoutTableLength = 256;
            ReadOnlySpan<byte> value = this.GetSpan(0, 4 + (4 * fanoutTableLength) + 4);

            ReadOnlySpan<byte> header = value.Slice(0, 4);
            int version = BinaryPrimitives.ReadInt32BigEndian(value.Slice(4, 4));
            Debug.Assert(header.SequenceEqual(Header));
            Debug.Assert(version == 2);

            for (int i = 1; i <= fanoutTableLength; i++)
            {
                this.fanoutTable[i] = BinaryPrimitives.ReadInt32BigEndian(value.Slice(4 + (4 * i), 4));
            }

            this.initialized = true;
        }
    }
}
