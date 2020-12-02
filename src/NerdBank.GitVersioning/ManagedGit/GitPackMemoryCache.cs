#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit
{
    internal class GitPackMemoryCache : GitPackCache
    {
        private readonly Dictionary<long, Stream> cache = new Dictionary<long, Stream>();

        public override Stream Add(long offset, Stream stream)
        {
            var cacheStream = new GitPackMemoryCacheStream(stream);
            return cacheStream;
        }

        public override bool TryOpen(long offset, [NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public override void GetCacheStatistics(StringBuilder builder)
        {
            builder.AppendLine($"{this.cache.Count} items in cache");
        }
    }
}
