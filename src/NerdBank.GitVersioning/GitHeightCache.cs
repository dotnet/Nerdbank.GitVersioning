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
        private readonly Version baseVersion;

        private static readonly JsonSerializer JsonSerializer = new JsonSerializer()
        {
            Converters =
            {
                new VersionConverter(),
                new ObjectIdConverter()
            }
        };
        
        /// <summary>
        /// The name used for the cache file. 
        /// </summary>
        public const string CacheFileName = "version.cache.json";
        
        private readonly string heightCacheFilePath;
        private readonly Lazy<bool> cachedHeightAvailable;

        /// <summary>
        /// Creates a new height cache.
        /// </summary>
        /// <param name="repositoryPath">The root path of the repository.</param>
        /// <param name="repoRelativeProjectDirectory">The relative path of the project within the repository.</param>
        /// <param name="version"></param>
        public GitHeightCache(string repositoryPath, string repoRelativeProjectDirectory, Version baseVersion)
        {
            this.repoRelativeProjectDirectory = repoRelativeProjectDirectory;
            this.baseVersion = baseVersion;

            if (repositoryPath == null)
                this.heightCacheFilePath = null;
            else
                this.heightCacheFilePath = Path.Combine(repositoryPath, repoRelativeProjectDirectory ?? "", CacheFileName);

            this.cachedHeightAvailable = new Lazy<bool>(() => this.heightCacheFilePath != null && File.Exists(this.heightCacheFilePath));
        }

        /// <summary>
        /// Determines if a cached version is available.
        /// </summary>
        public bool CachedHeightAvailable => cachedHeightAvailable.Value;
        
        /// <summary>
        /// Fetches the <see cref="CachedHeight"/>. May return null if the cache is not valid.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public CachedHeight GetHeight()
        {
            try
            {
                using (var sr = File.OpenText(this.heightCacheFilePath))
                using (var jsonReader = new JsonTextReader(sr))
                {
                    var cachedHeight = JsonSerializer.Deserialize<CachedHeight>(jsonReader);

                    // Indicates any cached height is irrelevant- every time the base version is bumped, we need to walk an entirely different set of commits 
                    if (cachedHeight.BaseVersion != this.baseVersion)
                        return null;
                    
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

        /// <summary>
        /// Caches the height of a commit, overwriting any previously cached values.
        /// </summary>
        /// <param name="commitId"></param>
        /// <param name="height"></param>
        public void SetHeight(ObjectId commitId, int height)
        {
            if (this.heightCacheFilePath == null || commitId == null || commitId == ObjectId.Zero || !Directory.Exists(Path.GetDirectoryName(this.heightCacheFilePath)))
                return;
            
            using (var sw = File.CreateText(this.heightCacheFilePath))
            using (var jsonWriter = new JsonTextWriter(sw))
            {
                jsonWriter.WriteComment("Cached commit height, created by Nerdbank.GitVersioning. Do not modify.");
                JsonSerializer.Serialize(jsonWriter, new CachedHeight(commitId, height, this.baseVersion, this.repoRelativeProjectDirectory));
            }
        }

        private class ObjectIdConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(((ObjectId) value).Sha);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => new ObjectId(reader.Value as string);

            public override bool CanConvert(Type objectType) => objectType == typeof(ObjectId);
        }
    }

    /// <summary>
    /// The cached git height of a project.
    /// </summary>
    public class CachedHeight
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="commitId"></param>
        /// <param name="height"></param>
        /// <param name="baseVersion"></param>
        /// <param name="relativeProjectDir"></param>
        public CachedHeight(ObjectId commitId, int height, Version baseVersion, string relativeProjectDir)
        {
            this.CommitId = commitId;
            this.Height = height;
            this.BaseVersion = baseVersion;
            this.RelativeProjectDir = relativeProjectDir;
        }
        
        /// <summary>
        /// The base version this cached height was calculated for.
        /// </summary>
        public Version BaseVersion { get; }
        
        /// <summary>
        /// The cached height.
        /// </summary>
        public int Height { get; }
        
        /// <summary>
        /// The commit id for the cached height.
        /// </summary>
        public ObjectId CommitId { get; }
        
        /// <summary>
        /// The relative path of the project this cached height belong to.
        /// </summary>
        public string RelativeProjectDir { get;  }

        /// <inheritdoc cref="Object.ToString"/>
        public override string ToString() => $"({CommitId}, {Height}, {BaseVersion})";
    }
}