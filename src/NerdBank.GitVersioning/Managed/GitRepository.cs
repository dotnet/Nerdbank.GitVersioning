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

        public static GitRepository Create(string workingDirectory)
        {
            // Search for the top-level directory of the current git repository. This is the directory
            // which contains a directory of file named .git.
            // Loop until Path.GetDirectoryName returns null; in this case, we've reached the root of
            // the file system (and we're not in a git repository).
            while (workingDirectory != null
                && !File.Exists(Path.Combine(workingDirectory, GitDirectoryName))
                && !Directory.Exists(Path.Combine(workingDirectory, GitDirectoryName)))
            {
                workingDirectory = Path.GetDirectoryName(workingDirectory);
            }

            if (workingDirectory == null)
            {
                return null;
            }

            var gitDirectory = Path.Combine(workingDirectory, GitDirectoryName);

            if (File.Exists(gitDirectory))
            {
                // This is a worktree, and the path to the git directory is stored in the .git file
                var worktreeConfig = File.ReadAllText(gitDirectory);

                var gitDirStart = worktreeConfig.IndexOf("gitdir: ");
                var gitDirEnd = worktreeConfig.IndexOf("\n", gitDirStart);

                gitDirectory = worktreeConfig.Substring(gitDirStart + 8, gitDirEnd - gitDirStart - 8);
            }

            if (!Directory.Exists(gitDirectory))
            {
                return null;
            }

            var commonDirectory = gitDirectory;

            var commonDirFile = Path.Combine(gitDirectory, "commondir");

            if (File.Exists(commonDirFile))
            {
                var commonDirectoryRelativePath = File.ReadAllText(commonDirFile).Trim('\n');
                commonDirectory = Path.Combine(gitDirectory, commonDirectoryRelativePath);
            }

            return new GitRepository(workingDirectory, gitDirectory, commonDirectory);
        }

        public static GitRepository Create(string workingDirectory, string gitDirectory, string commonDirectory)
        {
            return new GitRepository(workingDirectory, gitDirectory, commonDirectory);
        }

        public GitRepository(string workingDirectory, string gitDirectory, string commonDirectory)
        {
            this.WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            this.GitDirectory = gitDirectory ?? throw new ArgumentNullException(nameof(gitDirectory));
            this.CommonDirectory = commonDirectory ?? throw new ArgumentNullException(nameof(commonDirectory));

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
                this.ObjectDirectory = Path.Combine(this.CommonDirectory, "objects");
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

        public string WorkingDirectory { get; private set; }
        public string GitDirectory { get; private set; }
        public string CommonDirectory { get; private set; }
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

        public GitCommit? GetHeadCommit()
        {
            var headCommitId = this.GetHeadCommitSha();

            if(headCommitId == GitObjectId.Empty)
            {
                return null;
            }

            return this.GetCommit(headCommitId);
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
            if (sha == GitObjectId.Empty)
            {
                return null;
            }

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

        public GitObjectId ResolveReference(object reference)
        {
            if (reference is string)
            {
                if (!FileHelpers.TryOpen(Path.Combine(this.GitDirectory, (string)reference), CreateFileFlags.FILE_ATTRIBUTE_NORMAL, out FileStream stream))
                {
                    return GitObjectId.Empty;
                }

                using (stream)
                {
                    Span<byte> objectId = stackalloc byte[40];
                    stream.Read(objectId);

                    return GitObjectId.ParseHex(objectId);
                }
            }
            else if (reference is GitObjectId)
            {
                return (GitObjectId)reference;
            }
            else
            {
                throw new GitException();
            }
        }

        public override string ToString()
        {
            return $"Git Repository: {this.WorkingDirectory}";
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
