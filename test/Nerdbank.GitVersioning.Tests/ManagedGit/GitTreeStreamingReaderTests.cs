// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitTreeStreamingReaderTests
{
    [Fact]
    public void FindBlobTest()
    {
        using (Stream stream = TestUtilities.GetEmbeddedResource(@"ManagedGit\tree.bin"))
        {
            GitObjectId blobObjectId = GitTreeStreamingReader.FindNode(stream, Encoding.UTF8.GetBytes("version.json"));
            Assert.Equal("59552a5eed6779aa4e5bb4dc96e80f36bb6e7380", blobObjectId.ToString());
        }
    }

    [Fact]
    public void FindTreeTest()
    {
        using (Stream stream = TestUtilities.GetEmbeddedResource(@"ManagedGit\tree.bin"))
        {
            GitObjectId blobObjectId = GitTreeStreamingReader.FindNode(stream, Encoding.UTF8.GetBytes("tools"));
            Assert.Equal("ec8e91fc4ad13d6a214584330f26d7a05495c8cc", blobObjectId.ToString());
        }
    }
}
