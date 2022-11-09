// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers;
using System.IO.Compression;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// A <see cref="Stream"/> which reads zlib-compressed data.
/// </summary>
/// <remarks>
/// <para>
///   This stream parses but ignores the two-byte zlib header at the start of the compressed
///   stream.
/// </para>
/// <para>
///   This stream keeps track of the current position and, if provided via the constructor,
///   the length.
/// </para>
/// <para>
///   This class wraps a <see cref="DeflateStream"/> rather than inheriting from it, because
///   <see cref="DeflateStream"/> detects whether <c>Read(Span{byte})</c> is being overriden
///   and behaves differently when it is.
/// </para>
/// <para>
///   .NET 5.0 ships with a built-in ZLibStream; which may render (parts of) this implementation
///   obsolete.
/// </para>
/// </remarks>
/// <seealso href="https://github.com/dotnet/runtime/blob/6072e4d3a7a2a1493f514cdf4be75a3d56580e84/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs#L236-L249"/>
public class ZLibStream : Stream
{
    private readonly DeflateStream stream;
    private long length;
    private long position;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZLibStream"/>  class.
    /// </summary>
    /// <param name="stream">
    /// The <see cref="Stream"/> from which to read data.
    /// </param>
    /// <param name="length">
    /// The size of the uncompressed data.
    /// </param>
    public ZLibStream(Stream stream, long length = -1)
    {
        this.stream = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: false);
        this.length = length;

        Span<byte> zlibHeader = stackalloc byte[2];
        stream.ReadAll(zlibHeader);

        if (zlibHeader[0] != 0x78 || (zlibHeader[1] != 0x01 && zlibHeader[1] != 0x9C && zlibHeader[1] != 0x5E && zlibHeader[1] != 0xDA))
        {
            throw new GitException($"Invalid zlib header {zlibHeader[0]:X2} {zlibHeader[1]:X2}");
        }
    }

    /// <summary>
    /// Gets the <see cref="Stream"/> from which the data is being read.
    /// </summary>
    public Stream BaseStream => this.stream;

    /// <inheritdoc/>
    public override long Position
    {
        get => this.position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override long Length => this.length;

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => true;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override int Read(byte[] array, int offset, int count)
    {
        int read = this.stream.Read(array, offset, count);
        this.position += read;
        return read;
    }

#if !NETSTANDARD2_0
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        int read = this.stream.Read(buffer);
        this.position += read;
        return read;
    }
#endif

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] array, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await this.stream.ReadAsync(array, offset, count, cancellationToken);
        this.position += read;
        return read;
    }

#if !NETSTANDARD2_0
    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await this.stream.ReadAsync(buffer, cancellationToken);
        this.position += read;
        return read;
    }
#endif

    /// <inheritdoc/>
    public override int ReadByte()
    {
        int value = this.stream.ReadByte();

        if (value != -1)
        {
            this.position += 1;
        }

        return value;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (origin == SeekOrigin.Begin && offset == this.position)
        {
            return this.position;
        }

        if (origin == SeekOrigin.Current && offset == 0)
        {
            return this.position;
        }

        if (origin == SeekOrigin.Begin && offset > this.position)
        {
            // We may be able to optimize this by skipping over the compressed data
            this.ReadExactly(checked((int)(offset - this.position)));
            return this.position;
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        this.stream.Dispose();
    }

    /// <summary>
    /// Initializes the length and position properties.
    /// </summary>
    /// <param name="length">
    /// The length of this <see cref="ZLibStream"/> class.
    /// </param>
    protected void Initialize(long length)
    {
        this.position = 0;
        this.length = length;
    }
}
