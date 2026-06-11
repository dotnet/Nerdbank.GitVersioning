// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitPackIndexMemoryReaderTests
{
    [Fact]
    public void ConstructorNullTest()
    {
        Assert.Throws<ArgumentNullException>(() => new GitPackIndexMemoryReader(null));
    }

    [Fact]
    public void GetOffsetTest()
    {
        string indexFile = Path.GetTempFileName();

        using (Stream resourceStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.idx"))
        using (FileStream stream = File.Open(indexFile, FileMode.Create))
        {
            resourceStream.CopyTo(stream);
        }

        using (FileStream stream = File.OpenRead(indexFile))
        using (GitPackIndexReader reader = new GitPackIndexMemoryReader(stream))
        {
            Assert.Equal(12, reader.GetOffset(GitObjectId.Parse("f5b401f40ad83f13030e946c9ea22cb54cb853cd")));
            Assert.Equal(317, reader.GetOffset(GitObjectId.Parse("d6781552a0a94adbf73ed77696712084754dc274")));
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
}
