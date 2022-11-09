// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Buffers;
using System.Diagnostics;

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Reads data from a deltafied object.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format#_deltified_representation"/>
public class GitPackDeltafiedStream : Stream
{
    private readonly long length;

    private readonly Stream baseStream;
    private readonly Stream deltaStream;

    private long position;
    private DeltaInstruction? current;
    private int offset;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPackDeltafiedStream"/> class.
    /// </summary>
    /// <param name="baseStream">
    /// The base stream to which the deltas are applied.
    /// </param>
    /// <param name="deltaStream">
    /// A <see cref="Stream"/> which contains a sequence of <see cref="DeltaInstruction"/>s.
    /// </param>
    public GitPackDeltafiedStream(Stream baseStream, Stream deltaStream)
    {
        this.baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        this.deltaStream = deltaStream ?? throw new ArgumentNullException(nameof(deltaStream));

        int baseObjectlength = deltaStream.ReadMbsInt();
        this.length = deltaStream.ReadMbsInt();
    }

    /// <summary>
    /// Gets the base stream to which the deltas are applied.
    /// </summary>
    public Stream BaseStream => this.baseStream;

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => this.length;

    /// <inheritdoc/>
    public override long Position
    {
        get => this.position;
        set => throw new NotImplementedException();
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Reads a sequence of bytes from the current <see cref="GitPackDeltafiedStream"/> and advances the position
    /// within the stream by the number of bytes read.
    /// </summary>
    /// <param name="span">
    /// A region of memory. When this method returns, the contents of this region are replaced by the bytes
    /// read from the current source.
    /// </param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number of bytes allocated
    /// in the buffer if that many bytes are not currently available, or zero (0) if the end of the stream
    /// has been reached.
    /// </returns>
    public int Read(Span<byte> span)
#else
    /// <inheritdoc/>
    public override int Read(Span<byte> span)
#endif
    {
        int read = 0;
        int canRead;
        int didRead;

        while (read < span.Length && this.TryGetInstruction(out DeltaInstruction instruction))
        {
            Stream? source = instruction.InstructionType == DeltaInstructionType.Copy ? this.baseStream : this.deltaStream;

            Debug.Assert(instruction.Size > this.offset);
            Debug.Assert(source.Position + instruction.Size - this.offset <= source.Length);
            canRead = Math.Min(span.Length - read, instruction.Size - this.offset);
            didRead = source.Read(span.Slice(read, canRead));

            Debug.Assert(didRead != 0);
            read += didRead;
            this.offset += didRead;
        }

        this.position += read;
        Debug.Assert(read <= span.Length);
        return read;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return this.Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        throw new NotImplementedException();
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
            // We can optimise this by skipping over instructions rather than executing them
            this.ReadExactly(checked((int)(offset - this.position)));
            return this.position;
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        this.deltaStream.Dispose();
        this.baseStream.Dispose();
    }

    private bool TryGetInstruction(out DeltaInstruction instruction)
    {
        if (this.current is not null && this.offset < this.current.Value.Size)
        {
            instruction = this.current.Value;
            return true;
        }

        this.current = DeltaStreamReader.Read(this.deltaStream);

        if (this.current is null)
        {
            instruction = default;
            return false;
        }

        instruction = this.current.Value;

        switch (instruction.InstructionType)
        {
            case DeltaInstructionType.Copy:
                this.baseStream.Seek(instruction.Offset, SeekOrigin.Begin);
                Debug.Assert(this.baseStream.Position == instruction.Offset);
                this.offset = 0;
                break;

            case DeltaInstructionType.Insert:
                this.offset = 0;
                break;

            default:
                throw new GitException();
        }

        return true;
    }
}
