using System;
using System.IO;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Abstractions;

[Trait("Engine", "Managed")]
public class GitContextManagedTests : GitContextTests
{
    public GitContextManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: false);
}

[Trait("Engine", "LibGit2")]
public class GitContextLibGit2Tests : GitContextTests
{
    public GitContextLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: true);
}

public abstract class GitContextTests : RepoTestBase
{
    protected GitContextTests(ITestOutputHelper logger) : base(logger)
    {
        this.InitializeSourceControl();
        this.AddCommits();
    }

    [Fact]
    public void InitialDefaultState()
    {
        Assert.Equal(this.LibGit2Repository.Head.Tip.Id.Sha, this.Context.GitCommitId);
        Assert.Equal(this.LibGit2Repository.Head.Tip.Author.When, this.Context.GitCommitDate);
        Assert.Equal("refs/heads/master", this.Context.HeadCanonicalName);
        Assert.Equal(this.RepoPath, this.Context.AbsoluteProjectDirectory);
        Assert.Equal(this.RepoPath, this.Context.WorkingTreePath);
        Assert.Equal(string.Empty, this.Context.RepoRelativeProjectDirectory);
        Assert.True(this.Context.IsHead);
        Assert.True(this.Context.IsRepository);
        Assert.False(this.Context.IsShallow);
        Assert.NotNull(this.Context.VersionFile);
    }

    [Fact]
    public void SelectHead()
    {
        Assert.True(this.Context.TrySelectCommit("HEAD"));
    }

    [Fact]
    public void SelectCommitByFullId()
    {
        Assert.True(this.Context.TrySelectCommit(this.Context.GitCommitId));
    }

    [Fact]
    public void SelectCommitByPartialId()
    {
        Assert.True(this.Context.TrySelectCommit(this.Context.GitCommitId.Substring(0, 12)));
    }

    [Fact]
    public void SelectCommitByTagSimpleName()
    {
        this.LibGit2Repository.Tags.Add("test", this.LibGit2Repository.Head.Tip);
        Assert.True(this.Context.TrySelectCommit("test"));
    }

    [Fact]
    public void SelectCommitByTagCanonicalName()
    {
        this.LibGit2Repository.Tags.Add("test", this.LibGit2Repository.Head.Tip);
        Assert.True(this.Context.TrySelectCommit("refs/tags/test"));
    }

    [Fact]
    public void SelectCommitByBranchSimpleName()
    {
        Assert.True(this.Context.TrySelectCommit("master"));
    }

    [Fact]
    public void SelectCommitByBranchCanonicalName()
    {
        Assert.True(this.Context.TrySelectCommit("refs/heads/master"));
    }

    [Fact]
    public void SelectDirectory_Empty()
    {
        this.Context.RepoRelativeProjectDirectory = string.Empty;
        Assert.Equal(string.Empty, this.Context.RepoRelativeProjectDirectory);
    }

    [Fact]
    public void SelectDirectory_SubDir()
    {
        string absolutePath = Path.Combine(this.RepoPath, "sub");
        Directory.CreateDirectory(absolutePath);
        this.Context.RepoRelativeProjectDirectory = "sub";
        Assert.Equal("sub", this.Context.RepoRelativeProjectDirectory);
        Assert.Equal(absolutePath, this.Context.AbsoluteProjectDirectory);
    }
}
