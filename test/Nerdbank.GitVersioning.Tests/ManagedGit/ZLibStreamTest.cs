// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;
using ZLibStream = Nerdbank.GitVersioning.ManagedGit.ZLibStream;

namespace ManagedGit
{
    public class ZLibStreamTest
    {
        [Fact]
        public void ReadTest()
        {
            using (Stream rawStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\3596ffe59898103a2675547d4597e742e1f2389c.gz"))
            using (ZLibStream stream = new ZLibStream(rawStream, -1))
            using (var sha = SHA1.Create())
            {
                DeflateStream deflateStream = Assert.IsType<DeflateStream>(stream.BaseStream);
                Assert.Same(rawStream, deflateStream.BaseStream);
                Assert.Equal(0, stream.Position);

                byte[] hash = sha.ComputeHash(stream);
                Assert.Equal("NZb/5ZiYEDomdVR9RZfnQuHyOJw=", Convert.ToBase64String(hash));

                Assert.Equal(148, stream.Position);
            }
        }

        [Fact]
        public void SeekTest()
        {
            using (Stream rawStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\3596ffe59898103a2675547d4597e742e1f2389c.gz"))
            using (ZLibStream stream = new ZLibStream(rawStream, -1))
            {
                // Seek past the commit 137 header, and make sure we can read the 'tree' word
                Assert.Equal(11, stream.Seek(11, SeekOrigin.Begin));
                byte[] tree = new byte[4];
                stream.Read(tree);
                Assert.Equal("tree", Encoding.UTF8.GetString(tree));

                // Valid no-ops
                Assert.Equal(15, stream.Seek(0, SeekOrigin.Current));
                Assert.Equal(15, stream.Seek(15, SeekOrigin.Begin));

                // Invalid seeks
                Assert.Throws<NotImplementedException>(() => stream.Seek(-1, SeekOrigin.Current));
                Assert.Throws<NotImplementedException>(() => stream.Seek(1, SeekOrigin.Current));
                Assert.Throws<NotImplementedException>(() => stream.Seek(-1, SeekOrigin.End));
            }
        }
    }
}
