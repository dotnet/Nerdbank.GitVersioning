using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LibGit2Sharp;

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
        private readonly string heightCacheFilePath;
        public const string CacheFileName = "version_height.cache";
        
        public GitHeightCache(string repositoryPath, string repoRelativeProjectDirectory)
        {
            if (repositoryPath == null)
                this.heightCacheFilePath = null;
            else
                this.heightCacheFilePath = Path.Combine(repositoryPath, repoRelativeProjectDirectory ?? "", CacheFileName);
        }

        public bool CachedHeightAvailable => this.heightCacheFilePath != null && File.Exists(this.heightCacheFilePath);
        
        public (ObjectId commitId, int height) GetHeight()
        {
            try
            {
                using (var sr = File.OpenText(this.heightCacheFilePath))
                {
                    // skip past comment header
                    sr.ReadLine();
                    
                    var line = sr.ReadLine();
                    
                    // We only cache a single commit id + height pair
                    if (!sr.EndOfStream)
                        throw new InvalidOperationException($"Unexpected additional lines in '{this.heightCacheFilePath}', possible corruption. Please delete this file and rebuild.");
                    
                    var match = Regex.Match(line, "^([a-f0-9]+)=([0-9]+)$");
                    
                    if (!match.Success)
                        throw new InvalidOperationException($"Contents of '{this.heightCacheFilePath}' were invalid ('{line}'), should be '[git_commit_id]=[height]'. Please delete this file and rebuild.");

                    if (!ObjectId.TryParse(match.Groups[1].Value, out var objectId))
                        throw new FormatException($"The cached commit id in '{this.heightCacheFilePath}' was not recognized as an git object id.");
                    
                    if (!int.TryParse(match.Groups[2].Value, out var height))
                        throw new FormatException($"The cached commit height in '{this.heightCacheFilePath}' was not recognized as an integer.");

                    return (objectId, height);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unexpected error occurred attempting to read and parse '{this.heightCacheFilePath}'. Try deleting this file and rebuilding.", ex);
            }
        }

        public void SetHeight(ObjectId commitId, int height)
        {
            if (this.heightCacheFilePath == null || commitId == null || commitId == ObjectId.Zero || !Directory.Exists(Path.GetDirectoryName(this.heightCacheFilePath)))
                return;
            
            using (var sw = File.CreateText(this.heightCacheFilePath))
            {
                sw.WriteLine("# Cached commit height, created by Nerdbank.GitVersioning. Do not modify.");
                sw.WriteLine($"{commitId}={height}");
            }
        }
    }
}