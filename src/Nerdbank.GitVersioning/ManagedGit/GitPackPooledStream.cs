// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// A pooled <see cref="Stream"/>, which wraps around a
/// <see cref="FileStream"/> which will be returned to a pool
/// instead of actually being closed when <see cref="Dispose(bool)"/> is called.
/// </summary>
public class GitPackPooledStream : Stream
{
    private readonly Stream stream;
    private readonly Queue<GitPackPooledStream> pool;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPackPooledStream"/> class.
    /// </summary>
    /// <param name="stream">
    /// The <see cref="FileStream"/> which is being pooled.
    /// </param>
    /// <param name="pool">
    /// A <see cref="Queue{T}"/> to which the stream will be returned.
    /// </param>
    public GitPackPooledStream(Stream stream, Queue<GitPackPooledStream> pool)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>
    /// Gets the underlying <see cref="FileStream"/> for this <see cref="GitPackPooledStream"/>.
    /// </summary>
    public Stream BaseStream => this.stream;

    /// <inheritdoc/>
    public override bool CanRead => this.stream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => this.stream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => this.stream.CanWrite;

    /// <inheritdoc/>
    public override long Length => this.stream.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => this.stream.Position;
        set => this.stream.Position = value;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        this.stream.Flush();
    }

#if !NETSTANDARD2_0
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        return this.stream.Read(buffer);
    }
#endif

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return this.stream.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return this.stream.Seek(offset, origin);
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        this.stream.SetLength(value);
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        this.pool.Enqueue(this);
        Debug.WriteLine("Returning stream to pool");
    }
}
