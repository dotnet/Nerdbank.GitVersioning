#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Validation;

namespace Nerdbank.GitVersioning.ManagedGit
{
    /// <summary>
    /// Provides access to a Git repository.
    /// </summary>
    public class GitRepository : IDisposable
    {
        private const string HeadFileName = "HEAD";
        private const string GitDirectoryName = ".git";
        private readonly Lazy<ReadOnlyMemory<GitPack>> packs;

        /// <summary>
        /// UTF-16 encoded string.
        /// </summary>
        private readonly char[] objectPathBuffer;

        private readonly List<GitRepository> alternates = new List<GitRepository>();

#if DEBUG
        private Dictionary<GitObjectId, int> histogram = new Dictionary<GitObjectId, int>();
#endif

        /// <summary>
        /// Creates a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <param name="workingDirectory"><inheritdoc cref="GitRepository(string, string, string, string)" path="/param[@name='workingDirectory']" /></param>
        /// <returns>
        /// A <see cref="GitRepository"/> which represents the git repository, or <see langword="null"/>
        /// if no git repository was found.
        /// </returns>
        public static GitRepository? Create(string? workingDirectory)
        {
            if (!GitContext.TryFindGitPaths(workingDirectory, out string? gitDirectory, out string? workingTreeDirectory, out string? workingTreeRelativePath))
            {
                return null;
            }

            string commonDirectory = gitDirectory;
            string commonDirFile = Path.Combine(gitDirectory, "commondir");

            if (File.Exists(commonDirFile))
            {
                var commonDirectoryRelativePath = File.ReadAllText(commonDirFile).Trim('\n');
                commonDirectory = Path.Combine(gitDirectory, commonDirectoryRelativePath);
            }

            string objectDirectory = Path.Combine(commonDirectory, "objects");

            return new GitRepository(workingDirectory!, gitDirectory, commonDirectory, objectDirectory);
        }

