using System;
using System.Text;
using NerdBank.GitVersioning.Managed;
using Xunit;
using Xunit.Abstractions;

namespace NerdBank.GitVersioning.Tests.Managed
{
    public class GitObjectIdTests
    {
        private readonly byte[] shaAsByteArray = new byte[] { 0x4e, 0x91, 0x27, 0x36, 0xc2, 0x7e, 0x40, 0xb3, 0x89, 0x90, 0x4d, 0x04, 0x6d, 0xc6, 0x3d, 0xc9, 0xf5, 0x78, 0x11, 0x7f };
        private const string shaAsHexString = "4e912736c27e40b389904d046dc63dc9f578117f";
        private readonly byte[] shaAsHexByteArray = Encoding.ASCII.GetBytes(shaAsHexString);

        [Fact]
        public void ParseByteArrayTest()
        {
            var objectId = GitObjectId.Parse(this.shaAsByteArray);

            Span<byte> value = stackalloc byte[20];
            objectId.CopyTo(value);
            Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
        }

        [Fact]
        public void ParseStringTest()
        {
            var objectId = GitObjectId.Parse(shaAsHexString);

            Span<byte> value = stackalloc byte[20];
            objectId.CopyTo(value);
            Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
        }

        [Fact]
        public void ParseHexArrayTest()
        {
            var objectId = GitObjectId.ParseHex(this.shaAsHexByteArray);

            Span<byte> value = stackalloc byte[20];
            objectId.CopyTo(value);
            Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
        }

        [Fact]
        public void EqualsObjectTest()
        {
            var objectId = GitObjectId.ParseHex(this.shaAsHexByteArray);
            var objectId2 = GitObjectId.ParseHex(this.shaAsHexByteArray);

            // Must be equal to itself
            Assert.True(objectId.Equals((object)objectId));
            Assert.True(objectId.Equals((object)objectId2));

            // Not equal to null
            Assert.False(objectId.Equals(null));

            // Not equal to other representations of the object id
            Assert.False(objectId.Equals(this.shaAsHexByteArray));
            Assert.False(objectId.Equals(this.shaAsByteArray));
            Assert.False(objectId.Equals(shaAsHexString));

            // Not equal to other object ids
            Assert.False(objectId.Equals((object)GitObjectId.Empty));
        }

        [Fact]
        public void EqualsObjectIdTest()
        {
            var objectId = GitObjectId.ParseHex(this.shaAsHexByteArray);
            var objectId2 = GitObjectId.ParseHex(this.shaAsHexByteArray);

            // Must be equal to itself
            Assert.True(objectId.Equals(objectId));
            Assert.True(objectId.Equals(objectId2));

            // Not equal to other object ids
            Assert.False(objectId.Equals(GitObjectId.Empty));
        }

        [Fact]
        public void GetHashCodeTest()
        {
            // The hash code is the int32 representation of the first 4 bytes
            var objectId = GitObjectId.ParseHex(this.shaAsHexByteArray);
            Assert.Equal(0x3627914e, objectId.GetHashCode());
            Assert.Equal(0, GitObjectId.Empty.GetHashCode());
        }

        [Fact]
        public void AsUInt16Test()
        {
            // The hash code is the int32 representation of the first 4 bytes
            var objectId = GitObjectId.ParseHex(this.shaAsHexByteArray);
            Assert.Equal(0x914e, objectId.AsUInt16());
            Assert.Equal(0, GitObjectId.Empty.GetHashCode());
        }

        [Fact]
        public void ToStringTest()
        {
            var objectId = GitObjectId.Parse(this.shaAsByteArray);
            Assert.Equal(shaAsHexString, objectId.ToString());
        }

        [Fact]
        public void CopyToUnicodeStringTest()
        {
            // Common use case: create the path to the object in the Git object store,
            // e.g. git/objects/[byte 0]/[bytes 1 - 19]
            byte[] value = Encoding.Unicode.GetBytes("git/objects/00/01020304050607080910111213141516171819");

            var objectId = GitObjectId.ParseHex(this.shaAsHexByteArray);
            objectId.CopyToUnicodeString(0, 1, value.AsSpan(24, 1 *4));
            objectId.CopyToUnicodeString(1, 19, value.AsSpan(30, 19 * 4));

            var path = Encoding.Unicode.GetString(value);
            Assert.Equal("git/objects/4e/912736c27e40b389904d046dc63dc9f578117f", path);
        }

        [Fact]
        public void CopyToTest()
        {
            var objectId = GitObjectId.Parse(this.shaAsByteArray);

            byte[] actual = new byte[20];
            objectId.CopyTo(actual);

            Assert.Equal(this.shaAsByteArray, actual);
        }
    }
}
