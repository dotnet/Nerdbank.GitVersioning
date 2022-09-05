// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// A <see cref="GitPackIndexReader"/> which uses a memory-mapped file to read from the index.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format"/>
public unsafe class GitPackIndexMappedReader : GitPackIndexReader
{
    private readonly MemoryMappedFile file;
    private readonly MemoryMappedViewAccessor accessor;

    // The fanout table consists of
    // 256 4-byte network byte order integers.
    // The N-th entry of this table records the number of objects in the corresponding pack,
    // the first byte of whose object name is less than or equal to N.
    private readonly int[] fanoutTable = new int[257];

    private readonly byte* ptr;
    private bool initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPackIndexMappedReader"/> class.
    /// </summary>
    /// <param name="stream">
    /// A <see cref="FileStream"/> which points to the index file.
    /// </param>
    public GitPackIndexMappedReader(FileStream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        this.file = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        this.accessor = this.file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        this.accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref this.ptr);
    }

    private ReadOnlySpan<byte> Value
    {
        get
        {
            return new ReadOnlySpan<byte>(this.ptr, (int)this.accessor.Capacity);
        }
    }

    /// <inheritdoc/>
    public override (long? Offset, GitObjectId? ObjectId) GetOffset(Span<byte> objectName, bool endsWithHalfByte = false)
    {
        this.Initialize();

        int packStart = this.fanoutTable[objectName[0]];
        int packEnd = this.fanoutTable[objectName[0] + 1];
        int objectCount = this.fanoutTable[256];

        // The fanout table is followed by a table of sorted 20-byte SHA-1 object names.
        // These are packed together without offset values to reduce the cache footprint of the binary search for a specific object name.

        // The object names start at: 4 (header) + 4 (version) + 256 * 4 (fanout table) + 20 * (packStart)
        // and end at                 4 (header) + 4 (version) + 256 * 4 (fanout table) + 20 * (packEnd)
        int i = 0;
        int order = 0;

        int tableSize = 20 * (packEnd - packStart + 1);
        ReadOnlySpan<byte> table = this.Value.Slice(4 + 4 + (256 * 4) + (20 * packStart), tableSize);

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
                // Copy out the value to be checked so we can zero out the last four bits,
                // so that it matches the last 4 bits of the objectName that isn't supposed to be compared.
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

        // Get the offset value. It's located at:
        // 4 (header) + 4 (version) + 256 * 4 (fanout table) + 20 * objectCount (SHA1 object name table) + 4 * objectCount (CRC32) + 4 * i (offset values)
        int offsetTableStart = 4 + 4 + (256 * 4) + (20 * objectCount) + (4 * objectCount);
        ReadOnlySpan<byte> offsetBuffer = this.Value.Slice(offsetTableStart + (4 * (i + originalPackStart)), 4);
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(offsetBuffer);

        if (offsetBuffer[0] < 128)
        {
            return (offset, GitObjectId.Parse(table.Slice(20 * i, 20)));
        }
        else
        {
            // If the first bit of the offset address is set, the offset is stored as a 64-bit value in the table of 8-byte offset entries,
            // which follows the table of 4-byte offset entries: "large offsets are encoded as an index into the next table with the msbit set."
            offset = offset & 0x7FFFFFFF;

            offsetBuffer = this.Value.Slice(offsetTableStart + (4 * objectCount) + (8 * (int)offset), 8);
            long offset64 = BinaryPrimitives.ReadInt64BigEndian(offsetBuffer);
            return (offset64, GitObjectId.Parse(table.Slice(20 * i, 20)));
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        this.accessor.Dispose();
        this.file.Dispose();
    }

    private void Initialize()
    {
        if (!this.initialized)
        {
            ReadOnlySpan<byte> value = this.Value;

            ReadOnlySpan<byte> header = value.Slice(0, 4);
            int version = BinaryPrimitives.ReadInt32BigEndian(value.Slice(4, 4));
            Debug.Assert(header.SequenceEqual(Header));
            Debug.Assert(version == 2);

            for (int i = 1; i <= 256; i++)
            {
                this.fanoutTable[i] = BinaryPrimitives.ReadInt32BigEndian(value.Slice(4 + (4 * i), 4));
            }

            this.initialized = true;
        }
    }
}
