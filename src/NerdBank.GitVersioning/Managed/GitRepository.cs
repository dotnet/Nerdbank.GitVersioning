using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    internal class GitRepository : IDisposable
    {
        private const string HeadFileName = "HEAD";
        private const string GitDirectoryName = ".git";
        private readonly Lazy<GitPack[]> packs;
        private readonly byte[] objectPathBuffer;
        private readonly int objectDirLength;

        public static GitRepository Create(string rootDirectory)
        {
            return Create(rootDirectory, Path.Combine(rootDirectory, GitDirectoryName));
        }

        public static GitRepository Create(string rootDirectory, string gitDirectory)
        {
            if (Directory.Exists(rootDirectory) && Directory.Exists(gitDirectory))
            {
                return new GitRepository(rootDirectory, gitDirectory);
            }

            return null;
        }

        public GitRepository(string rootDirectory, string gitDirectory)
        {
            this.RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            this.GitDirectory = gitDirectory ?? throw new ArgumentNullException(nameof(gitDirectory));

            if (FileHelpers.TryOpen(
                Path.Combine(this.GitDirectory, "objects", "info", "alternates"),
                CreateFileFlags.FILE_ATTRIBUTE_NORMAL,
                out var alternateStream))
            {
                Span<byte> filename = stackalloc byte[4096];
                var length = alternateStream.Read(filename);
                length = filename.IndexOf((byte)'\n');

                this.ObjectDirectory = Path.Combine(gitDirectory, "objects", Encoding.GetString(filename.Slice(0, length)));
            }
            else
            {
                this.ObjectDirectory = Path.Combine(this.GitDirectory, "objects");
            }


            this.objectDirLength = Encoding.Unicode.GetByteCount(this.ObjectDirectory);
            int pathLength = this.objectDirLength;
            pathLength += 2; // '/'
            pathLength += 4; // 'xy'
            pathLength += 2; // '/'
            pathLength += 76; // 19 bytes * 2 chars / byte * 2 bytes / char
            pathLength += 2; // Trailing 0 character
            this.objectPathBuffer = new byte[pathLength];

            Encoding.Unicode.GetBytes(this.ObjectDirectory, this.objectPathBuffer.AsSpan(0, this.objectDirLength));
            Encoding.Unicode.GetBytes("/", this.objectPathBuffer.AsSpan(this.objectDirLength, 2));
            Encoding.Unicode.GetBytes("/".ToCharArray().AsSpan(), this.objectPathBuffer.AsSpan(this.objectDirLength + 2 + 4, 2));
            this.objectPathBuffer[pathLength - 2] = 0; // Make sure to initialize with zeros
            this.objectPathBuffer[pathLength - 1] = 0;

            this.packs = new Lazy<GitPack[]>(this.LoadPacks);
        }

        public string RootDirectory { get; private set; }
        public string GitDirectory { get; private set; }
        public string ObjectDirectory { get; private set; }

        public static Encoding Encoding => Encoding.ASCII;

        public GitObjectId GetHeadCommitSha()
        {
            using (var stream = File.OpenRead(Path.Combine(this.GitDirectory, HeadFileName)))
            {
                var reference = GitReferenceReader.ReadReference(stream);
                var objectId = this.ResolveReference(reference);
                return objectId;
            }
        }

        public GitCommit GetHeadCommit()
        {
            return this.GetCommit(this.GetHeadCommitSha());
        }

        public GitCommit GetCommit(GitObjectId sha)
        {
            using (Stream stream = this.GetObjectBySha(sha, "commit"))
            {
                return GitCommitReader.Read(stream, sha);
            }
        }

        public GitObjectId GetTreeEntry(GitObjectId treeId, ReadOnlySpan<byte> nodeName)
        {
            using (Stream treeStream = this.GetObjectBySha(treeId, "tree"))
            {
                return GitTreeStreamingReader.FindNode(treeStream, nodeName);
            }
        }

#if DEBUG
        private Dictionary<GitObjectId, int> histogram = new Dictionary<GitObjectId, int>();
#endif

        public Stream GetObjectBySha(GitObjectId sha, string objectType, bool seekable = false)
        {
#if DEBUG
            if (!this.histogram.TryAdd(sha, 1))
            {
                this.histogram[sha] += 1;
            }
#endif

            Stream value = this.GetObjectByPath(sha, objectType, seekable);

            if (value != null)
            {
                return value;
            }

            foreach (var pack in this.packs.Value)
            {
                if (pack.TryGetObject(sha, objectType, out Stream packValue))
                {
                    return packValue;
                }
            }

            throw new GitException();
        }

        public Stream GetObjectByPath(GitObjectId sha, string objectType, bool seekable)
        {
            sha.CreateUnicodeString(0, 1, this.objectPathBuffer.AsSpan(this.objectDirLength + 2, 4));
            sha.CreateUnicodeString(1, 19, this.objectPathBuffer.AsSpan(this.objectDirLength + 2 + 4 + 2));

            if (!FileHelpers.TryOpen(this.objectPathBuffer, CreateFileFlags.FILE_ATTRIBUTE_NORMAL | CreateFileFlags.FILE_FLAG_SEQUENTIAL_SCAN, out var compressedFile))
            {
                return null;
            }

            var file = GitObjectStream.Create(compressedFile, -1);
            file.ReadObjectTypeAndLength(objectType);

            if (string.CompareOrdinal(file.ObjectType, objectType) != 0)
            {
                throw new GitException();
            }

            if (seekable)
            {
                return new GitPackMemoryCacheStream(file);
            }
            else
            {
                return file;
            }
        }

        public GitObjectId ResolveReference(string reference)
        {
            using (var stream = File.OpenRead(Path.Combine(this.GitDirectory, reference)))
            {
                Span<byte> objectId = stackalloc byte[40];
                stream.Read(objectId);

                return GitObjectId.ParseHex(objectId);
            }
        }

        public override string ToString()
        {
            return $"Git Repository: {this.RootDirectory}";
        }

        private GitPack[] LoadPacks()
        {
            var packDirectory = Path.Combine(this.ObjectDirectory, "pack/");

            if (!Directory.Exists(packDirectory))
            {
                return Array.Empty<GitPack>();
            }

            var indexFiles = Directory.GetFiles(packDirectory, "*.idx");
            GitPack[] packs = new GitPack[indexFiles.Length];

            for (int i = 0; i < indexFiles.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(indexFiles[i]);
                packs[i] = new GitPack(this, name);
            }

            return packs;
        }

        public Func<GitPack, GitPackCache> CacheFactory { get; set; } = (cache) => new GitPackMemoryCache(cache);

        public string GetCacheStatistics()
        {
            StringBuilder builder = new StringBuilder();

#if DEBUG
            int histogramCount = 25;

            builder.AppendLine("Overall repository:");
            builder.AppendLine($"Top {histogramCount} / {this.histogram.Count} items:");

            foreach (var item in this.histogram.OrderByDescending(v => v.Value).Take(25))
            {
                builder.AppendLine($"  {item.Key}: {item.Value}");
            }

            builder.AppendLine();
#endif

            foreach (var pack in this.packs.Value)
            {
                pack.GetCacheStatistics(builder);
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            if (this.packs.IsValueCreated)
            {
                foreach (var pack in this.packs.Value)
                {
                    pack.Dispose();
                }
            }
        }
    }
}
