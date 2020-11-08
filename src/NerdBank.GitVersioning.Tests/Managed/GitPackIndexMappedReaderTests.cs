using System;
using System.IO;
using Nerdbank.GitVersioning.Managed;
using Xunit;

namespace Managed
{
    public class GitPackIndexMappedReaderTests
    {
        [Fact]
        public void ConstructorNullTest()
        {
            Assert.Throws<ArgumentNullException>(() => new GitPackIndexStreamReader(null));
        }

        [Fact]
        public void GetOffsetTest()
        {
            var indexFile = Path.GetTempFileName();

            using (Stream resourceStream = TestUtilities.GetEmbeddedResource(@"Managed\pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.idx"))
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
    }
}
