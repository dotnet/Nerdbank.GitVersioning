using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit
{
    internal class GitPackMemoryCacheViewStream : Stream
    {
        private readonly GitPackMemoryCacheStream baseStream;

        public GitPackMemoryCacheViewStream(GitPackMemoryCacheStream baseStream)
        {
            this.baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => this.baseStream.Length;

        private long position;

        public override long Position
        {
            get => this.position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotImplementedException();

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

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin != SeekOrigin.Begin)
            {
                throw new NotSupportedException();
            }

            this.position = Math.Min(offset, this.Length);
            return this.position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
