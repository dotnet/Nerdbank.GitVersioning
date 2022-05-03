// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;
using Xunit.Abstractions;

namespace ManagedGit;

public class GitObjectIdTests
{
    private const string ShaAsHexString = "4e912736c27e40b389904d046dc63dc9f578117f";
    private readonly byte[] shaAsByteArray = new byte[] { 0x4e, 0x91, 0x27, 0x36, 0xc2, 0x7e, 0x40, 0xb3, 0x89, 0x90, 0x4d, 0x04, 0x6d, 0xc6, 0x3d, 0xc9, 0xf5, 0x78, 0x11, 0x7f };
    private readonly byte[] shaAsHexAsciiByteArray = Encoding.ASCII.GetBytes(ShaAsHexString);

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
        var objectId = GitObjectId.Parse(ShaAsHexString);

        Span<byte> value = stackalloc byte[20];
        objectId.CopyTo(value);
        Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
    }

    [Fact]
    public void ParseHexArrayTest()
    {
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);

        Span<byte> value = stackalloc byte[20];
        objectId.CopyTo(value);
        Assert.True(value.SequenceEqual(this.shaAsByteArray.AsSpan()));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public void GetHashCodeTest()
    {
        // The hash code is the int32 representation of the first 4 bytes
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        Assert.Equal(0x3627914e, objectId.GetHashCode());
        Assert.Equal(0, GitObjectId.Empty.GetHashCode());
    }

    [Fact]
    public void AsUInt16Test()
    {
        // The hash code is the int32 representation of the first 4 bytes
        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        Assert.Equal(0x4e91, objectId.AsUInt16());
        Assert.Equal(0, GitObjectId.Empty.GetHashCode());
    }

    [Fact]
    public void ToStringTest()
    {
        var objectId = GitObjectId.Parse(this.shaAsByteArray);
        Assert.Equal(ShaAsHexString, objectId.ToString());
    }

    [Fact]
    public void CopyToUtf16StringTest()
    {
        // Common use case: create the path to the object in the Git object store,
        // e.g. git/objects/[byte 0]/[bytes 1 - 19]
        byte[] valueAsBytes = Encoding.Unicode.GetBytes("git/objects/00/01020304050607080910111213141516171819");
        Span<char> valueAsChars = MemoryMarshal.Cast<byte, char>(valueAsBytes);

        var objectId = GitObjectId.ParseHex(this.shaAsHexAsciiByteArray);
        objectId.CopyAsHex(0, 1, valueAsChars.Slice(12, 1 * 2));
        objectId.CopyAsHex(1, 19, valueAsChars.Slice(15, 19 * 2));

        string path = Encoding.Unicode.GetString(valueAsBytes);
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
