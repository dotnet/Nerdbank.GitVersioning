#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit
{
    internal class GitPackMemoryCache : GitPackCache
    {
        private readonly Dictionary<long, GitPackMemoryCacheStream> cache = new Dictionary<long, GitPackMemoryCacheStream>();

        public override Stream Add(long offset, Stream stream)
        {
            var cacheStream = new GitPackMemoryCacheStream(stream);
            this.cache.Add(offset, cacheStream);
            return new GitPackMemoryCacheViewStream(cacheStream);
        }

        public override bool TryOpen(long offset, [NotNullWhen(true)] out Stream? stream)
        {
            if (this.cache.TryGetValue(offset, out GitPackMemoryCacheStream? cacheStream))
            {
                stream = new GitPackMemoryCacheViewStream(cacheStream!);
                return true;
            }

            stream = null;
            return false;
        }

        public override void GetCacheStatistics(StringBuilder builder)
        {
            builder.AppendLine($"{this.cache.Count} items in cache");
        }
    }
}