        /// <summary>
        /// Creates a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <param name="workingDirectory"><inheritdoc cref="GitRepository(string, string, string, string)" path="/param[@name='workingDirectory']" /></param>
        /// <param name="gitDirectory"><inheritdoc cref="GitRepository(string, string, string, string)" path="/param[@name='gitDirectory']" /> </param>
        /// <param name="commonDirectory"><inheritdoc cref="GitRepository(string, string, string, string)" path="/param[@name='commonDirectory']" /></param>
        /// <param name="objectDirectory"><inheritdoc cref="GitRepository(string, string, string, string)" path="/param[@name='objectDirectory']" /></param>
        public static GitRepository Create(string workingDirectory, string gitDirectory, string commonDirectory, string objectDirectory)
        {
            return new GitRepository(workingDirectory, gitDirectory, commonDirectory, objectDirectory);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitRepository"/> class.
        /// </summary>
        /// <param name="workingDirectory">
        /// The current working directory. This can be a subdirectory of the Git repository.
        /// </param>
        /// <param name="gitDirectory">
        /// The directory in which the git HEAD file is stored. This is the .git directory unless the working directory is a worktree.
        /// </param>
        /// <param name="commonDirectory">
        /// The common Git directory, which is parent to the objects, refs, and other directories.
        /// </param>
        /// <param name="objectDirectory">
        /// The object directory in which Git objects are stored.
        /// </param>
        public GitRepository(string workingDirectory, string gitDirectory, string commonDirectory, string objectDirectory)
        {
            this.WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            this.GitDirectory = gitDirectory ?? throw new ArgumentNullException(nameof(gitDirectory));
            this.CommonDirectory = commonDirectory ?? throw new ArgumentNullException(nameof(commonDirectory));
            this.ObjectDirectory = objectDirectory ?? throw new ArgumentNullException(nameof(objectDirectory));

            // Normalize paths
            this.WorkingDirectory = TrimEndingDirectorySeparator(Path.GetFullPath(this.WorkingDirectory));
            this.GitDirectory = TrimEndingDirectorySeparator(Path.GetFullPath(this.GitDirectory));
            this.CommonDirectory = TrimEndingDirectorySeparator(Path.GetFullPath(this.CommonDirectory));
            this.ObjectDirectory = TrimEndingDirectorySeparator(Path.GetFullPath(this.ObjectDirectory));

            if (FileHelpers.TryOpen(
                Path.Combine(this.ObjectDirectory, "info", "alternates"),
                out var alternateStream))
            {
                // There's not a lot of documentation on git alternates; but this StackOverflow question
                // https://stackoverflow.com/questions/36123655/what-is-the-git-alternates-mechanism
                // provides a good starting point.
                Span<byte> alternates = stackalloc byte[4096];
                var length = alternateStream!.Read(alternates);
                alternates = alternates.Slice(0, length);

                foreach (var alternate in ParseAlternates(alternates))
                {
                    this.alternates.Add(
                        GitRepository.Create(
                            workingDirectory,
                            gitDirectory,
                            commonDirectory,
                            objectDirectory: Path.GetFullPath(Path.Combine(this.ObjectDirectory, alternate))));
                }
            }


            int pathLengthInChars = this.ObjectDirectory.Length
                + 1 // '/'
                + 2 // 'xy' is first byte as 2 hex characters.
                + 1 // '/'
                + 38 // 19 bytes * 2 hex chars each
                + 1; // Trailing null character
            this.objectPathBuffer = new char[pathLengthInChars];
            this.ObjectDirectory.CopyTo(0, this.objectPathBuffer, 0, this.ObjectDirectory.Length);

            this.objectPathBuffer[this.ObjectDirectory.Length] = '/';
            this.objectPathBuffer[this.ObjectDirectory.Length + 3] = '/';
            this.objectPathBuffer[pathLengthInChars - 1] = '\0'; // Make sure to initialize with zeros

            this.packs = new Lazy<ReadOnlyMemory<GitPack>>(this.LoadPacks);
        }

        // TODO: read from Git settings
        /// <summary>
        /// Gets a value indicating whether this Git repository is case-insensitive.
        /// </summary>
        public bool IgnoreCase { get; private set; } = false;

        /// <summary>
        /// Gets the path to the current working directory.
        /// </summary>
        public string WorkingDirectory { get; private set; }

        /// <summary>
        /// Gets the path to the Git directory, in which at minimum HEAD is stored.
        /// Use <see cref="CommonDirectory"/> for all other metadata (e.g. references, configuration).
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
        /// Gets the encoding used by this Git repository.
        /// </summary>
        public static Encoding Encoding => Encoding.UTF8;

        /// <summary>
        /// Shortens the object id
        /// </summary>
        /// <param name="objectId">
        /// The object Id to shorten.
        /// </param>
        /// <param name="minimum">
        /// The minimum string length.
        /// </param>
        /// <returns>
        /// The short object id.
        /// </returns>
        public string ShortenObjectId(GitObjectId objectId, int minimum)
        {
            var sha = objectId.ToString();

            for (int length = minimum; length < sha.Length; length += 2)
            {
                var objectish = sha.Substring(0, length);

                if (this.Lookup(objectish) is not null)
                {
                    return objectish;
                }
            }

            return sha;
        }

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
            return this.Lookup("HEAD") ?? GitObjectId.Empty;
        }

        /// <summary>
        /// Gets the current HEAD commit, if available.
        /// </summary>
        /// <param name="readAuthor">
        /// A value indicating whether to populate the <see cref="GitCommit.Author"/> field.
        /// </param>
        /// <returns>
        /// The current HEAD commit, or <see langword="null"/> if not available.
        /// </returns>
        public GitCommit? GetHeadCommit(bool readAuthor = false)
        {
            var headCommitId = this.GetHeadCommitSha();

            if (headCommitId == GitObjectId.Empty)
            {
                return null;
            }

            return this.GetCommit(headCommitId, readAuthor);
        }

