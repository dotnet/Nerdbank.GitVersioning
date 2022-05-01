using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace NerdBank.GitVersioning.Tests.ManagedGit
{
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

                var stream1 = cache.Add(0, stream);
                Assert.True(cache.TryOpen(0, out Stream stream2));

                using (stream1)
                using (stream2)
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
}
