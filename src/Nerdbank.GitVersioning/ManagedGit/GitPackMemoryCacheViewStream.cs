// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.GitVersioning.ManagedGit;

internal class GitPackMemoryCacheViewStream : Stream
{
    private readonly GitPackMemoryCacheStream baseStream;

    private long position;

    public GitPackMemoryCacheViewStream(GitPackMemoryCacheStream baseStream)
    {
        this.baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => true;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => this.baseStream.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => this.position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotImplementedException();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return this.Read(buffer.AsSpan(offset, count));
    }

#if NETSTANDARD2_0
    public int Read(Span<byte> buffer)
#else
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
#endif
    {
        int read = 0;

        lock (this.baseStream)
        {
            if (this.baseStream.Position != this.position)
            {
                this.baseStream.Seek(this.position, SeekOrigin.Begin);
            }

            read = this.baseStream.Read(buffer);
        }

        this.position += read;
        return read;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (origin != SeekOrigin.Begin)
        {
            throw new NotSupportedException();
        }

        this.position = Math.Min(offset, this.Length);
        return this.position;
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
