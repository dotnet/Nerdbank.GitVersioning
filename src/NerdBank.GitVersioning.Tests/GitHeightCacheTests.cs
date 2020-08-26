using System.IO;
using LibGit2Sharp;
using Xunit;

namespace Nerdbank.GitVersioning
{
    public class GitHeightCacheTests
    {
        [Fact]
        public void CachedHeightAvailable_NoCacheFile()
        {
            var cache = new GitHeightCache(Directory.GetCurrentDirectory(), "non-existent-dir");
            Assert.False(cache.CachedHeightAvailable);
        }
        
        [Fact]
        public void CachedHeightAvailable_RootCacheFile()
        {
            File.WriteAllText($"./{GitHeightCache.CacheFileName}", "");
            var cache = new GitHeightCache(Directory.GetCurrentDirectory(), null);
            Assert.True(cache.CachedHeightAvailable);
        }
        
        [Fact]
        public void CachedHeightAvailable_CacheFile()
        {
            Directory.CreateDirectory("./testDir");
            File.WriteAllText($"./testDir/{GitHeightCache.CacheFileName}", "");
            var cache = new GitHeightCache(Directory.GetCurrentDirectory(),"testDir/");
            Assert.True(cache.CachedHeightAvailable);
        }

        [Fact]
        public void GitHeightCache_RoundtripCaching()
        {
            var cache = new GitHeightCache(Directory.GetCurrentDirectory(), null);
            
            // test initial set
            cache.SetHeight(new ObjectId("8b1f731de6b98aaf536085a62c40dfd3e38817b6"), 2);
            var (commitId, height) = cache.GetHeight();
            Assert.Equal("8b1f731de6b98aaf536085a62c40dfd3e38817b6", commitId.Sha);
            Assert.Equal(2, height);
            
            // verify overwriting works correctly
            cache.SetHeight(new ObjectId("352459698e082aebef799d77807961d222e75efe"), 3);
            (commitId, height) = cache.GetHeight();
            Assert.Equal("352459698e082aebef799d77807961d222e75efe", commitId.Sha);
            Assert.Equal(3, height);
        }
    }
}