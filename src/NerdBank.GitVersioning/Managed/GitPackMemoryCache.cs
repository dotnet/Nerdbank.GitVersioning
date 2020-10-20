using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    internal class GitPackMemoryCache : GitPackCache
    {
        private readonly Dictionary<int, Stream> cache = new Dictionary<int, Stream>();

        public override Stream Add(int offset, Stream stream)
        {
            var cacheStream = new GitPackMemoryCacheStream(stream);
            this.cache.Add(offset, cacheStream);
            return cacheStream;
        }

        public override bool TryOpen(int offset, out Stream stream)
        {
            if (this.cache.TryGetValue(offset, out stream))
            {
                stream.Seek(0, SeekOrigin.Begin);
                return true;
            }

            return false;
        }

        public override void GetCacheStatistics(StringBuilder builder)
        {
            builder.AppendLine($"{this.cache.Count} items in cache");
        }
    }
}
