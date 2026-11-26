// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitSha256RepositoryTests : RepoTestBase
{
    public GitSha256RepositoryTests()
        : base(TestOutputHelper.Instance)
    {
    }

    [Test]
    public void LooseObjectsAndVersionOracle()
    {
        this.InitializeSha256Repo();
        Directory.CreateDirectory(Path.Combine(this.RepoPath, "src"));
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"1.2\",\"publicReleaseRefSpec\":[\"^refs/tags/v1.2$\"]}");
        File.WriteAllText(Path.Combine(this.RepoPath, "src", "file.txt"), "hello sha256");
        this.Git(this.RepoPath, "add", ".");
        string first = this.Commit("first");

        File.WriteAllText(Path.Combine(this.RepoPath, "src", "file.txt"), "hello sha256 v2");
        this.Git(this.RepoPath, "add", ".");
        string second = this.Commit("second");

        this.Git(this.RepoPath, "checkout", "-b", "feature", first);
        File.WriteAllText(Path.Combine(this.RepoPath, "branch.txt"), "from feature");
        this.Git(this.RepoPath, "add", ".");
        string featureCommit = this.Commit("feature");

        this.Git(this.RepoPath, "checkout", "main");
        this.Git(this.RepoPath, "merge", "--no-ff", "feature", "-m", "merge feature");
        string head = this.Git(this.RepoPath, "rev-parse", "HEAD").Trim();
        Assert.Equal(64, head.Length);

        this.Git(this.RepoPath, "tag", "lightweight");
        this.Git(this.RepoPath, "tag", "-a", "v1.2", "-m", "annotated release");

        using (GitRepository repository = GitRepository.Create(this.RepoPath)!)
        {
            Assert.Equal(GitObjectFormat.Sha256, repository.ObjectFormat);
            Assert.Equal("refs/heads/main", repository.GetHeadAsReferenceOrSha());
            Assert.Equal(GitObjectId.Parse(head), repository.GetHeadCommitSha());

            GitCommit headCommit = repository.GetHeadCommit(readAuthor: true)!.Value;
            Assert.Equal(GitObjectId.Parse(head), headCommit.Sha);
            Assert.Equal("Test", headCommit.Author!.Value.Name);
            Assert.Equal(new[] { GitObjectId.Parse(second), GitObjectId.Parse(featureCommit) }, headCommit.Parents.ToArray());

            // Full and abbreviated lookups (both even and odd lengths)
            Assert.Equal(GitObjectId.Parse(head), repository.Lookup(head));
            Assert.Equal(GitObjectId.Parse(head), repository.Lookup(head.Substring(0, 7)));
            Assert.Equal(GitObjectId.Parse(head), repository.Lookup(head.Substring(0, 8)));
            Assert.Equal(GitObjectId.Parse(second), repository.Lookup("HEAD~1"));
            Assert.Equal(GitObjectId.Parse(first), repository.Lookup("HEAD~2"));
            Assert.Equal(head.Substring(0, 10), repository.ShortenObjectId(GitObjectId.Parse(head), 10));

            // Tree reading
            GitTree rootTree = repository.GetTree(headCommit.Tree);
            Assert.True(rootTree.Children.ContainsKey("version.json"));
            Assert.True(rootTree.Children.ContainsKey("src"));
            GitObjectId srcTreeId = repository.GetTreeEntry(headCommit.Tree, Encoding.UTF8.GetBytes("src"));
            Assert.NotEqual(GitObjectId.Empty, srcTreeId);
            GitObjectId fileBlobId = repository.GetTreeEntry(srcTreeId, Encoding.UTF8.GetBytes("file.txt"));
            Assert.Equal(this.Git(this.RepoPath, "rev-parse", "HEAD:src/file.txt").Trim(), fileBlobId.ToString());

            // Lightweight and annotated tags
            Assert.Equal(
                new[] { "refs/tags/lightweight", "refs/tags/v1.2" },
                repository.LookupTags(GitObjectId.Parse(head)).OrderBy(t => t).ToArray());
        }

        using (GitContext context = this.CreateGitContext(this.RepoPath))
        {
            var oracle = new VersionOracle(context);
            Assert.Equal(head, oracle.GitCommitId);
            Assert.Equal(head.Substring(0, 10), oracle.GitCommitIdShort);
            Assert.Equal(3, oracle.VersionHeight);
            Assert.Equal(GitObjectId.Parse(head).AsUInt16(), oracle.Version.Revision);
            Assert.True(oracle.PublicRelease);
            Assert.Equal("main", context.GetDefaultBranch());
        }
    }

    [Test]
    public void PackedObjectsPackedRefsAndRefDelta()
    {
        this.InitializeSha256Repo();
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"3.0\"}");
        string payload = new string('a', 4096);
        File.WriteAllText(Path.Combine(this.RepoPath, "data.txt"), payload + "\nline1\n");
        this.Git(this.RepoPath, "add", ".");
        string first = this.Commit("first");

        File.WriteAllText(Path.Combine(this.RepoPath, "data.txt"), payload + "\nline2\n");
        this.Git(this.RepoPath, "add", ".");
        string second = this.Commit("second");

        this.Git(this.RepoPath, "branch", "other");
        this.Git(this.RepoPath, "tag", "lw-tag", first);
        this.Git(this.RepoPath, "tag", "-a", "ann-tag", "-m", "annotated", second);

        // Pack all objects using REF_DELTA (omitting --delta-base-offset) and pack all refs,
        // then prune loose objects so every read goes through pack + idx + packed-refs.
        this.Git(this.RepoPath, "config", "pack.useDeltaBaseOffset", "false");
        this.Git(this.RepoPath, "repack", "-a", "-d");
        this.Git(this.RepoPath, "pack-refs", "--all", "--prune");

        string packDir = Path.Combine(this.RepoPath, ".git", "objects", "pack");
        string idxPath = Directory.GetFiles(packDir, "*.idx").Single();

        using (var idxReader = new GitPackIndexMappedReader(File.OpenRead(idxPath), GitObjectFormat.Sha256))
        {
            Assert.NotNull(idxReader.GetOffset(GitObjectId.Parse(second)));
            Assert.Null(idxReader.GetOffset(GitObjectId.Empty));
        }

        using (GitRepository repository = GitRepository.Create(this.RepoPath)!)
        {
            Assert.Equal(GitObjectFormat.Sha256, repository.ObjectFormat);
            Assert.Equal(GitObjectId.Parse(second), repository.GetHeadCommitSha());
            Assert.Equal(GitObjectId.Parse(first), repository.Lookup("HEAD~1"));
            Assert.Equal(GitObjectId.Parse(second), repository.Lookup("other"));
            Assert.Equal(GitObjectId.Parse(second), repository.Lookup(second.Substring(0, 7)));
            Assert.Equal(GitObjectId.Parse(second), repository.Lookup(second.Substring(0, 8)));
            Assert.Equal(new[] { "refs/tags/lw-tag" }, repository.LookupTags(GitObjectId.Parse(first)));
            Assert.Equal(new[] { "refs/tags/ann-tag" }, repository.LookupTags(GitObjectId.Parse(second)));

            // Reading both blobs exercises delta reconstruction (including OBJ_REF_DELTA) in the SHA-256 pack.
            GitCommit c1 = repository.GetCommit(GitObjectId.Parse(first));
            GitCommit c2 = repository.GetCommit(GitObjectId.Parse(second));
            using var s1 = new StreamReader(repository.GetObjectBySha(repository.GetTreeEntry(c1.Tree, Encoding.UTF8.GetBytes("data.txt")), "blob")!);
            using var s2 = new StreamReader(repository.GetObjectBySha(repository.GetTreeEntry(c2.Tree, Encoding.UTF8.GetBytes("data.txt")), "blob")!);
            Assert.Equal(payload + "\nline1\n", s1.ReadToEnd());
            Assert.Equal(payload + "\nline2\n", s2.ReadToEnd());
        }

        using (GitContext context = this.CreateGitContext(this.RepoPath))
        {
            var oracle = new VersionOracle(context);
            Assert.Equal(second, oracle.GitCommitId);
            Assert.Equal(2, oracle.VersionHeight);
            Assert.Equal("main", context.GetDefaultBranch());
        }
    }

    [Test]
    public void DetachedHeadAndWorktree()
    {
        this.InitializeSha256Repo();
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"1.0\"}");
        this.Git(this.RepoPath, "add", "version.json");
        string first = this.Commit("first");
        string second = this.Commit("second");

        this.Git(this.RepoPath, "checkout", "--detach", first);
        using (GitRepository repository = GitRepository.Create(this.RepoPath)!)
        {
            Assert.Equal(GitObjectFormat.Sha256, repository.ObjectFormat);
            Assert.Equal(GitObjectId.Parse(first), repository.GetHeadAsReferenceOrSha());
            Assert.Equal(GitObjectId.Parse(first), repository.GetHeadCommitSha());
        }

        string worktree = this.CreateDirectoryForNewRepo();
        this.Git(this.RepoPath, "worktree", "add", "-b", "wt-branch", worktree, second);
        using (GitContext context = this.CreateGitContext(worktree))
        {
            var oracle = new VersionOracle(context);
            Assert.Equal(second, oracle.GitCommitId);
            Assert.Equal(2, oracle.VersionHeight);
            Assert.Equal("refs/heads/wt-branch", context.HeadCanonicalName);
        }
    }

    [Test]
    public void PathFiltersComputeHeightOnSha256Trees()
    {
        this.InitializeSha256Repo();
        Directory.CreateDirectory(Path.Combine(this.RepoPath, "src"));
        Directory.CreateDirectory(Path.Combine(this.RepoPath, "docs"));
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"1.0\",\"pathFilters\":[\":/src\"]}");
        File.WriteAllText(Path.Combine(this.RepoPath, "src", "a.txt"), "1");
        this.Git(this.RepoPath, "add", ".");
        this.Commit("initial");

        File.WriteAllText(Path.Combine(this.RepoPath, "docs", "readme.md"), "doc change");
        this.Git(this.RepoPath, "add", ".");
        this.Commit("docs only");

        using (GitContext context = this.CreateGitContext(this.RepoPath))
        {
            var oracle = new VersionOracle(context);
            Assert.Equal(1, oracle.VersionHeight);
        }

        File.WriteAllText(Path.Combine(this.RepoPath, "src", "a.txt"), "2");
        this.Git(this.RepoPath, "add", ".");
        this.Commit("src change");

        using (GitContext context = this.CreateGitContext(this.RepoPath))
        {
            var oracle = new VersionOracle(context);
            Assert.Equal(2, oracle.VersionHeight);
        }
    }

    [Test]
    public void LibGit2EngineThrowsClearGitExceptionOnSha256Repo()
    {
        this.InitializeSha256Repo();
        this.Commit("initial");

        GitException ex = Assert.Throws<GitException>(() => GitContext.Create(this.RepoPath, engine: GitContext.Engine.ReadWrite));
        Assert.Contains("SHA-256", ex.Message);
        Assert.Contains("Managed", ex.Message);
    }

    [Test]
    public void UnsupportedObjectFormatConfigThrows()
    {
        this.InitializeSha256Repo();
        string configPath = Path.Combine(this.RepoPath, ".git", "config");
        File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("sha256", "unknownhash"));

        GitException ex = Assert.Throws<GitException>(() => GitRepository.Create(this.RepoPath));
        Assert.Contains("unknownhash", ex.Message);
    }

