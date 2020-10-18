using System;
using System.Buffers.Binary;
using System.Diagnostics;

namespace NerdBank.GitVersioning.Managed
{
    internal unsafe struct GitObjectId : IEquatable<GitObjectId>
    {
        private const string hexDigits = "0123456789abcdef";
        private readonly static byte[] hexBytes = new byte[] { (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9', (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f' };
        private const int NativeSize = 20;
        public fixed byte value[NativeSize];
        private string sha;

        private static readonly byte[] ReverseHexDigits = BuildReverseHexDigits();

        public static GitObjectId Empty { get; } = GitObjectId.Parse(new byte[20]);

        public static GitObjectId Parse(Span<byte> value)
        {
            Debug.Assert(value.Length == 20);

            GitObjectId objectId = new GitObjectId();
            Span<byte> bytes = new Span<byte>(objectId.value, NativeSize);
            value.CopyTo(bytes);
            return objectId;
        }

        public static GitObjectId Parse(string value)
        {
            Debug.Assert(value.Length == 40);

            GitObjectId objectId = new GitObjectId();
            Span<byte> bytes = new Span<byte>(objectId.value, NativeSize);

            for (int i = 0; i < value.Length; i++)
            {
                int c1 = ReverseHexDigits[value[i++] - '0'] << 4;
                int c2 = ReverseHexDigits[value[i] - '0'];

                bytes[i >> 1] = (byte)(c1 + c2);
            }

            objectId.sha = value;
            return objectId;
        }

        public static GitObjectId ParseHex(Span<byte> value)
        {
            Debug.Assert(value.Length == 40);

            GitObjectId objectId = new GitObjectId();
            Span<byte> bytes = new Span<byte>(objectId.value, NativeSize);

            for (int i = 0; i < value.Length; i++)
            {
                int c1 = ReverseHexDigits[value[i++] - '0'] << 4;
                int c2 = ReverseHexDigits[value[i] - '0'];

                bytes[i >> 1] = (byte)(c1 + c2);
            }

            return objectId;
        }

        private static byte[] BuildReverseHexDigits()
        {
            var bytes = new byte['f' - '0' + 1];

            for (int i = 0; i < 10; i++)
            {
                bytes[i] = (byte)i;
            }

            for (int i = 10; i < 16; i++)
            {
                bytes[i + 'a' - '0' - 0x0a] = (byte)(i);
            }

            return bytes;
        }

        public override bool Equals(object obj)
        {
            if (obj is GitObjectId)
            {
                return this.Equals((GitObjectId)obj);
            }

            return false;
        }

        public bool Equals(GitObjectId other)
        {
            fixed (byte* thisValue = this.value)
            {
                return new Span<byte>(thisValue, NativeSize).SequenceEqual(new Span<byte>(other.value, NativeSize));
            }
        }

        public static bool operator ==(GitObjectId left, GitObjectId right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(GitObjectId left, GitObjectId right)
        {
            return !Equals(left, right);
        }

        public override int GetHashCode()
        {
            fixed (byte* thisValue = this.value)
            {
                return BinaryPrimitives.ReadInt32LittleEndian(new Span<byte>(thisValue, 4));
            }
        }

        public override string ToString()
        {
            if (this.sha == null)
            {
                this.sha = this.CreateString(0, 20);
            }

            return this.sha;
        }

        public string CreateString(int start, int length)
        {
            // Inspired from http://stackoverflow.com/questions/623104/c-byte-to-hex-string/3974535#3974535
            int lengthInNibbles = length * 2;
            var c = new char[lengthInNibbles];

            for (int i = 0; i < (lengthInNibbles & -2); i++)
            {
                int index0 = +i >> 1;
                var b = ((byte)(this.value[start + index0] >> 4));
                c[i++] = hexDigits[b];

                b = ((byte)(this.value[start + index0] & 0x0F));
                c[i] = hexDigits[b];
            }

            return new string(c);
        }
        public void CreateUnicodeString(int start, int length, Span<byte> bytes)
        {
            // Inspired from http://stackoverflow.com/questions/623104/c-byte-to-hex-string/3974535#3974535
            int lengthInNibbles = length * 2;

            for (int i = 0; i < (lengthInNibbles & -2); i++)
            {
                int index0 = +i >> 1;
                var b = ((byte)(this.value[start + index0] >> 4));
                bytes[2 * i + 1] = 0;
                bytes[2 * i++] = hexBytes[b];

                b = ((byte)(this.value[start + index0] & 0x0F));
                bytes[2 * i + 1] = 0;
                bytes[2 * i] = hexBytes[b];
            }
        }

        private byte[] array;

        public ReadOnlySpan<byte> AsSpan()
        {
            if (this.array == null)
            {
                this.array = new byte[20];
                this.CopyTo(this.array);
            }

            return this.array;
        }

        public void CopyTo(Span<byte> value)
        {
            fixed (byte* thisValue = this.value)
            {
                new Span<byte>(thisValue, NativeSize).CopyTo(value);
            }
        }
    }
}