        /// <summary>
        /// Gets a commit by its Git object Id.
        /// </summary>
        /// <param name="sha">
        /// The Git object Id of the commit.
        /// </param>
        /// <param name="readAuthor">
        /// A value indicating whether to populate the <see cref="GitCommit.Author"/> field.
        /// </param>
        /// <returns>
        /// The requested commit.
        /// </returns>
        public GitCommit GetCommit(GitObjectId sha, bool readAuthor = false)
        {
            using (Stream? stream = this.GetObjectBySha(sha, "commit"))
            {
                if (stream is null)
                {
                    throw new GitException($"The commit {sha} was not found in this repository.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
                }

                return GitCommitReader.Read(stream, sha, readAuthor);
            }
        }

        /// <summary>
        /// Parses any committish to an object id.
        /// </summary>
        /// <param name="objectish">Any "objectish" string (e.g. commit ID (partial or full), branch name, tag name, or "HEAD").</param>
        /// <returns>The object ID referenced by <paramref name="objectish"/> if found; otherwise <see langword="null"/>.</returns>
        public GitObjectId? Lookup(string objectish)
        {
            bool skipObjectIdLookup = false;

            if (objectish == "HEAD")
            {
                var reference = this.GetHeadAsReferenceOrSha();
                if (reference is GitObjectId headObjectId)
                {
                    return headObjectId;
                }

                objectish = (string)reference;
            }

            var possibleLooseFileMatches = new List<string>();
            if (objectish.StartsWith("refs/", StringComparison.Ordinal))
            {
                // Match on loose ref files by their canonical name.
                possibleLooseFileMatches.Add(Path.Combine(this.CommonDirectory, objectish));
                skipObjectIdLookup = true;
            }
            else
            {
                // Look for simple names for branch or tag.
                possibleLooseFileMatches.Add(Path.Combine(this.CommonDirectory, "refs", "heads", objectish));
                possibleLooseFileMatches.Add(Path.Combine(this.CommonDirectory, "refs", "tags", objectish));
                possibleLooseFileMatches.Add(Path.Combine(this.CommonDirectory, "refs", "remotes", objectish));
            }

            if (possibleLooseFileMatches.FirstOrDefault(File.Exists) is string existingPath)
            {
                return GitObjectId.Parse(File.ReadAllText(existingPath).TrimEnd());
            }

            // Match in packed-refs file.
            string packedRefPath = Path.Combine(this.CommonDirectory, "packed-refs");
            if (File.Exists(packedRefPath))
            {
                using var refReader = File.OpenText(packedRefPath);
                string? line;
                while ((line = refReader.ReadLine()) is object)
                {
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string refName = line.Substring(41);
                    if (string.Equals(refName, objectish, StringComparison.Ordinal))
                    {
                        return GitObjectId.Parse(line.Substring(0, 40));
                    }
                    else if (!objectish.StartsWith("refs/", StringComparison.Ordinal))
                    {
                        // Not a canonical ref, so try heads and tags
                        if (string.Equals(refName, "refs/heads/" + objectish, StringComparison.Ordinal))
                        {
                            return GitObjectId.Parse(line.Substring(0, 40));
                        }
                        else if (string.Equals(refName, "refs/tags/" + objectish, StringComparison.Ordinal))
                        {
                            return GitObjectId.Parse(line.Substring(0, 40));
                        }
                        else if (string.Equals(refName, "refs/remotes/" + objectish, StringComparison.Ordinal))
                        {
                            return GitObjectId.Parse(line.Substring(0, 40));
                        }
                    }
                }
            }

            if (skipObjectIdLookup)
            {
                return null;
            }

            if (objectish.Length == 40)
            {
                return GitObjectId.Parse(objectish);
            }

            var possibleObjectIds = new List<GitObjectId>();
            if (objectish.Length > 2 && objectish.Length < 40)
            {
                // Search for _any_ object whose id starts with objectish in the object database
                var directory = Path.Combine(this.ObjectDirectory, objectish.Substring(0, 2));

                if (Directory.Exists(directory))
                {
                    var files = Directory.GetFiles(directory, $"{objectish.Substring(2)}*");

                    foreach (var file in files)
                    {
                        var objectId = $"{objectish.Substring(0, 2)}{Path.GetFileName(file)}";
                        possibleObjectIds.Add(GitObjectId.Parse(objectId));
                    }
                }

                // Search for _any_ object whose id starts with objectish in the packfile
                bool endsWithHalfByte = objectish.Length % 2 == 1;
                if (endsWithHalfByte)
                {
                    // Add one more character so hex can be converted to bytes.
                    // The bit length to be compared will not consider the last four bits.
                    objectish += "0";
                }

                if (objectish.Length <= 40 && objectish.Length % 2 == 0)
                {
                    Span<byte> decodedHex = stackalloc byte[objectish.Length / 2];
                    if (TryConvertHexStringToByteArray(objectish, decodedHex))
                    {
                        foreach (var pack in this.packs.Value.Span)
                        {
                            var objectId = pack.Lookup(decodedHex, endsWithHalfByte);

                            // It's possible for the same object to be present in both the object database and the pack files,
                            // or in multiple pack files.
                            if (objectId is not null && !possibleObjectIds.Contains(objectId.Value))
                            {
                                if (possibleObjectIds.Count > 0)
                                {
                                    // If objectish already resolved to at least one object which is different from the current
                                    // object id, objectish is not well-defined; so stop resolving and return null instead.
                                    return null;
                                }
                                else
                                {
                                    possibleObjectIds.Add(objectId.Value);
                                }
                            }
                        }
                    }
                }
            }

            if (possibleObjectIds.Count == 1)
            {
                return possibleObjectIds[0];
            }

            return null;
        }

        /// <summary>
        /// Gets a tree object by its Git object Id.
        /// </summary>
        /// <param name="sha">
        /// The Git object Id of the tree.
        /// </param>
        /// <returns>
        /// The requested tree.
        /// </returns>
        public GitTree GetTree(GitObjectId sha)
        {
            using (Stream? stream = this.GetObjectBySha(sha, "tree"))
            {
                if (stream is null)
                {
                    throw new GitException($"The tree {sha} was not found in this repository.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
                }

                return GitTreeReader.Read(stream, sha);
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
            using (Stream? treeStream = this.GetObjectBySha(treeId, "tree"))
            {
                if (treeStream is null)
                {
                    throw new GitException($"The tree {treeId} was not found in this repository.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
                }

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
        public Stream? GetObjectBySha(GitObjectId sha, string objectType)
        {
            if (sha == GitObjectId.Empty)
            {
                return null;
            }

            if (this.TryGetObjectBySha(sha, objectType, out Stream? value))
            {
                return value;
            }
            else
            {
                throw new GitException($"An {objectType} object with SHA {sha} could not be found.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
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
        /// <param name="value">
        /// An output parameter which retrieves the requested Git object.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the object could be found; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        public bool TryGetObjectBySha(GitObjectId sha, string objectType, out Stream? value)
        {
#if DEBUG
            if (!this.histogram.TryAdd(sha, 1))
            {
                this.histogram[sha] += 1;
            }
#endif

            foreach (var pack in this.packs.Value.Span)
            {
                if (pack.TryGetObject(sha, objectType, out value))
                {
                    return true;
                }
            }

            if (this.TryGetObjectByPath(sha, objectType, out value))
            {
                return true;
            }

            foreach (var alternate in this.alternates)
            {
                if (alternate.TryGetObjectBySha(sha, objectType, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
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

            foreach (var pack in this.packs.Value.Span)
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
                foreach (var pack in this.packs.Value.Span)
                {
                    pack.Dispose();
                }
            }
        }

        private bool TryGetObjectByPath(GitObjectId sha, string objectType, [NotNullWhen(true)] out Stream? value)
        {
            sha.CopyAsHex(0, 1, this.objectPathBuffer.AsSpan(this.ObjectDirectory.Length + 1, 2));
            sha.CopyAsHex(1, 19, this.objectPathBuffer.AsSpan(this.ObjectDirectory.Length + 1 + 2 + 1));

            if (!FileHelpers.TryOpen(this.objectPathBuffer, out var compressedFile))
            {
                value = null;
                return false;
            }

            var objectStream = new GitObjectStream(compressedFile!, objectType);

            if (string.CompareOrdinal(objectStream.ObjectType, objectType) != 0)
            {
                throw new GitException($"Got a {objectStream.ObjectType} instead of a {objectType} when opening object {sha}");
            }

            value = objectStream;
            return true;
        }

        private ReadOnlyMemory<GitPack> LoadPacks()
        {
            var packDirectory = Path.Combine(this.ObjectDirectory, "pack/");

            if (!Directory.Exists(packDirectory))
            {
                return Array.Empty<GitPack>();
            }

            var indexFiles = Directory.GetFiles(packDirectory, "*.idx");
            var packs = new GitPack[indexFiles.Length];
            int addCount = 0;

            for (int i = 0; i < indexFiles.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(indexFiles[i]);
                var indexPath = Path.Combine(this.ObjectDirectory, "pack", $"{name}.idx");
                var packPath = Path.Combine(this.ObjectDirectory, "pack", $"{name}.pack");

                // Only proceed if both the packfile and index file exist.
                if (File.Exists(packPath))
                {
                    packs[addCount++] = new GitPack(this.GetObjectBySha, indexPath, packPath);
                }
            }

            return packs.AsMemory(0, addCount);
        }

        private static string TrimEndingDirectorySeparator(string path)
        {
#if NETSTANDARD2_0
            if (string.IsNullOrEmpty(path) || path.Length == 1)
            {
                return path;
            }

            var last = path[path.Length - 1];

            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
            {
                return path.Substring(0, path.Length - 1);
            }

            return path;
#else
            return Path.TrimEndingDirectorySeparator(path);
#endif
        }

        private static bool TryConvertHexStringToByteArray(string hexString, Span<byte> data)
        {
            // https://stackoverflow.com/questions/321370/how-can-i-convert-a-hex-string-to-a-byte-array
            if (hexString.Length % 2 != 0)
            {
                data = null;
                return false;
            }

            Requires.Argument(data.Length == hexString.Length / 2, nameof(data), "Length must be exactly half that of " + nameof(hexString) + ".");
            for (int index = 0; index < data.Length; index++)
            {
#if !NETSTANDARD2_0
                ReadOnlySpan<char> byteValue = hexString.AsSpan(index * 2, 2);
                if (!byte.TryParse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[index]))
                {
                    return false;
                }
#else
                string byteValue = hexString.Substring(index * 2, 2);
                if (!byte.TryParse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[index]))
                {
                    return false;
                }
#endif
            }

            return true;
        }

        /// <summary>
        /// Decodes a sequence of bytes from the specified byte array into a <see cref="string"/>.
        /// </summary>
        /// <param name="bytes">
        /// The span containing the sequence of UTF-8 bytes to decode.
        /// </param>
        /// <returns>
        /// A <see cref="string"/> that contains the results of decoding the specified sequence of bytes.
        /// </returns>
        public static unsafe string GetString(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* pBytes = bytes)
            {
                return Encoding.GetString(pBytes, bytes.Length);
            }
        }

        /// <summary>
        /// Parses the contents of the alternates file, and returns a list of (relative) paths to the alternate object directories.
        /// </summary>
        /// <param name="alternates">
        /// The contents of the alternates files.
        /// </param>
        /// <returns>
        /// A list of (relative) paths to the alternate object directories.
        /// </returns>
        public static List<string> ParseAlternates(ReadOnlySpan<byte> alternates)
            => ParseAlternates(alternates, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 2 : 0);

        /// <summary>
        /// Parses the contents of the alternates file, and returns a list of (relative) paths to the alternate object directories.
        /// </summary>
        /// <param name="alternates">
        /// The contents of the alternates files.
        /// </param>
        /// <param name="skipCount">
        /// The number of bytes to skip in the span when looking for a delimiter.
        /// </param>
        /// <returns>
        /// A list of (relative) paths to the alternate object directories.
        /// </returns>
        public static List<string> ParseAlternates(ReadOnlySpan<byte> alternates, int skipCount)
        {
            List<string> values = new List<string>();

            int index;
            int length;

            // The alternates path is colon (:)-separated. On Windows, there may be full paths, such as
            // C:/Users/username/source/repos/nbgv/.git, which also contain a colon. Because the colon
            // can only appear at the second position, we skip the first two characters (e.g. C:) on Windows.
            while (alternates.Length > skipCount)
            {
                index = alternates.Slice(skipCount).IndexOfAny((byte)':', (byte)'\n');
                length = index > 0 ? skipCount + index : alternates.Length;

                values.Add(GetString(alternates.Slice(0, length)));
                alternates = index > 0 ? alternates.Slice(length + 1) : Span<byte>.Empty;
            }

            return values;
        }
    }
}