#if NET
    [Test]
    public async Task NbgvGetVersionCliWorksOnSha256Repo()
    {
        this.InitializeSha256Repo();
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"2.5\"}");
        this.Git(this.RepoPath, "add", "version.json");
        string head = this.Commit("initial");

        (int exitCode, string stdOut, string stdErr) = await this.RunNbgvGetVersionAsync(this.RepoPath, "-v", "GitCommitId");
        Assert.Equal(0, exitCode);
        Assert.Equal(head, stdOut.Trim());
        Assert.DoesNotContain("Dirty version.json", stdErr);

        // Dirtying version.json should emit the warning without failing on LibGit2.
        File.WriteAllText(Path.Combine(this.RepoPath, "version.json"), "{\"version\":\"2.6\"}");
        (exitCode, stdOut, stdErr) = await this.RunNbgvGetVersionAsync(this.RepoPath, "-v", "Version");
        Assert.Equal(0, exitCode);
        Assert.Contains("Dirty version.json files must be committed", stdErr);
    }
#endif

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string? committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadOnly);

    private void InitializeSha256Repo()
    {
        this.Git(this.RepoPath, "init", "--object-format=sha256", "--initial-branch=main");
        this.Git(this.RepoPath, "config", "user.name", "Test");
        this.Git(this.RepoPath, "config", "user.email", "test@example.com");
        this.Git(this.RepoPath, "config", "commit.gpgSign", "false");
        this.Git(this.RepoPath, "config", "tag.gpgSign", "false");
    }

    private string Commit(string message)
    {
        this.Git(this.RepoPath, "commit", "--allow-empty", "-m", message);
        return this.Git(this.RepoPath, "rev-parse", "HEAD").Trim();
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
        Assert.True(process.ExitCode == 0, $"Git failed: {standardError}");
        return standardOutput;
    }

#if NET
    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunNbgvGetVersionAsync(string project, params string[] extraArgs)
    {
        string nbgvToolPath = typeof(GitSha256RepositoryTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "NbgvToolPath")
            .Value!;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(nbgvToolPath);
        startInfo.ArgumentList.Add("get-version");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        foreach (string arg in extraArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["NBGV_GitEngine"] = "Managed";

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current!.Execution.CancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(TestContext.Current!.Execution.CancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(TestContext.Current!.Execution.CancellationToken));
        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }
#endif
}
