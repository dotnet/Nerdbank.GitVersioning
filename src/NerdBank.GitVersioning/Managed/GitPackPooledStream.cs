using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    internal class GitPackPooledStream : Stream
    {
        private readonly FileStream stream;
        private readonly Queue<GitPackPooledStream> pool;

        public GitPackPooledStream(FileStream stream, Queue<GitPackPooledStream> pool)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        public override bool CanRead => this.stream.CanRead;

        public override bool CanSeek => this.stream.CanSeek;

        public override bool CanWrite => this.stream.CanWrite;

        public override long Length => this.stream.Length;

        public override long Position
        {
            get => this.stream.Position;
            set => this.stream.Position = value;
        }

        public override void Flush()
        {
            this.stream.Flush();
        }

        public override int Read(Span<byte> buffer)
        {
            return this.stream.Read(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            this.pool.Enqueue(this);
            Debug.WriteLine("Returning stream to pool");
        }
    }
}
