using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NerdBank.GitVersioning.Managed
{
    internal class GitObjectStream : Stream
    {
        private long length;
        private long position;
        private DeflateStream stream;

        public GitObjectStream(Stream stream, long length)
        {
            this.stream = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: false);
            this.length = length;
        }

        public override long Position
        {
            get => this.position;
            set => throw new NotSupportedException();
        }

        public override long Length => this.length;

        public string ObjectType { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public static GitObjectStream Create(Stream stream, long length)
        {
            Span<byte> zlibHeader = stackalloc byte[2];
            stream.ReadAll(zlibHeader);

            if (zlibHeader[0] != 0x78 || (zlibHeader[1] != 0x01 && zlibHeader[1] != 0x9C))
            {
                throw new GitException();
            }

            return new GitObjectStream(stream, length);
        }

        public void ReadObjectTypeAndLength(string objectType)
        {
            Span<byte> buffer = stackalloc byte[128];
            this.Read(buffer.Slice(0, objectType.Length + 1));

#if DEBUG && !NETSTANDARD2_0
            var actualObjectType = Encoding.ASCII.GetString(buffer.Slice(0, objectType.Length));
            Debug.Assert(objectType == actualObjectType);
            Debug.Assert(buffer[objectType.Length] == ' ');
#endif

            this.ObjectType = objectType;

            int headerLength = 0;
            this.length = 0;

            while (headerLength < buffer.Length)
            {
                this.Read(buffer.Slice(headerLength, 1));

                if (buffer[headerLength] == 0)
                {
                    break;
                }

                // Direct conversion from ASCII to int
                this.length = (10 * this.length) + (buffer[headerLength] - (byte)'0');

                headerLength += 1;
            }

            this.position = 0;
        }

        public override int Read(byte[] array, int offset, int count)
        {
            int read = this.stream.Read(array, offset, count);
            this.position += read;
            return read;
        }

#if NETSTANDARD2_0
        public int Read(Span<byte> buffer)
        {
            byte[] array = new byte[buffer.Length];
            int read = this.Read(array, 0, array.Length);
            array.CopyTo(buffer);
            return read;
        }

#else
        public override int Read(Span<byte> buffer)
        {
            int read = this.stream.Read(buffer);
            this.position += read;
            return read;
        }
#endif

        public override async Task<int> ReadAsync(byte[] array, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await this.stream.ReadAsync(array, offset, count, cancellationToken);
            this.position += read;
            return read;
        }

#if !NETSTANDARD2_0
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await this.stream.ReadAsync(buffer, cancellationToken);
            this.position += read;
            return read;
        }
#endif

        public override int ReadByte()
        {
            int value = this.stream.ReadByte();

            if (value != -1)
            {
                this.position += 1;
            }

            return value;
        }

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
                int length = (int)(offset - this.position);

                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                this.Read(buffer, 0, length);
                ArrayPool<byte>.Shared.Return(buffer);
                return this.position;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            this.stream.Dispose();
        }
    }
}
