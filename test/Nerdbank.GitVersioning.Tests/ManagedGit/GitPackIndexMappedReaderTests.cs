// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitPackIndexMappedReaderTests
{
    [Fact]
    public void ConstructorNullTest()
    {
        Assert.Throws<ArgumentNullException>(() => new GitPackIndexMappedReader(null));
    }

    [Fact]
    public void GetOffsetTest()
    {
        string indexFile = Path.GetTempFileName();

        using (Stream resourceStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.idx"))
        using (FileStream stream = File.Open(indexFile, FileMode.Open))
        {
            resourceStream.CopyTo(stream);
        }

        using (FileStream stream = File.OpenRead(indexFile))
        using (GitPackIndexReader reader = new GitPackIndexMappedReader(stream))
        {
            // Offset of an object which is present
            Assert.Equal(12, reader.GetOffset(GitObjectId.Parse("f5b401f40ad83f13030e946c9ea22cb54cb853cd")));
            Assert.Equal(317, reader.GetOffset(GitObjectId.Parse("d6781552a0a94adbf73ed77696712084754dc274")));

            // null for an object which is not present
            Assert.Null(reader.GetOffset(GitObjectId.Empty));
        }

        try
        {
            File.Delete(indexFile);
        }
        catch (UnauthorizedAccessException)
        {
            // TBD: Figure out what's keeping a lock on the file. Seems to be unique to Windows.
        }
    }

    [Fact]
    public void GetOffsetFromPartialTest()
    {
        string indexFile = Path.GetTempFileName();

        using (Stream resourceStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.idx"))
        using (FileStream stream = File.Open(indexFile, FileMode.Open))
        {
            resourceStream.CopyTo(stream);
        }

        using (FileStream stream = File.OpenRead(indexFile))
        using (var reader = new GitPackIndexMappedReader(stream))
        {
            // Offset of an object which is present
            (long? offset, GitObjectId? objectId) = reader.GetOffset(new byte[] { 0xf5, 0xb4, 0x01, 0xf4 });
            Assert.Equal(12, offset);
            Assert.Equal(GitObjectId.Parse("f5b401f40ad83f13030e946c9ea22cb54cb853cd"), objectId);

            (offset, objectId) = reader.GetOffset(new byte[] { 0xd6, 0x78, 0x15, 0x52 });
            Assert.Equal(317, offset);
            Assert.Equal(GitObjectId.Parse("d6781552a0a94adbf73ed77696712084754dc274"), objectId);

            // null for an object which is not present
            (offset, objectId) = reader.GetOffset(new byte[] { 0x00, 0x00, 0x00, 0x00 });
            Assert.Null(offset);
            Assert.Null(objectId);
        }

        try
        {
            File.Delete(indexFile);
        }
        catch (UnauthorizedAccessException)
        {
            // TBD: Figure out what's keeping a lock on the file. Seems to be unique to Windows.
        }
    }
}
