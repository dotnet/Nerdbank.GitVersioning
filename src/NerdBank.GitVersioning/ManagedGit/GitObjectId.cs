#nullable enable

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nerdbank.GitVersioning.ManagedGit
{
    /// <summary>
    /// A <see cref="GitObjectId"/> identifies an object stored in the Git repository. The
    /// <see cref="GitObjectId"/> of an object is the SHA-1 hash of the contents of that
    /// object.
    /// </summary>
    /// <seealso href="https://git-scm.com/book/en/v2/Git-Internals-Git-Objects"/>.
    public unsafe struct GitObjectId : IEquatable<GitObjectId>
    {
        private const string hexDigits = "0123456789abcdef";
        private readonly static byte[] hexBytes = new byte[] { (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9', (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f' };
        private const int NativeSize = 20;
        private fixed byte value[NativeSize];
        private string? sha;

        /// <summary>
        /// Gets the 20 byte ID of this object as a span from the <see cref="value"/> field.
        /// </summary>
        private Span<byte> Value
        {
            get
            {
                fixed (byte* value = this.value)
                {
                    return new Span<byte>(value, NativeSize);
                }
            }
        }

        private static readonly byte[] ReverseHexDigits = BuildReverseHexDigits();

        /// <summary>
        /// Gets a <see cref="GitObjectId"/> which represents an empty <see cref="GitObjectId"/>.
        /// </summary>
        public static GitObjectId Empty => default(GitObjectId);

        /// <summary>
        /// Parses a <see cref="ReadOnlySpan{T}"/> which contains the <see cref="GitObjectId"/>
        /// as a sequence of byte values.
        /// </summary>
        /// <param name="value">
        /// The <see cref="GitObjectId"/> as a sequence of byte values. Must be exactly 20 bytes in length.
        /// </param>
        /// <returns>
        /// A <see cref="GitObjectId"/>.
        /// </returns>
        public static GitObjectId Parse(ReadOnlySpan<byte> value)
        {
            Debug.Assert(value.Length == 20);

            GitObjectId objectId = new GitObjectId();
            value.CopyTo(objectId.Value);
            return objectId;
        }

        /// <summary>
        /// Parses a <see cref="string"/> which contains the hexadecimal representation of a
        /// <see cref="GitObjectId"/>.
        /// </summary>
        /// <param name="value">
        /// A <see cref="string"/> which contains the hexadecimal representation of the
        /// <see cref="GitObjectId"/>.
        /// </param>
        /// <returns>
        /// A <see cref="GitObjectId"/>.
        /// </returns>
        public static GitObjectId Parse(string value)
        {
            Debug.Assert(value.Length == 40);

            GitObjectId objectId = new GitObjectId();
            Span<byte> bytes = objectId.Value;

            for (int i = 0; i < value.Length; i++)
            {
                int c1 = ReverseHexDigits[value[i++] - '0'] << 4;
                int c2 = ReverseHexDigits[value[i] - '0'];

                bytes[i >> 1] = (byte)(c1 + c2);
            }

            objectId.sha = value.ToLower();
            return objectId;
        }

        /// <summary>
        /// Parses a <see cref="ReadOnlySpan{T}"/> which contains the hexadecimal representation of a
        /// <see cref="GitObjectId"/>.
        /// </summary>
        /// <param name="value">
        /// A <see cref="ReadOnlySpan{T}"/> which contains the hexadecimal representation of the
        /// <see cref="GitObjectId"/> encoded in ASCII.
        /// </param>
        /// <returns>
        /// A <see cref="GitObjectId"/>.
        /// </returns>
        public static GitObjectId ParseHex(ReadOnlySpan<byte> value)
        {
            Debug.Assert(value.Length == 40);

            GitObjectId objectId = new GitObjectId();
            Span<byte> bytes = objectId.Value;

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
                bytes[i + 'a' - '0' - 0x0a] = (byte)i;
                bytes[i + 'A' - '0' - 0x0a] = (byte)i;
            }

            return bytes;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is GitObjectId)
            {
                return this.Equals((GitObjectId)obj);
            }

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(GitObjectId other) => this.Value.SequenceEqual(other.Value);

        /// <inheritdoc/>
        public static bool operator ==(GitObjectId left, GitObjectId right) => Equals(left, right);

        /// <inheritdoc/>
        public static bool operator !=(GitObjectId left, GitObjectId right) => !Equals(left, right);

        /// <inheritdoc/>
        public override int GetHashCode() => BinaryPrimitives.ReadInt32LittleEndian(this.Value.Slice(0, 4));

        /// <summary>
        /// Gets a <see cref="ushort"/> which represents the first two bytes of this <see cref="GitObjectId"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="ushort"/> which represents the first two bytes of this <see cref="GitObjectId"/>.
        /// </returns>
        public ushort AsUInt16() => BinaryPrimitives.ReadUInt16BigEndian(this.Value.Slice(0, 2));

        /// <summary>
        /// Returns the SHA1 hash of this object.
        /// </summary>
        public override string ToString()
        {
            if (this.sha is null)
            {
                this.sha = this.CreateString(0, 20);
            }

            return this.sha;
        }

        private string CreateString(int start, int length)
        {
            // Inspired byte http://stackoverflow.com/questions/623104/c-byte-to-hex-string/3974535#3974535
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

        /// <summary>
        /// Encodes a portion of this <see cref="GitObjectId"/> as hex.
        /// </summary>
        /// <param name="start">
        /// The index of the first byte of this <see cref="GitObjectId"/> to start copying.
        /// </param>
        /// <param name="length">
        /// The number of bytes of this <see cref="GitObjectId"/> to copy.
        /// </param>
        /// <param name="chars">The buffer that receives the hex characters. It must be at least twice as long as <paramref name="length"/>.</param>
        /// <remarks>
        /// This method is used to populate file paths as byte* objects which are passed to UTF-16-based
        /// Windows APIs.
        /// </remarks>
        public void CopyAsHex(int start, int length, Span<char> chars)
        {
            Span<byte> bytes = MemoryMarshal.Cast<char, byte>(chars);

            // Inspired by http://stackoverflow.com/questions/623104/c-byte-to-hex-string/3974535#3974535
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

        /// <summary>
        /// Copies the byte representation of this <see cref="GitObjectId"/> to a <see cref="Span{T}"/>.
        /// </summary>
        /// <param name="value">
        /// The memory to which to copy this <see cref="GitObjectId"/>.
        /// </param>
        public void CopyTo(Span<byte> value) => this.Value.CopyTo(value);
    }
}
