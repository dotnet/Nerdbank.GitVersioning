using System;
using System.IO;
using NerdBank.GitVersioning.Managed;
using Xunit;

namespace NerdBank.GitVersioning.Tests.Managed
{
    public class GitPackIndexReaderTests
    {
        [Fact]
        public void ConstructorNullTest()
        {
            Assert.Throws<ArgumentNullException>(() => new GitPackIndexReader(null));
        }

        [Fact]
        public void GetOffsetTest()
        {
            using (Stream stream = File.OpenRead("Managed/pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.idx"))
            using (GitPackIndexReader reader = new GitPackIndexReader(stream))
            {
                // Offset of an object which is present
                Assert.Equal(12, reader.GetOffset(GitObjectId.Parse("f5b401f40ad83f13030e946c9ea22cb54cb853cd")));
                Assert.Equal(317, reader.GetOffset(GitObjectId.Parse("d6781552a0a94adbf73ed77696712084754dc274")));

                // null for an object which is not present
                Assert.Null(reader.GetOffset(GitObjectId.Empty));
            }
        }
    }
}
