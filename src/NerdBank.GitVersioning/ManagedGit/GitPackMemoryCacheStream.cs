#nullable enable

using System;
using System.Buffers;
using System.IO;

namespace Nerdbank.GitVersioning.ManagedGit
{
    internal class GitPackMemoryCacheStream : Stream
    {
        private Stream stream;
        private readonly MemoryStream cacheStream = new MemoryStream();
        private long length;

        public GitPackMemoryCacheStream(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.length = this.stream.Length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => this.length;

        public override long Position
        {
            get => this.cacheStream.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

#if NETSTANDARD2_0
        public int Read(Span<byte> buffer)
#else
        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
#endif
        {
            if (this.cacheStream.Length < this.length
                && this.cacheStream.Position + buffer.Length > this.cacheStream.Length)
            {
                var currentPosition = this.cacheStream.Position;
                var toRead = (int)(buffer.Length - this.cacheStream.Length + this.cacheStream.Position);
                int actualRead = this.stream.Read(buffer.Slice(0, toRead));
                this.cacheStream.Seek(0, SeekOrigin.End);
                this.cacheStream.Write(buffer.Slice(0, actualRead));
                this.cacheStream.Seek(currentPosition, SeekOrigin.Begin);
                this.DisposeStreamIfRead();
            }

            return this.cacheStream.Read(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.Read(buffer.AsSpan(offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin != SeekOrigin.Begin)
            {
                throw new NotSupportedException();
            }

            if (offset > this.cacheStream.Length)
            {
                var toRead = (int)(offset - this.cacheStream.Length);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(toRead);
                int read = this.stream.Read(buffer, 0, toRead);
                this.cacheStream.Seek(0, SeekOrigin.End);
                this.cacheStream.Write(buffer, 0, read);
                ArrayPool<byte>.Shared.Return(buffer);

                this.DisposeStreamIfRead();
                return this.cacheStream.Position;
            }
            else
            {
                return this.cacheStream.Seek(offset, origin);
            }
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
            if (disposing)
            {
                this.stream.Dispose();
                this.cacheStream.Dispose();
            }

            base.Dispose(disposing);
        }

        private void DisposeStreamIfRead()
        {
            if (this.cacheStream.Length == this.stream.Length)
            {
                this.stream.Dispose();
            }
        }
    }
}
