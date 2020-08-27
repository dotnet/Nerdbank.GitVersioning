using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Version = System.Version;

namespace Nerdbank.GitVersioning
{
    /// <summary>
    /// Allows caching the height of a commit for a project, reducing repetitive height calculations.
    /// </summary>
    /// <remarks>
    /// Calculating the height of commits can be time consuming for repositories with 1000s of commits between major/ minor version bumps,
    /// so caching the height of a commit can save time. This is especially when packaging projects where height calculation must be done multiple times, 
    /// see https://github.com/dotnet/Nerdbank.GitVersioning/issues/114#issuecomment-669713622.
    /// </remarks>
    public class GitHeightCache
    {
        private readonly string repoRelativeProjectDirectory;

        private static readonly JsonSerializer JsonSerializer = new JsonSerializer()
        {
            Converters =
            {
                new VersionConverter(),
                new ObjectIdConverter()
            }
        };
        
        public const string CacheFileName = "version.cache.json";
        
        private readonly string heightCacheFilePath;
        private readonly Lazy<bool> cachedHeightAvailable;

        public GitHeightCache(string repositoryPath, string repoRelativeProjectDirectory)
        {
            this.repoRelativeProjectDirectory = repoRelativeProjectDirectory;
            
            if (repositoryPath == null)
                this.heightCacheFilePath = null;
            else
                this.heightCacheFilePath = Path.Combine(repositoryPath, repoRelativeProjectDirectory ?? "", CacheFileName);

            this.cachedHeightAvailable = new Lazy<bool>(() => this.heightCacheFilePath != null && File.Exists(this.heightCacheFilePath));
        }

        public bool CachedHeightAvailable => cachedHeightAvailable.Value;
        
        public CachedHeight GetHeight()
        {
            try
            {
                using (var sr = File.OpenText(this.heightCacheFilePath))
                using (var jsonReader = new JsonTextReader(sr))
                {
                    var cachedHeight = JsonSerializer.Deserialize<CachedHeight>(jsonReader);
                    
                    // Indicates that the project the cache is associated with has moved directories- any cached results may be invalid, so discard
                    if (cachedHeight.RelativeProjectDir != this.repoRelativeProjectDirectory)
                        return null;

                    return cachedHeight;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unexpected error occurred attempting to read and deserialize '{this.heightCacheFilePath}'. Try deleting this file and rebuilding.", ex);
            }
        }

        public void SetHeight(ObjectId commitId, int height, Version baseVersion)
        {
            if (this.heightCacheFilePath == null || commitId == null || commitId == ObjectId.Zero || !Directory.Exists(Path.GetDirectoryName(this.heightCacheFilePath)))
                return;
            
            using (var sw = File.CreateText(this.heightCacheFilePath))
            using (var jsonWriter = new JsonTextWriter(sw))
            {
                jsonWriter.WriteComment("Cached commit height, created by Nerdbank.GitVersioning. Do not modify.");
                JsonSerializer.Serialize(jsonWriter, new CachedHeight(commitId, height, baseVersion, this.repoRelativeProjectDirectory));
            }
        }

        private class ObjectIdConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(((ObjectId) value).Sha);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => new ObjectId(reader.Value as string);

            public override bool CanConvert(Type objectType) => objectType == typeof(ObjectId);
        }
    }

    public class CachedHeight
    {
        public CachedHeight(ObjectId commitId, int height, Version baseVersion, string relativeProjectDir)
        {
            this.CommitId = commitId;
            this.Height = height;
            this.BaseVersion = baseVersion;
            this.RelativeProjectDir = relativeProjectDir;
        }
        
        public Version BaseVersion { get; }
        public int Height { get; }
        public ObjectId CommitId { get; }
        public string RelativeProjectDir { get;  }

        public override string ToString() => $"({CommitId}, {Height}, {BaseVersion})";
    }
}