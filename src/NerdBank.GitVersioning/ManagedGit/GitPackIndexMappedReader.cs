// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

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

    // The fanout table consists of
    // 256 4-byte network byte order integers.
    // The N-th entry of this table records the number of objects in the corresponding pack,
    // the first byte of whose object name is less than or equal to N.
    private readonly int[] fanoutTable = new int[257];
    private readonly ulong fileLength;

    private bool initialized;
    private MemoryMappedViewAccessor? accessor;
    private ulong accessorOffset;
    private ulong accessorSize;
    private byte* accessorPtr;

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

        this.fileLength = (ulong)stream.Length;
        this.file = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
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
        ReadOnlySpan<byte> table = this.GetSpan((ulong)(4 + 4 + (256 * 4) + (20 * packStart)), tableSize);

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
        ReadOnlySpan<byte> offsetBuffer = this.GetSpan((ulong)(offsetTableStart + (4 * (i + originalPackStart))), 4);
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

            offsetBuffer = this.GetSpan((ulong)(offsetTableStart + (4 * objectCount) + (8 * (int)offset)), 8);
            long offset64 = BinaryPrimitives.ReadInt64BigEndian(offsetBuffer);
            return (offset64, GitObjectId.Parse(table.Slice(20 * i, 20)));
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (this.accessorPtr is not null && this.accessor is not null)
        {
            this.accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            this.accessorPtr = null;
        }

        this.accessor?.Dispose();
        this.accessor = null;
        this.file.Dispose();
    }

    private unsafe ReadOnlySpan<byte> GetSpan(ulong offset, int length)
    {
        checked
        {
            // If the request is for a window that we have not currently mapped, throw away what we have.
            if (this.accessor is not null && (this.accessorOffset > offset || this.accessorOffset + this.accessorSize < offset + (ulong)length))
            {
                if (this.accessorPtr is not null)
                {
                    this.accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                    this.accessorPtr = null;
                }

                this.accessor.Dispose();
                this.accessor = null;
            }

            if (this.accessor is null)
            {
                const int minimumLength = 10 * 1024 * 1024;
                uint windowSize = (uint)Math.Min((ulong)Math.Max(minimumLength, length), this.fileLength);

                // Push window 'to the left' if our preferred minimum size doesn't fit when we start at the offset requested.
                ulong actualOffset = offset + windowSize > this.fileLength ? this.fileLength - windowSize : offset;

                this.accessor = this.file.CreateViewAccessor((long)actualOffset, windowSize, MemoryMappedFileAccess.Read);

                // Record the *actual* offset into the file that the pointer to native memory points at.
                // This may be earlier in the file than we requested, and if so, go ahead and take advantage of that.
                this.accessorOffset = actualOffset - (ulong)this.accessor.PointerOffset;

                // Also record the *actual* length of the mapped memory, again so we can take full advantage before reallocating the view.
                this.accessorSize = this.accessor.SafeMemoryMappedViewHandle.ByteLength;
            }

            Debug.Assert(offset >= (ulong)this.accessor.PointerOffset);
            byte* ptr = this.accessorPtr;
            if (ptr is null)
            {
                this.accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref this.accessorPtr);
                ptr = this.accessorPtr;
            }

            ptr += offset - this.accessorOffset;
            return new ReadOnlySpan<byte>(ptr, length);
        }
    }

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
