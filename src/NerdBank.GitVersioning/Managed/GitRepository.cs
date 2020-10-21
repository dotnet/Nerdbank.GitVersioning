using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Provides access to a Git repository.
    /// </summary>
    public class GitRepository : IDisposable
    {
        private const string HeadFileName = "HEAD";
        private const string GitDirectoryName = ".git";
        private readonly Lazy<GitPack[]> packs;
        private readonly byte[] objectPathBuffer;
        private readonly int objectDirLength;

#if DEBUG
        private Dictionary<GitObjectId, int> histogram = new Dictionary<GitObjectId, int>();
#endif

        /// <summary>
        /// Creates a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <param name="workingDirectory">
        /// The current working directory. This can be a subdirectory of the Git repository.
        /// </param>
        /// <returns>
        /// A <see cref="GitRepository"/> which represents the git repository, or <see langword="null"/>
        /// if no git repository was found.
        /// </returns>
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

        /// <summary>
        /// Creates a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <param name="workingDirectory">
        /// The current working directory. This can be a subdirectory of the Git repository.
        /// </param>
        /// <param name="gitDirectory">
        /// The directory in which git metadata (such as refs,...) is stored.
        /// </param>
        /// <param name="commonDirectory">
        /// The common Git directory, in which Git objects are stored.
        /// </param>
        public static GitRepository Create(string workingDirectory, string gitDirectory, string commonDirectory)
        {
            return new GitRepository(workingDirectory, gitDirectory, commonDirectory);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <param name="workingDirectory">
        /// The current working directory. This can be a subdirectory of the Git repository.
        /// </param>
        /// <param name="gitDirectory">
        /// The directory in which git metadata (such as refs,...) is stored.
        /// </param>
        /// <param name="commonDirectory">
        /// The common Git directory, in which Git objects are stored.
        /// </param>
        public GitRepository(string workingDirectory, string gitDirectory, string commonDirectory)
        {
            this.WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            this.GitDirectory = gitDirectory ?? throw new ArgumentNullException(nameof(gitDirectory));
            this.CommonDirectory = commonDirectory ?? throw new ArgumentNullException(nameof(commonDirectory));

            // Normalize paths
            this.WorkingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.WorkingDirectory));
            this.GitDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.GitDirectory));
            this.CommonDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.CommonDirectory));

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

        /// <summary>
        /// Initializes a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <remarks>
        /// Intended for mocking purposes only.
        /// </remarks>
        protected GitRepository()
        {
        }

        /// <summary>
        /// Gets the path to the current working directory.
        /// </summary>
        public string WorkingDirectory { get; private set; }

        /// <summary>
        /// Gets the path to the Git directory, in which metadata (e.g. references) is stored.
        /// </summary>
        public string GitDirectory { get; private set; }

        /// <summary>
        /// Gets the path to the common directory, in which shared Git data (e.g. objects) are stored.
        /// </summary>
        public string CommonDirectory { get; private set; }

        /// <summary>
        /// Gets the path to the Git object directory. It is a subdirectory of <see cref="CommonDirectory"/>.
        /// </summary>
        public string ObjectDirectory { get; private set; }

        /// <summary>
        /// Gets the encoding used by this Git repsitory.
        /// </summary>
        public static Encoding Encoding => Encoding.UTF8;

        /// <summary>
        /// Returns the current HEAD as a reference (if available) or a Git object id.
        /// </summary>
        /// <returns>
        /// The current HEAD as a reference (if available) or a Git object id.
        /// </returns>
        public object GetHeadAsReferenceOrSha()
        {
            using (var stream = File.OpenRead(Path.Combine(this.GitDirectory, HeadFileName)))
            {
                return GitReferenceReader.ReadReference(stream);
            }
        }

        /// <summary>
        /// Gets the object ID of the current HEAD.
        /// </summary>
        /// <returns>
        /// The object ID of the current HEAD.
        /// </returns>
        public GitObjectId GetHeadCommitSha()
        {
            var reference = this.GetHeadAsReferenceOrSha();
            var objectId = this.ResolveReference(reference);
            return objectId;
        }

        /// <summary>
        /// Gets the current HEAD commit, if available.
        /// </summary>
        /// <returns>
        /// The current HEAD commit, or <see langword="null"/> if not available.
        /// </returns>
        public GitCommit? GetHeadCommit()
        {
            var headCommitId = this.GetHeadCommitSha();

            if (headCommitId == GitObjectId.Empty)
            {
                return null;
            }

            return this.GetCommit(headCommitId);
        }

        /// <summary>
        /// Gets a commit by its Git object Id.
        /// </summary>
        /// <param name="sha">
        /// The Git object Id of the commit.
        /// </param>
        /// <returns>
        /// The requested commit.
        /// </returns>
        public GitCommit GetCommit(GitObjectId sha)
        {
            using (Stream stream = this.GetObjectBySha(sha, "commit"))
            {
                if (stream == null)
                {
                    throw new GitException($"The commit {sha} was not found in this repository.");
                }

                return GitCommitReader.Read(stream, sha);
            }
        }

        /// <summary>
        /// Gets an entry in a git tree.
        /// </summary>
        /// <param name="treeId">
        /// The Git object Id of the Git tree.
        /// </param>
        /// <param name="nodeName">
        /// The name of the node in the Git tree.
        /// </param>
        /// <returns>
        /// The object Id of the requested entry. Returns <see cref="GitObjectId.Empty"/> if the entry
        /// could not be found.
        /// </returns>
        public GitObjectId GetTreeEntry(GitObjectId treeId, ReadOnlySpan<byte> nodeName)
        {
            using (Stream treeStream = this.GetObjectBySha(treeId, "tree"))
            {
                return GitTreeStreamingReader.FindNode(treeStream, nodeName);
            }
        }

        /// <summary>
        /// Gets a Git object by its Git object Id.
        /// </summary>
        /// <param name="sha">
        /// The Git object id of the object to retrieve.
        /// </param>
        /// <param name="objectType">
        /// The type of object to retrieve.
        /// </param>
        /// <returns>
        /// A <see cref="Stream"/> which represents the requested object.
        /// </returns>
        /// <exception cref="GitException">
        /// The requested object could not be found.
        /// </exception>
        /// <remarks>
        /// As a special case, a <see langword="null"/> value will be returned for
        /// <see cref="GitObjectId.Empty"/>.
        /// </remarks>
        public Stream GetObjectBySha(GitObjectId sha, string objectType)
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

            Stream value = this.GetObjectByPath(sha, objectType);

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

            throw new GitException($"An {objectType} object with SHA {sha} could not be found.");
        }

        /// <summary>
        /// Gets cache usage statistics.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> which represents the cache usage statistics.
        /// </returns>
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

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Git Repository: {this.WorkingDirectory}";
        }

        /// <inheritdoc/>
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

        private Stream GetObjectByPath(GitObjectId sha, string objectType)
        {
            sha.CopyToUnicodeString(0, 1, this.objectPathBuffer.AsSpan(this.objectDirLength + 2, 4));
            sha.CopyToUnicodeString(1, 19, this.objectPathBuffer.AsSpan(this.objectDirLength + 2 + 4 + 2));

            if (!FileHelpers.TryOpen(this.objectPathBuffer, CreateFileFlags.FILE_ATTRIBUTE_NORMAL | CreateFileFlags.FILE_FLAG_SEQUENTIAL_SCAN, out var compressedFile))
            {
                return null;
            }

            var file = new GitObjectStream(compressedFile, objectType);

            if (string.CompareOrdinal(file.ObjectType, objectType) != 0)
            {
                throw new GitException($"Got a {file.ObjectType} instead of a {objectType} when opening object {sha}");
            }

            return file;
        }

        private GitObjectId ResolveReference(object reference)
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
    }
}
