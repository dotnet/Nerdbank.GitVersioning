// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Validation;

namespace Nerdbank.GitVersioning.ManagedGit;

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
    private readonly Dictionary<GitObjectId, int> histogram = new Dictionary<GitObjectId, int>();
#endif

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
            out FileStream? alternateStream))
        {
            // There's not a lot of documentation on git alternates; but this StackOverflow question
            // https://stackoverflow.com/questions/36123655/what-is-the-git-alternates-mechanism
            // provides a good starting point.
            Span<byte> alternates = stackalloc byte[4096];
            int length = alternateStream!.Read(alternates);
            alternates = alternates.Slice(0, length);

            foreach (string? alternate in ParseAlternates(alternates))
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
    /// Gets the encoding used by this Git repository.
    /// </summary>
    public static Encoding Encoding => Encoding.UTF8;

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
            string? commonDirectoryRelativePath = File.ReadAllText(commonDirFile).Trim('\n');
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
    /// <returns>A newly created instance.</returns>
    public static GitRepository Create(string workingDirectory, string gitDirectory, string commonDirectory, string objectDirectory)
    {
        return new GitRepository(workingDirectory, gitDirectory, commonDirectory, objectDirectory);
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
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

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
        var values = new List<string>();

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

    /// <summary>
    /// Shortens the object id.
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
        string? sha = objectId.ToString();

        for (int length = minimum; length < sha.Length; length += 2)
        {
            string? objectish = sha.Substring(0, length);

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
        using FileStream? stream = File.OpenRead(Path.Combine(this.GitDirectory, HeadFileName));
        return GitReferenceReader.ReadReference(stream);
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
        GitObjectId headCommitId = this.GetHeadCommitSha();

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
        using Stream? stream = this.GetObjectBySha(sha, "commit");
        if (stream is null)
        {
            throw new GitException($"The commit {sha} was not found in this repository.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
        }

        return GitCommitReader.Read(stream, sha, readAuthor);
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
            object? reference = this.GetHeadAsReferenceOrSha();
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
        foreach ((string line, string? _) in this.EnumeratePackedRefsWithPeelLines(out var _))
        {
            var refName = line.Substring(41);
            GitObjectId GetObjId() => GitObjectId.Parse(line.AsSpan().Slice(0, 40));

            if (string.Equals(refName, objectish, StringComparison.Ordinal))
            {
                return GetObjId();
            }
            else if (!objectish.StartsWith("refs/", StringComparison.Ordinal))
            {
                // Not a canonical ref, so try heads and tags
                if (string.Equals(refName, "refs/heads/" + objectish, StringComparison.Ordinal))
                {
                    return GetObjId();
                }
                else if (string.Equals(refName, "refs/tags/" + objectish, StringComparison.Ordinal))
                {
                    return GetObjId();
                }
                else if (string.Equals(refName, "refs/remotes/" + objectish, StringComparison.Ordinal))
                {
                    return GetObjId();
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
            string? directory = Path.Combine(this.ObjectDirectory, objectish.Substring(0, 2));

            if (Directory.Exists(directory))
            {
                string[]? files = Directory.GetFiles(directory, $"{objectish.Substring(2)}*");

                foreach (string? file in files)
                {
                    string? objectId = $"{objectish.Substring(0, 2)}{Path.GetFileName(file)}";
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
                    foreach (GitPack? pack in this.packs.Value.Span)
                    {
                        GitObjectId? objectId = pack.Lookup(decodedHex, endsWithHalfByte);

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
        using Stream? stream = this.GetObjectBySha(sha, "tree");
        if (stream is null)
        {
            throw new GitException($"The tree {sha} was not found in this repository.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
        }

        return GitTreeReader.Read(stream, sha);
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
        using Stream? treeStream = this.GetObjectBySha(treeId, "tree");
        if (treeStream is null)
        {
            throw new GitException($"The tree {treeId} was not found in this repository.") { ErrorCode = GitException.ErrorCodes.ObjectNotFound };
        }

        return GitTreeStreamingReader.FindNode(treeStream, nodeName);
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
    public bool TryGetObjectBySha(GitObjectId sha, string objectType, [NotNullWhen(true)] out Stream? value)
    {
#if DEBUG
        if (!this.histogram.TryAdd(sha, 1))
        {
            this.histogram[sha] += 1;
        }
#endif

        foreach (GitPack? pack in this.packs.Value.Span)
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

        foreach (GitRepository? alternate in this.alternates)
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
        var builder = new StringBuilder();

#if DEBUG
        int histogramCount = 25;

        builder.AppendLine("Overall repository:");
        builder.AppendLine($"Top {histogramCount} / {this.histogram.Count} items:");

        foreach (KeyValuePair<GitObjectId, int> item in this.histogram.OrderByDescending(v => v.Value).Take(25))
        {
            builder.AppendLine($"  {item.Key}: {item.Value}");
        }

        builder.AppendLine();
#endif

        foreach (GitPack? pack in this.packs.Value.Span)
        {
            pack.GetCacheStatistics(builder);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns a list of canonical names of tags that point to the given Git object id.
    /// </summary>
    /// <param name="objectId">The git object id to get the corresponding tags for.</param>
    /// <returns>A list of canonical names of tags.</returns>
    public List<string> LookupTags(GitObjectId objectId)
    {
        var tags = new List<string>();

        void HandleCandidate(GitObjectId pointsAt, string tagName, bool isPeeled)
        {
            if (objectId.Equals(pointsAt))
            {
                tags.Add(tagName);
            }
            else if (!isPeeled && this.TryGetObjectBySha(pointsAt, "tag", out Stream? tagContent))
            {
                GitAnnotatedTag tag = GitAnnotatedTagReader.Read(tagContent, pointsAt);
                if ("commit".Equals(tag.Type, StringComparison.Ordinal) && objectId.Equals(tag.Object))
                {
                    tags.Add($"refs/tags/{tag.Tag}");
                }
            }
        }

        // Both tag files and packed-refs might either contain lightweight or annotated tags.
        // tag files
        var tagDir = Path.Combine(this.CommonDirectory, "refs", "tags");
        foreach (var tagFile in Directory.EnumerateFiles(tagDir, "*", SearchOption.AllDirectories))
        {
            var tagObjId = GitObjectId.ParseHex(File.ReadAllBytes(tagFile).AsSpan().Slice(0, 40));

            // \ is not legal in git tag names
            var tagName = tagFile.Substring(tagDir.Length + 1).Replace('\\', '/');
            var canonical = $"refs/tags/{tagName}";

            HandleCandidate(tagObjId, canonical, false);
        }

        // packed-refs file
        foreach ((string line, string? peelLine) in this.EnumeratePackedRefsWithPeelLines(out var tagsPeeled))
        {
            var refName = line.Substring(41);

            // If we remove this check we do find local and remote branch heads too.
            if (refName.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                ReadOnlySpan<char> tagSpan = peelLine is null ? line.AsSpan().Slice(0, 40) : peelLine.AsSpan().Slice(1, 40);
                var tagObjId = GitObjectId.Parse(tagSpan);
                HandleCandidate(tagObjId, refName, tagsPeeled);
            }
        }

        return tags;
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
            foreach (GitPack? pack in this.packs.Value.Span)
            {
                pack.Dispose();
            }
        }
    }

    private static string TrimEndingDirectorySeparator(string path)
    {
#if NETFRAMEWORK
        if (string.IsNullOrEmpty(path) || path.Length == 1)
        {
            return path;
        }

        char last = path[path.Length - 1];

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
#if NET6_0_OR_GREATER
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

    private bool TryGetObjectByPath(GitObjectId sha, string objectType, [NotNullWhen(true)] out Stream? value)
    {
        sha.CopyAsHex(0, 1, this.objectPathBuffer.AsSpan(this.ObjectDirectory.Length + 1, 2));
        sha.CopyAsHex(1, 19, this.objectPathBuffer.AsSpan(this.ObjectDirectory.Length + 1 + 2 + 1));

        if (!FileHelpers.TryOpen(this.objectPathBuffer, out FileStream? compressedFile))
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
        string? packDirectory = Path.Combine(this.ObjectDirectory, "pack/");

        if (!Directory.Exists(packDirectory))
        {
            return Array.Empty<GitPack>();
        }

        string[]? indexFiles = Directory.GetFiles(packDirectory, "*.idx");
        var packs = new GitPack[indexFiles.Length];
        int addCount = 0;

        for (int i = 0; i < indexFiles.Length; i++)
        {
            string? name = Path.GetFileNameWithoutExtension(indexFiles[i]);
            string? indexPath = Path.Combine(this.ObjectDirectory, "pack", $"{name}.idx");
            string? packPath = Path.Combine(this.ObjectDirectory, "pack", $"{name}.pack");

            // Only proceed if both the packfile and index file exist.
            if (File.Exists(packPath))
            {
                packs[addCount++] = new GitPack(this.GetObjectBySha, indexPath, packPath);
            }
        }

        return packs.AsMemory(0, addCount);
    }

    private IEnumerable<string> EnumerateLines(string filePath)
    {
        using StreamReader sr = File.OpenText(filePath);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    /// <summary>
    /// Enumerate the lines in the packed-refs file. Skips comment lines.
    /// </summary>
    private IEnumerable<string> EnumeratePackedRefsRaw(out bool tagsPeeled)
    {
        tagsPeeled = false;
        string packedRefPath = Path.Combine(this.CommonDirectory, "packed-refs");
        if (!File.Exists(packedRefPath))
        {
            return Enumerable.Empty<string>();
        }

        // We use the rather simple EnumerateLines iterator here because this way
        // the disposable StreamReader can survive when this method already returned and
        // Enumerate() runs.
        IEnumerator<string> lines = this.EnumerateLines(packedRefPath).GetEnumerator();
        if (!lines.MoveNext())
        {
            return Enumerable.Empty<string>();
        }

        // see https://github.com/git/git/blob/d9d677b2d8cc5f70499db04e633ba7a400f64cbf/refs/packed-backend.c#L618
        const string fileHeaderPrefix = "# pack-refs with:";
        string firstLine = lines.Current;
        if (firstLine.StartsWith(fileHeaderPrefix))
        {
            // could contain "peeled" or "fully-peeled" or (typically) both.
            // The meaning of any of these is equivalent for our use case.
#if NETFRAMEWORK
            tagsPeeled = firstLine.IndexOf("peeled", StringComparison.Ordinal) >= 0;
#else
            tagsPeeled = firstLine.Contains("peeled", StringComparison.Ordinal);
#endif
        }

        IEnumerable<string> Enumerate()
        {
            do
            {
                // We process the first line here again and continue because it starts with #.
                // We could add a MoveNext() above if the header prefix was found, but we'd need
                // to handle the case that it returned false then.
                var line = lines.Current;
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return line;
            }
            while (lines.MoveNext());
        }

        return Enumerate();
    }

    /// <summary>
    /// Enumerate the lines in the packed-refs file. If a line has a corresponding peel
    /// line, they are returned together.
    /// </summary>
    private IEnumerable<(string Record, string? PeelLine)> EnumeratePackedRefsWithPeelLines(out bool tagsPeeled)
    {
        IEnumerable<string> rawEnum = this.EnumeratePackedRefsRaw(out tagsPeeled);
        return Enumerate();

        IEnumerable<(string Record, string? PeelLine)> Enumerate()
        {
            string? recordLine = null;
            foreach (var line in rawEnum)
            {
                if (line[0] == '^')
                {
                    if (recordLine is null)
                    {
                        throw new GitException("packed-refs format is broken. Found a peel line without a preceeding record it belongs to.");
                    }

                    yield return (recordLine, line);
                    recordLine = null;
                }
                else
                {
                    if (recordLine is not null)
                    {
                        yield return (recordLine, null);
                    }

                    recordLine = line;
                }
            }

            if (recordLine is not null)
            {
                yield return (recordLine, null);
            }
        }
    }
}
