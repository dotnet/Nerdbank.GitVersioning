// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitReftableTests : RepoTestBase
{
    private const string ObjectId = "0123456789012345678901234567890123456789";

    public GitReftableTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void UnbornHead()
    {
        this.InitializeReftable();
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Equal("refs/heads/main", repository.GetHeadAsReferenceOrSha());
        Assert.Equal(GitObjectId.Empty, repository.GetHeadCommitSha());
        Assert.Null(repository.GetHeadCommit());
        Assert.Null(repository.Lookup("main"));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(4096)]
    public void ReferencesAcrossBlocksAndStackUpdates(int blockSize)
    {
        this.Git(this.RepoPath, "init", "--ref-format=files", "--initial-branch=main");
        this.ConfigureGit();
        this.Git(this.RepoPath, "config", "reftable.blockSize", blockSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string first = this.Commit();
        string second = this.Commit();

        // Migration lets Git produce a multi-block table without relying on redirected stdin encoding.
        string headsDirectory = Path.Combine(this.RepoPath, ".git", "refs", "heads");
        for (int i = 0; i < 500; i++)
        {
            File.WriteAllText(Path.Combine(headsDirectory, $"branch-{i:D4}"), first + "\n");
        }

        this.Git(this.RepoPath, "refs", "migrate", "--ref-format=reftable");
        Assert.Contains(
            File.ReadAllLines(Path.Combine(this.RepoPath, ".git", "reftable", "tables.list")),
            name =>
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(this.RepoPath, ".git", "reftable", name));
                return bytes.Length > blockSize + 68
                    && ((bytes[5] << 16) | (bytes[6] << 8) | bytes[7]) == blockSize
                    && bytes[blockSize] == (byte)'r';
            });
        this.Git(this.RepoPath, "update-ref", "refs/heads/branch-0000", second);
        this.Git(this.RepoPath, "update-ref", "-d", "refs/heads/branch-0001");
        this.Git(this.RepoPath, "branch", "topic/nested");
        this.Git(this.RepoPath, "branch", "topic-\u00e9");
        this.Git(this.RepoPath, "symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
        this.Git(this.RepoPath, "update-ref", "refs/remotes/origin/main", second);
        this.Git(this.RepoPath, "tag", "lightweight");
        this.Git(this.RepoPath, "tag", "-a", "annotated", "-m", "annotation");
        this.Git(this.RepoPath, "tag", "deleted", first);
        this.Git(this.RepoPath, "tag", "-d", "deleted");

        Assert.True(File.ReadAllLines(Path.Combine(this.RepoPath, ".git", "reftable", "tables.list")).Length > 1);
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        this.AssertReferences(repository);
        Assert.Equal(GitObjectId.Parse(first), repository.Lookup("HEAD~1"));
        Assert.Equal("refs/heads/main", repository.GetHeadAsReferenceOrSha());
        Assert.Equal(new[] { "refs/tags/annotated", "refs/tags/lightweight" }, repository.LookupTags(GitObjectId.Parse(second)).OrderBy(name => name).ToArray());
        Assert.Empty(repository.LookupTags(GitObjectId.Parse(first)));

        // The same reader must observe a new stack after Git compacts it.
        this.Git(this.RepoPath, "pack-refs", "--all");
        this.AssertReferences(repository);
        this.Git(this.RepoPath, "checkout", "--detach", first);
        Assert.Equal(GitObjectId.Parse(first), repository.GetHeadAsReferenceOrSha());
        Assert.Equal(GitObjectId.Parse(first), repository.GetHeadCommitSha());
    }

    [Fact]
    public void CloneCalculatesSameVersionAsFilesBackend()
    {
        this.Git(this.RepoPath, "init", "--ref-format=files", "--initial-branch=main");
        this.ConfigureGit();
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"1.2\",\"publicReleaseRefSpec\":[\"^refs/tags/v1.2$\"]}");
        this.Git(this.RepoPath, "add", "version.json");
        this.Commit();
        this.Commit();
        string head = this.Commit();
        this.Git(this.RepoPath, "tag", "-a", "v1.2", "-m", "release");
        using GitContext source = this.CreateGitContext(this.RepoPath);
        var expected = new VersionOracle(source);

        string clone = this.CreateDirectoryForNewRepo();
        this.Git(this.RepoPath, "clone", "--no-local", "--ref-format=reftable", this.RepoPath, clone);
        using GitContext context = this.CreateGitContext(clone);
        var actual = new VersionOracle(context);
        Assert.Equal(head, context.GitCommitId);
        Assert.Equal("refs/heads/main", context.HeadCanonicalName);
        Assert.Equal("main", context.GetDefaultBranch());
        Assert.Contains("refs/tags/v1.2", context.HeadTags!);
        Assert.True(actual.PublicRelease);
        Assert.Equal(3, actual.VersionHeight);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.NuGetPackageVersion, actual.NuGetPackageVersion);
        Assert.Equal(expected.GitCommitId, actual.GitCommitId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorktreeUsesPrivateHeadAndSharedReferences(bool detached)
    {
        this.InitializeReftable();
        string first = this.Commit();
        string second = this.Commit();
        this.Git(this.RepoPath, "tag", "first", first);
        this.Git(this.RepoPath, "update-ref", "refs/worktree/private", second);
        string worktree = this.CreateDirectoryForNewRepo();
        if (detached)
        {
            this.Git(this.RepoPath, "worktree", "add", "--detach", worktree, first);
        }
        else
        {
            this.Git(this.RepoPath, "worktree", "add", "-b", "topic", worktree, first);
        }

        using GitRepository repository = GitRepository.Create(worktree)!;
        Assert.Equal(GitObjectId.Parse(first), repository.GetHeadCommitSha());
        if (detached)
        {
            Assert.Equal(GitObjectId.Parse(first), repository.GetHeadAsReferenceOrSha());
        }
        else
        {
            Assert.Equal("refs/heads/topic", repository.GetHeadAsReferenceOrSha());
        }

        Assert.Equal(GitObjectId.Parse(second), repository.Lookup("main"));
        Assert.Null(repository.Lookup("refs/worktree/private"));
        this.Git(worktree, "update-ref", "refs/worktree/private", first);
        Assert.Equal(GitObjectId.Parse(first), repository.Lookup("refs/worktree/private"));
        Assert.Equal(new[] { "refs/tags/first" }, repository.LookupTags(GitObjectId.Parse(first)));
        using GitContext context = this.CreateGitContext(worktree);
        Assert.Equal(first, context.GitCommitId);
    }

    [Fact]
    public void DefaultBranchReadsRemoteSymbolicReference()
    {
        this.InitializeReftable();
        string head = this.Commit();
        this.Git(this.RepoPath, "update-ref", "refs/remotes/fork/trunk", head);
        this.Git(this.RepoPath, "symbolic-ref", "refs/remotes/fork/HEAD", "refs/remotes/fork/trunk");
        using GitContext context = this.CreateGitContext(this.RepoPath);
        Assert.Equal("trunk", context.GetDefaultBranch());
    }

    [Fact]
    public void DefaultBranchReadsLocalBranches()
    {
        this.InitializeReftable();
        this.Commit();
        this.Git(this.RepoPath, "branch", "-m", "release/v1.0");
        using GitContext context = this.CreateGitContext(this.RepoPath);
        Assert.Equal("release/v1.0", context.GetDefaultBranch());
    }

    [Fact]
    public void SymbolicReferenceCycleThrows()
    {
        this.InitializeReftable();
        this.Git(this.RepoPath, "symbolic-ref", "refs/heads/a", "refs/heads/b");
        this.Git(this.RepoPath, "symbolic-ref", "refs/heads/b", "refs/heads/a");
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Contains("cycle", Assert.Throws<GitException>(() => repository.Lookup("refs/heads/a")).Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnalignedTableVersions(int version)
    {
        this.WriteTable(CreateTable((byte)version));
        Assert.Equal(ObjectId, this.Git(this.RepoPath, "rev-parse", "HEAD").Trim());
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Equal(GitObjectId.Parse(ObjectId), repository.GetHeadAsReferenceOrSha());
        Assert.Equal(GitObjectId.Parse(ObjectId), repository.GetHeadCommitSha());
    }

    [Theory]
    [InlineData("header")]
    [InlineData("version")]
    [InlineData("footer")]
    [InlineData("crc")]
    [InlineData("block-length")]
    [InlineData("prefix")]
    [InlineData("varint")]
    [InlineData("value-type")]
    [InlineData("restart")]
    [InlineData("truncated")]
    public void InvalidTableThrows(string corruption)
    {
        byte[] table = CreateTable(1);
        switch (corruption)
        {
            case "header": table[0] = 0; break;
            case "version": table[4] = 3; break;
            case "footer": table[table.Length - 68] = 0; break;
            case "crc": table[table.Length - 1] ^= 1; break;
            case "block-length": table[25] = 0xff; break;
            case "prefix": table[28] = 1; break;
            case "varint":
                for (int i = 28; i < 56; i++)
                {
                    table[i] = 0xff;
                }

                break;
            case "value-type": table[29] |= 7; break;
            case "restart": table[table.Length - 68 - 5] = 1; break;
            case "truncated": Array.Resize(ref table, 50); break;
        }

        this.WriteTable(table);
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Throws<GitException>(() => repository.GetHeadCommitSha());
    }

    [Fact]
    public void Sha256TableIsRejected()
    {
        this.WriteTable(CreateTable(2, "s256"));
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Contains("Only SHA-1", Assert.Throws<GitException>(() => repository.GetHeadCommitSha()).Message);
    }

    [Fact]
    public void EmptyTableDoesNotHideOlderReferences()
    {
        this.WriteTable(CreateTable(1));
        File.WriteAllBytes(Path.Combine(this.RepoPath, ".git", "reftable", "empty.ref"), CreateTable(1, empty: true));
        File.AppendAllText(Path.Combine(this.RepoPath, ".git", "reftable", "tables.list"), "empty.ref\n");
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Equal(GitObjectId.Parse(ObjectId), repository.GetHeadCommitSha());
    }

    [Fact]
    public void LogOnlyTableDoesNotHideOlderReferences()
    {
        this.InitializeReftable();
        string head = this.Commit();
        this.Git(this.RepoPath, "reflog", "delete", "HEAD@{0}");
        Assert.Contains(
            File.ReadAllLines(Path.Combine(this.RepoPath, ".git", "reftable", "tables.list")),
            name => File.ReadAllBytes(Path.Combine(this.RepoPath, ".git", "reftable", name))[24] == (byte)'g');
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Equal(GitObjectId.Parse(head), repository.GetHeadCommitSha());
    }

    [Fact]
    public void UnlistedTableIsIgnored()
    {
        this.WriteTable(CreateTable(1));
        File.WriteAllBytes(Path.Combine(this.RepoPath, ".git", "reftable", "unlisted.ref"), new byte[100]);
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Equal(GitObjectId.Parse(ObjectId), repository.GetHeadCommitSha());
    }

    [Fact]
    public void MissingTableThrows()
    {
        this.WriteTable(CreateTable(1));
        File.AppendAllText(Path.Combine(this.RepoPath, ".git", "reftable", "tables.list"), "missing.ref\n");
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Throws<FileNotFoundException>(() => repository.GetHeadCommitSha());
    }

    [Fact]
    public void MissingHeadThrows()
    {
        this.WriteTable(CreateTable(1, empty: true));
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Throws<GitException>(() => repository.GetHeadAsReferenceOrSha());
        Assert.Throws<GitException>(() => repository.GetHeadCommitSha());
    }

    [Theory]
    [InlineData("../outside.ref")]
    [InlineData("..\\outside.ref")]
    [InlineData("")]
    public void InvalidTableFilenameThrows(string filename)
    {
        this.WriteTable(CreateTable(1));
        File.WriteAllText(Path.Combine(this.RepoPath, ".git", "reftable", "tables.list"), filename + "\n");
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Throws<GitException>(() => repository.GetHeadCommitSha());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnalignedMultipleBlocks(int version)
    {
        this.WriteTable(CreateTable((byte)version, multipleBlocks: true));
        Assert.Equal(ObjectId, this.Git(this.RepoPath, "rev-parse", "HEAD").Trim());
        Assert.Equal(ObjectId, this.Git(this.RepoPath, "rev-parse", "refs/heads/main").Trim());
        using GitRepository repository = GitRepository.Create(this.RepoPath)!;
        Assert.Equal(GitObjectId.Parse(ObjectId), repository.GetHeadCommitSha());
        Assert.Equal(GitObjectId.Parse(ObjectId), repository.Lookup("refs/heads/main"));
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string? committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadOnly);

    private static byte[] CreateTable(byte version, string hash = "sha1", bool empty = false, bool multipleBlocks = false)
    {
        int headerLength = version == 1 ? 24 : 28;
        byte[] header = new byte[headerLength];
        Encoding.ASCII.GetBytes("REFT").CopyTo(header, 0);
        header[4] = version;
        if (version == 2)
        {
            Encoding.ASCII.GetBytes(hash).CopyTo(header, 24);
        }

        using var table = new MemoryStream();
        table.Write(header, 0, header.Length);
        long indexPosition = 0;
        if (!empty)
        {
            // One uncompressed reference record, followed by its restart offset and count.
            int blockLength = headerLength + 4 + 2 + 4 + 1 + 20 + 5;
            table.WriteByte((byte)'r');
            table.WriteByte((byte)(blockLength >> 16));
            table.WriteByte((byte)(blockLength >> 8));
            table.WriteByte((byte)blockLength);
            table.WriteByte(0);
            table.WriteByte((4 << 3) | 1);
            table.Write(Encoding.ASCII.GetBytes("HEAD"), 0, 4);
            table.WriteByte(0);
            byte[] id = Enumerable.Range(0, 20).Select(i => Convert.ToByte(ObjectId.Substring(i * 2, 2), 16)).ToArray();
            table.Write(id, 0, id.Length);
            table.WriteByte(0);
            table.WriteByte(0);
            table.WriteByte((byte)(headerLength + 4));
            table.WriteByte(0);
            table.WriteByte(1);
        }

        if (multipleBlocks)
        {
            byte secondBlockPosition = (byte)table.Position;
            byte[] name = Encoding.ASCII.GetBytes("refs/heads/main");
            int blockLength = 4 + 2 + name.Length + 1 + 20 + 5;
            table.WriteByte((byte)'r');
            table.WriteByte(0);
            table.WriteByte(0);
            table.WriteByte((byte)blockLength);
            table.WriteByte(0);
            table.WriteByte((byte)((name.Length << 3) | 1));
            table.Write(name, 0, name.Length);
            table.WriteByte(0);
            byte[] id = Enumerable.Range(0, 20).Select(i => Convert.ToByte(ObjectId.Substring(i * 2, 2), 16)).ToArray();
            table.Write(id, 0, id.Length);
            table.Write(new byte[] { 0, 0, 4, 0, 1 }, 0, 5);

            indexPosition = table.Position;
            using var index = new MemoryStream();
            index.Write(new byte[] { (byte)'i', 0, 0, 0 }, 0, 4);
            index.Write(new byte[] { 0, 4 << 3, (byte)'H', (byte)'E', (byte)'A', (byte)'D', 0 }, 0, 7);
            byte secondRestart = (byte)index.Position;
            index.WriteByte(0);
            index.WriteByte((byte)(name.Length << 3));
            index.Write(name, 0, name.Length);
            index.WriteByte(secondBlockPosition);
            index.Write(new byte[] { 0, 0, 4, 0, 0, secondRestart, 0, 2 }, 0, 8);
            byte[] indexBytes = index.ToArray();
            indexBytes[3] = (byte)indexBytes.Length;
            table.Write(indexBytes, 0, indexBytes.Length);
        }

        byte[] footer = new byte[headerLength + 44];
        header.CopyTo(footer, 0);
        BinaryPrimitives.WriteUInt64BigEndian(footer.AsSpan(headerLength, 8), (ulong)indexPosition);
        uint crc = uint.MaxValue;
        foreach (byte value in footer.Take(footer.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(footer.Length - 4), ~crc);
        table.Write(footer, 0, footer.Length);
        return table.ToArray();
    }

    private void WriteTable(byte[] table)
    {
        string gitDirectory = Path.Combine(this.RepoPath, ".git");
        string reftableDirectory = Path.Combine(gitDirectory, "reftable");
        Directory.CreateDirectory(reftableDirectory);
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs"));
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/.invalid\n");
        File.WriteAllText(Path.Combine(gitDirectory, "refs", "heads"), string.Empty);
        File.WriteAllText(Path.Combine(gitDirectory, "config"), "[core]\nrepositoryformatversion = 1\n[extensions]\nrefStorage = reftable\n");
        File.WriteAllText(Path.Combine(reftableDirectory, "tables.list"), "test.ref\n");
        File.WriteAllBytes(Path.Combine(reftableDirectory, "test.ref"), table);
    }

    private void InitializeReftable()
    {
        this.Git(this.RepoPath, "init", "--ref-format=reftable", "--initial-branch=main");
        this.ConfigureGit();
    }

    private void ConfigureGit()
    {
        this.Git(this.RepoPath, "config", "user.name", "Test");
        this.Git(this.RepoPath, "config", "user.email", "test@example.com");
        this.Git(this.RepoPath, "config", "commit.gpgSign", "false");
        this.Git(this.RepoPath, "config", "tag.gpgSign", "false");
    }

    private string Commit()
    {
        this.Git(this.RepoPath, "commit", "--allow-empty", "-m", "commit");
        return this.Git(this.RepoPath, "rev-parse", "HEAD").Trim();
    }

    private void AssertReferences(GitRepository repository)
    {
        string[] references = this.Git(this.RepoPath, "for-each-ref", "--format=%(refname) %(objectname)")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string reference in references)
        {
            string[] fields = reference.Split(' ');
            Assert.Equal(GitObjectId.Parse(fields[1]), repository.Lookup(fields[0]));
        }

        Assert.Null(repository.Lookup("refs/heads/branch-0001"));
        Assert.Null(repository.Lookup("refs/tags/deleted"));
        Assert.Null(repository.Lookup("refs/heads/missing"));
    }

    private string Git(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.EnvironmentVariables["GIT_TEST_REFTABLE_AUTOCOMPACTION"] = "0";

#if NET
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
#else
        startInfo.Arguments = string.Join(" ", arguments.Select(argument => $"\"{argument.Replace("\"", "\\\"")}\""));
#endif

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string standardOutput = output.GetAwaiter().GetResult();
        string standardError = error.GetAwaiter().GetResult();
        this.Logger.WriteLine($"git {string.Join(" ", arguments)}: {standardError}");
        Assert.True(process.ExitCode == 0, $"Git failed (reftable tests require Git 2.51 or newer): {standardError}");
        return standardOutput;
    }
}
