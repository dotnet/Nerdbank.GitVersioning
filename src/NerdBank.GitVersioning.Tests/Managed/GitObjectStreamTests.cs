using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Nerdbank.GitVersioning.Managed;
using Xunit;

namespace Managed
{
    public class GitObjectStreamTests
    {
        [Fact]
        public void ReadTest()
        {
            using (Stream rawStream = TestUtilities.GetEmbeddedResource(@"Managed\3596ffe59898103a2675547d4597e742e1f2389c.gz"))
            using (GitObjectStream stream = new GitObjectStream(rawStream, "commit"))
            using (var sha = SHA1.Create())
            {
                Assert.Equal(137, stream.Length);
                var deflateStream = Assert.IsType<DeflateStream>(stream.BaseStream);
                Assert.Same(rawStream, deflateStream.BaseStream);
                Assert.Equal("commit", stream.ObjectType);
                Assert.Equal(0, stream.Position);

                var hash = sha.ComputeHash(stream);
                Assert.Equal("U1WYLbBP+xD47Y32m+hpCCTpnLA=", Convert.ToBase64String(hash));

                Assert.Equal(stream.Length, stream.Position);
            }
        }
    }
}
