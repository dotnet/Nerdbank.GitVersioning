// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitObjectIdTests
{
    private const string ShaAsHexString = "4e912736c27e40b389904d046dc63dc9f578117f";
    private const string Sha256AsHexString = "86f96791761c200764fb74a52bc75444d1e7134e68f435b4ea27cb7b8fc53087";
    private readonly byte[] shaAsByteArray = new byte[] { 0x4e, 0x91, 0x27, 0x36, 0xc2, 0x7e, 0x40, 0xb3, 0x89, 0x90, 0x4d, 0x04, 0x6d, 0xc6, 0x3d, 0xc9, 0xf5, 0x78, 0x11, 0x7f };
    private readonly byte[] shaAsHexAsciiByteArray = Encoding.ASCII.GetBytes(ShaAsHexString);
    private readonly byte[] sha256AsByteArray = new byte[]
    {
        0x86, 0xf9, 0x67, 0x91, 0x76, 0x1c, 0x20, 0x07, 0x64, 0xfb, 0x74, 0xa5, 0x2b, 0xc7, 0x54, 0x44,
        0xd1, 0xe7, 0x13, 0x4e, 0x68, 0xf4, 0x35, 0xb4, 0xea, 0x27, 0xcb, 0x7b, 0x8f, 0xc5, 0x30, 0x87,
    };

    private readonly byte[] sha256AsHexAsciiByteArray = Encoding.ASCII.GetBytes(Sha256AsHexString);

    [Test]
    public void ParseByteArrayTest()
    {
        var objectId = GitObjectId.Parse(this.shaAsByteArray);

        Span<byte> value = stackalloc byte[20];
        objectId.CopyTo(value);
        Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
    }

    [Test]
    public void ParseStringTest()
    {
        var objectId = GitObjectId.Parse(ShaAsHexString);

        Span<byte> value = stackalloc byte[20];
        objectId.CopyTo(value);
        Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
    }

    [Test]
    public void ParseHexArrayTest()
    {
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);

        Span<byte> value = stackalloc byte[20];
        objectId.CopyTo(value);
        Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
    }

    [Test]
    public void EqualsObjectTest()
    {
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        var objectId2 = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);

        // Must be equal to itself
        Assert.True(objectId.Equals((object)objectId));
        Assert.True(objectId.Equals((object)objectId2));

        // Not equal to null
        Assert.False(objectId.Equals(null));

        // Not equal to other representations of the object id
        Assert.False(objectId.Equals(this.shaAsHexAsciiByteArray));
        Assert.False(objectId.Equals(this.shaAsByteArray));
        Assert.False(objectId.Equals(ShaAsHexString));

        // Not equal to other object ids
        Assert.False(objectId.Equals((object)GitObjectId.Empty));
    }

    [Test]
    public void EqualsObjectIdTest()
    {
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        var objectId2 = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);

        // Must be equal to itself
        Assert.True(objectId.Equals(objectId));
        Assert.True(objectId.Equals(objectId2));

        // Not equal to other object ids
        Assert.False(objectId.Equals(GitObjectId.Empty));
    }

    [Test]
    public void GetHashCodeTest()
    {
        // The hash code is the int32 representation of the first 4 bytes
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        Assert.Equal(0x3627914e, objectId.GetHashCode());
        Assert.Equal(0, GitObjectId.Empty.GetHashCode());
    }

    [Test]
    public void AsUInt16Test()
    {
        // The hash code is the int32 representation of the first 4 bytes
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        Assert.Equal(0x4e91, objectId.AsUInt16());
        Assert.Equal(0, GitObjectId.Empty.GetHashCode());
    }

    [Test]
    public void ToStringTest()
    {
        var objectId = GitObjectId.Parse(this.shaAsByteArray);
        Assert.Equal(ShaAsHexString, objectId.ToString());
    }

    [Test]
    public void CopyToUtf16StringTest()
    {
        // Common use case: create the path to the object in the Git object store,
        // e.g. git/objects/[byte 0]/[bytes 1 - 19]
        byte[] valueAsBytes = Encoding.Unicode.GetBytes("git/objects/00/01020304050607080910111213141516171819");
        Span<char> valueAsChars = MemoryMarshal.Cast<byte, char>((Span<byte>)valueAsBytes);

        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        objectId.CopyAsHex(0, 1, valueAsChars.Slice(12, 1 * 2));
        objectId.CopyAsHex(1, 19, valueAsChars.Slice(15, 19 * 2));

        string path = Encoding.Unicode.GetString(valueAsBytes);
        Assert.Equal("git/objects/4e/912736c27e40b389904d046dc63dc9f578117f", path);
    }

    [Test]
    public void CopyToTest()
    {
        var objectId = GitObjectId.Parse(this.shaAsByteArray);

        byte[] actual = new byte[20];
        objectId.CopyTo(actual);

        Assert.Equal(this.shaAsByteArray, actual);
    }

    [Test]
    public void ParseSha256Tests()
    {
        var fromBytes = GitObjectId.Parse(this.sha256AsByteArray);
        var fromString = GitObjectId.Parse(Sha256AsHexString);
        var fromUpperString = GitObjectId.Parse(Sha256AsHexString.ToUpperInvariant());
        var fromAsciiHex = GitObjectId.ParseHex(this.sha256AsHexAsciiByteArray);

        Assert.Equal(32, fromBytes.ByteLength);
        Assert.Equal(20, GitObjectId.Empty.ByteLength);
        Assert.Equal(Sha256AsHexString, fromBytes.ToString());
        Assert.Equal(Sha256AsHexString, fromString.ToString());
        Assert.Equal(Sha256AsHexString, fromUpperString.ToString());
        Assert.Equal(Sha256AsHexString, fromAsciiHex.ToString());

        Assert.Equal(fromBytes, fromString);
        Assert.Equal(fromBytes, fromUpperString);
        Assert.Equal(fromBytes, fromAsciiHex);
        Assert.NotEqual(GitObjectId.Empty, fromBytes);
        Assert.Equal(0x86f9, fromBytes.AsUInt16());

        byte[] copied = new byte[32];
        fromBytes.CopyTo(copied);
        Assert.Equal(this.sha256AsByteArray, copied);

        byte[] pathBytes = Encoding.Unicode.GetBytes("git/objects/00/" + new string('0', 62));
        Span<char> pathChars = MemoryMarshal.Cast<byte, char>((Span<byte>)pathBytes);
        fromBytes.CopyAsHex(0, 1, pathChars.Slice(12, 2));
        fromBytes.CopyAsHex(1, 31, pathChars.Slice(15, 62));
        Assert.Equal($"git/objects/{Sha256AsHexString.Substring(0, 2)}/{Sha256AsHexString.Substring(2)}", Encoding.Unicode.GetString(pathBytes));
    }

    [Test]
    public void Sha1AndSha256WithSamePrefixAreNotEqual()
    {
        byte[] paddedSha256 = new byte[32];
        this.shaAsByteArray.CopyTo(paddedSha256, 0);

        var sha1Id = GitObjectId.Parse(this.shaAsByteArray);
        var sha256Id = GitObjectId.Parse(paddedSha256);

        Assert.NotEqual(sha1Id, sha256Id);
        Assert.False(sha1Id.Equals(sha256Id));
    }

    [Test]
    public void ParseInvalidInputsThrow()
    {
        Assert.Throws<ArgumentException>(() => GitObjectId.Parse(new byte[19]));
        Assert.Throws<ArgumentException>(() => GitObjectId.Parse(new byte[31]));
        Assert.Throws<ArgumentException>(() => GitObjectId.Parse("1234"));
        Assert.Throws<ArgumentException>(() => GitObjectId.Parse(new string('g', 40)));
        Assert.Throws<ArgumentException>(() => GitObjectId.Parse(new string('z', 64)));
        Assert.Throws<ArgumentException>(() => GitObjectId.ParseHex(Encoding.ASCII.GetBytes(new string('0', 41))));
        Assert.Throws<ArgumentException>(() => GitObjectId.ParseHex(Encoding.ASCII.GetBytes(new string('z', 64))));
    }
}
