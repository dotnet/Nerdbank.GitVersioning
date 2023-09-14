// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

/// <summary>
/// Tests the <see cref="GitPackMemoryCache"/> class.
/// </summary>
public class GitPackMemoryCacheTests
{
    [Fact]
    public void StreamsAreIndependent()
    {
        using (MemoryStream stream = new MemoryStream(
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }))
        {
            var cache = new GitPackMemoryCache();

            Stream stream1 = cache.Add(0, stream, "anObjectType");
            Assert.True(cache.TryOpen(0, out (Stream ContentStream, string ObjectType)? hit));
            Assert.True(hit.HasValue);
            Assert.Equal("anObjectType", hit.Value.ObjectType);

            using (stream1)
            using (Stream stream2 = hit.Value.ContentStream)
            {
                stream1.Seek(5, SeekOrigin.Begin);
                Assert.Equal(5, stream1.Position);
                Assert.Equal(0, stream2.Position);
                Assert.Equal(5, stream1.ReadByte());

                Assert.Equal(6, stream1.Position);
                Assert.Equal(0, stream2.Position);

                Assert.Equal(0, stream2.ReadByte());
                Assert.Equal(6, stream1.Position);
                Assert.Equal(1, stream2.Position);
            }
        }
    }
}
